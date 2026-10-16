using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardBridge;

static class WebPushChecks
{
    public static PushSubscription Subscription()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = recipient.ExportParameters(false).Q;
        return new("https://fcm.googleapis.com/fcm/send/isolated-offline-test", new(WebPush.Url64([4, .. point.X!, .. point.Y!]), WebPush.Url64(RandomNumberGenerator.GetBytes(16))));
    }
    sealed class Capture : HttpMessageHandler
    {
        public int Count; public byte[] Body = []; public string? Authorization; public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++; Body = await request.Content!.ReadAsByteArrayAsync(ct); Authorization = request.Headers.GetValues("Authorization").Single(); Uri = request.RequestUri;
            return new(HttpStatusCode.Created);
        }
    }
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("clipboard-webpush-checks-");
        try
        {
            using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var point = recipient.ExportParameters(false).Q;
            byte[] receiverPoint = [4, .. point.X!, .. point.Y!], auth = RandomNumberGenerator.GetBytes(16);
            var subscription = new PushSubscription("https://fcm.googleapis.com/fcm/send/isolated-offline-test", new(WebPush.Url64(receiverPoint), WebPush.Url64(auth)));
            byte[] message = Encoding.UTF8.GetBytes("Web Push 中文 📋");
            byte[] encrypted = WebPush.Encrypt(message, subscription);
            check(BinaryPrimitives.ReadInt32BigEndian(encrypted.AsSpan(16, 4)) == 4096 && encrypted[20] == 65, "RFC 8188/8291 header uses a 4096-byte record and uncompressed P-256 point");
            byte[] senderPoint = encrypted[21..86];
            using var sender = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = senderPoint[1..33], Y = senderPoint[33..] } });
            byte[] shared = recipient.DeriveRawSecretAgreement(sender.PublicKey);
            byte[] ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. receiverPoint, .. senderPoint]);
            byte[] cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, encrypted[..16], Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            byte[] nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, encrypted[..16], Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
            byte[] opened = new byte[encrypted.Length - 86 - 16];
            using (var aes = new AesGcm(cek, 16)) aes.Decrypt(nonce, encrypted.AsSpan(86, opened.Length), encrypted.AsSpan(encrypted.Length - 16), opened);
            check(opened[^1] == 2 && opened[..^1].SequenceEqual(message), "Web Push payload decrypts with the recipient's distinct private key and final-record delimiter");
            bool rejects = false;try { WebPush.Validate(subscription with { Endpoint = "http://127.0.0.1/internal" }); } catch (InvalidDataException) { rejects = true; }
            check(rejects, "Authenticated phone registration cannot turn the push sender into a local-network request proxy");
            var vault = new Vault(directory.FullName); string privateKey = vault.Read().VapidPrivateKey;
            check(new Vault(directory.FullName).Read().VapidPrivateKey == privateKey, "Per-computer VAPID key survives a vault restart with DPAPI protection");
            string authorization = WebPush.Authorization(privateKey), jwt = authorization.Split("t=")[1].Split(", k=")[0];
            string[] pieces = jwt.Split('.');using var signing = ECDsa.Create();signing.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
            check(signing.VerifyData(Encoding.ASCII.GetBytes(pieces[0] + "." + pieces[1]), WebPush.Decode64(pieces[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "VAPID JWT has a valid ES256 signature without a Firebase service account");
            using var claims = JsonDocument.Parse(WebPush.Decode64(pieces[1]));
            check(claims.RootElement.GetProperty("aud").GetString() == "https://fcm.googleapis.com" && claims.RootElement.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "VAPID authorization is scoped to Google with a short-lived expiry");
            using var capture = new Capture();using var http = new HttpClient(capture);using var broker = new Broker(vault, new WebPushSender(http));
            string first = Guid.NewGuid().ToString("N"), paused = Guid.NewGuid().ToString("N"), resumed = Guid.NewGuid().ToString("N");
            Device Device(string generation, long revision, bool pause = false) => new(subscription, "Offline test phone", generation, revision, pause, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            Task<Box?> Register(Device device) => broker.Register(new Box(Bridge.Seal(Bridge.Encode(device), vault.Read().Key)), CancellationToken.None);
            await Register(Device(first, 1));
            var command = new Command("send", "text/plain", Convert.ToBase64String(message));
            check((await broker.Handle(command, CancellationToken.None)).Ok && capture.Count == 1 && capture.Uri!.Host == "fcm.googleapis.com" && capture.Authorization!.StartsWith("vapid "), "Broker submits encrypted Web Push directly to Google without OAuth or Firebase files");
            var pending = Bridge.Decode<Delivery>(File.ReadAllBytes(Path.Combine(directory.FullName, "pending.json")));
            var manifest = Bridge.Decode<ClipManifest>(Bridge.Open(pending.Envelope, vault.Read().Key));
            check(manifest.ReceiveGeneration == first, "Each clipboard envelope is bound to the phone's current receive session");
            await Register(Device(paused, 2, true));
            check(!File.Exists(Path.Combine(directory.FullName, "pending.json")) && !(await broker.Handle(command, CancellationToken.None)).Ok && capture.Count == 1, "Phone pause clears pending delivery and stops Windows push submissions");
            bool stale = false;try { await Register(Device(first, 1)); } catch (InvalidDataException) { stale = true; }
            check(stale && vault.Read().PhonePaused, "A delayed old registration cannot undo a newer pause");
            await Register(Device(resumed, 3));
            check(!(await broker.Handle(new Command("status"), CancellationToken.None)).Message.Contains("暂停") && !File.Exists(Path.Combine(directory.FullName, "pending.json")), "Resume starts a fresh session without replaying paused content");
            check((await broker.Handle(command, CancellationToken.None)).Ok && capture.Count == 2, "Fresh content can be sent after the phone resumes");
            pending = Bridge.Decode<Delivery>(File.ReadAllBytes(Path.Combine(directory.FullName, "pending.json")));
            check(Bridge.Decode<ClipManifest>(Bridge.Open(pending.Envelope, vault.Read().Key)).ReceiveGeneration == resumed, "Resumed content carries the new receive generation");
            await broker.AcceptReceipt(new(pending.Id, pending.Sequence, "discarded", null), CancellationToken.None);
            check(!File.Exists(Path.Combine(directory.FullName, "pending.json")), "A discarded-content receipt ends delivery without claiming a clipboard write");

            var legacyDir = Directory.CreateDirectory(Path.Combine(directory.FullName, "legacy"));
            byte[] old = Bridge.Encode(new { key = vault.Read().Key, computerId = "legacy", deviceName = "Legacy phone", serviceAccountJson = "old-private-placeholder", firebaseAndroidJson = "old-config-placeholder" });
            byte[] oldProtected = ProtectedData.Protect(old, null, DataProtectionScope.LocalMachine);File.WriteAllBytes(Path.Combine(legacyDir.FullName, "configuration.dpapi"), oldProtected);
            var migrated = new Vault(legacyDir.FullName);var migratedConfig = migrated.Read();
            check(migratedConfig.Key == vault.Read().Key && migratedConfig.DeviceName == "Legacy phone" && migrated.Read().VapidPrivateKey == migratedConfig.VapidPrivateKey,
                "Legacy upgrade preserves pairing and persists the generated Web Push identity once");
            check(File.ReadAllBytes(Path.Combine(legacyDir.FullName, "configuration.pre-webpush.dpapi")).SequenceEqual(oldProtected), "Unused Firebase credentials remain in an exact encrypted local backup");
            string upgraded = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(legacyDir.FullName, "configuration.dpapi")), null, DataProtectionScope.LocalMachine));
            check(!upgraded.Contains("old-private-placeholder") && !upgraded.Contains("firebaseAndroidJson"), "Active configuration no longer contains or uses Firebase credentials");
        }
        finally
        {
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Parent!.FullName != parent || !directory.Name.StartsWith("clipboard-webpush-checks-", StringComparison.Ordinal)) throw new IOException("Unexpected test directory");
            directory.Delete(true);
        }
    }
}
