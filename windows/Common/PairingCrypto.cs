using System.Security.Cryptography;
using System.Text;

namespace ClipboardBridge;

public sealed class PairingIdentity : IDisposable
{
    readonly ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    public string PublicKey => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    public string Nonce { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string Agree(string publicKey, string transcript)
    {
        var encoded = Convert.FromBase64String(publicKey);
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(encoded, out int used);
        if (used != encoded.Length || peer.KeySize != 256 || peer.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new InvalidDataException("配对公钥无效");
        var secret = key.DeriveRawSecretAgreement(peer.PublicKey);
        try
        {
            var salt = SHA256.HashData(Encoding.UTF8.GetBytes(transcript));
            return Convert.ToBase64String(HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, Encoding.UTF8.GetBytes("ClipboardBridge.Pairing.v1")));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    public void Dispose() => key.Dispose();
}

public static class PairingCrypto
{
    public const string Protocol = "ClipboardBridge.Pairing.v1";
    public static string Transcript(PairHello hello, PairOffer offer) => string.Join('\n', Protocol, hello.Id,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(hello.Name)), hello.Nonce, hello.PublicKey,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offer.Name)), offer.Id, offer.Nonce, offer.PublicKey);
    public static string Proof(string key, string message) => Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(key), Encoding.UTF8.GetBytes(message)));
    public static bool Verify(string key, string message, string proof)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Proof(key, message)), Convert.FromBase64String(proof)); }
        catch (FormatException) { return false; }
    }
    public static string Code(string key)
    {
        byte[] bytes = Convert.FromBase64String(Proof(key, "confirmation-code"));
        uint number = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return (number % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
public sealed record PairHello(string Protocol, string Kind, string Id, string Name, string Nonce, string PublicKey);
public sealed record PairOffer(string Protocol, string Kind, string Id, string Name, string Nonce, string PublicKey, int Port, long ExpiresAt);
public sealed record KnownHello(string Protocol, string Kind, string ComputerId, string Nonce, string Proof);
public sealed record KnownOffer(string Protocol, string Kind, string ComputerId, string RequestComputerId, string Nonce, int Port, string Proof);
public sealed record PairPeer(string Id, string Name, string Code, bool ComputerApproved, bool PhoneApproved, bool Completed, long ExpiresAt);
public sealed record PairState(bool Searching, long ExpiresAt, PairPeer[] Phones);
public sealed record PairCompletion(string Kind, string SessionId, PushSubscription Subscription, string ReceiveGeneration, long Revision, bool Paused = false);
