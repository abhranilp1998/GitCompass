using System.Text;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GitCompass.Terminal.Windows;

/// <summary>Offline xterm.js renderer. Its host bridge can act only on this view's session.</summary>
public sealed class TerminalView : UserControl, IAsyncDisposable
{
    private readonly WebView2 _webView = new();
    private readonly TerminalSession _session;
    private readonly DispatcherTimer _timer;
    private readonly object _outputLock = new();
    private readonly StringBuilder _pending = new();
    private bool _ready, _disposed, _pendingTruncated;
    private Task? _initialization;
    private bool _pasteDialogOpen;
    private const int MaximumPending = 256 * 1024;

    public TerminalView(TerminalSession session)
    {
        _session = session;
        Content = _webView;
        Focusable = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => FlushOutput(), Dispatcher);
        _timer.Stop();
        session.OutputReceived += ReceiveOutput;
        Loaded += async (_, _) => { try { await InitializeAsync(); } catch (Exception exception) { HostFailure?.Invoke(this, exception.Message); } };
    }

    public TerminalSession Session => _session;
    public event EventHandler<string>? HostFailure;
    public bool RendererReady => _ready;
    public static string DetectRuntimeVersion() => CoreWebView2Environment.GetAvailableBrowserVersionString();

    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TerminalView));
        var assets = Path.Combine(AppContext.BaseDirectory, "terminal");
        if (!File.Exists(Path.Combine(assets, "vendor", "xterm.js")))
            throw new FileNotFoundException("Bundled terminal renderer is missing. Rebuild the local xterm.js assets.");
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitCompass", "webview", "terminal");
        var environment = await CoreWebView2Environment.CreateAsync(null, userData);
        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.SetVirtualHostNameToFolderMapping("terminal.gitcompass.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, args) => args.Cancel = !TerminalBridge.IsDocument(args.Uri);
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!TerminalBridge.IsLocalResource(args.Request.Uri))
                args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked external terminal resource", "Content-Type: text/plain");
        };
        core.WebMessageReceived += HandleMessage;
        core.Navigate(TerminalBridge.Document);
        _timer.Start();
    }

    private void ReceiveOutput(object? sender, TerminalOutputEventArgs args)
    {
        lock (_outputLock)
        {
            if (!_ready || _disposed) return; // First ready uses the session's bounded history.
            if (_pending.Length + args.Text.Length > MaximumPending)
            {
                var remove = Math.Min(_pending.Length, _pending.Length + args.Text.Length - MaximumPending);
                _pending.Remove(0, remove); _pendingTruncated = true;
            }
            _pending.Append(args.Text);
        }
    }

    private void FlushOutput()
    {
        if (!_ready || _disposed || _webView.CoreWebView2 is null || !TerminalBridge.IsDocument(_webView.Source?.AbsoluteUri)) return;
        string output;
        lock (_outputLock)
        {
            if (_pending.Length == 0) return;
            output = (_pendingTruncated ? "\r\n[Terminal rendering skipped earlier output under load.]\r\n" : "") + _pending.ToString();
            _pending.Clear(); _pendingTruncated = false;
        }
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "output", data = output }));
    }

    private async void HandleMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || !TerminalBridge.TryParse(args.Source, args.WebMessageAsJson, out var message)) return;
        try
        {
            switch (message!.Type)
            {
                case "ready":
                    lock (_outputLock) { _pending.Clear(); _pending.Append(_session.ReadScrollback()); _ready = true; }
                    FlushOutput(); break;
                case "input":
                    if (message.Data!.Length > 1 && message.Data.Any(character => character is '\n' or '\r'))
                        await PasteAsync(message.Data!);
                    else await _session.SendInputAsync(message.Data!);
                    break;
                case "resize": _session.Resize(message.Columns, message.Rows); break;
                case "pasteRequest":
                    _session.DemandInputPermission();
                    if (Clipboard.ContainsText()) await PasteAsync(Clipboard.GetText());
                    break;
                case "close": await _session.CloseAsync(); break;
            }
        }
        catch (Exception exception) { HostFailure?.Invoke(this, exception.Message); }
    }

    public Task InterruptAsync() => _session.InterruptAsync();

    public async Task PasteAsync(string text)
    {
        _session.DemandInputPermission();
        if (_pasteDialogOpen || string.IsNullOrEmpty(text)) return;
        if (text.Length > 64 * 1024) throw new ArgumentOutOfRangeException(nameof(text), "Paste is limited to 64 KB.");
        if (text.Contains('\r') || text.Contains('\n'))
        {
            _pasteDialogOpen = true;
            try { if (!ReviewMultilinePaste(text)) return; }
            finally { _pasteDialogOpen = false; }
        }
        await _session.SendInputAsync(text.Replace("\r\n", "\r").Replace('\n', '\r'));
    }

    private bool ReviewMultilinePaste(string text)
    {
        var panel = new DockPanel { Margin = new Thickness(20) };
        var explanation = new TextBlock { Text = "These lines will be sent to PowerShell. They may run immediately and change files or repository history. Review every line before pasting.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(explanation, Dock.Top); panel.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var paste = new Button { Content = "Paste reviewed text", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        buttons.Children.Add(paste); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") });
        var dialog = new Window { Title = $"Review paste — {_session.DisplayName}", Content = panel, Width = 700, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
        paste.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true;
    }

    public async Task CopySelectionAsync()
    {
        if (!_ready || !TerminalBridge.IsDocument(_webView.Source?.AbsoluteUri)) return;
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("window.gitCompassSelection()");
        var selected = JsonSerializer.Deserialize<string>(result);
        if (!string.IsNullOrEmpty(selected)) Clipboard.SetText(selected);
    }

    internal async Task<string> ReadRenderedScreenAsync()
    {
        var result = await _webView.CoreWebView2.ExecuteScriptAsync("window.gitCompassReadScreen()");
        return JsonSerializer.Deserialize<string>(result) ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop();
        _session.OutputReceived -= ReceiveOutput;
        await _session.CloseAsync();
        _webView.Dispose();
    }
}
