using System.Security.Cryptography;
using System.Text;

namespace ClipboardBridge;

public static class UploadContent
{
    public static (UploadManifest Manifest, byte[] Bytes) Open(ClipboardUpload upload, string key)
    {
        if (string.IsNullOrEmpty(upload.Envelope) || upload.Envelope.Length > 4096 ||
            string.IsNullOrEmpty(upload.Data) || upload.Data.Length > (Bridge.MaxBytes + 30L) * 4 / 3 + 4)
            throw new InvalidDataException("上传大小无效");
        var manifest = Bridge.Decode<UploadManifest>(Bridge.Open(upload.Envelope, key));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!LowerHex(manifest.Id, 32) || !LowerHex(manifest.Sha256, 64) || manifest.Sequence <= 0 ||
            manifest.Size is < 1 or > Bridge.MaxBytes || manifest.Mime is not ("text/plain" or "image/png"))
            throw new InvalidDataException("剪贴板上传格式无效");
        if (manifest.ExpiresAt <= now || manifest.ExpiresAt > now + 600_000)
            throw new InvalidDataException("发送内容已过期或设备时间不正确，请重新发送");
        var bytes = Bridge.Open(upload.Data, key);
        if (bytes.Length != manifest.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("剪贴板内容完整性校验失败");
        if (manifest.Mime == "text/plain")
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Contains('\0')) throw new InvalidDataException("Windows 文字剪贴板不支持空字符");
        }
        else if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("PNG 文件格式无效");
        return (manifest, bytes);
    }

    private static bool LowerHex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
