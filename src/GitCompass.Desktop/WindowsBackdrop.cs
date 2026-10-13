using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.ViewManagement;

namespace GitCompass.Desktop;

/// <summary>
/// Uses Windows' documented DWM materials while retaining the normal system window frame.
/// Shell backgrounds must use ShellBackdropBrush; content cards should remain opaque.
/// </summary>
internal sealed class WindowsBackdrop : IDisposable, INotifyPropertyChanged
{
    private const string BrushKey = "ShellBackdropBrush";
    private readonly Window window;
    private readonly UISettings? uiSettings;
    private HwndSource? source;
    private nint powerRegistration;
    private bool dark;
    private bool disposed;
    private bool refreshQueued;

    public WindowsBackdrop(Window window)
    {
        this.window = window;
        // Failure to read a system preference must leave an opaque, usable window.
        try
        {
            uiSettings = new UISettings();
            uiSettings.AdvancedEffectsEnabledChanged += AdvancedEffectsChanged;
        }
        catch (Exception error) when (error is COMException or TypeLoadException or NotSupportedException)
        {
            uiSettings = null;
        }

        window.SourceInitialized += SourceInitialized;
        window.Loaded += Loaded;
        window.Closed += Closed;
        SystemParameters.StaticPropertyChanged += SystemParametersChanged;
        SetSolidBackground();
    }

    public string RequestedMaterial { get; private set; } = "Mica";
    public string EffectiveMaterial { get; private set; } = "Solid";
    public bool IsNativeEnabled => EffectiveMaterial is "Mica" or "Acrylic";
    public string Status { get; private set; } = "The selected effect appears when the window opens.";
    public event EventHandler? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Call after applying the Fluent theme and the application's palette.</summary>
    public void Apply(string? material, bool useDarkTheme)
    {
        window.Dispatcher.VerifyAccess();
        if (disposed) return;
        RequestedMaterial = material is "Solid" or "Acrylic" ? material : "Mica";
        dark = useDarkTheme;
        ApplyCore();
    }

    private void SourceInitialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source is not null)
        {
            source.AddHook(WindowMessage);
            // Energy-saver changes are not guaranteed to produce a general settings notification.
            var powerSaving = new Guid("E00958C0-C213-4ACE-AC77-FECCED2EEEA5"); // GUID_POWER_SAVING_STATUS
            powerRegistration = RegisterPowerSettingNotification(source.Handle, ref powerSaving, 0);
        }
        QueueRefresh();
    }

    private void Loaded(object sender, RoutedEventArgs e) => QueueRefresh();
    private void AdvancedEffectsChanged(UISettings sender, object args) => QueueRefresh();
    private void SystemParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x031E or 0x031A or 0x001A || // Composition, theme, and settings changes.
            (message == 0x0218 && wParam == 0x8013)) // PBT_POWERSETTINGCHANGE.
            QueueRefresh();
        return 0; // WPF and the system retain normal window behavior.
    }

    private void QueueRefresh()
    {
        if (disposed || window.Dispatcher.HasShutdownStarted) return;
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (disposed || refreshQueued) return;
            refreshQueued = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                refreshQueued = false;
                if (!disposed) ApplyCore();
            });
        });
    }

    private void ApplyCore()
    {
        var reason = SolidReason();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            SetSolidBackground();
            UpdateState("Solid", reason ?? "The selected effect appears when the window opens.");
            return;
        }

        if (reason is null && source?.CompositionTarget is not null)
        {
            var value = RequestedMaterial == "Acrylic" ? 3 : 2; // DWMSBT_TRANSIENTWINDOW / DWMSBT_MAINWINDOW.
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            var immersiveDark = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref immersiveDark, sizeof(int));
            // Reveal DWM only after both operations succeed. Unsupported/failing APIs stay opaque.
            if (DwmExtendFrameIntoClientArea(handle, ref margins) >= 0 &&
                DwmSetWindowAttribute(handle, 38, ref value, sizeof(int)) >= 0)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Resources[BrushKey] = Brushes.Transparent;
                UpdateState(RequestedMaterial, RequestedMaterial == "Acrylic"
                    ? "Windows Acrylic gives the window a frosted glass background. Content cards remain solid."
                    : "Windows Mica adds a subtle wallpaper tint around the content.");
                return;
            }
            reason = "Windows could not apply the effect. A solid background is used.";
        }

        RemoveNativeBackdrop(handle);
        SetSolidBackground();
        UpdateState("Solid", reason ?? "A solid background is used.");
    }

    private string? SolidReason()
    {
        if (SystemParameters.HighContrast) return "A solid background follows your Windows contrast theme.";
        if (RequestedMaterial == "Solid") return "A solid background is used.";
        // DWM's documented system-backdrop attribute requires Windows 11 22H2 (build 22621).
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            return "Glass effects require Windows 11 22H2 or later. A solid background is used.";
        if (window.AllowsTransparency) return "A solid background is used for this window.";
        if (SystemParameters.IsRemoteSession) return "A solid background is used during a remote desktop session.";
        try
        {
            if (uiSettings is null) return "Windows appearance preferences could not be read. A solid background is used.";
            if (!uiSettings.AdvancedEffectsEnabled)
                return "A solid background is used because Windows transparency effects are off.";
        }
        catch (COMException)
        {
            return "Windows appearance preferences could not be read. A solid background is used.";
        }
        if (RequestedMaterial == "Acrylic" && GetSystemPowerStatus(out var power) && power.SystemStatusFlag == 1)
            return "A solid background is used while Windows energy saver is on.";
        return null;
    }

    private void RemoveNativeBackdrop(nint handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        var none = 1; // DWMSBT_NONE; AUTO may otherwise keep the Fluent title-bar backdrop.
        _ = DwmSetWindowAttribute(handle, 38, ref none, sizeof(int));
        var margins = new Margins();
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
    }

    private void SetSolidBackground()
    {
        var brush = SystemParameters.HighContrast ? SystemColors.WindowBrush
            : window.TryFindResource("WindowBrush") as SolidColorBrush ?? SystemColors.WindowBrush;
        window.Resources[BrushKey] = brush;
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = brush.Color;
    }

    private void UpdateState(string material, string status)
    {
        if (EffectiveMaterial == material && Status == status) return;
        EffectiveMaterial = material;
        Status = status;
        PropertyChanged?.Invoke(this, new(nameof(EffectiveMaterial)));
        PropertyChanged?.Invoke(this, new(nameof(IsNativeEnabled)));
        PropertyChanged?.Invoke(this, new(nameof(Status)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.SourceInitialized -= SourceInitialized;
        window.Loaded -= Loaded;
        window.Closed -= Closed;
        SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
        if (uiSettings is not null) uiSettings.AdvancedEffectsEnabledChanged -= AdvancedEffectsChanged;
        if (source is not null && !source.IsDisposed) source.RemoveHook(WindowMessage);
        if (powerRegistration != 0)
        {
            _ = UnregisterPowerSettingNotification(powerRegistration);
            powerRegistration = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, uint size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSetting, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
}
