using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.UI.ViewManagement;

namespace GitCompass.Desktop;

/// <summary>
/// A local material experiment. Its optical layers sample the independent vector scene,
/// never the project, desktop or their own content. Pointer movement updates one frame;
/// there are no timers or composition-rendering subscriptions.
/// </summary>
public partial class LiquidGlassPreview : UserControl
{
    private readonly List<Lens> lenses = [];
    private UISettings? uiSettings;
    private bool initialized, subscribed, refreshQueued;

    public LiquidGlassPreview()
    {
        InitializeComponent();
        foreach (var border in new[] { ToolbarLens, ProjectLens, StatusLens, NoteLens, ControlsLens })
            lenses.Add(new Lens(border, Artwork, this));
        initialized = true;
        SizeChanged += (_, _) => UpdateLayoutMode();
        Artwork.SizeChanged += (_, _) => RefreshSamples();
        Loaded += PreviewLoaded;
        Unloaded += PreviewUnloaded;
        ApplyAppearance(false);
        SelectTab("Overview");
    }

    public bool IsDark { get; private set; }
    public double GlassStrength => StrengthSlider.Value;
    public string SelectedSampleTab { get; private set; } = "Overview";
    public int SceneIndex { get; private set; }
    public bool EffectsEnabled { get; private set; }
    public bool RefractionEnabled => EffectsEnabled && LiquidRefractionEffect.IsAvailable;
    public bool MotionEnabled { get; private set; }
    public string MaterialStatus => EffectStatus.Text;

    public void ApplyAppearance(bool dark)
    {
        Dispatcher.VerifyAccess();
        IsDark = dark;
        ApplySystemPreferences();
    }

    private void PreviewLoaded(object sender, RoutedEventArgs args)
    {
        if (!subscribed)
        {
            subscribed = true;
            SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
            try
            {
                uiSettings = new UISettings();
                uiSettings.AdvancedEffectsEnabledChanged += AdvancedEffectsChanged;
            }
            catch (Exception error) when (error is COMException or TypeLoadException or NotSupportedException)
            { uiSettings = null; }
        }
        ApplySystemPreferences();
        UpdateLayoutMode();
        RefreshSamples();
    }

    private void PreviewUnloaded(object sender, RoutedEventArgs args)
    {
        if (!subscribed) return;
        SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
        if (uiSettings is not null) uiSettings.AdvancedEffectsEnabledChanged -= AdvancedEffectsChanged;
        uiSettings = null;
        subscribed = false;
        foreach (var lens in lenses) lens.ResetPointer();
    }

