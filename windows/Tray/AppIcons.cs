using System.Reflection;

static class AppIcons
{
    public static readonly Icon Application = Load("ClipboardBridge.App.ico", new Size(64, 64));
    public static readonly Icon Tray = Load("ClipboardBridge.App.ico", SystemInformation.SmallIconSize);
    public static readonly Icon Paused = Load("ClipboardBridge.Paused.ico", SystemInformation.SmallIconSize);

    private static Icon Load(string resource, Size size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("应用图标资源缺失：" + resource);
        using var source = new Icon(stream, size);
        return (Icon)source.Clone();
    }
}
