using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipboardBridge;

static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.SequenceEqual(new[]{"--share-identity"})) { Console.WriteLine(ShareFiles.Identity()); return; }
        var share = ShareFiles.Activated();
        if (share is not null)
        {
            using (share) Application.Run(new FileSendForm(share));
            return;
        }
        if (args.Length > 0 && args[0] == "--send-files")
        {
            if (args.Length > 1) Application.Run(new FileSendForm(args.Skip(1).ToArray()));
            return;
        }
        using var mutex = new Mutex(true, "Local\\ClipboardBridge.Tray", out bool first);
        bool showSettings = args.Contains("--settings");
        if (!first)
        {
            if (showSettings)
            {
                var existing = FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "ClipboardBridge.Clipboard");
                if (existing != IntPtr.Zero) PostMessage(existing, ClipboardWindow.OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero);
            }
            return;
        }
        Application.Run(new TrayContext(showSettings));
    }
}

static class Ipc
{
    public static async Task<Reply> Call(Command command)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var pipe = new NamedPipeClientStream(".", Bridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(2000, ct.Token);
        await Bridge.WriteFrame(pipe, Bridge.Encode(command), ct.Token);
        var reply = Bridge.Decode<Reply>(await Bridge.ReadFrame(pipe, ct.Token));
        return reply with { Message = UiText.Translate(reply.Message) };
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon icon;
    readonly ClipboardWindow window;
    SettingsForm? settings;
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer receiveTimer = new() { Interval = 500 };
    readonly SemaphoreSlim sending = new(1, 1);
    bool paused, sendAgain;
    bool receiving;
    string? failedFileId;
    uint? receivedClipboardSequence;
    string? lastHash;
    string status = UiText.Get("win_connecting");
    public TrayContext(bool showSettings = false)
    {
        window = new ClipboardWindow(); window.Changed += () => { if (!paused && receivedClipboardSequence != ClipboardWindow.Sequence) { debounce.Stop(); debounce.Start(); } }; window.ManualSend += async () => await Send(true);
        window.SettingsRequested += ShowSettings;
        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.Get("win_send_now"), null, async (_, _) => await Send(true));
        menu.Items.Add(UiText.Get("files_choose"), null, (_, _) =>
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = UiText.Get("files_choose"), CheckFileExists = true };
            if (dialog.ShowDialog() == DialogResult.OK) new FileSendForm(dialog.FileNames).Show();
        });
        menu.Items.Add(UiText.Get("files_open_folder"), null, (_, _) =>
        {
            try { Directory.CreateDirectory(FilePreferences.SaveDirectory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FilePreferences.SaveDirectory) { UseShellExecute = true }); }
            catch (Exception e) { Notify(e.Message, true); }
        });
        var toggle = new ToolStripMenuItem(UiText.Get("win_pause")) { CheckOnClick = true };
        toggle.CheckedChanged += (_, _) => { paused = toggle.Checked; SavePreferences(); icon!.Text = paused ? UiText.Get("win_paused") : UiText.Get("win_automatic"); icon.Icon = paused ? AppIcons.Paused : AppIcons.Tray; }; menu.Items.Add(toggle);
        menu.Items.Add(UiText.Get("win_open_settings"), null, (_, _) => ShowSettings());
        menu.Items.Add(UiText.Get("win_quit"), null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = AppIcons.Tray, Text = UiText.Get("app_name"), Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowSettings();
        debounce.Tick += async (_, _) => { debounce.Stop(); await Send(false); };
        receiveTimer.Tick += async (_, _) => await Receive(); receiveTimer.Start();
        var pref = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge", "paused.txt");
        paused = File.Exists(pref) && File.ReadAllText(pref) == "true"; toggle.Checked = paused;
        if (showSettings || !File.Exists(pref)) ShowSettings();
        if (!window.HotkeyRegistered) Notify(UiText.Get("win_hotkey_busy"), true);
    }
    void SavePreferences()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge");
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "paused.txt"), paused ? "true" : "false");
    }
    async Task Send(bool manual)
    {
        if (!manual && receivedClipboardSequence == ClipboardWindow.Sequence) return;
        if (!await sending.WaitAsync(0)) { sendAgain = true; if (manual) Notify(UiText.Get("win_sending_previous"), false); return; }
        try
        {
            uint sequence = ClipboardWindow.Sequence;
            (string mime, byte[] bytes) = await ReadClipboard();
            if (!manual && (sequence != ClipboardWindow.Sequence || receivedClipboardSequence == ClipboardWindow.Sequence)) return;
            if (bytes.Length == 0) return;
            if (bytes.Length > Bridge.MaxBytes) throw new InvalidOperationException(UiText.Get("error_content_size"));
            string hash = mime + Convert.ToHexString(SHA256.HashData(bytes));
            if (!manual && hash == lastHash) return;
            var reply = await Ipc.Call(new Command("send", mime, Convert.ToBase64String(bytes)));
            status = reply.Message;
            if (reply.Ok && sequence == ClipboardWindow.Sequence) lastHash = hash;
            if (manual || !reply.Ok) Notify(reply.Message, !reply.Ok);
        }
        catch (Exception e) { status = e is TimeoutException ? UiText.Get("win_connect_retry") : UiText.Translate(e.Message); if (manual || e is not ExternalException) Notify(status, true); }
        finally { sending.Release(); if (sendAgain) { sendAgain = false; debounce.Stop(); debounce.Start(); } }
    }
    async Task Receive()
    {
        if (receiving) return;
        receiving = true;
        try
        {
            var files = await Ipc.Call(new Command("files-incoming"));
            if (files.Ok && files.Value is not null)
            {
                var batch = Bridge.Decode<FileInboxBatch>(Encoding.UTF8.GetBytes(files.Value));
                try
                {
                    string[] saved = await Task.Run(() => FileInbox.Save(batch, FilePreferences.SaveDirectory, Path.Combine(FilePreferences.DirectoryPath, "file-receipts")));
                    var ack = await Ipc.Call(new Command("files-incoming-ack", Value: batch.Id));
                    if (!ack.Ok) throw new IOException(ack.Message);
                    File.Delete(Path.Combine(FilePreferences.DirectoryPath, "file-receipts", batch.Id + ".json"));
                    failedFileId = null; Notify(UiText.Get("files_received", saved.Length, Path.GetDirectoryName(saved[0])), false);
                }
                catch (Exception e)
                {
                    if (failedFileId != batch.Id) { failedFileId = batch.Id; Notify(UiText.Get("files_save_failed", e.Message), true); }
                }
            }
            var reply = await Ipc.Call(new Command("receive"));
            if (!reply.Ok || reply.Value is null) return;
            var clip = Bridge.Decode<IncomingClip>(Encoding.UTF8.GetBytes(reply.Value));
            Receipt receipt;
            try
            {
                WindowsClipboard.Apply(clip);
                receivedClipboardSequence = ClipboardWindow.Sequence;
                debounce.Stop(); sendAgain = false; lastHash = null;
                receipt = new Receipt(clip.Manifest.Id, clip.Manifest.Sequence, "applied", null);
                status = UiText.Get("win_clipboard_written");
            }
            catch (Exception e)
            {
                receipt = new Receipt(clip.Manifest.Id, clip.Manifest.Sequence, "error", UiText.Get("win_clipboard_failed", e.Message));
                status = receipt.Detail!;
            }
            await Ipc.Call(new Command("receive-ack", Value: System.Text.Json.JsonSerializer.Serialize(receipt, Bridge.Json)));
            Notify(status, receipt.Status != "applied");
        }
        catch (Exception)
        { /* The next timer tick reconnects after a service restart. */ }
        finally { receiving = false; }
    }
    static async Task<(string, byte[])> ReadClipboard()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using var image = Clipboard.GetImage(); if (image is null) return ("", []);
                    using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return ("image/png", stream.ToArray());
                }
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count != 1) throw new InvalidOperationException(UiText.Get("win_image_type"));
                    var path = files[0]!;
                    if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return ("", []);
                    if (new FileInfo(path).Length > Bridge.MaxBytes) throw new InvalidOperationException(UiText.Get("win_image_size"));
                    using var image = Image.FromFile(path); using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return ("image/png", stream.ToArray());
                }
                if (Clipboard.ContainsText()) return ("text/plain", Encoding.UTF8.GetBytes(Clipboard.GetText(TextDataFormat.UnicodeText)));
                return ("", []);
            }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(75); }
        }
        return ("", []);
    }
    void Notify(string text, bool error) { icon.BalloonTipTitle = error ? UiText.Get("win_attention") : UiText.Get("app_name"); icon.BalloonTipText = text.Length > 250 ? text[..250] : text; icon.BalloonTipIcon = error ? ToolTipIcon.Warning : ToolTipIcon.Info; icon.ShowBalloonTip(3500); }
    void ShowSettings()
    {
        if (settings is null || settings.IsDisposed) settings = new SettingsForm();
        if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
        settings.Show(); settings.Activate();
    }
    protected override void ExitThreadCore() { receiveTimer.Dispose(); icon.Visible = false; icon.Dispose(); debounce.Dispose(); window.Dispose(); base.ExitThreadCore(); }
}

sealed class ClipboardWindow : NativeWindow, IDisposable
{
    public const uint OpenSettingsMessage = 0x8001;
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    public static uint Sequence => GetClipboardSequenceNumber();
    public event Action? Changed, ManualSend, SettingsRequested;
    public bool HotkeyRegistered { get; }
    public ClipboardWindow() { CreateHandle(new CreateParams { Caption = "ClipboardBridge.Clipboard", Parent = new IntPtr(-3) }); AddClipboardFormatListener(Handle); HotkeyRegistered = RegisterHotKey(Handle, 1, 0x4000 | 0x0001 | 0x0002, 0x56); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x031D) Changed?.Invoke(); if (m.Msg == 0x0312) ManualSend?.Invoke(); if (m.Msg == OpenSettingsMessage) SettingsRequested?.Invoke(); base.WndProc(ref m); }
    public void Dispose() { RemoveClipboardFormatListener(Handle); UnregisterHotKey(Handle, 1); DestroyHandle(); }
}