    private void SystemPreferenceChanged(object? sender, PropertyChangedEventArgs args) => QueuePreferenceRefresh();
    private void AdvancedEffectsChanged(UISettings sender, object args) => QueuePreferenceRefresh();
    private void QueuePreferenceRefresh()
    {
        if (!subscribed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!subscribed || refreshQueued) return;
            refreshQueued = true;
            try { ApplySystemPreferences(); }
            finally { refreshQueued = false; }
        });
    }

    private GitCompass.Domain.ThemeContrast.GlassPalette Glass => IsDark ? GitCompass.Domain.ThemeContrast.GlassDark : GitCompass.Domain.ThemeContrast.GlassLight;
    private void ApplySystemPreferences()
    {
        if (!initialized) return;
        var highContrast = SystemParameters.HighContrast;
        var transparency = true;
        try { transparency = uiSettings?.AdvancedEffectsEnabled ?? new UISettings().AdvancedEffectsEnabled; }
        catch (Exception error) when (error is COMException or TypeLoadException or NotSupportedException)
        { transparency = false; }
        EffectsEnabled = !highContrast && transparency && (RenderCapability.Tier >> 16) > 0;
        MotionEnabled = EffectsEnabled && SystemParameters.ClientAreaAnimation;
        SetBrush("GlassTextBrush", highContrast ? SystemColors.WindowTextColor : ColorOf(Glass.Text));
        SetBrush("GlassPageBrush", highContrast ? SystemColors.WindowColor : ColorOf(Glass.Page));
        SetBrush("GlassMutedBrush", highContrast ? SystemColors.WindowTextColor : ColorOf(Glass.Muted));
        SetBrush("GlassAccentBrush", highContrast ? SystemColors.HighlightColor : ColorOf(Glass.Accent));
        SetBrush("GlassButtonBrush", highContrast ? SystemColors.WindowColor : ColorOf(Glass.Button));
        SetBrush("GlassButtonEdgeBrush", highContrast ? SystemColors.WindowTextColor : ColorOf(IsDark ? "#8EA5C6EF" : "#B7FFFFFF"));
        Artwork.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        SceneRoot.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(ColorOf(Glass.Scene));
        SceneFrame.BorderBrush = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(ColorOf(IsDark ? "#7A9DAFDB" : "#B8FFFFFF"));
        UpdateScene();
        UpdateMaterial();
        UpdateTabButtons();
        LightButton.Background = IsDark ? (Brush)Resources["GlassButtonBrush"] : SelectedButtonBrush();
        DarkButton.Background = IsDark ? SelectedButtonBrush() : (Brush)Resources["GlassButtonBrush"];
        EffectStatus.Text = highContrast
            ? "High contrast is on: this preview uses solid Windows colors."
            : !EffectsEnabled
                ? "Windows transparency or accelerated effects are unavailable: the preview uses solid surfaces."
                : MotionEnabled
                    ? RefractionEnabled
                        ? "Live scene blur, refracted edges, glossy rims and pointer reflections. A custom Liquid Glass-inspired material."
                        : "Live scene blur, offset lens sampling, glossy rims and pointer reflections. Refraction is unavailable on this device."
                    : RefractionEnabled
                        ? "Live scene blur, refracted edges and glossy rims. Pointer movement follows your reduced Windows animation preference."
                        : "Live scene blur and glossy rims. Refraction is unavailable; pointer movement follows your reduced animation preference.";
    }

    private void SetBrush(string key, Color color) => Resources[key] = new SolidColorBrush(color);
    private static Color ColorOf(string color) => (Color)ColorConverter.ConvertFromString(color);
    private Brush SelectedButtonBrush() => SystemParameters.HighContrast
        ? SystemColors.HighlightBrush
        : new SolidColorBrush(ColorOf(IsDark ? "#D9DDEAFF" : "#E6FFFFFF"));

    private void LightClicked(object sender, RoutedEventArgs args) => ApplyAppearance(false);
    private void DarkClicked(object sender, RoutedEventArgs args) => ApplyAppearance(true);
    private void StrengthChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    { if (initialized) UpdateMaterial(); }
    private void SceneClicked(object sender, RoutedEventArgs args)
    {
        SceneIndex = (SceneIndex + 1) % 3;
        UpdateScene();
        RefreshSamples();
        SampleStatus.Text = $"Scene {SceneIndex + 1} of 3. Sample data only; your project is unchanged.";
    }
    private void TabClicked(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string tab }) SelectTab(tab); }

    private void SelectTab(string tab)
    {
        SelectedSampleTab = tab;
        switch (tab)
        {
            case "Changed files":
                ProjectHeading.Text = "A little progress.";
                ProjectDescription.Text = "See what you changed before keeping a version. In the real app, you can review each file first.";
                DetailOne.Text = "ideas.md · Updated";
                DetailTwo.Text = "weekend.txt · Updated";
                DetailThree.Text = "photos.txt · New file";
                NextStepHeading.Text = "Review, then save.";
                NextStepDescription.Text = "Choose only the changes you want in your next checkpoint.";
                SampleAction.Content = "Preview these changes  →";
                break;
            case "Saved versions":
                ProjectHeading.Text = "Room to come back.";
                ProjectDescription.Text = "Saved versions are checkpoints. You can look back at a working idea without changing today’s files.";
                DetailOne.Text = "Weekend plan · Today, 10:42";
                DetailTwo.Text = "First ideas · Yesterday, 18:15";
                DetailThree.Text = "2 versions kept on this computer";
                NextStepHeading.Text = "Keep your history.";
                NextStepDescription.Text = "Give each version a short, useful name. Future you will thank you.";
                SampleAction.Content = "Preview the latest version  →";
                break;
            default:
                ProjectHeading.Text = "Weekend notes";
                ProjectDescription.Text = "A few ideas, a little progress. Everything is saved on this computer until you choose to share.";
                DetailOne.Text = "3 files changed";
                DetailTwo.Text = "2 local versions";
                DetailThree.Text = "Ready when you are.";
                NextStepHeading.Text = "Keep a version.";
                NextStepDescription.Text = "Save a checkpoint before you try something new. You can come back to it later.";
                SampleAction.Content = "Preview a saved version  →";
                break;
        }
        UpdateTabButtons();
        SampleStatus.Text = $"{SelectedSampleTab} sample. These controls do not open or change your project.";
    }
    private void SampleActionClicked(object sender, RoutedEventArgs args) => SampleStatus.Text =
        SelectedSampleTab == "Changed files"
            ? "Sample preview: ideas.md, weekend.txt and photos.txt. No real files were opened or changed."
            : "Sample preview: Weekend plan — 2 local checkpoints. No real files were opened or changed.";

    private void UpdateTabButtons()
    {
        foreach (var button in new[] { OverviewButton, ChangesButton, VersionsButton })
        {
            var selected = button.Tag?.ToString() == SelectedSampleTab;
            button.Background = selected ? SelectedButtonBrush() : Brushes.Transparent;
            button.Foreground = selected && SystemParameters.HighContrast ? SystemColors.HighlightTextBrush
                : selected && IsDark ? new SolidColorBrush(ColorOf("#1C3152")) : (Brush)Resources["GlassTextBrush"];
            button.BorderBrush = selected ? (Brush)Resources["GlassButtonEdgeBrush"] : Brushes.Transparent;
        }
        if (SystemParameters.HighContrast)
        {
            LightButton.Foreground = !IsDark ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush;
            DarkButton.Foreground = IsDark ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush;
        }
        else
        {
            LightButton.Foreground = !IsDark ? new SolidColorBrush(ColorOf("#1C3152")) : (Brush)Resources["GlassTextBrush"];
            DarkButton.Foreground = IsDark ? new SolidColorBrush(ColorOf("#1C3152")) : (Brush)Resources["GlassTextBrush"];
        }
    }

    private void UpdateScene()
    {
        var dark = IsDark;
        var colors = SceneIndex switch
        {
            1 => dark ? new[] { "#0B3140", "#214B66", "#496360" } : new[] { "#C9EFF0", "#C0D8FA", "#F9DEC7" },
            2 => dark ? new[] { "#38213F", "#3F335B", "#754454" } : new[] { "#F3CFDC", "#DEDDFA", "#F9DDAF" },
            _ => dark ? new[] { "#0D244D", "#353862", "#795562" } : new[] { "#BBDDFB", "#E1DDFA", "#F8CFC4" }
        };
        SceneBase.Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(ColorOf(colors[0]), 0), new(ColorOf(colors[1]), .55), new(ColorOf(colors[2]), 1)
        }, new Point(0, 0), new Point(1, 1));
        BlueOrb.Fill = new SolidColorBrush(ColorOf(SceneIndex == 1 ? "#21AFC2" : SceneIndex == 2 ? "#C158D4" : "#1679F5"));
        LilacOrb.Fill = new SolidColorBrush(ColorOf(SceneIndex == 1 ? "#79A9F1" : SceneIndex == 2 ? "#AC83F4" : "#986EFF"));
        PeachOrb.Fill = new SolidColorBrush(ColorOf(SceneIndex == 1 ? "#E7D59E" : SceneIndex == 2 ? "#FFD199" : "#FFC8B3"));
        BlueOrb.Opacity = dark ? .62 : .84;
        LilacOrb.Opacity = dark ? .65 : .88;
        PeachOrb.Opacity = dark ? .35 : .97;
        Canvas.SetLeft(BlueOrb, -310 + SceneIndex * 140);
        Canvas.SetTop(LilacOrb, 10 + SceneIndex * 55);
        Canvas.SetLeft(PeachOrb, 590 - SceneIndex * 115);
    }

    private void UpdateMaterial()
    {
        foreach (var lens in lenses) lens.SetAppearance(IsDark, GlassStrength / 100, EffectsEnabled, SystemParameters.HighContrast);
        SampleBadge.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(ColorOf(IsDark ? "#3E627BAB" : "#63FFFFFF"));
        LocalFirstBadge.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(ColorOf(IsDark ? "#3E627BAB" : "#63FFFFFF"));
        RefreshSamples();
    }

    private void UpdateLayoutMode()
    {
        if (!initialized || ActualWidth <= 0) return;
        var contentWidth = Math.Max(0, ActualWidth - PreviewSurface.Padding.Left - PreviewSurface.Padding.Right);
        var narrow = contentWidth < 700;
        SceneContent.Margin = new(narrow ? 18 : 28);
        ProjectLens.Margin = narrow ? new(0, 0, 0, 16) : new(0, 0, 20, 0);
        lenses.First(lens => lens.Border == ProjectLens).SetPadding(new(narrow ? 24 : 30));
        ProjectHeading.FontSize = narrow ? 31 : 37;
        Grid.SetColumnSpan(ProjectLens, narrow ? 2 : 1);
        Grid.SetColumn(SidePanel, narrow ? 0 : 1);
        Grid.SetRow(SidePanel, narrow ? 1 : 0);
        Grid.SetColumnSpan(SidePanel, narrow ? 2 : 1);
        var stackIntro = contentWidth < 850;
        Grid.SetColumnSpan(IntroText, stackIntro ? 2 : 1);
        Grid.SetRow(AppearanceControls, stackIntro ? 1 : 0);
        Grid.SetColumn(AppearanceControls, stackIntro ? 0 : 1);
        AppearanceControls.Margin = stackIntro ? new(0, 14, 0, 0) : new(0);
        var stackControls = contentWidth < 540;
        Grid.SetColumnSpan(StrengthPanel, stackControls ? 2 : 1);
        Grid.SetRow(SceneButton, stackControls ? 1 : 0);
        Grid.SetColumn(SceneButton, stackControls ? 0 : 1);
        SceneButton.HorizontalAlignment = stackControls ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        SceneButton.Margin = stackControls ? new(0, 12, 0, 0) : new(0);
        RefreshSamples();
    }
    private void RefreshSamples()
    {
        if (!initialized) return;
        SceneRoot.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, SceneRoot.ActualWidth), Math.Max(0, SceneRoot.ActualHeight)), 30, 30);
        foreach (var lens in lenses) lens.RefreshSample();
    }

    private sealed class Lens
    {
        private readonly Border border;
        private readonly FrameworkElement scene;
        private readonly LiquidGlassPreview owner;
        private readonly Grid optics;
        private readonly Grid content, refractionLayer;
        private readonly VisualBrush sampleBrush, offsetBrush;
        private readonly Rectangle sample, offset, tint;
        private readonly Border rim, glint;
        private readonly LinearGradientBrush highlight;
        private readonly BlurEffect blur = new() { Radius = 10, RenderingBias = RenderingBias.Quality };
        private LiquidRefractionEffect? refraction;
        private double strength;
        private bool enabled;
        private Point pointer = new(.5, .5);

        public Lens(Border border, FrameworkElement scene, LiquidGlassPreview owner)
        {
            this.border = border; this.scene = scene; this.owner = owner;
            content = (Grid)border.Child;
            sampleBrush = new VisualBrush(scene) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            RenderOptions.SetCachingHint(sampleBrush, CachingHint.Cache);
            offsetBrush = sampleBrush.Clone();
            sample = new Rectangle { Fill = sampleBrush, Effect = blur, IsHitTestVisible = false };
            offset = new Rectangle { Fill = offsetBrush, Opacity = .18, IsHitTestVisible = false };
            tint = new Rectangle { IsHitTestVisible = false };
            optics = new Grid { IsHitTestVisible = false };
            refractionLayer = new Grid { IsHitTestVisible = false };
            refractionLayer.Children.Add(sample);
            optics.Children.Add(refractionLayer); optics.Children.Add(offset); optics.Children.Add(tint);
            // Fill the Border, including its padding, while keeping text in the original grid.
            border.Child = null;
            var shell = new Grid();
            shell.Children.Add(optics);
            content.Margin = border.Padding;
            border.Padding = new Thickness(0);
            shell.Children.Add(content);
            highlight = new LinearGradientBrush(new GradientStopCollection
            {
                new(ColorOf("#DEFFFFFF"), 0), new(ColorOf("#24FFFFFF"), .34), new(ColorOf("#12FFFFFF"), .68), new(ColorOf("#99D7ECFF"), 1)
            }, new Point(0, 0), new Point(1, 1));
            rim = new Border { CornerRadius = border.CornerRadius, BorderThickness = new Thickness(1.1), BorderBrush = highlight, IsHitTestVisible = false };
            glint = new Border { CornerRadius = border.CornerRadius, Margin = new Thickness(2), BorderThickness = new Thickness(1, 1, 0, 0), BorderBrush = new SolidColorBrush(ColorOf("#46FFFFFF")), IsHitTestVisible = false };
            shell.Children.Add(rim); shell.Children.Add(glint);
            border.Child = shell;
            border.Background = Brushes.Transparent;
            border.Effect = new DropShadowEffect { Color = ColorOf("#203355"), BlurRadius = 26, ShadowDepth = 8, Opacity = .12, RenderingBias = RenderingBias.Quality };
            border.SizeChanged += (_, _) => RefreshSample();
            border.MouseMove += PointerMoved;
            border.MouseLeave += (_, _) => ResetPointer();
        }

        public Border Border => border;
        public void SetPadding(Thickness padding) => content.Margin = padding;

        public void SetAppearance(bool dark, double strength, bool enabled, bool highContrast)
        {
            this.strength = strength; this.enabled = enabled;
            optics.Opacity = 1;
            refractionLayer.Visibility = offset.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            sample.Opacity = .7 + strength * .2;
            offset.Opacity = .07 + strength * .18;
            blur.Radius = 3 + strength * 13;
            tint.Fill = highContrast ? SystemColors.WindowBrush
                : new SolidColorBrush(dark
                    ? Color.FromArgb((byte)(enabled ? 200 - strength * 40 : 255), 23, 40, 70)
                    : Color.FromArgb((byte)(enabled ? 220 - strength * 85 : 255), 247, 250, 255));
            rim.BorderBrush = highContrast ? SystemColors.WindowTextBrush : highlight;
            glint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            border.Effect = highContrast || !enabled ? null : new DropShadowEffect { Color = ColorOf("#203355"), BlurRadius = 26, ShadowDepth = 8, Opacity = dark ? .22 : .12, RenderingBias = RenderingBias.Quality };
            if (enabled && LiquidRefractionEffect.IsAvailable)
            {
                refraction ??= new LiquidRefractionEffect();
                refraction.Strength = strength;
                refractionLayer.Effect = refraction;
            }
            else refractionLayer.Effect = null;
            ResetPointer();
        }

        public void RefreshSample()
        {
            var width = border.ActualWidth; var height = border.ActualHeight;
            if (width <= 0 || height <= 0) return;
            var radius = border.CornerRadius.TopLeft;
            optics.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
            if (refraction is not null)
            {
                refraction.AspectRatio = width / height;
                refraction.Radius = radius / height;
            }
            if (!enabled || scene.ActualWidth <= 0 || scene.ActualHeight <= 0) return;
            try
            {
                var origin = border.TransformToVisual(scene).Transform(new Point(0, 0));
                sampleBrush.Viewbox = new Rect(origin.X, origin.Y, width, height);
                // A separately offset, slightly magnified scene creates a lens-like double edge.
                // It is deliberately an approximation, not a physical refraction shader.
                var inset = 2 + strength * 4;
                offsetBrush.Viewbox = new Rect(origin.X + inset + (pointer.X - .5) * 5,
                    origin.Y + inset + (pointer.Y - .5) * 5,
                    Math.Max(1, width - inset * 2), Math.Max(1, height - inset * 2));
            }
            catch (InvalidOperationException) { /* A detached page will sample after its next layout. */ }
        }

        private void PointerMoved(object sender, MouseEventArgs args)
        {
            if (!enabled || !owner.MotionEnabled) return;
            var position = args.GetPosition(border);
            pointer = new(Math.Clamp(position.X / Math.Max(1, border.ActualWidth), 0, 1),
                Math.Clamp(position.Y / Math.Max(1, border.ActualHeight), 0, 1));
            highlight.StartPoint = new Point(pointer.X * .55, pointer.Y * .55);
            highlight.EndPoint = new Point(1 - pointer.X * .25, 1 - pointer.Y * .25);
            RefreshSample();
        }
        public void ResetPointer()
        {
            pointer = new(.5, .5);
            highlight.StartPoint = new Point(0, 0); highlight.EndPoint = new Point(1, 1);
            RefreshSample();
        }
    }
}
