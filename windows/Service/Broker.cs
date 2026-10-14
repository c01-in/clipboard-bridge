using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipboardBridge;

public sealed class Broker(Vault vault, FcmSender sender) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string pendingPath = Path.Combine(vault.DataDir, "pending.json");
    private string status = "等待配置 Firebase 和配对手机";
    private Delivery? pending;
    private PendingIncoming? incoming;
    private sealed record PendingIncoming(UploadManifest Manifest, byte[] Bytes, long Deadline, TaskCompletionSource<Receipt> Completion);
    public string Status => status;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = vault.Read();
        if (initial.ServiceAccountJson is not null && initial.DeviceToken is not null) status = "已配置，等待复制内容";
        if (File.Exists(pendingPath))
            try { pending = Bridge.Decode<Delivery>(File.ReadAllBytes(pendingPath)); } catch (Exception e) when (e is JsonException or IOException) { status = "上次待发送记录损坏，请重新复制内容"; }
        await Task.WhenAll(PipeLoop(stoppingToken), RetryLoop(stoppingToken));
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
            using var pipe = NamedPipeServerStreamAcl.Create(Bridge.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var command = Bridge.Decode<Command>(await Bridge.ReadFrame(pipe, timeout.Token));
                Reply reply;
                try { reply = await Handle(command, timeout.Token); }
                catch (Exception e) when (e is not OperationCanceledException) { reply = new Reply(false, e.Message); }
                await Bridge.WriteFrame(pipe, Bridge.Encode(reply), timeout.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or JsonException) { if (ct.IsCancellationRequested) break; }
        }
    }
    public async Task<Reply> Handle(Command c, CancellationToken ct)
    {
        switch (c.Op)
        {
            case "status": return new Reply(true, status, JsonSerializer.Serialize(new { device = vault.Read().DeviceName, configured = vault.Read().ServiceAccountJson is not null, endpoints = vault.Read().Endpoints }, Bridge.Json));
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
            case "account":
                using (var a = JsonDocument.Parse(c.Value ?? ""))
                {
                    if (a.RootElement.GetProperty("type").GetString() != "service_account") throw new InvalidDataException("请选择服务账号 JSON 凭据");
                    using var rsa = RSA.Create(); rsa.ImportFromPem(a.RootElement.GetProperty("private_key").GetString()!);
                    var project = a.RootElement.GetProperty("project_id").GetString();
                    var android = vault.Read().FirebaseAndroidJson;
                    if (android is not null && JsonDocument.Parse(android).RootElement.GetProperty("project_info").GetProperty("project_id").GetString() != project) throw new InvalidDataException("Android 配置与发送凭据必须属于同一 Firebase 项目");
                }
                vault.Update(x => x.ServiceAccountJson = c.Value); return new Reply(true, "发送凭据已加密保存");
            case "android-config":
                using (var a = JsonDocument.Parse(c.Value ?? "")) { _ = a.RootElement.GetProperty("project_info").GetProperty("project_number"); _ = a.RootElement.GetProperty("client"); }
                vault.Update(x => x.FirebaseAndroidJson = c.Value); return new Reply(true, "Android Firebase 配置已保存");
            case "endpoints":
                var urls = (c.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (urls.Length == 0 || urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)) throw new InvalidDataException("请输入 http://电脑IP:45871；多个地址用逗号分隔");
                vault.Update(x => x.Endpoints = urls.Select(u => u.TrimEnd('/')).ToArray()); return new Reply(true, "地址已保存，地址变化后请重新配对手机");
            case "pair":
                var config = vault.Read();
                if (config.FirebaseAndroidJson is null) throw new InvalidOperationException("请先导入 google-services.json");
                using (var firebase = JsonDocument.Parse(config.FirebaseAndroidJson))
                {
                    var root = firebase.RootElement;
                    var client = root.GetProperty("client").EnumerateArray().First(x => x.GetProperty("client_info").GetProperty("android_client_info").GetProperty("package_name").GetString() == "com.colin.clipbridge");
                    var minimal = new { project_info = root.GetProperty("project_info"), client = new[] { new { client_info = client.GetProperty("client_info"), api_key = client.GetProperty("api_key") } } };
                    return new Reply(true, "配对链接包含设备密钥，请仅提供给自己的手机", "clipbridge://pair?data=" + Uri.EscapeDataString(Convert.ToBase64String(Bridge.Encode(new { endpoints = config.Endpoints, key = config.Key, firebase = minimal }))));
                }
            case "send":
                await gate.WaitAsync(ct);
                try
                {
                    var cfg = vault.Read();
                    if (cfg.ServiceAccountJson is null || cfg.DeviceToken is null) throw new InvalidOperationException("尚未完成 Firebase 配置或手机配对");
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
                    var manifest = new ClipManifest(id, seq, c.Mime, expiry, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, inline, file is null ? null : id);
                    var next = new Delivery(id, seq, expiry, Bridge.Seal(Bridge.Encode(manifest), cfg.Key), file);
                    ClearPending(); pending = next; SavePending();
                    try { await Submit(ct); return new Reply(true, status); }
                    catch (Exception e) when (e is not OperationCanceledException) { status = "已暂存，等待重试：" + e.Message; return new Reply(false, status); }
                }
                finally { gate.Release(); }
            default: throw new InvalidOperationException("未知操作");
        }
    }
    private async Task Submit(CancellationToken ct)
    {
        if (pending is null) return;
        await sender.Send(vault.Read(), pending.Envelope, ct);
        pending = pending with { Submitted = true }; SavePending();
        status = "已提交 FCM，等待手机确认写入剪贴板";
    }
    public async Task<Receipt> ReceiveFromAndroid(UploadManifest manifest, byte[] bytes, CancellationToken ct)
    {
        PendingIncoming next;
        await gate.WaitAsync(ct);
        try
        {
            var cfg = vault.Read();
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
            status = receipt.Status == "applied" ? "手机已确认：剪贴板已写入（" + DateTime.Now.ToString("HH:mm:ss") + "）" : "手机接收失败：" + receipt.Detail;
            if (receipt.Status == "applied") ClearPending();
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
