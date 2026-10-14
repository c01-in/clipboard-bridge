using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipboardBridge;
using QRCoder;

static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\ClipboardBridge.Tray", out bool first);
        if (!first) return;
        Application.Run(new TrayContext());
    }
}

static class Ipc
{
    public static async Task<Reply> Call(Command command)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var pipe = new NamedPipeClientStream(".", Bridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, ct.Token);
        await Bridge.WriteFrame(pipe, Bridge.Encode(command), ct.Token);
        return Bridge.Decode<Reply>(await Bridge.ReadFrame(pipe, ct.Token));
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon icon;
    readonly ClipboardWindow window;
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 250 };
    readonly System.Windows.Forms.Timer receiveTimer = new() { Interval = 500 };
    readonly SemaphoreSlim sending = new(1, 1);
    bool paused, sendAgain;
    bool receiving;
    uint? receivedClipboardSequence;
    string? lastHash;
    string status = "正在连接后台服务…";
    public TrayContext()
    {
        window = new ClipboardWindow(); window.Changed += () => { if (!paused && receivedClipboardSequence != ClipboardWindow.Sequence) { debounce.Stop(); debounce.Start(); } }; window.ManualSend += async () => await Send(true);
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即发送剪贴板  Ctrl+Alt+V", null, async (_, _) => await Send(true));
        var toggle = new ToolStripMenuItem("暂停 Windows 自动发送") { CheckOnClick = true };
        toggle.CheckedChanged += (_, _) => { paused = toggle.Checked; SavePreferences(); icon!.Text = paused ? "剪贴板同步 · 已暂停" : "剪贴板同步 · 自动"; icon.Icon = paused ? AppIcons.Paused : AppIcons.Tray; }; menu.Items.Add(toggle);
        menu.Items.Add("设置与手机配对", null, (_, _) => ShowSettings());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = AppIcons.Tray, Text = "剪贴板同步", Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowSettings();
        debounce.Tick += async (_, _) => { debounce.Stop(); await Send(false); };
        receiveTimer.Tick += async (_, _) => await Receive(); receiveTimer.Start();
        var pref = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge", "paused.txt");
        paused = File.Exists(pref) && File.ReadAllText(pref) == "true"; toggle.Checked = paused;
        if (!File.Exists(pref)) ShowSettings();
        if (!window.HotkeyRegistered) Notify("Ctrl+Alt+V 已被其他程序占用；可从托盘菜单手动发送", true);
    }
    void SavePreferences()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge");
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "paused.txt"), paused ? "true" : "false");
    }
    async Task Send(bool manual)
    {
        if (!manual && receivedClipboardSequence == ClipboardWindow.Sequence) return;
        if (!await sending.WaitAsync(0)) { sendAgain = true; if (manual) Notify("正在处理上一条剪贴板，随后会发送最新内容", false); return; }
        try
        {
            uint sequence = ClipboardWindow.Sequence;
            (string mime, byte[] bytes) = await ReadClipboard();
            if (!manual && (sequence != ClipboardWindow.Sequence || receivedClipboardSequence == ClipboardWindow.Sequence)) return;
            if (bytes.Length == 0) return;
            if (bytes.Length > Bridge.MaxBytes) throw new InvalidOperationException("内容超过 16 MiB，未发送");
            string hash = mime + Convert.ToHexString(SHA256.HashData(bytes));
            if (!manual && hash == lastHash) return;
            var reply = await Ipc.Call(new Command("send", mime, Convert.ToBase64String(bytes)));
            status = reply.Message;
            if (reply.Ok && sequence == ClipboardWindow.Sequence) lastHash = hash;
            if (manual || !reply.Ok) Notify(reply.Message, !reply.Ok);
        }
        catch (Exception e) { status = e is TimeoutException ? "后台服务未启动，请运行安装脚本" : e.Message; if (manual || e is not ExternalException) Notify(status, true); }
        finally { sending.Release(); if (sendAgain) { sendAgain = false; debounce.Stop(); debounce.Start(); } }
    }
    async Task Receive()
    {
        if (receiving) return;
        receiving = true;
        try
        {
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
                status = "手机内容已写入 Windows 剪贴板，可以粘贴";
            }
            catch (Exception e)
            {
                receipt = new Receipt(clip.Manifest.Id, clip.Manifest.Sequence, "error", "Windows 剪贴板写入失败：" + e.Message);
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
                    if (files.Count != 1) throw new InvalidOperationException("图片文件一次只能发送一张");
                    var path = files[0]!;
                    if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return ("", []);
                    if (new FileInfo(path).Length > Bridge.MaxBytes) throw new InvalidOperationException("图片文件超过 16 MiB");
                    using var image = Image.FromFile(path); using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return ("image/png", stream.ToArray());
                }
                if (Clipboard.ContainsText()) return ("text/plain", Encoding.UTF8.GetBytes(Clipboard.GetText(TextDataFormat.UnicodeText)));
                return ("", []);
            }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(75); }
        }
        return ("", []);
    }
    void Notify(string text, bool error) { icon.BalloonTipTitle = error ? "剪贴板同步需要处理" : "剪贴板同步"; icon.BalloonTipText = text.Length > 250 ? text[..250] : text; icon.BalloonTipIcon = error ? ToolTipIcon.Warning : ToolTipIcon.Info; icon.ShowBalloonTip(3500); }
    void ShowSettings() { var form = new SettingsForm(() => status); form.Show(); }
    protected override void ExitThreadCore() { receiveTimer.Dispose(); icon.Visible = false; icon.Dispose(); debounce.Dispose(); window.Dispose(); base.ExitThreadCore(); }
}

