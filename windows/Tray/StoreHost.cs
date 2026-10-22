using System.Diagnostics;
using System.IO.Pipes;
using ClipboardBridge;

static class StoreHost
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static DateTime checkedAt;

    public static async Task EnsureRunning()
    {
        if (!DesktopDeployment.PerUser || DateTime.UtcNow - checkedAt < TimeSpan.FromSeconds(2)) return;
        await Gate.WaitAsync();
        try
        {
            if (await Listening()) { checkedAt = DateTime.UtcNow; return; }
            var executable = Path.Combine(AppContext.BaseDirectory, "ClipboardBridge.Service.exe");
            if (!File.Exists(executable)) executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service", "ClipboardBridge.Service.exe"));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            // Child processes use the same per-user pipe and credential scope.
            start.Environment["CLIPBRIDGE_USER_MODE"] = "1";
            using var process = Process.Start(start) ?? throw new IOException("The background process could not start.");
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (await Listening()) { checkedAt = DateTime.UtcNow; return; }
                if (process.HasExited && process.ExitCode != 0) throw new IOException("The background process could not start. Close the desktop edition before using the Store edition.");
                await Task.Delay(100);
            }
            throw new IOException("The background process did not become ready.");
        }
        finally { Gate.Release(); }
    }

    static async Task<bool> Listening()
    {
        using var pipe = new NamedPipeClientStream(".", Bridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(200);
        try
        {
            await pipe.ConnectAsync(timeout.Token);
            await Bridge.WriteFrame(pipe, Bridge.Encode(new Command("status")), timeout.Token);
            return Bridge.Decode<Reply>(await Bridge.ReadFrame(pipe, timeout.Token)).Ok;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException) { return false; }
    }
}
