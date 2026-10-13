using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GitCompass.Desktop;

/// <summary>Exercises the actual standalone preview without creating or opening a repository.</summary>
internal static class LiquidGlassChecks
{
    private static ShellWindow? window;
    private static Exception? failure;
    private static int checks;
    private static readonly List<object> captures = [];

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var app = new App { IsTestHost = true };
        app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, args) =>
        {
            failure = args.Exception; args.Handled = true;
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        };
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Exercise(root); }
            catch (Exception error) { failure = error; }
            finally
            {
                if (window is not null)
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult(); window.Close();
                    try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (Exception error) { failure ??= error; }
                }
                File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
                {
                    ChecksPassed = checks,
                    CaptureMethod = "Actual WPF RenderTargetBitmap; page-owned scene/effects included, desktop compositor excluded",
                    Failure = failure?.ToString(),
                    Captures = captures
                }, new JsonSerializerOptions { WriteIndented = true }));
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        if (failure is not null) throw new InvalidOperationException("Actual WPF Liquid Glass preview checks failed. Evidence: " + root, failure);
        Console.WriteLine($"PASS Liquid Glass preview: {checks} native checks. Captures: {root}");
    }

    private static async Task Exercise(string root)
    {
        var stateRoot = Path.Combine(root, "state");
        window = new ShellWindow(stateRoot)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show(); await window.InitializeAsync();
        var initialState = Fingerprint(stateRoot);
        AssertNoProject("before opening the design preview");

        window.ConfigureView("Settings", "Dark"); await Settle();
        var entry = FindControl<ButtonBase>(PageContent, "Open Liquid Glass preview");
        entry.BringIntoView(); await Settle();
        Check(entry.IsEnabled && entry.IsVisible, "Settings offers an enabled preview entry without requiring a project");
        Click(entry); await Settle();
        Check(window.SmokeDescription.Contains("Page: Liquid Glass preview.", StringComparison.Ordinal),
            "the Settings entry opens the standalone Liquid Glass preview route");
        var firstPreview = Preview;
        Check(firstPreview.IsLoaded, "the real preview component is attached to the shown native shell");

        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var size in new[] { (Width: 1200d, Height: 860d), (Width: 640d, Height: 560d) })
        {
            window.ConfigureView("Liquid Glass preview", theme);
            await Resize(size.Width, size.Height);
            var preview = Preview;
            Check(Read<bool>(preview, "IsDark") == (theme == "Dark"), theme + " shell appearance is reflected by the preview");
            AssertFits(theme + " preview " + size.Width + "x" + size.Height);
            var image = Capture(root, $"liquid-{theme.ToLowerInvariant()}-{size.Width:0}x{size.Height:0}", preview);
            Check(AllOpaque(image), theme + " preview capture has an opaque final page with no transparency edges");
            if (!SystemParameters.HighContrast)
                Check(ColoredPixelFraction(Render(preview)) > .02,
                    theme + " preview contains a colored scene rather than a flat theme-card background");
            Check(!string.IsNullOrWhiteSpace(Read<string>(preview, "MaterialStatus")),
                theme + " preview explains the active material or its fallback");
            AssertEffectPreferences(preview, theme + " material");
            AssertNoProject(theme + " preview at " + size.Width + "x" + size.Height);
        }

        window.ConfigureView("Liquid Glass preview", "Dark"); await Resize(1200, 860);
        var activePreview = Preview;
        var slider = FindControl<Slider>(activePreview, "Glass strength");
        Check(slider.Maximum > slider.Minimum && slider.IsEnabled, "the visible glass-strength control has a usable range");
        var value = slider.Minimum + (slider.Maximum - slider.Minimum) * .37;
        slider.Value = value; await Settle();
        Check(Math.Abs(Read<double>(activePreview, "GlassStrength") - value) < .0001,
            "moving the actual strength slider changes the preview's material strength");
        await Resize(640, 560); await Resize(1200, 860);
        Check(ReferenceEquals(activePreview, Preview) && ReferenceEquals(slider, FindControl<Slider>(Preview, "Glass strength")) &&
            Math.Abs(slider.Value - value) < .0001,
            "compact-to-wide resizing preserves the preview instance and current strength selection");

        var initialTab = Read<string>(activePreview, "SelectedSampleTab");
        var initialTexts = TextSignature(activePreview);
        Click(FindControl<ButtonBase>(activePreview, "Changed files sample")); await Settle();
        Check(Read<string>(activePreview, "SelectedSampleTab") != initialTab && TextSignature(activePreview) != initialTexts,
            "the Changed files sample tab changes the visible demonstration content");
        var changedTab = Read<string>(activePreview, "SelectedSampleTab");
        var changedTexts = TextSignature(activePreview);
        Click(FindControl<ButtonBase>(activePreview, "Saved versions sample")); await Settle();
        Check(Read<string>(activePreview, "SelectedSampleTab") != changedTab && TextSignature(activePreview) != changedTexts,
            "the Saved versions sample tab changes the visible demonstration content");
        Click(FindControl<ButtonBase>(activePreview, "Overview sample")); await Settle();
        Check(Read<string>(activePreview, "SelectedSampleTab") == "Overview" && TextSignature(activePreview).Contains("Weekend notes", StringComparison.Ordinal),
            "the Overview sample restores the demonstration project rather than opening a real one");
        var sampleStatus = FindControl<TextBlock>(activePreview, "Glass preview sample status");
        var initialStatus = sampleStatus.Text;
        Click(FindControl<ButtonBase>(activePreview, "Preview sample action")); await Settle();
        Check(sampleStatus.Text != initialStatus && sampleStatus.Text.Contains("No real files", StringComparison.Ordinal),
            "the demonstration action shows a local sample result without performing a Git operation");

        var beforeScene = Render(activePreview);
        var initialScene = Read<int>(activePreview, "SceneIndex");
        Click(FindControl<ButtonBase>(activePreview, "Change scene")); await Settle();
        Check(Read<int>(activePreview, "SceneIndex") != initialScene,
            "the actual Change scene control selects another demonstration background");
        if (!SystemParameters.HighContrast)
            Check(ChangedPixelFraction(beforeScene, Render(activePreview)) > .01,
                "changing the scene changes the rendered background over a visible area");

        Click(FindControl<ButtonBase>(activePreview, "Light preview")); await Settle();
        Check(!Read<bool>(activePreview, "IsDark"), "the Light preview control changes the local visual appearance");
        Click(FindControl<ButtonBase>(activePreview, "Dark preview")); await Settle();
        Check(Read<bool>(activePreview, "IsDark"), "the Dark preview control restores the local visual appearance");
        Check(Fingerprint(stateRoot) == initialState,
            "preview strength, scene, tabs and local appearance do not write persisted preferences or user state");
        AssertNoProject("after using every preview control");

        var unloaded = false;
        activePreview.Unloaded += (_, _) => unloaded = true;
        Click(FindControl<ButtonBase>(PageContent, "Back to Settings")); await Settle();
        Check(window.SmokeDescription.Contains("Page: Settings.", StringComparison.Ordinal) && unloaded && !activePreview.IsLoaded,
            "Back to Settings unloads the preview and returns to the normal application page");
        Check(!ReadPrivate<bool>(activePreview, "subscribed") && ReadPrivate<object?>(activePreview, "uiSettings") is null,
            "unloading the preview detaches its Windows preference listeners");
        Click(FindControl<ButtonBase>(PageContent, "Open Liquid Glass preview")); await Settle();
        Check(Preview.IsLoaded && ReadPrivate<bool>(Preview, "subscribed"),
            "revisiting attaches the preview and resumes its Windows preference listener lifecycle");
        var revisitedScene = Read<int>(Preview, "SceneIndex");
        Click(FindControl<ButtonBase>(Preview, "Change scene")); await Settle();
        Check(Read<int>(Preview, "SceneIndex") == (revisitedScene + 1) % 3,
            "revisiting keeps one scene-change action per click");
        Check(Fingerprint(stateRoot) == initialState, "entering and leaving the preview preserves isolated persistent state");
        AssertNoProject("after leaving and revisiting the preview");
    }

    private static FrameworkElement PageContent => (FrameworkElement)window!.FindName("PageContent");
    private static FrameworkElement Preview => Descendants<FrameworkElement>(PageContent)
        .Single(item => item.GetType().Name == "LiquidGlassPreview");

    private static T Read<T>(object owner, string property)
    {
        var info = owner.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("The preview does not expose " + property + ".");
        return (T)info.GetValue(owner)!;
    }

    private static T ReadPrivate<T>(object owner, string field) =>
        (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void AssertEffectPreferences(FrameworkElement preview, string scenario)
    {
        var effects = Read<bool>(preview, "EffectsEnabled");
        var motion = Read<bool>(preview, "MotionEnabled");
        Check(!motion || effects && SystemParameters.ClientAreaAnimation,
            scenario + " only enables pointer motion when Windows permits animation and material effects are available");
        if (SystemParameters.HighContrast || (RenderCapability.Tier >> 16) == 0)
            Check(!effects, scenario + " uses the solid fallback for high contrast or unavailable rendering acceleration");
        if (!effects)
            Check(Read<string>(preview, "MaterialStatus").Contains("solid", StringComparison.OrdinalIgnoreCase),
                scenario + " explains its solid fallback");
    }

    private static void AssertNoProject(string scenario)
    {
        var model = typeof(ShellWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var workspace = typeof(ShellWindow).GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        var projects = workspace?.GetType().GetProperty("Projects")?.GetValue(workspace) as IEnumerable;
        Check(model.GetType().GetProperty("Snapshot")!.GetValue(model) is null && (projects is null || !projects.Cast<object>().Any()),
            scenario + " has no repository snapshot or registered project");
    }

    private static void AssertFits(string scenario)
    {
        var scroll = (ScrollViewer)window!.FindName("PageScroll");
        var page = PageContent;
        Check(page.ActualWidth >= 300 && scroll.ViewportHeight >= 150,
            scenario + " retains a usable native page viewport");
        Check(scroll.ExtentWidth <= scroll.ViewportWidth + 2,
            scenario + " fits the page without horizontal scrolling");
        var overflow = Descendants<FrameworkElement>(Preview)
            .Where(item => item.IsVisible && item.ActualWidth > 0 && item.ActualHeight > 0 &&
                item is TextBlock or ButtonBase or Slider or TextBox or ComboBox)
            .Select(item => (Item: item, Bounds: item.TransformToAncestor(page).TransformBounds(new Rect(new Point(), item.RenderSize))))
            .Where(item => item.Bounds.Left < -2 || item.Bounds.Right > page.ActualWidth + 2)
            .Select(item => $"{item.Item.GetType().Name} {AutomationProperties.GetName(item.Item)} {item.Bounds.Left:0.0}..{item.Bounds.Right:0.0}")
            .ToArray();
        Check(overflow.Length == 0, scenario + " has no horizontally clipped interactive controls or labels" +
            (overflow.Length == 0 ? "" : ": " + string.Join("; ", overflow)));
    }

    private static T FindControl<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(control => AutomationProperties.GetName(control) == name ||
            control is ContentControl content && content.Content is string text && text == name);

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static string TextSignature(DependencyObject root) =>
        string.Join("\n", Descendants<TextBlock>(root).Where(text => text.IsVisible).Select(text => text.Text));

    private static async Task Resize(double width, double height)
    {
        window!.Width = width; window.Height = height; await Settle();
    }

    private static async Task Settle()
    {
        await window!.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(80); window.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var visited = new HashSet<DependencyObject>();
        return Walk(root);
        IEnumerable<T> Walk(DependencyObject item)
        {
            if (!visited.Add(item)) yield break;
            if (item is T match) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
                foreach (var descendant in Walk(child)) yield return descendant;
            if (item is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
                    foreach (var descendant in Walk(VisualTreeHelper.GetChild(item, i))) yield return descendant;
        }
    }

    private static RenderTargetBitmap Capture(string root, string name, FrameworkElement preview)
    {
        window!.UpdateLayout();
        var surface = (FrameworkElement)window.Content;
        var bitmap = Render(surface);
        using var stream = File.Create(Path.Combine(root, name + ".png"));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        var description = window.SmokeDescription + $" Actual window: {window.ActualWidth:0}x{window.ActualHeight:0}; client: {surface.ActualWidth:0}x{surface.ActualHeight:0}.";
        File.WriteAllText(Path.Combine(root, name + ".txt"), description);
        captures.Add(new
        {
            Name = name, WindowWidth = window.ActualWidth, WindowHeight = window.ActualHeight,
            ClientWidth = surface.ActualWidth, ClientHeight = surface.ActualHeight,
            Description = description, IsDark = Read<bool>(preview, "IsDark"),
            GlassStrength = Read<double>(preview, "GlassStrength"),
            EffectsEnabled = Read<bool>(preview, "EffectsEnabled"), MotionEnabled = Read<bool>(preview, "MotionEnabled"),
            RefractionEnabled = Read<bool>(preview, "RefractionEnabled"),
            MaterialStatus = Read<string>(preview, "MaterialStatus"), RenderingTier = RenderCapability.Tier >> 16,
            HighContrast = SystemParameters.HighContrast, AnimationsEnabled = SystemParameters.ClientAreaAnimation
        });
        return bitmap;
    }

    private static RenderTargetBitmap Render(FrameworkElement surface)
    {
        var width = Math.Max(1, (int)Math.Ceiling(surface.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(surface.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
            // The preview's own scene is sampled by its material layers and belongs to WPF.
            // DWM's separate desktop backdrop is absent, so use an opaque theme underlay.
            drawing.DrawRectangle((Brush)window!.FindResource("WindowBrush"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(surface)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds,
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
            }, null, bounds);
        }
        bitmap.Render(visual); return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }

    private static bool AllOpaque(BitmapSource bitmap)
    {
        var pixels = Pixels(bitmap);
        for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] != 255) return false;
        return true;
    }

    private static double ColoredPixelFraction(BitmapSource bitmap)
    {
        var pixels = Pixels(bitmap); var colored = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (Math.Max(pixels[index], Math.Max(pixels[index + 1], pixels[index + 2])) -
                Math.Min(pixels[index], Math.Min(pixels[index + 1], pixels[index + 2])) > 24) colored++;
        return (double)colored / (pixels.Length / 4);
    }

    private static double ChangedPixelFraction(BitmapSource before, BitmapSource after)
    {
        if (before.PixelWidth != after.PixelWidth || before.PixelHeight != after.PixelHeight) return 1;
        var a = Pixels(before); var b = Pixels(after); var changed = 0;
        for (var index = 0; index < a.Length; index += 4)
            if (Math.Abs(a[index] - b[index]) + Math.Abs(a[index + 1] - b[index + 1]) + Math.Abs(a[index + 2] - b[index + 2]) > 18) changed++;
        return (double)changed / (a.Length / 4);
    }

    private static string Fingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (Directory.Exists(root))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file)));
                hash.AppendData(File.ReadAllBytes(file));
            }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Check(bool result, string meaning)
    {
        if (!result) throw new InvalidOperationException("ASSERTION: " + meaning);
        checks++; Console.WriteLine("PASS " + meaning);
    }
}