sealed class ClipboardWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    public static uint Sequence => GetClipboardSequenceNumber();
    public event Action? Changed, ManualSend;
    public bool HotkeyRegistered { get; }
    public ClipboardWindow() { CreateHandle(new CreateParams { Caption = "ClipboardBridge.Clipboard", Parent = new IntPtr(-3) }); AddClipboardFormatListener(Handle); HotkeyRegistered = RegisterHotKey(Handle, 1, 0x4000 | 0x0001 | 0x0002, 0x56); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x031D) Changed?.Invoke(); if (m.Msg == 0x0312) ManualSend?.Invoke(); base.WndProc(ref m); }
    public void Dispose() { RemoveClipboardFormatListener(Handle); UnregisterHotKey(Handle, 1); DestroyHandle(); }
}

sealed class SettingsForm : Form
{
    readonly TextBox endpoints = new() { Dock = DockStyle.Top };
    readonly TextBox pairing = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly Label state = new() { AutoSize = true, MaximumSize = new Size(610, 0) };
    readonly PictureBox qr = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(280, 280) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    public SettingsForm(Func<string> lastStatus)
    {
        Text = "剪贴板同步 · 设置与手机配对"; Icon = AppIcons.Application; ClientSize = new Size(660, 710); MinimumSize = new Size(680, 740); Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 292)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Text = "Windows 自动发送 · Android 点击发送", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(state);
        var imports = new FlowLayoutPanel { Dock = DockStyle.Fill };
        imports.Controls.Add(Button("导入 Firebase 发送凭据", async () => await Import("account")));
        imports.Controls.Add(Button("导入 Android Firebase 配置", async () => await Import("android-config"))); layout.Controls.Add(imports);
        var addresses = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; addresses.Controls.Add(new Label { Text = "电脑接收地址（可填局域网或 Tailscale；多个地址用逗号分隔）", AutoSize = true }); addresses.Controls.Add(endpoints); layout.Controls.Add(addresses);
        layout.Controls.Add(Button("保存地址", async () => await Action(new Command("endpoints", Value: endpoints.Text))));
        layout.Controls.Add(new Label { Text = "在手机安装 App 后，扫描二维码或粘贴下面的配对链接。\n配对链接含设备密钥，请勿分享。", AutoSize = true });
        layout.Controls.Add(qr); layout.Controls.Add(pairing);
        layout.Controls.Add(Button("生成配对二维码／链接", async () => { var r = await Action(new Command("pair")); if (r.Ok && r.Value is not null) { pairing.Text = r.Value; using var g = new QRCodeGenerator(); using var data = g.CreateQrCode(r.Value, QRCodeGenerator.ECCLevel.L); using var png = new PngByteQRCode(data); using var ms = new MemoryStream(png.GetGraphic(4)); qr.Image?.Dispose(); qr.Image = new Bitmap(ms); } }));
        Controls.Add(layout);
        Shown += async (_, _) => await RefreshStatus();
        timer.Tick += async (_, _) => await RefreshStatus(); timer.Start();
        FormClosed += (_, _) => { timer.Dispose(); qr.Image?.Dispose(); };
    }
    static Button Button(string text, Func<Task> action) { var b = new Button { Text = text, AutoSize = true, Height = 32 }; b.Click += async (_, _) => { b.Enabled = false; try { await action(); } catch (Exception e) { MessageBox.Show(e.Message, "剪贴板同步"); } finally { if (!b.IsDisposed) b.Enabled = true; } }; return b; }
    async Task<Reply> Action(Command c) { var r = await Ipc.Call(c); state.Text = r.Message; if (!r.Ok) MessageBox.Show(r.Message, "剪贴板同步"); return r; }
    async Task Import(string op) { using var d = new OpenFileDialog { Filter = "JSON 配置|*.json" }; if (d.ShowDialog(this) == DialogResult.OK) await Action(new Command(op, Value: await File.ReadAllTextAsync(d.FileName))); }
    async Task RefreshStatus()
    {
        try
        {
            var r = await Ipc.Call(new Command("status")); state.Text = r.Message;
            if (r.Value is not null && !endpoints.Focused)
            {
                using var value = System.Text.Json.JsonDocument.Parse(r.Value);
                if (endpoints.Text.Length == 0) endpoints.Text = string.Join(",", value.RootElement.GetProperty("endpoints").EnumerateArray().Select(e => e.GetString()));
                var device = value.RootElement.GetProperty("device"); state.Text = (device.ValueKind == System.Text.Json.JsonValueKind.String ? device.GetString() + " · " : "尚未配对手机 · ") + r.Message;
            }
        }
        catch (Exception) { state.Text = "后台服务未启动。请先以管理员身份运行 Install.ps1。"; }
    }
}
