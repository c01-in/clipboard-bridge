using System.Security.Cryptography;
using System.Text;

namespace ClipboardBridge;

public sealed record SharedFile(string Name, string Mime, long Size);
public sealed record FileBatch(string Id, long Sequence, long ExpiresAt, string ReceiveGeneration, SharedFile[] Files);
public sealed record SavedFile(string Name, string Path, long Size, string Sha256);
public sealed record FileInboxBatch(string Id, SavedFile[] Files);
public sealed record FileRequest(string Op, string Id, string Nonce, long ExpiresAt, string ReceiveGeneration,
    int Index = 0, long Offset = 0, string? Data = null, long Size = 0, string? Sha256 = null, FileBatch? Batch = null);
public sealed record FileResponse(string Id, string Nonce, string Status, long Offset = 0, string? Data = null,
    FileBatch? Batch = null, string? Detail = null);
public sealed record FileTransferStatus(string Id, string Status, long Bytes, long Total, string? Detail = null);

/// <summary>File transfers use a separate, bounded AES-GCM message for each chunk.</summary>
public static class FileProtocol
{
    public const int ChunkBytes = 256 * 1024;
    public const int MaxFiles = 1000;
    public const int MaxEnvelopeBytes = 1024 * 1024;
    // Authenticated peers use independent clocks. Allow a bounded positive skew
    // at the maximum-lifetime boundary, while still rejecting expired messages.
    public const long ClockSkewMilliseconds = 60_000;
    public const string Mime = "application/vnd.clipboardbridge.files";
    public static bool IsId(string? value) => value?.Length == 32 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public static void Validate(FileBatch batch)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!IsId(batch.Id) || batch.Sequence <= 0 || batch.ExpiresAt <= now || batch.ExpiresAt > now + 3_600_000 + ClockSkewMilliseconds ||
            batch.Files is null || batch.Files.Length is < 1 or > MaxFiles ||
            batch.Files.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 240 ||
                f.Mime is null || f.Mime.Length > 200 || f.Size < -1) || Bridge.Encode(batch).Length > 512 * 1024)
            throw new InvalidDataException("Invalid file transfer manifest.");
        // Detect overflow before calculating space/progress totals.
        _ = batch.Files.Aggregate(0L, (total, f) => checked(total + Math.Max(0, f.Size)));
    }
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = new(name.Select(c => invalid.Contains(c) || c < 32 ? '_' : c).ToArray());
        safe = safe.Trim().TrimEnd('.', ' ');
        if (safe.Length == 0 || safe is "." or "..") safe = "file";
        string stem = safe.Split('.')[0].TrimEnd(' ');
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (char.IsDigit(stem[3]) || "¹²³".Contains(stem[3])))
            safe = "_" + safe;
        return safe.Length > 240 ? safe[..240] : safe;
    }
    public static string HashFile(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
