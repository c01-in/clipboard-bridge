using System.Security.Cryptography;
using System.Text;
using ClipboardBridge;

internal static class WindowsClipboard
{
    public static void Apply(IncomingClip clip)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("剪贴板写入必须在桌面 STA 线程执行");
        var manifest = clip.Manifest;
        var bytes = Convert.FromBase64String(clip.Data);
        if (manifest.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidDataException("手机发送的内容已过期，请重新发送");
        if (bytes.Length != manifest.Size || bytes.Length is < 1 or > Bridge.MaxBytes ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("手机内容完整性校验失败");
        if (manifest.Mime == "text/plain")
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Contains('\0')) throw new InvalidDataException("Windows 文字剪贴板不支持空字符");
            var data = new DataObject(); data.SetText(text, TextDataFormat.UnicodeText);
            Clipboard.SetDataObject(data, true, 6, 75);
        }
        else if (manifest.Mime == "image/png")
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, false, true);
            using var bitmap = new Bitmap(image);
            var data = new DataObject();
            data.SetImage(bitmap);
            using var png = new MemoryStream(bytes);
            data.SetData("PNG", false, png);
            Clipboard.SetDataObject(data, true, 6, 75);
        }
        else throw new InvalidDataException("不支持的手机剪贴板类型");
    }
}
