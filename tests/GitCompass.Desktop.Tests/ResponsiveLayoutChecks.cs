using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GitCompass.Desktop;
using GitCompass.Infrastructure.Local;

internal static class ResponsiveLayoutChecks
{
    private static ShellWindow? window;
    private static Exception? failure;
    private static int checks;

    public static void Run(string root)
    {
        var app = new App { IsTestHost = true };
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        if (failure is not null) throw new InvalidOperationException("Actual WPF responsive-layout checks failed. Captures: " + root, failure);
        Console.WriteLine($"PASS responsive layout: {checks} native checks. Captures: {root}");
    }

    private static async Task Exercise(string root)
    {
        window = new ShellWindow(Path.Combine(root, "state"))
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false
        };
        window.Show(); await window.InitializeAsync();
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var size in new[] { (Width: 1200d, Height: 860d), (Width: 900d, Height: 620d), (Width: 760d, Height: 640d), (Width: 640d, Height: 600d), (Width: 640d, Height: 560d) })
        {
            window.ConfigureView("Settings", theme);
            await Resize(size.Width, size.Height);
            Capture(root, $"settings-{theme.ToLowerInvariant()}-{size.Width:0}x{size.Height:0}");
            Check(Math.Abs(window.ActualWidth - size.Width) < 2 && Math.Abs(window.ActualHeight - size.Height) < 2,
                $"{theme} accepts the actual {size.Width:0}x{size.Height:0} window size");
            AssertPageFits($"{theme} Settings {size.Width:0}x{size.Height:0}");
            AssertSettingsRowDoesNotOverlap($"{theme} Settings {size.Width:0}x{size.Height:0}");
            AssertContextUsable($"{theme} header {size.Width:0}x{size.Height:0}");
            AssertClosedDockFillsSpace($"{theme} closed activity {size.Width:0}x{size.Height:0}");
        }

        // Use the same live shell rather than recreating it at each width: layout must recover.
        window.ConfigureView("Settings", "Dark"); await Resize(1200, 860);
        var access = (ComboBox)window.FindName("AccessSelector"); var initialMode = access.SelectedIndex;
        await Resize(640, 600); AssertContextUsable("resized compact header");
        await AssertNavigationDrawer(root);
        await Resize(1200, 860); AssertContextUsable("restored wide header");
        Check(access.SelectedIndex == initialMode, "wide to compact to wide resizing preserves project access selection");
        Check(((FrameworkElement)window.FindName("SidebarSurface")).IsVisible &&
            !((FrameworkElement)window.FindName("NavigationToggleButton")).IsVisible,
            "restoring a wide window returns labeled navigation without leaving an overlay open");
        Capture(root, "settings-dark-restored-wide");
        var interval = LogicalDescendants<TextBox>((FrameworkElement)window.FindName("PageContent"))
            .Single(item => AutomationProperties.GetName(item).StartsWith("Remote check interval", StringComparison.Ordinal));
        interval.Text = "37";
        await Resize(640, 600); await Resize(1200, 860);
        Check(interval.Text == "37" && LogicalDescendants<TextBox>((FrameworkElement)window.FindName("PageContent")).Contains(interval),
            "resizing preserves the current unsaved interval draft and its attached control");

        foreach (var page in new[] { "Overview", "Learn", "Projects" })
        {
            window.ConfigureView(page, "Light"); await Resize(640, 600);
            AssertPageFits("compact " + page); AssertContextUsable("compact " + page);
            Capture(root, page.Replace(' ', '-').ToLowerInvariant() + "-light-640x600");
        }

        // This ordinary folder lives outside the checkout, so parent Git metadata cannot be inherited.
        var candidate = Path.Combine(Path.GetTempPath(), "GitCompass-responsive", Guid.NewGuid().ToString("N"),
            "An ordinary project with a long name for compact windows");
        Directory.CreateDirectory(candidate);
        await File.WriteAllTextAsync(Path.Combine(candidate, "My notes.txt"), "An ordinary project remains unchanged by layout checks.\n");
        await window.OpenProjectAsync(candidate); window.ConfigureView("Overview", "Dark"); await Resize(640, 600);
        AssertPageFits("compact ordinary-folder setup"); AssertContextUsable("compact ordinary-folder header");
        Check(!Directory.Exists(Path.Combine(candidate, ".git")), "resizing and viewing an ordinary folder never initializes Git");
        Capture(root, "ordinary-folder-dark-640x600");

        window.ConfigureView("Settings", "Dark"); await Resize(640, 600);
        var activityToggle = (Button)window.FindName("CompactActivityToggleButton");
        Check(activityToggle.IsVisible, "compact header keeps activity available while the navigation drawer is closed");
        activityToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        AssertActivityUsable("compact activity panel");
        await AssertCompactActivityOptions();
        Capture(root, "settings-dark-640x600-activity-open");
        await Resize(900, 620); AssertActivityUsable("900x620 activity panel");
        Capture(root, "settings-dark-900x620-activity-open");
        await Resize(640, 560); AssertActivityUsable("minimum-size activity panel");
        Capture(root, "settings-dark-640x560-activity-open");
        typeof(ShellWindow).GetMethod("Banner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,
            new object[] { string.Join("\n", Enumerable.Repeat("A detailed project notice remains readable while the page and activity tools stay available.", 12)) });
        await Settle(); Capture(root, "settings-dark-640x560-long-notice"); AssertActivityUsable("minimum-size activity with a long notice");
        var notice = (ScrollViewer)window.FindName("StatusMessageScroll");
        Check(notice.ScrollableHeight > 0 && notice.ViewportHeight >= 18,
            "a long compact notice can be scrolled without moving the dock outside the window");
        Capture(root, "settings-dark-640x560-long-notice");
        ((FrameworkElement)window.FindName("StatusBanner")).Visibility = Visibility.Collapsed; await Settle();
        await AssertCompactTerminal(root);
        activityToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        Check(((FrameworkElement)window.FindName("PageContent")).ActualHeight > 0, "closing activity restores a usable page");
        AssertClosedDockFillsSpace("closed activity after compact resizing");
        await AssertMaterials(root);
    }

    private static void AssertSettingsRowDoesNotOverlap(string scenario)
    {
        var page = (FrameworkElement)window!.FindName("PageContent");
        var appearance = LogicalDescendants<ComboBox>(page).Single(item => AutomationProperties.GetName(item) == "Appearance theme");
        var description = LogicalDescendants<TextBlock>(page).Single(item => item.Text.StartsWith("Follow Windows, or choose", StringComparison.Ordinal));
        var actionBounds = Bounds(appearance, page); var textBounds = Bounds(description, page);
        Check(!actionBounds.IntersectsWith(textBounds), scenario + " appearance description and selector do not overlap");
        Check(appearance.ActualWidth >= 140, scenario + " theme choices keep a usable control width");
        if (appearance.Parent is FrameworkElement row && row.ActualWidth < 600)
            Check(actionBounds.Top >= textBounds.Bottom - 1,
                scenario + " places the theme selector below its description at a narrow card width");
    }

    private static void AssertContextUsable(string scenario)
    {
        var surface = (FrameworkElement)window!.Content;
        var branch = (TextBlock)window.FindName("BranchLabel");
        var access = (ComboBox)window.FindName("AccessSelector");
        var updates = (Button)window.FindName("CheckRemoteButton");
        if (((FrameworkElement)window.FindName("HeaderContext")).IsVisible)
        {
            Check(branch.ActualWidth >= 40 && access.ActualWidth >= 130,
                scenario + " keeps project context and access choices readable");
            var actions = new[] { Bounds(access, surface), Bounds(updates, surface) };
            Check(actions.All(bounds => bounds.Left >= 0 && bounds.Right <= surface.ActualWidth + 1),
                scenario + " keeps both project actions within the window");
            Check(!Bounds(branch, surface).IntersectsWith(actions[0]) && !Bounds(branch, surface).IntersectsWith(actions[1]),
                scenario + " project actions do not overlap project context");
            var context = (FrameworkElement)window.FindName("HeaderContext");
            if (context.ActualWidth < 680)
            {
                var summaryBounds = Bounds((FrameworkElement)window.FindName("ProjectSummary"), surface);
                var actionBounds = Bounds((FrameworkElement)window.FindName("HeaderActions"), surface);
                Check(actionBounds.Top >= summaryBounds.Bottom - 1,
                    scenario + " stacks project actions below the project summary at narrow widths");
            }
        }
        else
        {
            var breadcrumb = (FrameworkElement)window.FindName("PageBreadcrumb"); var bounds = Bounds(breadcrumb, surface);
            Check(breadcrumb.IsVisible && breadcrumb.ActualWidth >= 100 && bounds.Left >= 0 && bounds.Right <= surface.ActualWidth + 1,
                scenario + " keeps the global page context readable without reserving project controls");
        }
        var nav = (ListBox)window.FindName("Navigation");
        Check(nav.Items.OfType<ListBoxItem>().All(item => AutomationProperties.GetName(item).Length > 0),
            scenario + " every navigation destination keeps an accessible name");
        if (((FrameworkElement)window.FindName("RootShell")).ActualWidth < 1000)
            Check(((FrameworkElement)window.FindName("NavigationToggleButton")).IsVisible,
                scenario + " retains a visible navigation menu button");
    }

    private static async Task AssertNavigationDrawer(string root)
    {
        var navigation = (ListBox)window!.FindName("Navigation");
        var sidebar = (FrameworkElement)window.FindName("SidebarSurface");
        var toggle = (Button)window.FindName("NavigationToggleButton");
        Check(!sidebar.IsVisible && toggle.IsVisible && AutomationProperties.GetName(toggle) == "Open navigation", "compact width gives content the full workspace and exposes labeled navigation on demand");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        Check(sidebar.IsVisible && navigation.Items.OfType<ListBoxItem>().All(item => item.IsVisible) &&
            ((FrameworkElement)window.FindName("SidebarScrim")).IsVisible && AutomationProperties.GetName(toggle) == "Close navigation",
            "compact navigation opens all named destinations and an outside-click surface");
        Capture(root, "navigation-drawer-dark-640x600");
        foreach (var tag in new[] { "Learn", "Pull requests", "Settings" })
        {
            navigation.SelectedItem = navigation.Items.OfType<ListBoxItem>().Single(item => item.Tag?.ToString() == tag);
            await Settle();
            Check(sidebar.IsVisible, "changing navigation selection keeps the compact drawer open: " + tag);
        }
        var draft = LogicalDescendants<TextBox>((FrameworkElement)window.FindName("PageContent"))
            .Single(item => AutomationProperties.GetName(item).StartsWith("Remote check interval", StringComparison.Ordinal));
        draft.Text = "58";
        MouseRelease(navigation); await Settle();
        Check(sidebar.IsVisible && draft.Text == "58" &&
            LogicalDescendants<TextBox>((FrameworkElement)window.FindName("PageContent")).Contains(draft),
            "releasing the pointer in empty navigation space neither closes the drawer nor discards a Settings draft");
        var settingsItem = navigation.Items.OfType<ListBoxItem>().Single(item => item.Tag?.ToString() == "Settings");
        MouseRelease(settingsItem); await Settle();
        Check(!sidebar.IsVisible && draft.Text == "58" &&
            LogicalDescendants<TextBox>((FrameworkElement)window.FindName("PageContent")).Contains(draft),
            "activating the current navigation page closes the drawer without rebuilding its unsaved controls");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        var destination = navigation.Items.OfType<ListBoxItem>().Single(item => item.Tag?.ToString() == "Learn");
        navigation.SelectedItem = destination; await Settle(); MouseRelease(destination); await Settle();
        Check(!sidebar.IsVisible && window.SmokeDescription.Contains("Page: Learn"),
            "activating a compact navigation destination opens the page and closes the drawer");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        ((Button)window.FindName("CloseNavigationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        Check(!sidebar.IsVisible, "the compact navigation close button returns to the page");
        window.ConfigureView("Settings", "Dark"); await Settle();
    }

    private static void MouseRelease(UIElement source) => source.RaiseEvent(new MouseButtonEventArgs(
        Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });

    private static void AssertActivityUsable(string scenario)
    {
        var dock = (FrameworkElement)window!.FindName("DockSurface");
        var surface = (FrameworkElement)window.Content;
        var dockBounds = Bounds(dock, surface);
        Check(dock.ActualHeight >= 100 && dockBounds.Bottom <= surface.ActualHeight + 1,
            scenario + $" remains inside the resized window (dock {dock.ActualHeight:0.00}, bottom {dockBounds.Bottom:0.00}, client {surface.ActualHeight:0.00}, header {((FrameworkElement)window.FindName("PageHeader")).ActualHeight:0.00}, banner {((FrameworkElement)window.FindName("StatusBanner")).ActualHeight:0.00}, splitter {((FrameworkElement)window.FindName("DockSplitter")).ActualHeight:0.00}, page row {((RowDefinition)window.FindName("PageRow")).ActualHeight:0.00}, notice max {((FrameworkElement)window.FindName("StatusMessageScroll")).MaxHeight:0.00})");
        var controls = new[] { "OutputChannel", "OutputSearch", "OutputSeverity", "ActivityOptionsButton" }
            .Select(name => (FrameworkElement)window.FindName(name)).ToArray();
        Check(controls.All(item => item.IsVisible && item.ActualWidth >= 30), scenario + " keeps filtering and additional options available");
        Check(controls.All(item => { var rect = Bounds(item, dock); return rect.Left >= -1 && rect.Right <= dock.ActualWidth + 1; }),
            scenario + " keeps toolbar controls inside its horizontal bounds");
        Check(controls.All(item =>
            {
                var rect = Bounds(item, dock);
                return rect.Top >= -1 && rect.Bottom <= dock.ActualHeight + 1 ||
                    Ancestors(item).TakeWhile(ancestor => ancestor != dock).OfType<ScrollViewer>()
                        .Any(scroll => scroll.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible);
            }), scenario + " keeps toolbar controls visible or explicitly scrollable within its height");
        var pageScroll = Ancestors((DependencyObject)window.FindName("PageContent")).OfType<ScrollViewer>().First();
        Check(pageScroll.ViewportHeight >= 120, scenario + " retains enough page space while tools are open");
    }

    private static void AssertClosedDockFillsSpace(string scenario)
    {
        var dockRow = (RowDefinition)window!.FindName("DockRow");
        var splitter = (FrameworkElement)window.FindName("DockSplitter");
        Check(dockRow.ActualHeight <= 1 && (!splitter.IsVisible || splitter.ActualHeight <= 1),
            scenario + " consumes no layout height for the closed dock or splitter");
        var workspace = (FrameworkElement)window.FindName("MainWorkspace");
        var header = (FrameworkElement)window.FindName("PageHeader");
        var banner = (FrameworkElement)window.FindName("StatusBanner");
        var scroll = (ScrollViewer)window.FindName("PageScroll");
        var available = workspace.ActualHeight - header.ActualHeight -
            (banner.IsVisible ? banner.ActualHeight + banner.Margin.Top + banner.Margin.Bottom : 0);
        Check(Math.Abs(scroll.ActualHeight - available) <= 2 &&
            scroll.ViewportHeight >= scroll.ActualHeight - scroll.Padding.Top - scroll.Padding.Bottom - 2,
            scenario + " lets the page viewport fill the remaining client height");
    }

    private static async Task AssertCompactActivityOptions()
    {
        var options = (Button)window!.FindName("ActivityOptionsButton");
        options.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        try
        {
            var items = options.ContextMenu.Items.OfType<MenuItem>().ToArray();
            Check(items.Any(item => item.Header?.ToString() == "Copy activity") && items.Any(item => item.Header?.ToString() == "Clear view") &&
                items.Any(item => item.Header?.ToString() == "Prepare support view"),
                "compact activity menu retains copy, clear and support actions");
            foreach (var (name, label) in new[] { ("AutoScroll", "Auto-scroll"), ("GroupOutput", "Group by operation") })
            {
                var checkbox = (CheckBox)window.FindName(name);
                var menuItem = items.Single(item => item.Header?.ToString() == label);
                var before = checkbox.IsChecked;
                Check(menuItem.IsCheckable && menuItem.IsChecked == checkbox.IsChecked,
                    "compact " + label + " menu reflects its retained control state");
                menuItem.IsChecked = before != true;
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Settle();
                Check(checkbox.IsChecked == menuItem.IsChecked && checkbox.IsChecked != before,
                    "compact " + label + " menu changes the actual activity setting");
            }
        }
        finally { options.ContextMenu.IsOpen = false; }
    }

    private static async Task AssertCompactTerminal(string root)
    {
        var tabs = (TabControl)window!.FindName("DockTabs"); tabs.SelectedIndex = 2; await Settle();
        var dock = (FrameworkElement)window.FindName("DockSurface");
        var create = (Button)window.FindName("NewTerminalButton");
        var options = (Button)window.FindName("TerminalOptionsButton");
        Check(create.IsVisible && options.IsVisible && !create.IsEnabled,
            "compact terminal retains its reviewed access requirement and additional session actions");
        Check(new[] { create, options }.All(item =>
            {
                var rect = Bounds(item, dock);
                return rect.Left >= -1 && rect.Right <= dock.ActualWidth + 1 && rect.Top >= -1 && rect.Bottom <= dock.ActualHeight + 1;
            }), "minimum-size terminal controls fit inside the dock without creating a shell session");
        var labels = options.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
        Check(new[] { "Interrupt", "Copy selection", "Paste", "Close session" }.All(labels.Contains),
            "compact terminal menu retains the existing session controls");
        var terminalArea = (FrameworkElement)window.FindName("TerminalTabs");
        Check(terminalArea.ActualHeight >= 24 && Bounds(terminalArea, dock).Bottom <= dock.ActualHeight + 1,
            "the minimum-size terminal retains a visible session viewport below its compact toolbar");
        Capture(root, "settings-dark-640x560-terminal-open");
        tabs.SelectedIndex = 0; await Settle();
    }

    private static async Task AssertMaterials(string root)
    {
        var activeWindow = window!;
        activeWindow.ConfigureView("Settings", "Dark"); await Resize(760, 640);
        var store = Field<AppStateStore>(activeWindow, "store");
        var before = await store.LoadSettingsAsync();
        Check(!activeWindow.AllowsTransparency && activeWindow.WindowStyle != WindowStyle.None,
            "window effects retain the native Windows frame and normal resizing");
        foreach (var material in new[] { "Solid", "Mica", "Acrylic" })
        {
            var selector = MaterialSelector(activeWindow); selector.SelectedValue = material;
            await Until(async () => (await store.LoadSettingsAsync()).WindowMaterial == material);
            AssertNativeMaterial(activeWindow, material, material + " selection");
            var status = LogicalDescendants<TextBlock>((FrameworkElement)activeWindow.FindName("PageContent"))
                .Single(item => AutomationProperties.GetName(item) == "Window background effect status");
            Check(status.Text == Property(Field<object>(activeWindow, "backdrop"), "Status") && status.Text.Length > 0,
                material + " selection updates the visible effect or fallback explanation");
            Capture(root, "material-" + material.ToLowerInvariant() + "-dark-760x640");
        }
        Check(((await store.LoadSettingsAsync()) with { WindowMaterial = before.WindowMaterial }) == before,
            "changing window material preserves all other saved preferences");

        var restarted = new ShellWindow(store.Root)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            restarted.Show(); await restarted.InitializeAsync(); restarted.ConfigureView("Settings", null);
            await restarted.Dispatcher.InvokeAsync(restarted.UpdateLayout, DispatcherPriority.ApplicationIdle);
            await Task.Delay(80); restarted.UpdateLayout();
            Check(MaterialSelector(restarted).SelectedValue?.ToString() == "Acrylic",
                "a fresh shell restores the saved glass preference");
            AssertNativeMaterial(restarted, "Acrylic", "restarted glass preference");
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            restarted.Closed += (_, _) => closed.TrySetResult(); restarted.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ComboBox MaterialSelector(ShellWindow target) =>
        LogicalDescendants<ComboBox>((FrameworkElement)target.FindName("PageContent"))
            .Single(item => AutomationProperties.GetName(item) == "Window background material");

    private static void AssertNativeMaterial(ShellWindow target, string requested, string scenario)
    {
        var backdrop = Field<object>(target, "backdrop");
        var effective = Property(backdrop, "EffectiveMaterial"); var status = Property(backdrop, "Status");
        Check(Property(backdrop, "RequestedMaterial") == requested && effective is "Mica" or "Acrylic" or "Solid",
            scenario + " distinguishes the requested material from the applied material");
        if (effective == "Solid")
            Check(status.Contains("solid", StringComparison.OrdinalIgnoreCase),
                scenario + " explains the opaque background or system fallback");
        var handle = new WindowInteropHelper(target).Handle;
        Check(handle != 0, scenario + " is checked on an actual native window handle");
        var result = DwmGetWindowAttribute(handle, 38, out var value, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            Check(result >= 0 && value == (effective switch { "Mica" => 2, "Acrylic" => 3, _ => 1 }),
                scenario + " matches the actual Windows system-backdrop attribute");
        else Check(effective == "Solid", scenario + " uses the opaque fallback on an unsupported Windows version");
        Console.WriteLine($"EVIDENCE {scenario}: requested={requested}; effective={effective}; DWM attribute 38={value}; HRESULT=0x{result:X8}; {status}");
    }

    private static T Field<T>(ShellWindow target, string name) =>
        (T)typeof(ShellWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static string Property(object owner, string name) => owner.GetType().GetProperty(name)!.GetValue(owner)?.ToString() ?? "";

    private static async Task Until(Func<Task<bool>> condition)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(10))
        {
            await Settle(); if (await condition()) return;
        }
        throw new TimeoutException("Responsive UI persistence condition did not complete.");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint windowHandle, uint attribute, out int value, uint size);

    private static void AssertPageFits(string scenario)
    {
        var page = (FrameworkElement)window!.FindName("PageContent");
        var pageScroll = Ancestors(page).OfType<ScrollViewer>().First();
        Check(page.ActualWidth >= 300 && pageScroll.ViewportHeight >= 100, scenario + " leaves a usable content viewport");
        var overflow = LogicalDescendants<FrameworkElement>(page)
            .Where(item => item.IsVisible && item.ActualWidth > 0 && item.ActualHeight > 0 &&
                item is TextBlock or Button or TextBox or ComboBox or CheckBox)
            .Where(item => !Ancestors(item).TakeWhile(ancestor => ancestor != page).OfType<ScrollViewer>()
                .Any(scroll => scroll.HorizontalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible))
            .Select(item => (Item: item, Rect: Bounds(item, page)))
            .Where(entry => entry.Rect.Left < -2 || entry.Rect.Right > page.ActualWidth + 2)
            .Select(entry => $"{entry.Item.GetType().Name} {AutomationProperties.GetName(entry.Item)} ({entry.Rect.Left:0.0}..{entry.Rect.Right:0.0} of {page.ActualWidth:0.0})")
            .ToArray();
        Check(overflow.Length == 0, scenario + " has no horizontally clipped page controls" + (overflow.Length == 0 ? "" : ": " + string.Join("; ", overflow)));
    }

    private static async Task Resize(double width, double height)
    {
        window!.Width = width; window.Height = height; await Settle();
    }

    private static async Task Settle()
    {
        var activeWindow = window!;
        await activeWindow.Dispatcher.InvokeAsync(activeWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(80); activeWindow.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement item, Visual ancestor) =>
        item.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(), item.RenderSize));

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject item)
    {
        for (var parent = VisualTreeHelper.GetParent(item); parent is not null; parent = VisualTreeHelper.GetParent(parent)) yield return parent;
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
    }

    private static void Capture(string root, string name)
    {
        window!.UpdateLayout(); var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
            // A WPF bitmap does not include DWM's desktop backdrop. Use the opaque theme underlay
            // for layout evidence; real compositor behavior is checked separately on the HWND.
            drawing.DrawRectangle((Brush)window.FindResource("WindowBrush"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(surface)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
                ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds,
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
            }, null, bounds);
        }
        bitmap.Render(visual); Directory.CreateDirectory(root);
        using var stream = File.Create(Path.Combine(root, name + ".png"));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        File.WriteAllText(Path.Combine(root, name + ".txt"), window.SmokeDescription + $" Actual window: {window.ActualWidth:0}x{window.ActualHeight:0}; client: {surface.ActualWidth:0}x{surface.ActualHeight:0}.");
    }

    private static void Check(bool result, string meaning)
    {
        if (!result) throw new InvalidOperationException("ASSERTION: " + meaning);
        checks++; Console.WriteLine("PASS " + meaning);
    }
}
