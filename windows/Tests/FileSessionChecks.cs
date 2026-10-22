using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardBridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class FileSessionChecks
{
    sealed class Clock : TimeProvider
    {
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        long ticks;
        public override DateTimeOffset GetUtcNow() => utc;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan elapsed) { ticks += elapsed.Ticks; utc += elapsed; }
        public void JumpWall(TimeSpan change) => utc += change;
    }
    sealed class PushPeer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
    }
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("clipboard-file-sessions-");
        try
        {
            var clock = new Clock(); var vault = new Vault(Path.Combine(directory.FullName, "vault"));
            string generation = FileProtocol.NewId();
            vault.Update(c => { c.PhoneGeneration = generation; c.Subscription = WebPushChecks.Subscription(); });
            string key = vault.Read().Key;
            using var push = new PushPeer(); using var pushHttp = new HttpClient(push);
            var sender = new WebPushSender(pushHttp);
            using var transfers = new FileTransfers(vault, sender, clock);
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(transfers);
            await using var app = builder.Build(); FileTransfers.Map(app); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            // Match Android JSONObject: expiry, index, offset and absent optional fields are omitted.
            Dictionary<string, object?> Wire(string op, string id, string? session = null) => new()
            { ["op"] = op, ["id"] = id, ["nonce"] = FileProtocol.NewId(), ["receiveGeneration"] = generation, ["session"] = session };
            async Task<HttpResponseMessage> Post(Dictionary<string, object?> request, string? sealingKey = null) =>
                await client.PostAsJsonAsync("/api/files", new Box(Bridge.Seal(Bridge.Encode(request), sealingKey ?? key)), Bridge.Json);
            async Task<FileResponse> Call(Dictionary<string, object?> request)
            {
                using var response = await Post(request); response.EnsureSuccessStatusCode();
                var box = (await response.Content.ReadFromJsonAsync<Box>(Bridge.Json))!;
                var opened = Bridge.Decode<FileResponse>(Bridge.Open(box.Data, key));
                if (opened.Id != (string)request["id"]! || opened.Nonce != (string)request["nonce"]!) throw new Exception("Session response not bound to its request");
                return opened;
            }
            async Task<bool> Rejected(Dictionary<string, object?> request, string? sealingKey = null)
            { using var response = await Post(request, sealingKey); return response.StatusCode == HttpStatusCode.BadRequest; }
            string id = FileProtocol.NewId();
            var issued = await Call(Wire("session", id)); string token = issued.Session!;
            check(issued.Status == "session" && FileProtocol.IsId(token) && issued.LifetimeMs == 3_600_000, "PC issues an authenticated transfer-bound one-hour session without any phone timestamp");
            check(await Rejected(Wire("session", id), Convert.ToBase64String(new byte[32])), "Unpaired callers cannot obtain a file session");
            check(await Rejected(Wire("result", id, FileProtocol.NewId())), "Unknown session tokens are rejected");
            check(await Rejected(Wire("result", FileProtocol.NewId(), token)), "A session cannot be moved to another transfer ID");
            var stale = Wire("result", id, token); stale["receiveGeneration"] = FileProtocol.NewId();
            check(await Rejected(stale), "A session cannot be moved to another phone generation");
            check(await Rejected(Wire("result", id)), "Omitting both session and legacy expiry does not bypass authentication lifetime checks");
            clock.Advance(TimeSpan.FromMinutes(5));
            var retry = await Call(Wire("session", id));
            check(retry.Session == token && retry.LifetimeMs == 3_300_000, "Retrying session creation reuses the token and does not extend its lifetime");

            // A synthetic PDF, never a real document or the user's statement.
            var document = new StringBuilder("%PDF-1.4\n");
            string[] objects = ["<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Count 1/Kids[3 0 R]>>", "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<<>>>>"];
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++) { offsets.Add(document.Length); document.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
            int xref = document.Length;
            document.Append("xref\n0 4\n0000000000 65535 f \n");
            foreach (int offset in offsets) document.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
            document.Append($"trailer\n<</Size 4/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
            byte[] pdf = Encoding.ASCII.GetBytes(document.ToString());
            string pdfHash = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
            var start = Wire("start", id, token);
            start["expiresAt"] = long.MinValue;
            start["batch"] = new { id, sequence = 1, expiresAt = long.MaxValue, receiveGeneration = generation, files = new[] { new { name = "测试文档.pdf", mime = "application/pdf", size = -1 } } };
            check((await Call(start)).Status == "ready", "A Chinese-named PDF with unknown size starts despite arbitrary phone request and manifest timestamps");
            clock.JumpWall(TimeSpan.FromDays(365));
            check((await Call(start)).Status == "ready", "A retried PDF start survives a PC wall-clock change without renewing its deadline");
            var write = Wire("write", id, token); write["data"] = Convert.ToBase64String(pdf); write["expiresAt"] = long.MaxValue;
            check((await Call(write)).Offset == pdf.Length, "Session upload accepts PDF bytes without comparing device clocks");
            var finish = Wire("finish", id, token); finish["size"] = pdf.Length; finish["sha256"] = pdfHash;
            check((await Call(finish)).Status == "complete" && (await Call(Wire("commit", id, token))).Status == "saving", "PDF hash verification and commit retain their original guarantees");
            var inbox = Bridge.Decode<FileInboxBatch>(Encoding.UTF8.GetBytes((await transfers.Local(new("files-incoming"), default)).Value!));
            string[] saved = FileInbox.Save(inbox, Path.Combine(directory.FullName, "downloads"), Path.Combine(directory.FullName, "receipts"));
            check(saved.Length == 1 && Path.GetFileName(saved[0]) == "测试文档.pdf" && File.ReadAllBytes(saved[0]).SequenceEqual(pdf), "Session PDF upload saves the original Chinese filename and exact bytes on Windows");
            await transfers.Local(new("files-incoming-ack", Value: id), default);
            check((await Call(Wire("result", id, token))).Status == "saved", "A session reports success only after Windows acknowledges saving");

            string next = FileProtocol.NewId(); var nextSession = await Call(Wire("session", next));
            var nextStart = Wire("start", next, nextSession.Session);
            nextStart["batch"] = new { id = next, sequence = 2, receiveGeneration = generation, files = new[] { new { name = "second.pdf", mime = "application/pdf", size = 0 } } };
            check((await Call(nextStart)).Status == "ready", "The new Android wire format needs no manifest or request expiry field");
            var captured = new Box(Bridge.Seal(Bridge.Encode(Wire("result", next, nextSession.Session)), key));
            using (var beforeExpiry = await client.PostAsJsonAsync("/api/files", captured, Bridge.Json))
                check(beforeExpiry.IsSuccessStatusCode, "The replay fixture was a valid authenticated request before its deadline");
            using (var restarted = new FileTransfers(vault, sender, clock))
            {
                bool revoked = false;
                try { await restarted.Request(new(Bridge.Seal(Bridge.Encode(Wire("result", next, nextSession.Session)), key)), default); }
                catch (InvalidDataException) { revoked = true; }
                check(revoked, "Restarting the PC service revokes in-memory session tokens");
                var recovery = Wire("session", next);
                var recovered = Bridge.Decode<FileResponse>(Bridge.Open((await restarted.Request(new(Bridge.Seal(Bridge.Encode(recovery), key)), default)).Data, key));
                check(recovered.Status == "session" && recovered.Session != nextSession.Session && recovered.LifetimeMs <= nextSession.LifetimeMs,
                    "Persisted incoming metadata can obtain a fresh session after restart without extending its PC deadline");
            }
            clock.JumpWall(TimeSpan.FromDays(-730));
            clock.Advance(TimeSpan.FromHours(1));
            check(await Rejected(Wire("result", next, nextSession.Session)), "Elapsed PC time expires a session even if its wall clock moves backwards");
            using (var replayed = await client.PostAsJsonAsync("/api/files", captured, Bridge.Json))
                check(replayed.StatusCode == HttpStatusCode.BadRequest, "Replaying the exact captured encrypted request after session expiry is rejected");
            check(await Rejected(Wire("session", next)), "An expired active transfer cannot be revived by requesting another session");
            // Exercise receive sessions independently of the expired incoming batch.
            string receiveId;
            using (var streams = new StreamHolder(FileTransfers.OpenFiles(saved)))
            {
                var sent = await transfers.Send(streams.Take(), default); receiveId = sent.Value!;
            }
            var receive = await Call(Wire("session", receiveId));
            clock.JumpWall(TimeSpan.FromDays(730));
            var offer = await Call(Wire("offer", receiveId, receive.Session));
            check(offer.Batch?.Files.Single().Name == "测试文档.pdf", "Windows-to-phone offers use the PC session despite unrelated wall-clock changes");
            var chunk = await Call(Wire("read", receiveId, receive.Session));
            check(Convert.FromBase64String(chunk.Data!).SequenceEqual(pdf) && (await Call(Wire("saved", receiveId, receive.Session))).Status == "saved",
                "Windows-to-phone session download preserves bytes and the final save acknowledgment");
            vault.Update(c => c.PhonePaused = true);
            check(await Rejected(Wire("result", receiveId, receive.Session)), "Pausing rejects file session requests");
            vault.Update(c => c.PhonePaused = false);
            check(await Rejected(Wire("result", receiveId, receive.Session)), "Resuming cannot reuse a token revoked during pause");
            vault.Update(c => { c.PhonePaused = false; c.PhoneGeneration = FileProtocol.NewId(); });
            generation = vault.Read().PhoneGeneration;
            check(await Rejected(Wire("result", receiveId, receive.Session)), "Changing the phone generation revokes previous sessions");

            Dictionary<string, object?> Start(string transferId, string? session, long sequence, long size)
            {
                var request = Wire("start", transferId, session);
                request["batch"] = new { id = transferId, sequence, receiveGeneration = generation, files = new[] { new { name = "recovery.pdf", mime = "application/pdf", size } } };
                return request;
            }
            string lostId = FileProtocol.NewId(), replacementId = FileProtocol.NewId();
            var lostSession = await Call(Wire("session", lostId));
            check((await Call(Start(lostId, lostSession.Session, 3, 10))).Status == "ready", "A new upload starts after an older expired batch");
            var replacementSession = await Call(Wire("session", replacementId));
            clock.Advance(TimeSpan.FromSeconds(30));
            check((await Call(Start(replacementId, replacementSession.Session, 4, 0))).Status == "error", "A second send cannot displace an active upload");
            var partial = Wire("write", lostId, lostSession.Session); partial["data"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
            await Call(partial);
            clock.Advance(TimeSpan.FromSeconds(91));
            check((await Call(Start(replacementId, replacementSession.Session, 4, 0))).Status == "error", "A valid chunk refreshes the upload's inactivity deadline");
            clock.Advance(TimeSpan.FromSeconds(30));
            check((await Call(Start(replacementId, replacementSession.Session, 4, 0))).Status == "ready", "A deliberate new send reclaims an incomplete upload after two minutes without progress");
            check(await Rejected(Wire("abort", lostId, lostSession.Session)), "An abandoned sender cannot cancel the replacement using its old session");
            check((await Call(Wire("result", replacementId, replacementSession.Session))).Status == "ready", "Reclaiming a stale upload leaves the replacement intact");
            var emptyFinish = Wire("finish", replacementId, replacementSession.Session);
            emptyFinish["size"] = 0; emptyFinish["sha256"] = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
            await Call(emptyFinish); await Call(Wire("commit", replacementId, replacementSession.Session));
            clock.Advance(TimeSpan.FromMinutes(3));
            string competingId = FileProtocol.NewId(); var competing = await Call(Wire("session", competingId));
            check((await Call(Start(competingId, competing.Session, 5, 0))).Status == "error", "A complete batch awaiting desktop saving is never reclaimed by inactivity");
            check((await Call(Wire("result", replacementId, replacementSession.Session))).Status == "saving", "A blocked new send preserves the previously uploaded batch");
            var recoveryInbox = Bridge.Decode<FileInboxBatch>(Encoding.UTF8.GetBytes((await transfers.Local(new("files-incoming"), default)).Value!));
            FileInbox.Save(recoveryInbox, Path.Combine(directory.FullName, "downloads"), Path.Combine(directory.FullName, "receipts"));
            await transfers.Local(new("files-incoming-ack", Value: replacementId), default);
            check((await Call(Wire("result", replacementId, replacementSession.Session))).Status == "saved", "The preserved complete batch can still be saved and acknowledged");

            string orphanId = FileProtocol.NewId(); var orphan = await Call(Wire("session", orphanId));
            await Call(Start(orphanId, orphan.Session, 6, 0));
            File.SetLastWriteTimeUtc(Path.Combine(vault.DataDir, "file-transfers", "incoming.json"), clock.GetUtcNow().AddMinutes(-3).UtcDateTime);
            using (var recoveredService = new FileTransfers(vault, sender, clock))
            {
                async Task<FileResponse> Recovered(Dictionary<string, object?> request) => Bridge.Decode<FileResponse>(Bridge.Open(
                    (await recoveredService.Request(new(Bridge.Seal(Bridge.Encode(request), key)), default)).Data, key));
                string freshId = FileProtocol.NewId(); var fresh = await Recovered(Wire("session", freshId));
                check((await Recovered(Start(freshId, fresh.Session, 7, 0))).Status == "ready", "A stale zero-byte upload recovered from disk cannot block the next send after restart");
                check((await Recovered(Wire("abort", freshId, fresh.Session))).Status == "cancelled", "The recovered replacement retains normal cancellation behavior");
                string activeId = FileProtocol.NewId(); var active = await Recovered(Wire("session", activeId));
                await Recovered(Start(activeId, active.Session, 8, 10));
                var activeWrite = Wire("write", activeId, active.Session); activeWrite["data"] = Convert.ToBase64String(new byte[] { 5, 6, 7, 8 });
                await Recovered(activeWrite);
                string staging = Path.Combine(vault.DataDir, "file-transfers");
                File.SetLastWriteTimeUtc(Path.Combine(staging, "incoming.json"), clock.GetUtcNow().AddMinutes(-3).UtcDateTime);
                File.SetLastWriteTimeUtc(Path.Combine(staging, activeId, "0.part"), clock.GetUtcNow().UtcDateTime);
                using var afterChunkRestart = new FileTransfers(vault, sender, clock);
                async Task<FileResponse> AfterChunk(Dictionary<string, object?> request) => Bridge.Decode<FileResponse>(Bridge.Open(
                    (await afterChunkRestart.Request(new(Bridge.Seal(Bridge.Encode(request), key)), default)).Data, key));
                string blockedId = FileProtocol.NewId(); var blocked = await AfterChunk(Wire("session", blockedId));
                check((await AfterChunk(Start(blockedId, blocked.Session, 9, 0))).Status == "error", "Recent persisted chunk progress protects an upload after restart even if its manifest is old");
                var resumedActive = await AfterChunk(Wire("session", activeId));
                await AfterChunk(Wire("abort", activeId, resumedActive.Session));
            }
            string currentId = FileProtocol.NewId(); var current = await Call(Wire("session", currentId));
            vault.Update(c => c.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))); key = vault.Read().Key;
            check(await Rejected(Wire("result", currentId, current.Session)), "Changing the pairing key revokes sessions even for a correctly encrypted new request");
        }
        finally
        {
            if (directory.Parent!.FullName != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) || !directory.Name.StartsWith("clipboard-file-sessions-", StringComparison.Ordinal)) throw new IOException("Unexpected test directory");
            directory.Delete(true);
        }
    }
    sealed class StreamHolder(FileStream[] streams) : IDisposable
    {
        FileStream[] owned = streams;
        public FileStream[] Take() { var value = owned; owned = []; return value; }
        public void Dispose() { foreach (var stream in owned) stream.Dispose(); }
    }
}
