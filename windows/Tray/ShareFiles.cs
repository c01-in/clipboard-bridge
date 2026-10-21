using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using System.Runtime.InteropServices;
using ClipboardBridge;

/// <summary>Windows owns the incoming share until the receiving phone confirms it.</summary>
sealed class ShareFiles : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
    readonly ShareOperation operation;
    readonly List<string> temporary = [];
    bool reported;
    ShareFiles(ShareOperation operation) => this.operation = operation;
    public static ShareFiles? Activated()
    {
        uint length = 0;
        if (GetCurrentPackageFullName(ref length, IntPtr.Zero) == 15700) return null;
        return AppInstance.GetActivatedEventArgs() is ShareTargetActivatedEventArgs args ? new(args.ShareOperation) : null;
    }
    public static string Identity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == 15700 ? "unpackaged" : Package.Current.Id.FullName;
    }
    public async Task<string[]> Read(CancellationToken ct)
    {
        operation.ReportStarted();
        if (!operation.Data.Contains(StandardDataFormats.StorageItems)) throw new InvalidDataException(UiText.Get("error_content_format"));
        var items = await operation.Data.GetStorageItemsAsync();
        if (items.Count is < 1 or > FileProtocol.MaxFiles) throw new InvalidDataException(UiText.Get("error_content_format"));
        // Validate the whole selection before copying or submitting any file.
        if (items.Any(item => !item.IsOfType(StorageItemTypes.File))) throw new InvalidDataException(UiText.Get("error_content_format"));
        var paths = new List<string>();
        foreach (StorageFile file in items)
        {
            ct.ThrowIfCancellationRequested();
            if (Path.IsPathFullyQualified(file.Path) && CanRead(file.Path)) { paths.Add(file.Path); continue; }
            // Virtual/provider-backed files may have no ordinary filesystem path.
            string root = Path.Combine(FilePreferences.ShareStagingDirectory, FileProtocol.NewId());
            Directory.CreateDirectory(root); temporary.Add(root);
            string path = Path.Combine(root, FileProtocol.SafeName(file.Name));
            using var input = await file.OpenStreamForReadAsync();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileProtocol.ChunkBytes, true);
            await input.CopyToAsync(output, FileProtocol.ChunkBytes, ct);
            paths.Add(path);
        }
        operation.ReportDataRetrieved();
        return paths.ToArray();
    }
    static bool CanRead(string path)
    {
        try { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    public void Complete(bool success, string message)
    {
        if (reported) return; reported = true;
        try { if (success) operation.ReportCompleted(); else operation.ReportError(message); }
        catch (COMException) { /* The source may close its share session before transfer completion. */ }
    }
    public void Dispose()
    {
        foreach (string root in temporary) { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
