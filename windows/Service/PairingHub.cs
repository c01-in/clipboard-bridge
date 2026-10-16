using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipboardBridge;

/// <summary>Only public ephemeral keys are discoverable. Both physical screens must approve before configuration is released.</summary>
public sealed class PairingHub
{
    readonly Vault vault;
    readonly object sync = new();
    readonly Dictionary<string, Session> sessions = [];
    long searchingUntil;
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    sealed class Session
    {
        public required PairHello Hello;
        public required PairOffer Offer;
        public required IPAddress Address;
        public required string TransportKey;
        public string? DeviceKey;
        public bool ComputerApproved, PhoneApproved, Completed;
        public Device? CompletedDevice;
    }
    public PairingHub(Vault vault)
    {
        this.vault = vault;
        if (vault.Read().ComputerId.Length == 0) vault.Update(c => c.ComputerId = Guid.NewGuid().ToString("N"));
    }
    void Prune() { foreach (var id in sessions.Where(s => s.Value.Offer.ExpiresAt <= Now).Select(s => s.Key).ToArray()) sessions.Remove(id); }
    public PairState Open()
    {
        lock (sync) { searchingUntil = Now + 120_000; return State(); }
    }
    public PairState State()
    {
        lock (sync)
        {
            Prune();
            return new(searchingUntil > Now, searchingUntil, sessions.Values.Select(s => new PairPeer(s.Offer.Id, s.Hello.Name,
                PairingCrypto.Code(s.TransportKey), s.ComputerApproved, s.PhoneApproved, s.Completed, s.Offer.ExpiresAt)).ToArray());
        }
    }
    public PairState Approve(string id, bool accept)
    {
        lock (sync)
        {
            Prune(); if (!sessions.TryGetValue(id, out var s)) throw new InvalidDataException("配对请求已过期，请重新搜索");
            if (accept) s.ComputerApproved = true; else sessions.Remove(id);
            return State();
        }
    }
    public byte[]? Discovery(byte[] packet, IPAddress address)
    {
        if (packet.Length > 1400 || !AllowedAddress(address)) return null;
        using var doc = JsonDocument.Parse(packet);
        if (doc.RootElement.GetProperty("protocol").GetString() != PairingCrypto.Protocol) return null;
        string? kind = doc.RootElement.GetProperty("kind").GetString();
        lock (sync)
        {
            if (kind == "reconnect")
            {
                var h = Bridge.Decode<KnownHello>(packet); var c = vault.Read();
                if (h.Nonce.Length != 44 || (h.ComputerId.Length != 0 && h.ComputerId != c.ComputerId) ||
                    !PairingCrypto.Verify(c.Key, "reconnect\n" + h.ComputerId + "\n" + h.Nonce, h.Proof)) return null;
                string proof = PairingCrypto.Proof(c.Key, "known\n" + h.ComputerId + "\n" + c.ComputerId + "\n" + h.Nonce + "\n45871");
                return Bridge.Encode(new KnownOffer(PairingCrypto.Protocol, "known", c.ComputerId, h.ComputerId, h.Nonce, 45871, proof));
            }
            Prune(); if (kind != "hello" || searchingUntil <= Now) return null;
            var hello = Bridge.Decode<PairHello>(packet);
            if (hello.Id.Length != 32 || hello.Id.Any(c => !Uri.IsHexDigit(c)) || hello.Name.Length is < 1 or > 100 || hello.Name.Any(char.IsControl) ||
                hello.PublicKey.Length > 160 || Convert.FromBase64String(hello.Nonce).Length != 32) return null;
            var existing = sessions.Values.FirstOrDefault(s => s.Hello.Id == hello.Id && s.Address.Equals(address));
            if (existing is not null) return existing.Hello == hello ? Bridge.Encode(existing.Offer) : null;
            if (sessions.Count >= 16) return null;
            using var identity = new PairingIdentity();
            var offer = new PairOffer(PairingCrypto.Protocol, "offer", Guid.NewGuid().ToString("N"), Environment.MachineName, identity.Nonce, identity.PublicKey, 45871, Now + 120_000);
            string key = identity.Agree(hello.PublicKey, PairingCrypto.Transcript(hello, offer));
            sessions.Add(offer.Id, new Session { Hello = hello, Offer = offer, Address = address, TransportKey = key });
            return Bridge.Encode(offer);
        }
    }
    public Box ConfirmPhone(string id, Box body, IPAddress address)
    {
        lock (sync)
        {
            var s = Find(id, address); using var data = JsonDocument.Parse(Bridge.Open(body.Data, s.TransportKey));
            if (data.RootElement.GetProperty("kind").GetString() != "phone-confirm" || data.RootElement.GetProperty("sessionId").GetString() != id) throw new InvalidDataException("配对确认无效");
            s.PhoneApproved = true; return Encrypt(s, new { accepted = true });
        }
    }
    public Box Configuration(string id, IPAddress address, string endpoint)
    {
        lock (sync)
        {
            var s = Find(id, address); RequireApproved(s); var c = vault.Read();
            s.DeviceKey ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var addresses = new[] { endpoint }.Concat(c.Endpoints).Distinct().ToArray();
            return Encrypt(s, new { key = s.DeviceKey, computerId = c.ComputerId, computerName = Environment.MachineName, endpoints = addresses, vapid = WebPush.PublicKey(c.VapidPrivateKey) });
        }
    }
    // Caller holds Broker's send/receive gate. The old pairing is retained until an authenticated completion.
    public Box Complete(string id, Box body, IPAddress address, Action<string, Device> commit)
    {
        lock (sync)
        {
            var s = Find(id, address); RequireApproved(s);
            var request = Bridge.Decode<PairCompletion>(Bridge.Open(body.Data, s.TransportKey));
            if (request.Kind != "complete" || request.SessionId != id || s.DeviceKey is null) throw new InvalidDataException("推送注册尚未完成");
            var device = new Device(request.Subscription, s.Hello.Name, request.ReceiveGeneration, request.Revision, request.Paused);
            ValidateDevice(device);
            if (!s.Completed) { commit(s.DeviceKey, device); s.CompletedDevice = device; s.Completed = true; }
            else if (device != s.CompletedDevice) throw new InvalidDataException("配对完成请求不匹配");
            return Encrypt(s, new { completed = true });
        }
    }
    public static void ValidateDevice(Device device)
    {
        WebPush.Validate(device.Subscription);
        if (device.DeviceName is null || device.DeviceName.Length > 160 || device.ReceiveGeneration is null || device.ReceiveGeneration.Length != 32 ||
            device.ReceiveGeneration.Any(c => !Uri.IsHexDigit(c)) || device.Revision <= 0 ||
            device.Challenge is not null && (device.Challenge.Length != 44 || Convert.FromBase64String(device.Challenge).Length != 32))
            throw new InvalidDataException("推送注册请求无效");
    }
    Session Find(string id, IPAddress address)
    {
        Prune(); if (!sessions.TryGetValue(id, out var s) || !s.Address.Equals(address)) throw new InvalidDataException("配对请求不存在或已过期"); return s;
    }
    static void RequireApproved(Session s) { if (!s.ComputerApproved || !s.PhoneApproved) throw new InvalidOperationException("请先在手机和电脑上确认相同的六位确认码"); }
    static Box Encrypt(Session s, object data) => new(Bridge.Seal(JsonSerializer.SerializeToUtf8Bytes(data, Bridge.Json), s.TransportKey));
    public static bool AllowedAddress(IPAddress ip)
    {
        ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip; if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes(); return b[0] == 10 || b[0] == 127 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }
}
