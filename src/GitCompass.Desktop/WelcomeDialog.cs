using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using GitCompass.Domain;

namespace GitCompass.Desktop;

/// <summary>First-launch welcome. Uses the app's theme brushes (contrast-checked in ThemeContrast), so it follows Light, Dark and
/// Liquid Glass. Tab/arrow keys move between the three choices, Enter picks one, Esc closes without choosing.</summary>
public sealed class WelcomeDialog : Window
{
    public ExperienceLevel? Choice { get; private set; }

    public WelcomeDialog(Window owner)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Welcome.Title;
        (Width, MaxHeight) = SkipQuizDialog.FitWithin(owner.ActualWidth, owner.ActualHeight);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(this, Welcome.Title);
        var body = new StackPanel { Margin = new Thickness(24) };
        var heading = Text(Welcome.Title, 24, FontWeights.SemiBold, "TextBrush");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        body.Children.Add(heading);
        foreach (var line in Welcome.Lines) body.Children.Add(Text(line, 14, FontWeights.Normal, "MutedBrush"));
        var question = Text(Welcome.Question, 17, FontWeights.SemiBold, "TextBrush"); question.Margin = new Thickness(0, 20, 0, 4);
        AutomationProperties.SetHeadingLevel(question, AutomationHeadingLevel.Level2);
        body.Children.Add(question);
        Button? first = null;
        foreach (var option in Welcome.Options)
        {
            var content = new StackPanel();
            content.Children.Add(Text(option.Label, 16, FontWeights.SemiBold, "TextBrush"));
            content.Children.Add(Text(option.Detail, 13, FontWeights.Normal, "MutedBrush"));
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 8, 0, 0), MinHeight = 56 };
            AutomationProperties.SetName(button, option.Label + ". " + option.Detail);
            var level = option.Level;
            button.Click += (_, _) => { Choice = level; DialogResult = true; };
            first ??= button;
            body.Children.Add(button);
        }
        body.Children.Add(Text("You can see this screen again from Settings or Learn.", 12, FontWeights.Normal, "MutedBrush"));
        var later = new Button { Content = "Not now", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 12, 0, 0), MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left };
        later.Click += (_, _) => Close(); body.Children.Add(later);
        KeyboardNavigation.SetDirectionalNavigation(body, KeyboardNavigationMode.Cycle);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => first?.Focus();
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush); return block;
    }
}
