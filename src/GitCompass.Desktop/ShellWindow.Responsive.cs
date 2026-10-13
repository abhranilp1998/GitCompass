using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace GitCompass.Desktop;

public partial class ShellWindow
{
    private bool compactNavigation, navigationDrawerOpen, updatingLayout;
    private double preferredDockHeight = 220;

    private void InitializeResponsiveLayout()
    {
        Loaded += (_, _) => UpdateResponsiveLayout();
        RootShell.SizeChanged += (_, _) => UpdateResponsiveLayout();
        MainWorkspace.SizeChanged += (_, _) => UpdateResponsiveLayout();
        PageHeader.SizeChanged += (_, _) => UpdateResponsiveLayout();
        StatusBanner.SizeChanged += (_, _) => UpdateResponsiveLayout();
        DockSplitter.DragCompleted += (_, _) => { preferredDockHeight = DockRow.ActualHeight; UpdateResponsiveLayout(); };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && navigationDrawerOpen) { SetNavigationDrawer(false); args.Handled = true; }
            else if (args.Key is Key.Enter or Key.Space && Navigation.IsKeyboardFocusWithin && Navigation.SelectedItem is ListBoxItem item)
            {
                ActivateNavigation(item); args.Handled = true;
            }
        };
    }
    private void UpdateResponsiveLayout()
    {
        if (updatingLayout || RootShell.ActualWidth <= 0) return;
        updatingLayout = true;
        try
        {
            compactNavigation = RootShell.ActualWidth < 1000;
            if (!compactNavigation) navigationDrawerOpen = false;
            SidebarColumn.Width = new(compactNavigation ? 0 : 260);
            Grid.SetColumnSpan(SidebarSurface, compactNavigation ? 2 : 1);
            SidebarSurface.Width = compactNavigation ? 280 : double.NaN;
            SidebarSurface.HorizontalAlignment = compactNavigation ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            SidebarSurface.Visibility = !compactNavigation || navigationDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
            SidebarSurface.SetResourceReference(Border.BackgroundProperty, compactNavigation ? "SurfaceBrush" : "ShellBackdropBrush");
            SidebarScrim.Visibility = compactNavigation && navigationDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
            MainWorkspace.IsEnabled = !navigationDrawerOpen;
            NavigationToggleButton.Visibility = CompactActivityToggleButton.Visibility = CloseNavigationButton.Visibility = compactNavigation ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(NavigationToggleButton, navigationDrawerOpen ? "Close navigation" : "Open navigation");
            var sideMargin = compactNavigation ? 20d : 30d;
            PageHeader.Padding = new(sideMargin, compactNavigation ? 12 : 20, sideMargin, 12);
            PageContent.Margin = new(sideMargin, 6, sideMargin, 24);
            StatusBanner.Margin = new(sideMargin, 0, sideMargin, 8);
            var contentWidth = Math.Max(0, MainWorkspace.ActualWidth - sideMargin * 2);
            var stacked = contentWidth < 680;
            Grid.SetColumn(HeaderActions, stacked ? 0 : 1); Grid.SetRow(HeaderActions, stacked ? 1 : 0);
            HeaderActions.Margin = stacked ? new(0, 10, 0, 0) : new(12, 0, 0, 0);
            Grid.SetColumnSpan(ProjectSummary, stacked ? 2 : 1);
            Grid.SetColumn(FreshnessLabel, stacked ? 0 : 1); Grid.SetRow(FreshnessLabel, stacked ? 1 : 0);
            FreshnessLabel.TextAlignment = stacked ? TextAlignment.Left : TextAlignment.Right;
            FreshnessLabel.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            FreshnessLabel.Margin = stacked ? new(0, 6, 0, 0) : new(0);
            Grid.SetColumnSpan(AccessHelper, stacked ? 2 : 1);
            var shortHeader = compactNavigation && RootShell.ActualHeight < 640;
            // A Grid's desired minimum can exceed its parent while an Auto row grows.
            // Budget against the real client height, not the child's overflow height.
            var workspaceHeight = RootShell.ActualHeight;
            var splitterHeight = Math.Max(6, DockSplitter.MinHeight);
            MainWorkspace.MaxHeight = workspaceHeight;
            var tightWithNotice = shortHeader && activityDockOpen && StatusBanner.IsVisible;
            PageRow.MinHeight = tightWithNotice ? 128 : 160;
            StatusMessageScroll.MaxHeight = tightWithNotice
                ? Math.Clamp(workspaceHeight - PageHeader.ActualHeight - 128 - 124 - splitterHeight - 38, 18, 72)
                : compactNavigation ? 120 : 200;
            AccessHelper.TextWrapping = shortHeader ? TextWrapping.NoWrap : TextWrapping.Wrap;
            AccessHelper.TextTrimming = TextTrimming.CharacterEllipsis;
            AccessHelper.ToolTip = AccessHelper.Text;
            OutputChannel.Width = compactNavigation ? 160 : 175;
            OutputSearch.Width = compactNavigation ? 100 : 105;
            OutputSeverity.Width = compactNavigation ? 110 : 120;
            AutoScroll.Visibility = GroupOutput.Visibility = CopyActivityButton.Visibility = ClearActivityButton.Visibility = compactNavigation ? Visibility.Collapsed : Visibility.Visible;
            InterruptTerminalButton.Visibility = CopyTerminalButton.Visibility = PasteTerminalButton.Visibility = CloseTerminalButton.Visibility = TerminalModeText.Visibility = compactNavigation ? Visibility.Collapsed : Visibility.Visible;
            TerminalOptionsButton.Visibility = compactNavigation ? Visibility.Visible : Visibility.Collapsed;
            var availableForDock = Math.Max(0, workspaceHeight - PageHeader.ActualHeight -
                (StatusBanner.IsVisible ? StatusBanner.ActualHeight + 8 : 0) - (tightWithNotice ? 128 : 180) - splitterHeight);
            var cap = Math.Min(workspaceHeight * 0.32, availableForDock);
            DockRow.Height = new(activityDockOpen ? Math.Min(preferredDockHeight, Math.Max(124, cap)) : 0);
            DockRow.MinHeight = activityDockOpen ? 124 : 0;
            DockSplitter.Height = activityDockOpen ? splitterHeight : 0;
            DockSurface.Visibility = DockSplitter.Visibility = activityDockOpen ? Visibility.Visible : Visibility.Collapsed;
            CompactActivityToggleButton.Content = activityDockOpen ? "Hide activity" : "Activity";
            AutomationProperties.SetName(CompactActivityToggleButton, activityDockOpen ? "Hide activity details" : "Show activity details");
        }
        finally { updatingLayout = false; }
    }
    private void ToggleNavigationDrawer(object sender, RoutedEventArgs args) => SetNavigationDrawer(!navigationDrawerOpen);
    private void CloseNavigationDrawer(object sender, RoutedEventArgs args) => SetNavigationDrawer(false);
    private void SetNavigationDrawer(bool open)
    {
        navigationDrawerOpen = compactNavigation && open; UpdateResponsiveLayout();
        if (navigationDrawerOpen) Navigation.Focus(); else if (compactNavigation) NavigationToggleButton.Focus();
    }
    private void NavigationClicked(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Navigation, source) is ListBoxItem item)
            ActivateNavigation(item);
    }
    private void ActivateNavigation(ListBoxItem item)
    {
        if (!ready || updating) return;
        var page = item.Tag?.ToString() ?? "Overview";
        if (model.Page != page || reviewing) Navigate(page);
        if (compactNavigation) SetNavigationDrawer(false);
    }
    private void ShowActivityOptions(object sender, RoutedEventArgs args)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Header?.ToString() == "Auto-scroll") item.IsChecked = AutoScroll.IsChecked == true;
                if (item.Header?.ToString() == "Group by operation") item.IsChecked = GroupOutput.IsChecked == true;
            }
            menu.PlacementTarget = button; menu.IsOpen = true;
        }
    }
    private void ToggleActivityAutoScroll(object sender, RoutedEventArgs args)
    { if (sender is MenuItem item) AutoScroll.IsChecked = item.IsChecked; }
    private void ToggleActivityGrouping(object sender, RoutedEventArgs args)
    { if (sender is MenuItem item) GroupOutput.IsChecked = item.IsChecked; }
    private void ShowTerminalOptions(object sender, RoutedEventArgs args)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
}
