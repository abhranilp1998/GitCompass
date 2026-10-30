using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GitCompass.Desktop;

/// <summary>Themed confirmation for leaving the walkthrough. Resume (default, Esc) keeps the user where they are; Leave exits with progress saved.</summary>
public sealed class LeaveWalkthroughDialog : Window
{
    public const string Question = "Leave the walkthrough?";
    public const string Detail = "Your progress is saved. You can pick up at the same mission any time from Overview → Resume walkthrough.";
    public bool Leave { get; private set; }

    public LeaveWalkthroughDialog(Window owner, string? missionLabel = null)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Question;
        (Width, MaxHeight) = SkipQuizDialog.FitWithin(owner.ActualWidth, owner.ActualHeight);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(Text(Question, 22, FontWeights.SemiBold, "TextBrush"));
        body.Children.Add(Text(missionLabel is null ? Detail : Detail + " You're on " + missionLabel + ".", 14, FontWeights.Normal, "MutedBrush"));
        var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
        row.Children.Add(Button("Resume", () => { Leave = false; DialogResult = true; }, true));
        row.Children.Add(Button("Leave", () => { Leave = true; DialogResult = true; }));
        body.Children.Add(row); Content = body;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Leave = false; Close(); } };
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush); return block;
    }
    private static Button Button(string title, Action action, bool primary = false)
    {
        var button = new Button { Content = title, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 8), IsDefault = primary, MinHeight = 36 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action(); return button;
    }
}
