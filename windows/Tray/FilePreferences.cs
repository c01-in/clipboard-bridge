using System.Runtime.InteropServices;
using ClipboardBridge;

static class FilePreferences
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge");
    public static string SaveDirectory
    {
        get
        {
            string path = Path.Combine(DirectoryPath, "downloads.txt");
            return File.Exists(path) ? File.ReadAllText(path) : Downloads();
        }
        set
        {
            if (!Path.IsPathFullyQualified(value)) throw new IOException(UiText.Get("files_folder_invalid"));
            Directory.CreateDirectory(value);
            string probe = Path.Combine(value, ".clipboard-bridge-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            Directory.CreateDirectory(DirectoryPath); File.WriteAllText(Path.Combine(DirectoryPath, "downloads.txt"), Path.GetFullPath(value));
        }
    }
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    static string Downloads()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStringUni(pointer)!; } finally { Marshal.FreeCoTaskMem(pointer); }
    }
}
