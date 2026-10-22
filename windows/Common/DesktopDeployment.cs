using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ClipboardBridge;

/// <summary>Separates Store-owned per-user state from the desktop Windows service.</summary>
public static class DesktopDeployment
{
    public static bool PerUser { get; } = DetectPerUser();
    public static string OwnerSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("The current Windows user has no SID.");
        }
    }
    public static string PipeName => PerUser ? "ClipboardBridge-user-" + OwnerSid : "ClipboardBridge-v1";
    public static int HttpPort { get; } = Port("CLIPBRIDGE_HTTP_PORT", 45871);
    public static int DiscoveryPort { get; } = Port("CLIPBRIDGE_DISCOVERY_PORT", 45872);
    public static string DataDirectory => Environment.GetEnvironmentVariable("CLIPBOARD_BRIDGE_DATA")
        ?? Path.Combine(Environment.GetFolderPath(PerUser ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.CommonApplicationData),
            "ClipboardBridge", PerUser ? "Store" : "");

    private static bool DetectPerUser()
    {
        // Explicit mode also lets integration tests exercise the actual Store host without
        // registering a package or touching the installed desktop service.
        if (Environment.GetEnvironmentVariable("CLIPBRIDGE_USER_MODE") == "1") return true;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "store-deployment.json"))) return false;
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) == 122;
    }

    private static int Port(string variable, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (value is null) return fallback;
        return int.TryParse(value, out int port) && port is > 0 and <= 65535
            ? port : throw new ArgumentException("Invalid local listening port: " + variable);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
