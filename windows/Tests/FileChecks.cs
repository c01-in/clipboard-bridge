using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ClipboardBridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class FileChecks
{
    sealed class PushPeer : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Count++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)); }
    }
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("clipboard-files-checks-");
        try
        {
            string source = Path.Combine(directory.FullName, "large file 中文.bin");
            byte[] buffer = RandomNumberGenerator.GetBytes(FileProtocol.ChunkBytes);
            using (var output = File.Create(source)) for (int i = 0; i < 81; i++) output.Write(buffer);
            long size = new FileInfo(source).Length; string hash = FileProtocol.HashFile(source);
            var vault = new Vault(Path.Combine(directory.FullName, "vault"));
            string generation = FileProtocol.NewId();
            vault.Update(c => { c.PhoneGeneration = generation; c.OwnerSid = WindowsIdentity.GetCurrent().User!.Value; c.Subscription = WebPushChecks.Subscription(); });
            string key = vault.Read().Key;
            using var push = new PushPeer(); using var pushHttp = new HttpClient(push);
            var sender = new WebPushSender(pushHttp);
            using var transfers = new FileTransfers(vault, sender);
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024);
            builder.Services.AddSingleton(transfers);
            await using var app = builder.Build(); FileTransfers.Map(app); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            long expiry = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
            FileRequest Request(string op, string id, int index = 0, long offset = 0, string? data = null, FileBatch? batch = null, long length = 0, string? sha = null) =>
                new(op, id, FileProtocol.NewId(), DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), generation, index, offset, data, length, sha, batch);
            async Task<FileResponse> Call(FileRequest request)
            {
                using var response = await client.PostAsJsonAsync("/api/files", new Box(Bridge.Seal(Bridge.Encode(request), key)), Bridge.Json);
                response.EnsureSuccessStatusCode();
                var box = (await response.Content.ReadFromJsonAsync<Box>(Bridge.Json))!;
                var opened = Bridge.Decode<FileResponse>(Bridge.Open(box.Data, key));
                if (opened.Nonce != request.Nonce || opened.Id != request.Id) throw new Exception("File response is not bound to its request");
                return opened;
            }
            string id = FileProtocol.NewId();
            var batch = new FileBatch(id, 1, DateTimeOffset.UtcNow.AddHours(1).AddSeconds(30).ToUnixTimeMilliseconds(), generation,
                [new("photo.png", "image/png", size), new("photo.png", "image/png", 0), new("../../CON.txt", "application/octet-stream", -1)]);
            check((await Call(Request("start", id, batch: batch) with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(150).ToUnixTimeMilliseconds() })).Status == "ready", "Real HTTP accepts a multi-file batch from an authenticated phone with a clock 30 seconds ahead");
            check((await transfers.Local(new("files-incoming"), default)).Value is null, "Files are not offered to the tray before a complete batch");
            using (var input = File.OpenRead(source))
            {
                long offset = 0; int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                {
                    var chunk = Request("write", id, offset: offset, data: Convert.ToBase64String(buffer, 0, count));
                    var result = await Call(chunk);
                    if (result.Status != "chunk" || result.Offset != offset + count) throw new Exception(result.Detail ?? "Wrong chunk offset");
                    if (offset == 0)
                    {
                        check((await Call(chunk)).Offset == count, "A lost-response chunk retry is idempotent");
                        check((await Call(chunk with { Data = Convert.ToBase64String(new byte[count]) })).Status == "error", "An overlapping retry with changed bytes is rejected");
                        check((await Call(chunk with { Offset = count + 1 })).Status == "error", "Out-of-order chunks cannot create sparse files");
                    }
                    offset += count;
                }
            }
            check((await Call(Request("finish", id, length: size, sha: new string('0', 64)))).Status == "error", "The full file hash is checked before acknowledging upload completion");
            check((await Call(Request("finish", id, length: size, sha: hash))).Status == "complete", "A file larger than 16 MiB transfers with bounded chunks and exact SHA-256");
            check((await Call(Request("commit", id))).Status == "error", "An incomplete multi-file batch cannot report success");
            string emptyHash = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
            check((await Call(Request("finish", id, index: 1, length: 0, sha: emptyHash))).Status == "complete", "Zero-byte attachments are supported");
            await Call(Request("write", id, index: 2, data: Convert.ToBase64String(buffer)));
            await Call(Request("finish", id, index: 2, length: buffer.Length, sha: Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant()));
            check((await Call(Request("commit", id))).Status == "saving" && (await Call(Request("result", id))).Status == "saving", "Android waits for actual desktop saving, not just successful HTTP upload");
            var ready = await transfers.Local(new("files-incoming"), default);
            var inbox = Bridge.Decode<FileInboxBatch>(Encoding.UTF8.GetBytes(ready.Value!));
            string destination = Path.Combine(directory.FullName, "downloads"); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "photo.png"), "existing file");
            string journal = Path.Combine(directory.FullName, "receipts");
            string[] saved = FileInbox.Save(inbox, destination, journal);
            check(saved.Length == 3 && saved.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3 && File.ReadAllText(Path.Combine(destination, "photo.png")) == "existing file",
                "Duplicate filenames are preserved separately without overwriting existing downloads");
            check(saved.All(p => Path.GetDirectoryName(p) == destination) && Path.GetFileName(saved[2]).Contains("_"), "Malicious path names cannot escape the selected receiving folder");
            check(FileProtocol.HashFile(saved[0]) == hash && new FileInfo(saved[1]).Length == 0, "Windows saves original bytes, including empty files");
            check(FileInbox.Save(inbox, destination, journal).SequenceEqual(saved) && Directory.GetFiles(destination).Length == 4,
                "Tray restart/retry reuses its persistent save plan without duplicate downloads");
            var longName = new FileInboxBatch(FileProtocol.NewId(), [new(new string('a', 236) + ".bin", source, size, hash)]);
            string[] longSaved = FileInbox.Save(longName, Path.Combine(directory.FullName, "long-names"), journal);
            check(Path.GetFileName(longSaved[0]).Length == 240 && FileProtocol.HashFile(longSaved[0]) == hash, "Long source names do not overflow the temporary filename component");
            // Reload persisted transfer metadata before the user-session acknowledgment.
            using (var restored = new FileTransfers(vault, sender))
            {
                check((await restored.Local(new("files-incoming"), default)).Value is not null, "Ready incoming files survive a service restart");
                await restored.Local(new("files-incoming-ack", Value: id), default);
                var response = Bridge.Decode<FileResponse>(Bridge.Open((await restored.Request(new(Bridge.Seal(Bridge.Encode(Request("result", id)), key)), default)).Data, key));
                check(response.Status == "saved" && vault.Read().FileIncomingSequence == 1, "Only the desktop save acknowledgment creates a durable success receipt");
            }
            using var unauthorized = await client.PostAsJsonAsync("/api/files", new Box(Bridge.Seal(Bridge.Encode(Request("result", id)), Convert.ToBase64String(new byte[32]))));
            check(unauthorized.StatusCode == HttpStatusCode.BadRequest, "Unpaired callers cannot read file-transfer state");
            using var expired = await client.PostAsJsonAsync("/api/files", new Box(Bridge.Seal(Bridge.Encode(Request("result", id) with { ExpiresAt = 1 }), key)));
            check(expired.StatusCode == HttpStatusCode.BadRequest, "Expired file requests are rejected");
            using var tooFarAhead = await client.PostAsJsonAsync("/api/files", new Box(Bridge.Seal(Bridge.Encode(Request("result", id) with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(4).ToUnixTimeMilliseconds() }), key)));
            check(tooFarAhead.StatusCode == HttpStatusCode.BadRequest, "Clock-skew tolerance does not permit unbounded future request lifetimes");
            bool farFutureBatchRejected = false;
            try { FileProtocol.Validate(batch with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds() }); }
            catch (InvalidDataException) { farFutureBatchRejected = true; }
            check(farFutureBatchRejected, "Clock-skew tolerance retains the file manifest lifetime limit");
            using var oversized = await client.PostAsync("/api/files", new StringContent("{\"data\":\"" + new string('a', FileProtocol.MaxEnvelopeBytes + 2048) + "\"}", Encoding.UTF8, "application/json"));
            check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "The real file endpoint rejects oversized HTTP bodies before unbounded allocation");

            // Exercise Explorer's path list through the actual impersonated named-pipe branch.
            string pipeName = "ClipboardBridge-Files-" + FileProtocol.NewId();
            using var broker = new Broker(vault, sender, pipeName, files: transfers); await broker.StartAsync(default);
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
                await pipe.ConnectAsync(2000);
                await Bridge.WriteFrame(pipe, Bridge.Encode(new Command("files-send", Value: JsonSerializer.Serialize(new[] { source, saved[1] }))));
                var sent = Bridge.Decode<Reply>(await Bridge.ReadFrame(pipe));
                check(sent.Ok && FileProtocol.IsId(sent.Value) && push.Count == 1, "A multi-selection with spaces and Unicode reaches the real file sender through authenticated user IPC");
                string outgoingId = sent.Value!;
                var offer = await Call(Request("offer", outgoingId));
                check(offer.Batch?.Files.Length == 2 && offer.Batch.Files[0].Size == size, "Windows offers all selected files with 64-bit sizes");
                check((await Call(Request("saved", outgoingId))).Status == "error", "The phone cannot acknowledge files that have not been downloaded");
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long offset = 0;
                while (offset < size)
                {
                    var chunk = await Call(Request("read", outgoingId, offset: offset));
                    byte[] content = Convert.FromBase64String(chunk.Data!); digest.AppendData(content); offset += content.Length;
                    if (content.Length is < 1 or > FileProtocol.ChunkBytes || offset != chunk.Offset) throw new Exception("Invalid download chunk");
                }
                check(Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant() == hash, "Windows-to-phone multi-file download preserves a >16 MiB file exactly");
                check((await Call(Request("saved", outgoingId))).Status == "saved", "A completed phone save produces a file-specific desktop receipt");
                check((await Call(Request("saved", outgoingId))).Status == "saved", "A lost final download receipt is safe to retry");
            }
            finally { await broker.StopAsync(default); }
            check(FileProtocol.SafeName("NUL.txt") == "_NUL.txt" && FileProtocol.SafeName("COM1") == "_COM1" && FileProtocol.SafeName("a?.png") == "a_.png" && FileProtocol.SafeName("COM¹.txt") == "_COM¹.txt" && FileProtocol.SafeName("CON .txt") == "_CON .txt", "Windows reserved device names and invalid characters are handled safely");
        }
        finally
        {
            if (directory.Parent!.FullName != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) || !directory.Name.StartsWith("clipboard-files-checks-", StringComparison.Ordinal)) throw new IOException("Unexpected test directory");
            directory.Delete(true);
        }
    }
}
