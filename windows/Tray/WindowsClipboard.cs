using System.Security.Cryptography;
using System.Text;
using ClipboardBridge;

internal static class WindowsClipboard
{
    public static void Apply(IncomingClip clip)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException(UiText.Get("win_clipboard_sta"));
        var manifest = clip.Manifest;
        var bytes = Convert.FromBase64String(clip.Data);
        if (manifest.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidDataException(UiText.Get("error_content_expired"));
        if (bytes.Length != manifest.Size || bytes.Length is < 1 or > Bridge.MaxBytes ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("error_integrity"));
        if (manifest.Mime == "text/plain")
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Contains('\0')) throw new InvalidDataException(UiText.Get("win_text_null"));
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
        else throw new InvalidDataException(UiText.Get("error_content_format"));
    }
}
