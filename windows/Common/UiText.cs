using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClipboardBridge;

public static class UiText
{
    static readonly string[] Languages = ["en", "zh-Hans", "zh-Hant", "es", "pt", "fr", "de", "ja", "ko"];
    static readonly Dictionary<string, Dictionary<string, string>> Catalog = Languages.ToDictionary(l => l, Load);
    static Dictionary<string, string> Load(string language)
    {
        using var input = typeof(UiText).Assembly.GetManifestResourceStream("ClipboardBridge.Locale." + language + ".xml") ?? throw new InvalidDataException("Missing locale " + language);
        return XDocument.Load(input).Root!.Elements("string").ToDictionary(e => e.Attribute("name")!.Value, e => e.Value.Replace("\\n", "\n").Replace("\\'", "'").Replace("\\\"", "\""));
    }
    public static string Language(CultureInfo culture)
    {
        string name = culture.Name.ToLowerInvariant();
        if (culture.TwoLetterISOLanguageName == "zh") return name.Contains("hant") || name.EndsWith("tw") || name.EndsWith("hk") || name.EndsWith("mo") ? "zh-Hant" : "zh-Hans";
        return Languages.Contains(culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : "en";
    }
    public static string Get(string key, params object?[] args) => GetFor(CultureInfo.CurrentUICulture, key, args);
    public static string GetFor(CultureInfo culture, string key, params object?[] args)
    {
        string text = Catalog[Language(culture)].GetValueOrDefault(key) ?? Catalog["en"][key];
        text = Regex.Replace(text, @"%(\d+)\$s", m => "{" + (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 1) + "}");
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }
    public static string Translate(string message)
    {
        var aliases = new Dictionary<string, string> {
            ["等待配对手机"]="win_setup_needed", ["已配置，等待复制内容"]="win_ready", ["已安全配对，等待复制内容"]="win_ready",
            ["仅支持 1 字节至 16 MiB 的内容"]="error_content_size", ["仅支持文字和单张 PNG 图片"]="error_content_format", ["PNG 文件格式无效"]="error_decode_image",
            ["手机内容已写入 Windows 剪贴板"]="win_clipboard_written",
            ["备用地址已保存，新配对时会自动传给手机"]="win_addresses_saved", ["已提交 Web Push，等待手机确认写入剪贴板"]="win_wait_ack", ["同步内容已过期，请重新复制；手机未确认接收"]="win_delivery_expired",
            ["请先配对 Android 手机"]="status_unpaired",
            ["请先在手机和电脑上确认相同的六位确认码"]="pair_compare", ["配对请求已过期，请重新搜索"]="pair_expired"
        };
        if (aliases.TryGetValue(message, out var alias)) return Get(alias);
        foreach (var item in Catalog["zh-Hans"].Concat(Catalog["en"]))
        {
            if (item.Value == message) return Get(item.Key);
            if (!item.Value.Contains("%1$s", StringComparison.Ordinal)) continue;
            string pattern = "^" + Regex.Escape(item.Value).Replace("%1\\$s", "(.*?)").Replace("%2\\$s", "(.*?)") + "$";
            var match = Regex.Match(message, pattern, RegexOptions.Singleline);
            if (match.Success) return Get(item.Key, match.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value).ToArray());
        }
        return message;
    }
}
