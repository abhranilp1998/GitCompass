using System.Windows.Automation;

namespace GitCompass.Desktop;

public partial class ShellWindow
{
    private LiquidGlassPreview? liquidGlassPreview;

    private void BuildLiquidGlassPreview()
    {
        PageBreadcrumb.Text = "GitCompass  ›  Liquid Glass preview";
        var back = Action("Back to Settings", () => { Navigate("Settings"); PageScroll.ScrollToTop(); });
        AutomationProperties.SetName(back, "Back to Settings");
        PageContent.Children.Add(back);
        liquidGlassPreview ??= new LiquidGlassPreview();
        liquidGlassPreview.ApplyAppearance(UsesDarkAppearance(appliedTheme));
        PageContent.Children.Add(liquidGlassPreview);
        PageScroll.ScrollToTop();
    }
}
