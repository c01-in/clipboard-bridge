using System.Text;
using System.Text.Json;
using ClipboardBridge;
using QRCoder;

sealed class SettingsForm : Form
{
    readonly Label state = new() { AutoSize = true };
    readonly Label searchState = new() { AutoSize = true };
    readonly Label pairingHelp;
    readonly ListBox phones = new() { Height = 126, Dock = DockStyle.Top, IntegralHeight = false, HorizontalScrollbar = true };
    readonly Button approve = new() { AutoSize = true, Enabled = false };
    readonly TextBox addresses = new() { Dock = DockStyle.Top };
    readonly Label fileProgressText = new() { AutoSize = true, Visible = false };
    readonly ProgressBar fileProgress = new() { Dock = DockStyle.Top, Maximum = 1000, Visible = false };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    readonly List<Label> wrapping = [];
    bool refreshing;
    sealed record PhoneRow(PairPeer Peer)
    {
        public override string ToString() => Peer.Name + "  ·  " + Peer.Code + "  ·  " + UiText.Get(Peer.Completed ? "win_pair_done" : Peer.ComputerApproved ? "win_wait_phone" : "win_confirm_here");
    }
    public SettingsForm()
    {
        Text = UiText.Get("win_settings_title"); Icon = AppIcons.Application; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(700, 600); MinimumSize = new Size(590, 500); StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));scroll.Controls.Add(content);Controls.Add(scroll);
        Label Label(string key, float size = 10)
        {
            var label = new Label { Text = UiText.Get(key), AutoSize = true, Margin = new Padding(0, 0, 0, 12), Font = new Font(Font.FontFamily, size, size > 15 ? FontStyle.Bold : FontStyle.Regular) };
            wrapping.Add(label); content.Controls.Add(label); return label;
        }
        state.Text = UiText.Get("win_connecting");state.Margin = new Padding(0, 0, 0, 20);wrapping.Add(state);content.Controls.Add(state);
        Label("files_folder_heading", 16);
        var folder = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Text = FilePreferences.SaveDirectory };
        content.Controls.Add(folder);
        content.Controls.Add(Button("files_change_folder", () =>
        {
            using var picker = new FolderBrowserDialog { Description = UiText.Get("files_folder_heading"), UseDescriptionForTitle = true, InitialDirectory = FilePreferences.SaveDirectory };
            if (picker.ShowDialog(this) == DialogResult.OK) { FilePreferences.SaveDirectory = picker.SelectedPath; folder.Text = FilePreferences.SaveDirectory; }
            return Task.CompletedTask;
        }));
        content.Controls.Add(fileProgressText); wrapping.Add(fileProgressText); content.Controls.Add(fileProgress);
        Label("win_pair_heading", 16);pairingHelp = Label("win_pair_help");pairingHelp.Visible = false;
        content.Controls.Add(phones);searchState.Margin = new Padding(0, 10, 0, 10);wrapping.Add(searchState);content.Controls.Add(searchState);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        actions.Controls.Add(Button("win_search_phones", async () => { await Ipc.Call(new Command("pairing-open")); await RefreshStatus(); }));
        approve.Text = UiText.Get("confirm_pair");approve.Click += async (_, _) => { try { await Approve(); } catch (Exception e) { MessageBox.Show(UiText.Translate(e.Message), UiText.Get("app_name")); } };actions.Controls.Add(approve);content.Controls.Add(actions);
        phones.SelectedIndexChanged += (_, _) => approve.Enabled = phones.SelectedItem is PhoneRow row && !row.Peer.ComputerApproved && !row.Peer.Completed;
        content.Controls.Add(Button("win_phone_update", () => { Download(); return Task.CompletedTask; }));
        var advanced = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Visible = false, Margin = new Padding(0, 14, 0, 0) };
        content.Controls.Add(Button("win_advanced", () => { advanced.Visible = !advanced.Visible; return Task.CompletedTask; }));
        var addressLabel = new Label { Text = UiText.Get("win_addresses"), AutoSize = true };wrapping.Add(addressLabel);advanced.Controls.Add(addressLabel);advanced.Controls.Add(addresses);
        advanced.Controls.Add(Button("win_save_addresses", () => Action(new Command("endpoints", Value: addresses.Text))));content.Controls.Add(advanced);
        var version = new Label
        {
            Text = UiText.Get("about_version", typeof(SettingsForm).Assembly.GetName().Version!.ToString(3)),
            AutoSize = true, Dock = DockStyle.Bottom, ForeColor = SystemColors.GrayText,
            Padding = new Padding(24, 0, 24, 12)
        };
        Controls.Add(version);
        scroll.SizeChanged += (_, _) => { int width = Math.Max(250, scroll.ClientSize.Width - scroll.Padding.Horizontal - 22);content.Width = width; foreach (var label in wrapping) label.MaximumSize = new Size(width, 0); };
        Shown += async (_, _) => { try { await Ipc.Call(new Command("pairing-open")); } catch (Exception e) { state.Text = UiText.Get("win_status_error", UiText.Translate(e.Message)); } await RefreshStatus(); };
        timer.Tick += async (_, _) => await RefreshStatus();timer.Start();FormClosed += (_, _) => timer.Dispose();
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }
    static Button Button(string key, Func<Task> action)
    {
        var b = new Button { Text = UiText.Get(key), AutoSize = true, Margin = new Padding(0, 0, 10, 8) };
        b.Click += async (_, _) => { b.Enabled = false; try { await action(); } catch (Exception e) { MessageBox.Show(UiText.Translate(e.Message), UiText.Get("app_name")); } finally { if (!b.IsDisposed) b.Enabled = true; } };return b;
    }
    async Task Action(Command command) { var r = await Ipc.Call(command);state.Text = r.Message;if (!r.Ok) MessageBox.Show(r.Message, UiText.Get("app_name")); }
    async Task Approve()
    {
        if (phones.SelectedItem is not PhoneRow row || row.Peer.Completed) return;
        if (MessageBox.Show(this, UiText.Get("win_pair_message", row.Peer.Name, row.Peer.Code), UiText.Get("confirm_pair"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await Action(new Command("pairing-approve", Value: row.Peer.Id));await RefreshStatus();
    }
    async Task RefreshStatus()
    {
        if (refreshing || IsDisposed) return; refreshing = true;
        try
        {
            var reply = await Ipc.Call(new Command("status"));if (IsDisposed) return;
            using var data = JsonDocument.Parse(reply.Value ?? "{}");var root = data.RootElement;
            string? device = root.GetProperty("device").GetString();
            bool configured = root.GetProperty("configured").GetBoolean();
            pairingHelp.Visible = !configured;
            state.Text = !configured ? UiText.Get("status_unpaired") : UiText.Get(device is null ? "win_no_phone" : "win_connected_device", device is null ? reply.Message : device, reply.Message);
            if (!addresses.Focused && addresses.Text.Length == 0) addresses.Text = string.Join(",", root.GetProperty("endpoints").EnumerateArray().Select(e => e.GetString()));
            var transfer = await Ipc.Call(new Command("files-incoming-status"));
            if (transfer.Ok && transfer.Value is not null)
            {
                var progress = Bridge.Decode<FileTransferStatus>(Encoding.UTF8.GetBytes(transfer.Value));
                fileProgress.Visible = fileProgressText.Visible = progress.Status is "receiving" or "saving";
                fileProgress.Style = progress.Total < 0 || progress.Status == "saving" ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                if (progress.Total > 0) fileProgress.Value = (int)Math.Clamp((double)progress.Bytes / progress.Total * 1000, 0, 1000);
                fileProgressText.Text = progress.Status == "saving" ? UiText.Get("files_saving_pc") : UiText.Get("files_progress", (progress.Bytes / (1024d * 1024)).ToString("0.0") + " MiB", progress.Total < 0 ? "?" : (progress.Total / (1024d * 1024)).ToString("0.0") + " MiB");
            }
            var response = await Ipc.Call(new Command("pairing-status"));if (IsDisposed) return;
            if (!response.Ok || response.Value is null) { searchState.Text = response.Message; return; }
            var pairing = Bridge.Decode<PairState>(Encoding.UTF8.GetBytes(response.Value));
            string? selected = (phones.SelectedItem as PhoneRow)?.Peer.Id;phones.BeginUpdate();phones.Items.Clear();
            foreach (var peer in pairing.Phones) phones.Items.Add(new PhoneRow(peer));
            phones.EndUpdate();int index = pairing.Phones.ToList().FindIndex(p => p.Id == selected);if (phones.Items.Count > 0) phones.SelectedIndex = index >= 0 ? index : 0;
            phones.Visible = approve.Visible = phones.Items.Count > 0;
            searchState.Text = UiText.Get(pairing.Searching ? "win_searching" : "win_search_expired");
        }
        catch (Exception e) { if (!IsDisposed) state.Text = e is TimeoutException or OperationCanceledException ? UiText.Get("win_connect_retry") : e is UnauthorizedAccessException ? UiText.Get("win_permission_error") : UiText.Get("win_status_error", UiText.Translate(e.Message)); }
        finally { refreshing = false; }
    }
    void Download()
    {
        string endpoint = Bridge.Addresses().FirstOrDefault() ?? "http://127.0.0.1:45871";
        using var form = new Form { Text = UiText.Get("win_phone_update"), Icon = AppIcons.Application, ClientSize = new Size(460, 540), MinimumSize = new Size(400, 500), Font = Font, StartPosition = FormStartPosition.CenterParent };
        var instructions = new Label { Text = UiText.Get("win_download_help"), Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(460, 0), Padding = new Padding(18) };
        form.SizeChanged += (_, _) => instructions.MaximumSize = new Size(form.ClientSize.Width, 0);
        string url = endpoint + "/app/android";
        using var generator = new QRCodeGenerator();using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);using var png = new PngByteQRCode(data);using var input = new MemoryStream(png.GetGraphic(5));
        using var bitmap = new Bitmap(input);var image = new PictureBox { Image = bitmap, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        var address = new TextBox { Text = url, ReadOnly = true, Dock = DockStyle.Bottom };
        form.Controls.Add(image);form.Controls.Add(instructions);form.Controls.Add(address);form.ShowDialog(this);
    }
}
