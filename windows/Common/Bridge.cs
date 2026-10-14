using System.Buffers.Binary;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipboardBridge;

public static class Bridge
{
    public const string PipeName = "ClipboardBridge-v1";
    public const int MaxBytes = 16 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static readonly string DataDir = Environment.GetEnvironmentVariable("CLIPBOARD_BRIDGE_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClipboardBridge");
    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static T Decode<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("空数据");
    public static string Seal(byte[] bytes, string key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12), cipher = new byte[bytes.Length], tag = new byte[16];
        using var aes = new AesGcm(Convert.FromBase64String(key), 16);
        aes.Encrypt(nonce, bytes, cipher, tag, Encoding.UTF8.GetBytes("ClipboardBridge/v1"));
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }
    public static byte[] Open(string box, string key)
    {
        var data = Convert.FromBase64String(box);
        if (data.Length < 28 || data.Length > MaxBytes + 28) throw new InvalidDataException("密文大小无效");
        byte[] plain = new byte[data.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(key), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(12, plain.Length), data.AsSpan(data.Length - 16), plain, Encoding.UTF8.GetBytes("ClipboardBridge/v1"));
        return plain;
    }
    public static async Task WriteFrame(Stream stream, byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length > MaxBytes * 2) throw new InvalidDataException("数据过大");
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static async Task<byte[]> ReadFrame(Stream stream, CancellationToken ct = default)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > MaxBytes * 2) throw new InvalidDataException("数据大小无效");
        var data = new byte[length]; await stream.ReadExactlyAsync(data, ct); return data;
    }
    public static IEnumerable<string> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
        .Select(a => "http://" + a.Address + ":45871").Distinct();
}

public sealed record Command(string Op, string? Mime = null, string? Data = null, string? Value = null);
public sealed record Reply(bool Ok, string Message, string? Value = null);
public sealed record Box(string Data);
public sealed record Device(string Token, string DeviceName);
public sealed record Receipt(string Id, long Sequence, string Status, string? Detail);
public sealed record ClipManifest(string Id, long Sequence, string Mime, long ExpiresAt, string Sha256, int Size, string? Inline, string? Blob);
public sealed record Delivery(string Id, long Sequence, long ExpiresAt, string Envelope, string? BlobFile, bool Submitted = false);

public sealed class Configuration
{
    public string Key { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string[] Endpoints { get; set; } = Bridge.Addresses().Take(1).ToArray();
    public string OwnerSid { get; set; } = "";
    public string? ServiceAccountJson { get; set; }
    public string? DeviceToken { get; set; }
    public string? DeviceName { get; set; }
    public string? FirebaseAndroidJson { get; set; }
    public long OutgoingSequence { get; set; }
}

public sealed class Vault
{
    private readonly object gate = new();
    private readonly string path;
    public Vault() { Directory.CreateDirectory(Bridge.DataDir); path = Path.Combine(Bridge.DataDir, "configuration.dpapi"); }
    public Configuration Read()
    {
        lock (gate)
        {
            if (!File.Exists(path)) { var first = new Configuration(); Write(first); return first; }
            return Bridge.Decode<Configuration>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine));
        }
    }
    public void Write(Configuration value)
    {
        lock (gate)
        {
            var bytes = ProtectedData.Protect(Bridge.Encode(value), null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(path + ".tmp", bytes); File.Move(path + ".tmp", path, true);
        }
    }
    public void Update(Action<Configuration> update) { lock (gate) { var c = Read(); update(c); Write(c); } }
}
