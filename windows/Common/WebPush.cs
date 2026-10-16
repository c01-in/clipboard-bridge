using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ClipboardBridge;

public sealed record PushKeys(string P256dh, string Auth);
public sealed record PushSubscription(string Endpoint, PushKeys Keys);

/// <summary>RFC 8291 single-record encryption and RFC 8292 VAPID. Keys belong to this PC, not a Firebase project.</summary>
public static class WebPush
{
    public static string GeneratePrivateKey() { using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); return Convert.ToBase64String(key.ExportPkcs8PrivateKey()); }
    public static string PublicKey(string privateKey)
    {
        using var key = ECDsa.Create(); key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return Url64(Point(key.ExportParameters(false).Q));
    }
    public static string Url64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode64(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
    static byte[] Point(ECPoint point) => [4, .. point.X!, .. point.Y!];
    public static void Validate(PushSubscription? subscription)
    {
        if (subscription?.Keys is null || subscription.Keys.P256dh is null || subscription.Keys.Auth is null || subscription.Endpoint is null || subscription.Endpoint.Length > 4096 ||
            !Uri.TryCreate(subscription.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "fcm.googleapis.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/fcm/send/", StringComparison.Ordinal) || uri.AbsolutePath.Length <= 10)
            throw new InvalidDataException("推送接收地址无效");
        byte[] point = Decode64(subscription.Keys.P256dh), auth = Decode64(subscription.Keys.Auth);
        if (point.Length != 65 || point[0] != 4 || auth.Length != 16) throw new InvalidDataException("推送接收密钥无效");
        using var remote = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..] } });
    }
    public static byte[] Encrypt(byte[] payload, PushSubscription subscription)
    {
        Validate(subscription);
        if (payload.Length > 3000) throw new InvalidDataException("推送消息过大");
        byte[] receiver = Decode64(subscription.Keys.P256dh), auth = Decode64(subscription.Keys.Auth);
        using var remote = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = receiver[1..33], Y = receiver[33..] } });
        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] senderPoint = Point(sender.ExportParameters(false).Q), shared = sender.DeriveRawSecretAgreement(remote.PublicKey);
        try
        {
            byte[] ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. receiver, .. senderPoint]);
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
            byte[] nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
            byte[] plain = [.. payload, 2], cipher = new byte[plain.Length], tag = new byte[16];
            using var aes = new AesGcm(cek, 16); aes.Encrypt(nonce, plain, cipher, tag);
            byte[] size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, 4096);
            return [.. salt, .. size, 65, .. senderPoint, .. cipher, .. tag];
        }
        finally { CryptographicOperations.ZeroMemory(shared); }
    }
    public static string Authorization(string privateKey)
    {
        string head = Url64(Bridge.Encode(new { typ = "JWT", alg = "ES256" }));
        string claims = Url64(Bridge.Encode(new { aud = "https://fcm.googleapis.com", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), sub = "https://github.com/c01-in/clipboard-bridge" }));
        string input = head + "." + claims;
        using var key = ECDsa.Create(); key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return "vapid t=" + input + "." + Url64(signature) + ", k=" + PublicKey(privateKey);
    }
}
