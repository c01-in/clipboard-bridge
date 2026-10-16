using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;

static class Setup
{
    internal static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClipboardBridge");
    internal static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardBridge", "install.log");
    internal static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
    static string Language()
    {
        string name = CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (language == "zh") return name.Contains("hant") || name.EndsWith("tw") || name.EndsWith("hk") || name.EndsWith("mo") ? "zh-Hant" : "zh-Hans";
        return new[] { "es", "pt", "fr", "de", "ja", "ko" }.Contains(language) ? language : "en";
    }
    internal static string Text(string key, params object[] args)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClipboardBridge.Locale." + Language() + ".xml"))
        {
            string value = XDocument.Load(input).Root.Elements("string").First(x => (string)x.Attribute("name") == key).Value.Replace("\\n", "\n").Replace("\\'", "'");
            value = Regex.Replace(value, @"%(\d+)\$s", m => "{" + (int.Parse(m.Groups[1].Value) - 1) + "}");
            return args.Length == 0 ? value : string.Format(CultureInfo.CurrentUICulture, value, args);
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--silent"))
        {
            try { Install(); return 0; }
            catch (Exception e) { WriteLog(e.ToString()); return 1; }
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm()); return 0;
    }
    static void WriteLog(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        File.AppendAllText(LogPath, text + Environment.NewLine, Encoding.UTF8);
    }
    internal static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); File.WriteAllText(LogPath, "Clipboard Bridge " + Version + Environment.NewLine, Encoding.UTF8);
        string stage = Path.Combine(Path.GetTempPath(), "ClipboardBridge-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClipboardBridge.Payload.zip"))
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string destination = Path.GetFullPath(Path.Combine(stage, entry.FullName));
                    if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid installer payload path");
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)); entry.ExtractToFile(destination);
                }
            }
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"));
            start.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(stage, @"scripts\Install.ps1") + "\" -PackageRoot \"" + stage + "\" -InstallRoot \"" + InstallRoot + "\" -NoStartTray";
            start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
            // Do not inherit PowerShell 7 module paths into Windows PowerShell 5.1.
            start.EnvironmentVariables["PSModulePath"] = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules");
            using (var process = Process.Start(start))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
                process.WaitForExit(); Task.WaitAll(output, errors); WriteLog(output.Result + Environment.NewLine + errors.Result);
                if (process.ExitCode != 0) throw new InvalidOperationException(Text("installer_log", LogPath));
            }
            File.Copy(Path.Combine(stage, "LICENSE"), Path.Combine(InstallRoot, "LICENSE"), true);
            File.Copy(Path.Combine(stage, "THIRD_PARTY_NOTICES.md"), Path.Combine(InstallRoot, "THIRD_PARTY_NOTICES.md"), true);
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClipboardBridge"))
            {
                key.SetValue("DisplayName", "Clipboard Bridge"); key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Clipboard Bridge Contributors"); key.SetValue("InstallLocation", InstallRoot);
                key.SetValue("DisplayIcon", Path.Combine(InstallRoot, @"tray\ClipboardBridge.exe"));
                key.SetValue("URLInfoAbout", "https://github.com/c01-in/clipboard-bridge");
                key.SetValue("UninstallString", "\"" + Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe") + "\" -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(InstallRoot, @"scripts\Uninstall.ps1") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        finally
        {
            string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.GetParent(stage).FullName != expectedParent || !Path.GetFileName(stage).StartsWith("ClipboardBridge-install-", StringComparison.Ordinal)) throw new IOException("Unexpected installer temporary directory");
            Directory.Delete(stage, true);
        }
    }
}

sealed class SetupForm : Form
{
    readonly Button install = new Button(), close = new Button();
    readonly Label status = new Label();
    bool working, complete;
    public SetupForm()
    {
        Text = Setup.Text("app_name") + " · " + Setup.Version;
        Font = new Font("Segoe UI", 11); ClientSize = new Size(620, 360); MinimumSize = new Size(560, 340); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = Setup.Text("about_version", Setup.Version), Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        var help = new Label { Text = Setup.Text("installer_description"), AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        status.AutoSize = true; status.Text = Setup.Text("installer_ready");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        close.Text = Setup.Text("installer_close"); close.AutoSize = true; close.Click += delegate { Close(); };
        install.Text = Setup.Text("installer_install"); install.AutoSize = true; install.Click += InstallClick;
        buttons.Controls.Add(close); buttons.Controls.Add(install);layout.Controls.Add(title);layout.Controls.Add(help);layout.Controls.Add(status);layout.Controls.Add(buttons);Controls.Add(layout);
        layout.SizeChanged += delegate { foreach (var label in new[] { title, help, status }) label.MaximumSize = new Size(Math.Max(250, layout.ClientSize.Width - 52), 0); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) e.Cancel = true; };
    }
    async void InstallClick(object sender, EventArgs args)
    {
        if (complete) { Process.Start(new ProcessStartInfo(Path.Combine(Setup.InstallRoot, @"tray\ClipboardBridge.exe"), "--settings") { UseShellExecute = true }); Close(); return; }
        working = true; install.Enabled = false; close.Enabled = false; status.Text = Setup.Text("installer_installing");
        try { await Task.Run((Action)Setup.Install); complete = true; status.Text = Setup.Text("installer_success"); install.Text = Setup.Text("win_open_settings"); }
        catch (Exception e) { status.Text = Setup.Text("installer_failed", e.Message); }
        finally { working = false; install.Enabled = true; close.Enabled = true; }
    }
}
