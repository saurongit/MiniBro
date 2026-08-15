using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniBro;

public partial class MainForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkFg = Color.FromArgb(224, 224, 224);

    private readonly string? _target;
    private readonly WebView2 _web;

    public MainForm(string? target)
    {
        _target = target;
        InitializeComponent();

        Text = "MiniBro";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = DarkBg;
        ForeColor = DarkFg;
        Opacity = 0;

        _web = new WebView2
        {
            Dock = DockStyle.Fill,
            BackColor = DarkBg,
            DefaultBackgroundColor = DarkBg,
        };
        Controls.Add(_web);

        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            string path = ResolveTarget();

            if (!File.Exists(path) && !Directory.Exists(path) && !IsUrl(path))
            {
                Close();
                MessageBox.Show($"Не удалось открыть:\n{path}\n\nФайл или адрес не найден.", "MiniBro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            await _web.EnsureCoreWebView2Async();
            _web.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "document.documentElement.style.colorScheme = 'dark';" +
                "document.addEventListener('keydown', e => { if (e.key === 'Escape') { window.close(); } });");

            var painted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _web.CoreWebView2.NavigationCompleted += (_, _) => painted.TrySetResult(true);

            if (IsUrl(path))
            {
                Text = "MiniBro — " + path;
                _web.CoreWebView2.Navigate(path);
            }
            else
            {
                string full = Path.GetFullPath(path);
                Text = "MiniBro — " + Path.GetFileName(full);
                string uri = new Uri(full).AbsoluteUri;
                if (Directory.Exists(full) && !uri.EndsWith("/"))
                    uri += "/";
                _web.CoreWebView2.Navigate(uri);
            }

            await Task.WhenAny(painted.Task, Task.Delay(5000));
            Opacity = 1;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var result = MessageBox.Show(
                "Не установлена WebView2 Runtime — она нужна для отображения страницы.\n\nСкачать её с сайта Microsoft?",
                "MiniBro", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.OK)
                Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703")
                {
                    UseShellExecute = true
                });
            Close();
        }
        catch (Exception ex)
        {
            Close();
            MessageBox.Show(ex.Message, "MiniBro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string ResolveTarget()
    {
        if (string.IsNullOrWhiteSpace(_target))
            return PickFile();

        string expanded = Environment.ExpandEnvironmentVariables(_target.Trim().Trim('"'));
        return expanded;
    }

    private string PickFile()
    {
        const string dialogTitle = "Выберите HTML-файл";
        using var dialog = new OpenFileDialog
        {
            Title = dialogTitle,
            Filter = "HTML-файлы (*.html;*.htm)|*.html;*.htm|Все файлы (*.*)|*.*",
        };

        using var darkTimer = new System.Windows.Forms.Timer { Interval = 100 };
        darkTimer.Tick += (_, _) =>
        {
            IntPtr hwnd = FindWindow(null, dialogTitle);
            if (hwnd == IntPtr.Zero)
                return;
            darkTimer.Stop();
            TryEnableDarkTitleBar(hwnd);
        };
        darkTimer.Start();

        if (dialog.ShowDialog(this) == DialogResult.OK)
            return dialog.FileName;

        Environment.Exit(0);
        return string.Empty;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TryEnableDarkTitleBar(Handle);
    }

    private static void TryEnableDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        int enabled = 1;
        int hr = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        if (hr != 0)
            _ = DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    private static bool IsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                Reload();
                return true;
            case Keys.Escape:
                Close();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Reload()
    {
        if (_web.CoreWebView2 == null)
            return;
        _web.CoreWebView2.Reload();
    }
}
