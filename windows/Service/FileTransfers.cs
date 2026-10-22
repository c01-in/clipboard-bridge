using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardBridge;

/// <summary>One bounded chunk per authenticated request; file bytes never pass through the clipboard or IPC.</summary>
public sealed class FileTransfers : BackgroundService
{
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/api/files", StringComparison.OrdinalIgnoreCase))
            {
                var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = FileProtocol.MaxEnvelopeBytes + 1024L;
            }
            await next(context);
        });
        app.MapPost("/api/files", async (Box body, FileTransfers files, CancellationToken ct) =>
        {
            try { return Results.Json(await files.Request(body, ct), Bridge.Json); }
            catch (Exception e) when (e is FormatException or CryptographicException or JsonException or InvalidDataException or ArgumentException or OverflowException)
            { return Results.BadRequest(); }
        });
    }
    readonly Vault vault;
    readonly WebPushSender sender;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly string root;
    Incoming? incoming;
    Outgoing? outgoing;
    FileTransferStatus? lastOutgoing;
    FileTransferStatus? lastIncoming;
    sealed record Incoming(FileBatch Batch, SavedFile?[] Completed, bool Ready = false);
    sealed class Outgoing(FileBatch batch, FileStream[] streams, string key)
    {
        public FileBatch Batch = batch;
        public FileStream[] Streams = streams;
        public string Key = key;
        public bool Submitted;
        public long[] ReadOffsets = new long[streams.Length];
        public void Close() { foreach (var stream in Streams) stream.Dispose(); }
    }
    public FileTransfers(Vault vault, WebPushSender sender)
    {
        this.vault = vault; this.sender = sender;
        root = Path.GetFullPath(Environment.GetEnvironmentVariable("CLIPBOARD_BRIDGE_FILE_STAGING") ?? Path.Combine(vault.DataDir, "file-transfers"));
        Directory.CreateDirectory(root);
        string state = Path.Combine(root, "incoming.json");
        if (File.Exists(state))
        {
            try { incoming = Bridge.Decode<Incoming>(File.ReadAllBytes(state)); }
            catch (Exception e) when (e is IOException or JsonException) { File.Move(state, state + ".invalid", true); }
        }
    }
    string BatchDirectory(string id) => Path.Combine(root, id);
    string PartPath(string id, int index) => Path.Combine(BatchDirectory(id), index + ".part");
    void Persist()
    {
        string path = Path.Combine(root, "incoming.json");
        if (incoming is null) { File.Delete(path); return; }
        File.WriteAllBytes(path + ".tmp", Bridge.Encode(incoming)); File.Move(path + ".tmp", path, true);
    }
    void ClearIncoming()
    {
        if (incoming is not null && FileProtocol.IsId(incoming.Batch.Id))
        {
            string directory = BatchDirectory(incoming.Batch.Id);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        incoming = null; Persist();
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await gate.WaitAsync(ct);
                try
                {
                    var cfg = vault.Read(); long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (incoming is not null && (incoming.Batch.ExpiresAt <= now || incoming.Batch.ReceiveGeneration != cfg.PhoneGeneration)) ClearIncoming();
                    if (outgoing is null) continue;
                    if (outgoing.Batch.ExpiresAt <= now || cfg.PhonePaused || outgoing.Batch.ReceiveGeneration != cfg.PhoneGeneration || cfg.Key != outgoing.Key)
                    { EndOutgoing("error", "File transfer expired, paused, or pairing changed."); continue; }
                    if (!outgoing.Submitted) await Push(ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { if (outgoing is not null) lastOutgoing = Status("pending", e.Message); }
                finally { gate.Release(); }
            }
        }
        finally { outgoing?.Close(); }
    }
    public static FileStream[] OpenFiles(string[] paths)
    {
        if (paths.Length is < 1 or > FileProtocol.MaxFiles || paths.Any(p => !Path.IsPathFullyQualified(p)))
            throw new InvalidDataException("Select between 1 and 1,000 files.");
        var streams = new List<FileStream>();
        try
        {
            foreach (string path in paths)
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new InvalidDataException("Select files, not folders.");
                // These handles must be opened while impersonating the pipe client, never with service privileges.
                streams.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess));
            }
            return streams.ToArray();
        }
        catch { foreach (var stream in streams) stream.Dispose(); throw; }
    }
    public async Task<Reply> Send(FileStream[] streams, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        bool retained = false;
        try
        {
            var cfg = vault.Read();
            if (cfg.Subscription is null) throw new InvalidOperationException(UiText.Get("status_unpaired"));
            if (cfg.PhonePaused) throw new InvalidOperationException(UiText.Get("phone_paused"));
            if (outgoing is not null) throw new InvalidOperationException(UiText.Get("files_busy"));
            long sequence = 0;
            vault.Update(c => { c.OutgoingSequence = Math.Max(DateTime.UtcNow.Ticks, c.OutgoingSequence + 1); sequence = c.OutgoingSequence; });
            var batch = new FileBatch(FileProtocol.NewId(), sequence, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), cfg.PhoneGeneration,
                streams.Select(s => new SharedFile(Path.GetFileName(s.Name), "application/octet-stream", s.Length)).ToArray());
            FileProtocol.Validate(batch);
            outgoing = new Outgoing(batch, streams, cfg.Key); retained = true;
            try { await Push(ct); } catch (Exception e) when (e is not OperationCanceledException) { lastOutgoing = Status("pending", e.Message); }
            return new Reply(true, UiText.Get("files_waiting"), batch.Id);
        }
        finally { if (!retained) foreach (var stream in streams) stream.Dispose(); gate.Release(); }
    }
    async Task Push(CancellationToken ct)
    {
        if (outgoing is null) return;
        var batch = outgoing.Batch;
        var wake = new ClipManifest(batch.Id, batch.Sequence, FileProtocol.Mime, batch.ExpiresAt, "", 0, null, null, batch.ReceiveGeneration);
        await sender.Send(vault.Read(), Bridge.Seal(Bridge.Encode(wake), outgoing.Key), ct);
        outgoing.Submitted = true; lastOutgoing = Status("sending");
    }
    FileTransferStatus Status(string state, string? detail = null) => outgoing is null ? lastOutgoing ?? new("", "idle", 0, 0) :
        new(outgoing.Batch.Id, state, outgoing.ReadOffsets.Sum(), outgoing.Batch.Files.Sum(f => f.Size), detail);
    void EndOutgoing(string state, string? detail)
    {
        lastOutgoing = Status(state, detail); outgoing?.Close(); outgoing = null;
    }
    public async Task<Reply> Local(Command command, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            switch (command.Op)
            {
                case "files-incoming-status":
                    var progress = incoming is null ? lastIncoming ?? new("", "idle", 0, 0) : new FileTransferStatus(incoming.Batch.Id, incoming.Ready ? "saving" : "receiving",
                        incoming.Batch.Files.Select((_, i) => File.Exists(PartPath(incoming.Batch.Id, i)) ? new FileInfo(PartPath(incoming.Batch.Id, i)).Length : 0).Sum(),
                        incoming.Batch.Files.Any(f => f.Size < 0) ? -1 : incoming.Batch.Files.Sum(f => f.Size));
                    return new(true, "", JsonSerializer.Serialize(progress, Bridge.Json));
                case "files-status": return new(true, "", JsonSerializer.Serialize(outgoing is null ? lastOutgoing ?? Status("idle") : Status(outgoing.Submitted ? "sending" : "pending"), Bridge.Json));
                case "files-cancel":
                    if (outgoing?.Batch.Id == command.Value) EndOutgoing("cancelled", UiText.Get("files_cancelled"));
                    return new(true, "");
                case "files-incoming":
                    return new(true, "", incoming?.Ready == true ? JsonSerializer.Serialize(new FileInboxBatch(incoming.Batch.Id, incoming.Completed.Select(f => f!).ToArray()), Bridge.Json) : null);
                case "files-incoming-ack":
                    if (incoming is null || incoming.Batch.Id != command.Value || !incoming.Ready) return new(false, "File transfer is no longer available.");
                    vault.Update(c => { c.FileIncomingSequence = incoming.Batch.Sequence; c.FileIncomingId = incoming.Batch.Id; });
                    lastIncoming = new(incoming.Batch.Id, "saved", incoming.Completed.Sum(f => f!.Size), incoming.Completed.Sum(f => f!.Size));
                    ClearIncoming(); return new(true, "");
                default: throw new InvalidDataException("Unknown file command.");
            }
        }
        finally { gate.Release(); }
    }
    public async Task<Box> Request(Box body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(body.Data) || body.Data.Length > FileProtocol.MaxEnvelopeBytes) throw new InvalidDataException("File request is too large.");
        var config = vault.Read();
        var request = Bridge.Decode<FileRequest>(Bridge.Open(body.Data, config.Key));
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!FileProtocol.IsId(request.Id) || !FileProtocol.IsId(request.Nonce) || request.ExpiresAt <= now || request.ExpiresAt > now + 120_000 + FileProtocol.ClockSkewMilliseconds)
            throw new InvalidDataException("Invalid or expired file request.");
        await gate.WaitAsync(ct);
        try
        {
            var current = vault.Read();
            if (config.Key != current.Key) throw new InvalidDataException("Pairing changed during file transfer.");
            config = current;
            if (config.PhonePaused || request.ReceiveGeneration != config.PhoneGeneration)
                throw new InvalidDataException(UiText.Get("phone_paused"));
            FileResponse response;
            try { response = await Handle(request, ct); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
            { response = new(request.Id, request.Nonce, "error", Detail: e.Message); }
            return new(Bridge.Seal(Bridge.Encode(response), config.Key));
        }
        finally { gate.Release(); }
    }
    async Task<FileResponse> Handle(FileRequest r, CancellationToken ct)
    {
        FileResponse Reply(string state, long offset = 0, string? data = null, FileBatch? batch = null) => new(r.Id, r.Nonce, state, offset, data, batch);
        if (r.Op is "offer" or "read" or "saved" or "reject")
        {
            if (outgoing?.Batch.Id != r.Id || outgoing.Batch.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                if (r.Op == "saved" && lastOutgoing?.Id == r.Id && lastOutgoing.Status == "saved") return Reply("saved");
                throw new InvalidDataException("File offer expired or was cancelled. Send the files again.");
            }
            if (r.Op == "offer") return Reply("ready", batch: outgoing.Batch);
            if (r.Op == "saved")
            {
                if (outgoing.ReadOffsets.Where((n, i) => n != outgoing.Batch.Files[i].Size).Any()) throw new InvalidDataException("File download is incomplete.");
                EndOutgoing("saved", null); return Reply("saved");
            }
            if (r.Op == "reject") { EndOutgoing("error", r.Data); return Reply("rejected"); }
            if (r.Index < 0 || r.Index >= outgoing.Streams.Length) throw new InvalidDataException("Invalid file index.");
            var stream = outgoing.Streams[r.Index];
            if (r.Offset < 0 || r.Offset > stream.Length || r.Offset > outgoing.ReadOffsets[r.Index]) throw new InvalidDataException("Invalid file offset.");
            stream.Position = r.Offset;
            byte[] data = new byte[(int)Math.Min(FileProtocol.ChunkBytes, stream.Length - r.Offset)];
            await stream.ReadExactlyAsync(data, ct);
            outgoing.ReadOffsets[r.Index] = Math.Max(outgoing.ReadOffsets[r.Index], r.Offset + data.Length);
            return Reply("chunk", r.Offset + data.Length, Convert.ToBase64String(data));
        }
        var cfg = vault.Read();
        if (r.Id == cfg.FileIncomingId) return Reply("saved");
        if (r.Op == "start")
        {
            var batch = r.Batch ?? throw new InvalidDataException("Missing file manifest."); FileProtocol.Validate(batch);
            if (batch.Id != r.Id || batch.ReceiveGeneration != r.ReceiveGeneration || batch.Sequence <= cfg.FileIncomingSequence)
                throw new InvalidDataException("Old or invalid file transfer.");
            if (incoming is not null && incoming.Batch.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ClearIncoming();
            if (incoming is not null)
            {
                if (!Bridge.Encode(incoming.Batch).SequenceEqual(Bridge.Encode(batch))) throw new InvalidDataException(UiText.Get("files_busy"));
                return Reply(incoming.Ready ? "saving" : "ready");
            }
            long size = batch.Files.Sum(f => Math.Max(0, f.Size));
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < size + Math.Min(size, 64 * 1024 * 1024)) throw new IOException("Not enough space on the receiving computer.");
            Directory.CreateDirectory(BatchDirectory(batch.Id)); incoming = new(batch, new SavedFile?[batch.Files.Length]); Persist();
            return Reply("ready");
        }
        if (incoming?.Batch.Id != r.Id || incoming.Batch.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new InvalidDataException("File transfer expired. Send the files again.");
        if (r.Op == "abort") { ClearIncoming(); return Reply("cancelled"); }
        if (r.Op == "result") return Reply(incoming.Ready ? "saving" : "ready");
        if (r.Op == "commit")
        {
            if (incoming.Completed.Any(f => f is null)) throw new InvalidDataException("Some files have not finished uploading.");
            incoming = incoming with { Ready = true }; Persist(); return Reply("saving");
        }
        if (incoming.Ready || r.Index < 0 || r.Index >= incoming.Batch.Files.Length) throw new InvalidDataException("Invalid file index or transfer state.");
        string path = PartPath(r.Id, r.Index);
        if (r.Op == "write")
        {
            if (r.Data is null || r.Data.Length > (FileProtocol.ChunkBytes + 2) / 3 * 4) throw new InvalidDataException("File chunk is too large.");
            byte[] bytes = Convert.FromBase64String(r.Data);
            if (bytes.Length is < 1 or > FileProtocol.ChunkBytes || r.Offset < 0) throw new InvalidDataException("Invalid file chunk.");
            long declared = incoming.Batch.Files[r.Index].Size;
            if (r.Offset > long.MaxValue - bytes.Length || declared >= 0 && r.Offset + bytes.Length > declared || incoming.Completed[r.Index] is not null)
                throw new InvalidDataException("File exceeds its declared size.");
            using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            if (r.Offset < file.Length)
            {
                if (r.Offset + bytes.Length > file.Length) throw new InvalidDataException("Overlapping file chunk.");
                file.Position = r.Offset; byte[] previous = new byte[bytes.Length]; await file.ReadExactlyAsync(previous, ct);
                if (!previous.SequenceEqual(bytes)) throw new InvalidDataException("A retried chunk has different content.");
            }
            else
            {
                if (r.Offset != file.Length) throw new InvalidDataException("File chunk is out of order.");
                file.Position = r.Offset; await file.WriteAsync(bytes, ct); file.Flush(true);
            }
            return Reply("chunk", r.Offset + bytes.Length);
        }
        if (r.Op == "finish")
        {
            if (incoming.Completed[r.Index] is SavedFile completed)
            {
                if (completed.Size != r.Size || completed.Sha256 != r.Sha256) throw new InvalidDataException("File completion changed.");
                return Reply("complete", completed.Size);
            }
            using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long declared = incoming.Batch.Files[r.Index].Size;
            if (file.Length != r.Size || declared >= 0 && r.Size != declared) throw new InvalidDataException("File size does not match its manifest.");
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
            if (hash != r.Sha256) throw new InvalidDataException("File integrity verification failed.");
            incoming.Completed[r.Index] = new(incoming.Batch.Files[r.Index].Name, path, r.Size, hash); Persist();
            return Reply("complete", r.Size);
        }
        throw new InvalidDataException("Unknown file operation.");
    }
    public override void Dispose() { outgoing?.Close(); gate.Dispose(); base.Dispose(); }
}
