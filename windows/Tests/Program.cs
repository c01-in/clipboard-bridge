using ClipboardBridge;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

int checks = 0;
void Assert(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
byte[] unicode = Encoding.UTF8.GetBytes("Windows → 三星：中文、emoji 📋\n第二行");
string box = Bridge.Seal(unicode, key);
Assert(Bridge.Open(box, key).SequenceEqual(unicode), "Unicode text encryption round trip");
Assert(Bridge.Seal(unicode, key) != box, "Unique nonce for repeated plaintext");
var damaged = Convert.FromBase64String(box); damaged[15] ^= 1;
try { Bridge.Open(Convert.ToBase64String(damaged), key); throw new Exception("tamper accepted"); } catch (CryptographicException) { Assert(true, "Reject tampered ciphertext"); }
try { Bridge.Open(box, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))); throw new Exception("wrong key accepted"); } catch (CryptographicException) { Assert(true, "Reject wrong pairing key"); }
var image = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
Assert(Bridge.Open(Bridge.Seal(image, key), key).SequenceEqual(image), "2 MiB binary payload integrity");
using var stream = new MemoryStream(); await Bridge.WriteFrame(stream, image); stream.Position = 0;
Assert((await Bridge.ReadFrame(stream)).SequenceEqual(image), "Length-framed IPC binary round trip");
var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, Bridge.MaxBytes * 2 + 1);
try { await Bridge.ReadFrame(new MemoryStream(prefix)); throw new Exception("oversized frame accepted"); } catch (InvalidDataException) { Assert(true, "Reject oversized IPC frame before allocation"); }
var protectedBytes = ProtectedData.Protect(unicode, null, DataProtectionScope.LocalMachine);
Assert(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine).SequenceEqual(unicode), "Persistent Windows DPAPI protection");
UploadManifest Manifest(byte[] content, long seq = 1, string mime = "text/plain") => new(
    Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), seq, mime,
    DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(), Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), content.Length);
ClipboardUpload Upload(UploadManifest manifest, byte[] content) => new(Bridge.Seal(Bridge.Encode(manifest), key), Bridge.Seal(content, key));
var uploadManifest = Manifest(unicode);
var opened = UploadContent.Open(Upload(uploadManifest, unicode), key);
Assert(opened.Manifest == uploadManifest && opened.Bytes.SequenceEqual(unicode), "Android upload envelope and payload round trip");
try { UploadContent.Open(Upload(uploadManifest with { ExpiresAt = 1 }, unicode), key); throw new Exception("expired upload accepted"); }
catch (InvalidDataException) { Assert(true, "Reject expired Android upload"); }
try { UploadContent.Open(Upload(uploadManifest with { Sha256 = new string('0', 64) }, unicode), key); throw new Exception("invalid hash accepted"); }
catch (InvalidDataException) { Assert(true, "Reject Android upload payload/hash mismatch"); }
try { UploadContent.Open(Upload(Manifest(unicode, mime: "image/png"), unicode), key); throw new Exception("fake PNG accepted"); }
catch (InvalidDataException) { Assert(true, "Reject invalid PNG upload"); }
var invalidText = new byte[] { 0xc3, 0x28 };
try { UploadContent.Open(Upload(Manifest(invalidText), invalidText), key); throw new Exception("invalid UTF-8 accepted"); }
catch (DecoderFallbackException) { Assert(true, "Reject malformed UTF-8 upload"); }

var testDirectory = Directory.CreateTempSubdirectory("clipboard-bridge-checks-");
try
{
    var vault = new Vault(testDirectory.FullName);
    using var http = new HttpClient();
    using var broker = new Broker(vault, new FcmSender(http));
    var receiving = broker.ReceiveFromAndroid(uploadManifest, unicode, CancellationToken.None);
    Assert(!receiving.IsCompleted, "No Android success before Windows clipboard acknowledgment");
    var duplicate = broker.ReceiveFromAndroid(uploadManifest, unicode, CancellationToken.None);
    Assert(!duplicate.IsCompleted, "Concurrent duplicate waits for the same write acknowledgment");
    var conflict = await broker.ReceiveFromAndroid(Manifest(unicode, 2), unicode, CancellationToken.None);
    Assert(conflict.Status == "error", "Concurrent different upload cannot replace an in-flight write");
    var queued = await broker.Handle(new Command("receive"), CancellationToken.None);
    var incoming = Bridge.Decode<IncomingClip>(Encoding.UTF8.GetBytes(queued.Value!));
    Assert(incoming.Manifest.Id == uploadManifest.Id && Convert.FromBase64String(incoming.Data).SequenceEqual(unicode), "Desktop receives the actual Android upload bytes");
    string Ack(Receipt receipt) => System.Text.Json.JsonSerializer.Serialize(receipt, Bridge.Json);
    var invalidAck = await broker.Handle(new Command("receive-ack", Value: Ack(new Receipt("wrong", 1, "applied", null))), CancellationToken.None);
    Assert(!invalidAck.Ok && !receiving.IsCompleted, "Unrelated acknowledgment cannot report Android upload success");
    await broker.Handle(new Command("receive-ack", Value: Ack(new Receipt(uploadManifest.Id, 1, "applied", null))), CancellationToken.None);
    Assert((await receiving).Status == "applied" && (await duplicate).Status == "applied", "Write acknowledgment completes both upload requests");
    Assert(vault.Read().IncomingSequence == 1 && vault.Read().IncomingId == uploadManifest.Id, "Successful Android upload replay protection persists with DPAPI");
    Assert((await broker.ReceiveFromAndroid(uploadManifest, unicode, CancellationToken.None)).Status == "applied" &&
        (await broker.Handle(new Command("receive"), CancellationToken.None)).Value is null, "Completed retry confirms success without rewriting the clipboard");
    using var restarted = new Broker(new Vault(testDirectory.FullName), new FcmSender(http));
    Assert((await restarted.ReceiveFromAndroid(Manifest(unicode, 1), unicode, CancellationToken.None)).Status == "error", "Old upload is rejected after broker restart");
    var failingManifest = Manifest(unicode, 2);
    var failing = broker.ReceiveFromAndroid(failingManifest, unicode, CancellationToken.None);
    await broker.Handle(new Command("receive-ack", Value: Ack(new Receipt(failingManifest.Id, 2, "error", "clipboard busy"))), CancellationToken.None);
    Assert((await failing).Status == "error" && vault.Read().IncomingSequence == 1, "Failed clipboard write reports failure and does not consume its sequence");
    using var cancellation = new CancellationTokenSource();
    var abandoned = broker.ReceiveFromAndroid(failingManifest, unicode, cancellation.Token);
    cancellation.Cancel();
    Assert((await abandoned).Status == "error" && (await broker.Handle(new Command("receive"), CancellationToken.None)).Value is null,
        "Disconnected sender does not leave stale clipboard work queued");
}
finally
{
    foreach (var name in new[] { "configuration.dpapi", "configuration.dpapi.tmp", "pending.json", "pending.json.tmp" })
        File.Delete(Path.Combine(testDirectory.FullName, name));
    testDirectory.Delete();
}
Console.WriteLine($"{checks} checks passed");
