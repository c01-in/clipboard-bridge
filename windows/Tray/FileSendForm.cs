using System.Text;
using System.Text.Json;
using ClipboardBridge;

sealed class FileSendForm : Form
{
    readonly Label status = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(20) };
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 20, Maximum = 1000 };
    readonly Button cancel = new() { AutoSize = true, Dock = DockStyle.Bottom };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    string? id;
    bool polling, completed, starting = true;
    public FileSendForm(string[] paths)
    {
        Text = UiText.Get("app_name"); Icon = AppIcons.Application; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(480, 190); StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(440, 230);
        status.MaximumSize = new Size(ClientSize.Width, 0);
        SizeChanged += (_, _) => status.MaximumSize = new Size(ClientSize.Width, 0);
        status.Text = UiText.Get("files_preparing", paths.Length); cancel.Text = UiText.Get("cancel");
        Controls.Add(progress); Controls.Add(status); Controls.Add(cancel);
        cancel.Enabled = false;
        cancel.Click += async (_, _) => { if (!completed && id is not null) await Cancel(); else Close(); };
        FormClosing += async (_, e) =>
        {
            if (starting && !completed) { e.Cancel = true; return; }
            if (!completed && id is not null) { e.Cancel = true; await Cancel(); if (completed) Close(); }
        };
        FormClosed += (_, _) => timer.Dispose();
        timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) =>
        {
            try
            {
                var reply = await Ipc.Call(new Command("files-send", Value: JsonSerializer.Serialize(paths, Bridge.Json)));
                if (!reply.Ok) throw new IOException(reply.Message);
                id = reply.Value; starting = false; cancel.Enabled = true; timer.Start(); await Poll();
            }
            catch (Exception e) { Done(UiText.Get("send_failed", UiText.Translate(e.Message))); }
        };
    }
    async Task Cancel()
    {
        try { await Ipc.Call(new Command("files-cancel", Value: id)); Done(UiText.Get("files_cancelled")); }
        catch (Exception e) { status.Text = e.Message; }
    }
    async Task Poll()
    {
        if (polling || IsDisposed || completed) return; polling = true;
        try
        {
            var response = await Ipc.Call(new Command("files-status"));
            if (IsDisposed || completed) return;
            var state = Bridge.Decode<FileTransferStatus>(Encoding.UTF8.GetBytes(response.Value ?? "{}"));
            if (state.Id != id) { Done(UiText.Get("files_interrupted")); return; }
            progress.Value = state.Total == 0 ? 0 : (int)Math.Clamp((double)state.Bytes / state.Total * 1000, 0, 1000);
            status.Text = UiText.Get(state.Bytes == state.Total && state.Status == "sending" ? "files_saving_phone" : "files_progress", Format(state.Bytes), Format(state.Total));
            if (state.Status == "saved") { progress.Value = 1000; Done(UiText.Get("files_sent")); }
            else if (state.Status is "cancelled" or "error") Done(UiText.Get("send_failed", UiText.Translate(state.Detail ?? "")));
            else if (state.Status == "pending") status.Text = UiText.Get("files_waiting") + "\n" + state.Detail;
        }
        catch (Exception e) { status.Text = UiText.Get("send_failed", UiText.Translate(e.Message)); }
        finally { polling = false; }
    }
    static string Format(long n) => n >= 1024 * 1024 * 1024 ? (n / (1024d * 1024 * 1024)).ToString("0.0") + " GiB" : (n / (1024d * 1024)).ToString("0.0") + " MiB";
    void Done(string text) { completed = true; starting = false; timer.Stop(); status.Text = text; cancel.Enabled = true; cancel.Text = UiText.Get("files_close"); }
}
