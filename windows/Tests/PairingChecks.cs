using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClipboardBridge;

static class PairingChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("clipboard-pairing-checks-");
        Process? java = null;
        try
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "android/app/src/main/java/com/colin/clipbridge/PairingIdentity.java"))) repo = repo.Parent;
            if (repo is null) throw new DirectoryNotFoundException("Cannot locate actual Android pairing source");
            string home = Environment.GetEnvironmentVariable("JAVA_HOME") ?? "";
            string Tool(string tool) => home.Length > 0 ? Path.Combine(home, "bin", tool + ".exe") : tool;
            var compile = new ProcessStartInfo(Tool("javac")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "--release", "17", "-encoding", "UTF-8", "-d", directory.FullName, Path.Combine(repo.FullName, "android/app/src/main/java/com/colin/clipbridge/PairingIdentity.java"), Path.Combine(repo.FullName, "windows/Tests/PairingCrossCheck.java") }) compile.ArgumentList.Add(arg);
            using (var compiler = Process.Start(compile)!)
            {
                string errors = await compiler.StandardError.ReadToEndAsync();await compiler.WaitForExitAsync();if (compiler.ExitCode != 0) throw new Exception("Actual Android crypto compilation failed: " + errors);
            }
            var start = new ProcessStartInfo(Tool("java")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-cp", directory.FullName, "com.colin.clipbridge.PairingCrossCheck" }) start.ArgumentList.Add(arg);
            java = Process.Start(start)!;
            string[] fields = (await java.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!.Split(' ');
            var hello = new PairHello(PairingCrypto.Protocol, "hello", Guid.NewGuid().ToString("N"), "Protocol test phone", fields[1], fields[0]);
            var vault = new Vault(directory.FullName);string oldKey = vault.Read().Key;
            vault.Update(c => c.DeviceName = "Existing test phone");
            var subscription = WebPushChecks.Subscription();
            string generation = Guid.NewGuid().ToString("N");
            var hub = new PairingHub(vault);var address = IPAddress.Parse("127.0.0.2");byte[] announcement = Bridge.Encode(hello);
            check(hub.Discovery(announcement, address) is null, "Unpaired discovery is closed until the local settings window opens it");
            hub.Open();byte[] response = hub.Discovery(announcement, address)!;var offer = Bridge.Decode<PairOffer>(response);
            check(!Encoding.UTF8.GetString(response).Contains("firebase", StringComparison.OrdinalIgnoreCase) && !Encoding.UTF8.GetString(response).Contains(oldKey), "Discovery broadcasts no permanent keys or Firebase configuration");
            check(Bridge.Decode<PairOffer>(hub.Discovery(announcement, address)!).Id == offer.Id, "Repeated announcements reuse the same confirmation session");
            await java.StandardInput.WriteLineAsync(offer.PublicKey + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(PairingCrypto.Transcript(hello, offer))));await java.StandardInput.FlushAsync();
            string[] agreement = (await java.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!.Split(' ');string transport = agreement[0];
            check(agreement[1] == hub.State().Phones.Single().Code, "Actual Android Java and Windows agree on ECDH, HKDF and six-digit confirmation");
            bool Blocked(Action action) { try { action(); return false; } catch (Exception e) when (e is InvalidOperationException or InvalidDataException or CryptographicException) { return true; } }
            check(Blocked(() => hub.Configuration(offer.Id, address, "http://127.0.0.1:45871")), "Configuration is unavailable before both physical confirmations");
            hub.Approve(offer.Id, true);
            check(Blocked(() => hub.Configuration(offer.Id, address, "http://127.0.0.1:45871")), "Computer-only approval cannot release pairing credentials");
            byte[] confirm = Bridge.Encode(new { kind = "phone-confirm", sessionId = offer.Id });
            check(Blocked(() => hub.ConfirmPhone(offer.Id, new Box(Bridge.Seal(confirm, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))), address)), "A forged phone confirmation is rejected");
            check(Blocked(() => hub.ConfirmPhone(offer.Id, new Box(Bridge.Seal(confirm, transport)), IPAddress.Parse("127.0.0.3"))), "Pairing confirmation is bound to the discovering phone address");
            hub.ConfirmPhone(offer.Id, new Box(Bridge.Seal(confirm, transport)), address);
            using var config = JsonDocument.Parse(Bridge.Open(hub.Configuration(offer.Id, address, "http://127.0.0.1:45871").Data, transport));
            string newKey = config.RootElement.GetProperty("key").GetString()!;
            check(newKey != oldKey && vault.Read().Key == oldKey, "A replacement key is prepared without overwriting the working pairing");
            check(!config.RootElement.TryGetProperty("serviceAccountJson", out _) && !config.RootElement.ToString().Contains("private_key"), "Phone bootstrap never includes sender credentials");
            using var http = new HttpClient();using var broker = new Broker(vault, new WebPushSender(http), pairing: hub);
            check(config.RootElement.GetProperty("vapid").GetString() == WebPush.PublicKey(vault.Read().VapidPrivateKey) && !config.RootElement.TryGetProperty("firebase", out _), "Pairing requires no Firebase files and releases only this PC's VAPID public key");
            var completion = new Box(Bridge.Seal(Bridge.Encode(new PairCompletion("complete", offer.Id, subscription, generation, 1)), transport));
            var completed = await broker.CompletePairing(offer.Id, completion, address, CancellationToken.None);
            check(vault.Read().Key == newKey && vault.Read().Subscription == subscription, "Authenticated completion commits the new phone and revokes the old pairing key");
            check(JsonDocument.Parse(Bridge.Open(completed.Data, transport)).RootElement.GetProperty("completed").GetBoolean(), "Pair completion is confirmed inside the encrypted session");
            await broker.CompletePairing(offer.Id, completion, address, CancellationToken.None);
            check(vault.Read().Key == newKey, "A completion retry after a lost response is idempotent");
            string registrationChallenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var registered = await broker.Register(new Box(Bridge.Seal(Bridge.Encode(new Device(subscription, hello.Name, generation, 1, Challenge: registrationChallenge)), newKey)), CancellationToken.None);
            using var registration = JsonDocument.Parse(Bridge.Open(registered!.Data, newKey));
            check(registration.RootElement.GetProperty("registered").GetBoolean() && registration.RootElement.GetProperty("challenge").GetString() == registrationChallenge,
                "Registration recovery requires an encrypted acknowledgment bound to a fresh phone challenge");
            bool rejectedOldRegistration = false;
            try { await broker.Register(new Box(Bridge.Seal(Bridge.Encode(new Device(subscription, "Old phone", generation, 1)), oldKey)), CancellationToken.None); }
            catch (CryptographicException) { rejectedOldRegistration = true; }
            check(rejectedOldRegistration && vault.Read().Subscription == subscription, "The revoked pairing key cannot overwrite the new phone registration");
            var restarted = new PairingHub(vault);string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var reconnect = new KnownHello(PairingCrypto.Protocol, "reconnect", "", nonce, PairingCrypto.Proof(newKey, "reconnect\n\n" + nonce));
            var known = Bridge.Decode<KnownOffer>(restarted.Discovery(Bridge.Encode(reconnect), address)!);
            check(known.ComputerId == vault.Read().ComputerId && PairingCrypto.Verify(newKey, "known\n\n" + known.ComputerId + "\n" + nonce + "\n45871", known.Proof), "Existing keys authenticate address rediscovery without re-pairing, including legacy configurations");
            check(restarted.Discovery(Bridge.Encode(reconnect with { Proof = PairingCrypto.Proof(oldKey, "reconnect\n\n" + nonce) }), address) is null, "The previous pairing key cannot rediscover the new pairing");
            check(!PairingHub.AllowedAddress(IPAddress.Parse("8.8.8.8")), "Discovery excludes public source addresses");

            string resourceRoot = Path.Combine(repo.FullName, "android/app/src/main/res");
            var baseline = XDocument.Load(Path.Combine(resourceRoot, "values/strings.xml")).Root!.Elements("string").ToDictionary(e => e.Attribute("name")!.Value, e => e.Value);
            foreach (string folder in new[] { "values-b+zh+Hans", "values-b+zh+Hant", "values-es", "values-pt", "values-fr", "values-de", "values-ja", "values-ko" })
            {
                var translated = XDocument.Load(Path.Combine(resourceRoot, folder, "strings.xml")).Root!.Elements("string").ToDictionary(e => e.Attribute("name")!.Value, e => e.Value);
                check(baseline.Keys.ToHashSet().SetEquals(translated.Keys) && baseline.All(k => Regex.Matches(k.Value, @"%\d+\$s").Select(m => m.Value).Order().SequenceEqual(Regex.Matches(translated[k.Key], @"%\d+\$s").Select(m => m.Value).Order())), "Complete translations and matching placeholders: " + folder);
            }
            check(UiText.Language(CultureInfo.GetCultureInfo("zh-HK")) == "zh-Hant" && UiText.Language(CultureInfo.GetCultureInfo("zh-SG")) == "zh-Hans", "Chinese script follows the system locale");
            check(UiText.GetFor(CultureInfo.GetCultureInfo("es-MX"), "confirm_pair") == "Confirmar emparejamiento" && UiText.Language(CultureInfo.GetCultureInfo("it-IT")) == "en", "Regional language selection and English fallback work");
        }
        finally
        {
            if (java is not null) { if (!java.HasExited) java.Kill(); java.Dispose(); }
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Parent!.FullName != parent || !directory.Name.StartsWith("clipboard-pairing-checks-", StringComparison.Ordinal)) throw new IOException("Unexpected test directory");
            directory.Delete(true);
        }
    }
}
