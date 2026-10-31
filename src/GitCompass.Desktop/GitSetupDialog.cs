using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GitCompass.Domain;

namespace GitCompass.Desktop;

/// <summary>One plain step before the welcome picker when Git is missing. Theme brushes only; keyboard and screen-reader friendly.</summary>
public sealed class GitSetupDialog : Window
{
    public bool CheckAgain { get; private set; }

    public GitSetupDialog(Window owner, bool stillMissing)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = GitSetupStep.Title;
        (Width, MaxHeight) = SkipQuizDialog.FitWithin(owner.ActualWidth, owner.ActualHeight);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new Thickness(24) };
        var heading = Text(GitSetupStep.Title, 22, FontWeights.SemiBold, "TextBrush");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1); body.Children.Add(heading);
        body.Children.Add(Text(GitSetupStep.Body, 14, FontWeights.Normal, "MutedBrush"));
        if (stillMissing) body.Children.Add(Text(GitSetupStep.StillMissing, 14, FontWeights.SemiBold, "TextBrush"));
        var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
        var download = Button(GitSetupStep.DownloadLabel, () =>
        {
            try { Process.Start(new ProcessStartInfo(GitSetupStep.DownloadUrl) { UseShellExecute = true }); } catch { /* the link text stays visible below */ }
        });
        download.SetResourceReference(StyleProperty, "PrimaryButton");
        row.Children.Add(download);
        row.Children.Add(Button(GitSetupStep.CheckAgainLabel, () => { CheckAgain = true; DialogResult = true; }));
        var later = Button(GitSetupStep.LaterLabel, Close); later.IsCancel = true; row.Children.Add(later);
        body.Children.Add(row);
        body.Children.Add(Text("Download page: " + GitSetupStep.DownloadUrl, 12, FontWeights.Normal, "MutedBrush"));
        body.Children.Add(Text(GitSetupStep.KeyboardHint, 12, FontWeights.Normal, "MutedBrush"));
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => download.Focus();
    }

    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 8), MinHeight = 36 };
        button.Click += (_, _) => action(); return button;
    }
    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush); return block;
    }
}
