using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardBridge;

public sealed class Broker(Vault vault, WebPushSender sender, string? pipeName = null, PairingHub? pairing = null, FileTransfers? files = null) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string pendingPath = Path.Combine(vault.DataDir, "pending.json");
    private string status = "等待配对手机";
    private Delivery? pending;
    private PendingIncoming? incoming;
    private sealed record PendingIncoming(UploadManifest Manifest, byte[] Bytes, long Deadline, TaskCompletionSource<Receipt> Completion);
    public string Status => status;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = vault.Read();
        if (initial.Subscription is not null) status = initial.PhonePaused ? UiText.Get("phone_paused") : "已配置，等待复制内容";
        if (File.Exists(pendingPath))
            try { pending = Bridge.Decode<Delivery>(File.ReadAllBytes(pendingPath)); } catch (Exception e) when (e is JsonException or IOException) { status = "上次待发送记录损坏，请重新复制内容"; }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var pipe = PipeLoop(lifetime.Token);
        var retry = RetryLoop(lifetime.Token);
        // A failed IPC listener must not leave a healthy-looking HTTP-only service running forever.
        await Task.WhenAny(pipe, retry);
        lifetime.Cancel();
        await Task.WhenAll(pipe, retry);
    }
    private async Task RetryLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await gate.WaitAsync(ct);
            try
            {
                if (pending is null) continue;
                if (pending.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { ClearPending(); status = "同步内容已过期，请重新复制；手机未确认接收"; continue; }
                if (vault.Read().PhonePaused) { ClearPending(); status = UiText.Get("phone_paused"); continue; }
                if (!pending.Submitted) await Submit(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException) { status = e.Message; }
            finally { gate.Release(); }
        }
    }
    private async Task PipeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            var owner = vault.Read().OwnerSid;
            if (owner.Length > 0) security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(owner), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            using var pipe = NamedPipeServerStreamAcl.Create(pipeName ?? Bridge.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var command = Bridge.Decode<Command>(await Bridge.ReadFrame(pipe, timeout.Token));
                Reply reply;
                try
                {
                    if (command.Op == "files-send" && files is not null)
                    {
                        var paths = Bridge.Decode<string[]>(System.Text.Encoding.UTF8.GetBytes(command.Value ?? "[]"));
                        FileStream[] streams = [];
                        pipe.RunAsClient(() => streams = FileTransfers.OpenFiles(paths));
                        reply = await files.Send(streams, timeout.Token);
                    }
                    else reply = await Handle(command, timeout.Token);
                }
                catch (Exception e) when (e is not OperationCanceledException) { reply = new Reply(false, e.Message); }
                await Bridge.WriteFrame(pipe, Bridge.Encode(reply), timeout.Token);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException or JsonException) { if (ct.IsCancellationRequested) break; }
        }
    }
    public async Task<Reply> Handle(Command c, CancellationToken ct)
    {
        if (c.Op.StartsWith("files-", StringComparison.Ordinal) && files is not null) return await files.Local(c, ct);
        switch (c.Op)
        {
            case "status":
                var state = vault.Read();
                return new Reply(true, state.PhonePaused ? UiText.Get("phone_paused") : status, JsonSerializer.Serialize(new { device = state.DeviceName, configured = state.Subscription is not null, phonePaused = state.PhonePaused, endpoints = state.Endpoints }, Bridge.Json));
            case "pairing-open": return PairReply(pairing?.Open());
            case "pairing-status": return PairReply(pairing?.State());
            case "pairing-approve": return PairReply(pairing?.Approve(c.Value ?? "", true));
            case "pairing-reject": return PairReply(pairing?.Approve(c.Value ?? "", false));
            case "receive":
                await gate.WaitAsync(ct);
                try
                {
                    if (incoming is null || incoming.Deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return new Reply(true, "没有手机待发送内容");
                    var clip = new IncomingClip(incoming.Manifest with { ExpiresAt = incoming.Deadline }, Convert.ToBase64String(incoming.Bytes));
                    return new Reply(true, "手机内容等待写入", JsonSerializer.Serialize(clip, Bridge.Json));
                }
                finally { gate.Release(); }
            case "receive-ack":
                var receipt = Bridge.Decode<Receipt>(System.Text.Encoding.UTF8.GetBytes(c.Value ?? ""));
                await gate.WaitAsync(ct);
                try
                {
                    if (incoming is null || incoming.Manifest.Id != receipt.Id || incoming.Manifest.Sequence != receipt.Sequence)
                        return new Reply(false, "该手机发送请求已结束");
                    if (receipt.Status is not ("applied" or "error")) throw new InvalidDataException("写入回执格式无效");
                    if (receipt.Status == "applied")
                    {
                        vault.Update(x => { x.IncomingSequence = receipt.Sequence; x.IncomingId = receipt.Id; });
                        ClearPending();
                    }
                    status = receipt.Status == "applied" ? "手机内容已写入 Windows 剪贴板" : "手机发送失败：" + receipt.Detail;
                    incoming.Completion.TrySetResult(receipt); incoming = null;
                    return new Reply(true, status);
                }
                finally { gate.Release(); }
            case "endpoints":
                var urls = (c.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (urls.Length == 0 || urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)) throw new InvalidDataException("请输入 http://电脑IP:45871；多个地址用逗号分隔");
                vault.Update(x => x.Endpoints = urls.Select(u => u.TrimEnd('/')).ToArray()); return new Reply(true, "备用地址已保存，新配对时会自动传给手机");
            case "send":
                await gate.WaitAsync(ct);
                try
                {
                    var cfg = vault.Read();
                    if (cfg.Subscription is null) throw new InvalidOperationException(UiText.Get("push_not_registered"));
                    if (cfg.PhonePaused) return new Reply(false, UiText.Get("phone_paused"));
                    var bytes = Convert.FromBase64String(c.Data ?? "");
                    if (bytes.Length == 0 || bytes.Length > Bridge.MaxBytes) throw new InvalidDataException("仅支持 1 字节至 16 MiB 的内容");
                    if (c.Mime != "text/plain" && c.Mime != "image/png") throw new InvalidDataException("仅支持文字和单张 PNG 图片");
                    if (c.Mime == "image/png" && (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))) throw new InvalidDataException("PNG 文件格式无效");
                    string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    long seq = 0, expiry = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
                    vault.Update(c => { c.OutgoingSequence = Math.Max(DateTime.UtcNow.Ticks, c.OutgoingSequence + 1); seq = c.OutgoingSequence; });
                    string? inline = bytes.Length <= 900 && c.Mime == "text/plain" ? Convert.ToBase64String(bytes) : null;
                    string? file = inline is null ? id + ".blob" : null;
                    if (file is not null) File.WriteAllText(Path.Combine(vault.DataDir, file), Bridge.Seal(bytes, cfg.Key));
                    var manifest = new ClipManifest(id, seq, c.Mime, expiry, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, inline, file is null ? null : id, cfg.PhoneGeneration);
                    var next = new Delivery(id, seq, expiry, Bridge.Seal(Bridge.Encode(manifest), cfg.Key), file);
                    ClearPending(); pending = next; SavePending();
                    try { await Submit(ct); return new Reply(true, status); }
                    catch (Exception e) when (e is not OperationCanceledException) { status = "已暂存，等待重试：" + e.Message; return new Reply(false, status); }
                }
                finally { gate.Release(); }
            default: throw new InvalidOperationException("未知操作");
        }
    }
    static Reply PairReply(PairState? state) => state is null ? new(false, "自动配对服务未启用") : new(true, "自动配对状态", JsonSerializer.Serialize(state, Bridge.Json));
    public async Task<Box?> Register(Box body, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            // Share the pairing-commit gate: an old registration must not overwrite a newly paired token.
            string key = vault.Read().Key;
            var device = Bridge.Decode<Device>(Bridge.Open(body.Data, key));
            PairingHub.ValidateDevice(device);
            var old = vault.Read();
            if (device.Revision < old.PhoneRevision || device.Revision == old.PhoneRevision &&
                (device.ReceiveGeneration != old.PhoneGeneration || device.Paused != old.PhonePaused))
                throw new InvalidDataException("手机同步状态已更新，请重试注册");
            if (device.ReceiveGeneration != old.PhoneGeneration || device.Paused)
            {
                ClearPending();
                if (incoming is not null) incoming.Completion.TrySetResult(new Receipt(incoming.Manifest.Id, incoming.Manifest.Sequence, "error", UiText.Get("phone_paused")));
                incoming = null;
            }
            SaveDevice(device);
            status = device.Paused ? UiText.Get("phone_paused") : "已配置，等待复制内容";
            return device.Challenge is null ? null : new Box(Bridge.Seal(Bridge.Encode(new { registered = true, challenge = device.Challenge, receiveGeneration = device.ReceiveGeneration, revision = device.Revision, paused = device.Paused }), key));
        }
        finally { gate.Release(); }
    }
    public async Task<Box> CompletePairing(string id, Box body, System.Net.IPAddress address, CancellationToken ct)
    {
        if (pairing is null) throw new InvalidOperationException("自动配对服务未启用");
        await gate.WaitAsync(ct);
        try
        {
            return pairing.Complete(id, body, address, (key, device) =>
            {
                vault.Update(c => { c.Key = key; c.IncomingSequence = 0; c.IncomingId = null; c.FileIncomingSequence = 0; c.FileIncomingId = null; ApplyDevice(c, device); });
                if (incoming is not null) incoming.Completion.TrySetResult(new Receipt(incoming.Manifest.Id, incoming.Manifest.Sequence, "error", "手机配对已更换"));
                incoming = null;
                ClearPending(); status = "已安全配对，等待复制内容";
            });
        }
        finally { gate.Release(); }
    }
    private void SaveDevice(Device device) => vault.Update(c => ApplyDevice(c, device));
    private static void ApplyDevice(Configuration c, Device device)
    {
        c.Subscription = device.Subscription; c.DeviceName = device.DeviceName;
        c.PhoneGeneration = device.ReceiveGeneration; c.PhoneRevision = device.Revision; c.PhonePaused = device.Paused;
    }
    private async Task Submit(CancellationToken ct)
    {
        if (pending is null) return;
        await sender.Send(vault.Read(), pending.Envelope, ct);
        pending = pending with { Submitted = true }; SavePending();
        status = "已提交 Web Push，等待手机确认写入剪贴板";
    }
    public async Task<Receipt> ReceiveFromAndroid(UploadManifest manifest, byte[] bytes, CancellationToken ct)
    {
        PendingIncoming next;
        await gate.WaitAsync(ct);
        try
        {
            var cfg = vault.Read();
            if (cfg.PhonePaused || cfg.PhoneGeneration.Length > 0 && manifest.ReceiveGeneration != cfg.PhoneGeneration)
                return new Receipt(manifest.Id, manifest.Sequence, "error", UiText.Get("phone_paused"));
            if (manifest.Sequence == cfg.IncomingSequence && manifest.Id == cfg.IncomingId)
                return new Receipt(manifest.Id, manifest.Sequence, "applied", null);
            if (manifest.Sequence <= cfg.IncomingSequence)
                return new Receipt(manifest.Id, manifest.Sequence, "error", "这是旧的发送请求，请重新点击发送");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (incoming is not null && incoming.Deadline <= now)
            {
                incoming.Completion.TrySetResult(new Receipt(incoming.Manifest.Id, incoming.Manifest.Sequence, "error", "Windows 未及时确认写入，请检查托盘程序"));
                incoming = null;
            }
            if (incoming is not null)
            {
                if (incoming.Manifest != manifest)
                    return new Receipt(manifest.Id, manifest.Sequence, "error", "电脑正在接收上一条内容，请稍后再试");
                next = incoming;
            }
            else
            {
                next = new PendingIncoming(manifest, bytes, Math.Min(manifest.ExpiresAt, now + 20_000), new(TaskCreationOptions.RunContinuationsAsynchronously));
                incoming = next;
            }
        }
        finally { gate.Release(); }
        try
        {
            var remaining = TimeSpan.FromMilliseconds(Math.Max(1, next.Deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            return await next.Completion.Task.WaitAsync(remaining, ct);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                if (next.Completion.Task.IsCompletedSuccessfully) return next.Completion.Task.Result;
                if (incoming == next) incoming = null;
                var failure = new Receipt(manifest.Id, manifest.Sequence, "error", "未收到 Windows 写入确认，请检查电脑托盘程序后重新发送");
                next.Completion.TrySetResult(failure);
                return failure;
            }
            finally { gate.Release(); }
        }
    }
    public async Task<string?> Blob(string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return pending is not null && pending.Id == id && pending.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() && pending.BlobFile is not null ? File.ReadAllText(Path.Combine(vault.DataDir, pending.BlobFile)) : null; }
        finally { gate.Release(); }
    }
    public async Task AcceptReceipt(Receipt receipt, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (pending?.Id != receipt.Id || pending.Sequence != receipt.Sequence) return;
            status = receipt.Status == "applied" ? "手机已确认：剪贴板已写入（" + DateTime.Now.ToString("HH:mm:ss") + "）" : receipt.Status == "discarded" ? UiText.Get("sync_discarded") : "手机接收失败：" + receipt.Detail;
            if (receipt.Status is "applied" or "discarded") ClearPending();
        }
        finally { gate.Release(); }
    }
    private void SavePending() { File.WriteAllBytes(pendingPath + ".tmp", Bridge.Encode(pending)); File.Move(pendingPath + ".tmp", pendingPath, true); }
    private void ClearPending()
    {
        if (pending?.BlobFile is not null) File.Delete(Path.Combine(vault.DataDir, pending.BlobFile));
        pending = null; File.Delete(pendingPath);
    }
}
