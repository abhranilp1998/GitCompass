using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using GitCompass.Domain;

namespace GitCompass.Desktop;

/// <summary>Keyboard-friendly "What's this?" help (F1 or the ? button): the everyday meaning of each Git word in view,
/// a button to open its offline lesson, and a link to the official docs. Esc or Close returns focus to where you were.</summary>
public sealed class WhatsThisDialog : Window
{
    public const string Heading = "What do these words mean?";
    public string? LessonToOpen { get; private set; }

    public WhatsThisDialog(Window owner, IReadOnlyList<GlossaryTerm> terms, Func<string, string?> lessonTitle)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Heading;
        (Width, MaxHeight) = SkipQuizDialog.FitWithin(owner.ActualWidth, owner.ActualHeight);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; FontFamily = owner.FontFamily;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(Text(Heading, 22, FontWeights.SemiBold, "TextBrush"));
        if (terms.Count == 0) body.Children.Add(Text("There are no Git words here. Hover over or focus text that mentions a Git word, then press F1.", 14, FontWeights.Normal, "MutedBrush"));
        foreach (var term in terms)
        {
            var entry = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            AutomationProperties.SetName(entry, term.Everyday + " (" + term.Term + "): " + term.Plain);
            entry.Children.Add(Text(term.Everyday + " (" + term.Term + ")", 16, FontWeights.SemiBold, "TextBrush"));
            entry.Children.Add(Text(term.Plain, 14, FontWeights.Normal, "MutedBrush"));
            var row = new WrapPanel();
            if (lessonTitle(term.LessonId) is { } title)
            {
                var lesson = new Button { Content = "Open lesson: " + title, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 4, 8, 8), MinHeight = 32 };
                lesson.Click += (_, _) => { LessonToOpen = term.LessonId; DialogResult = true; }; row.Children.Add(lesson);
            }
            row.Children.Add(DocLinks.Chip(term.DocUrl));
            entry.Children.Add(row); body.Children.Add(entry);
        }
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 20, 0, 0), MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close(); body.Children.Add(close);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => (FindFirstFocusable(body) ?? close).Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private static Control? FindFirstFocusable(Panel panel) =>
        panel.Children.OfType<StackPanel>().SelectMany(s => s.Children.OfType<WrapPanel>()).SelectMany(w => w.Children.OfType<Button>()).FirstOrDefault();

    private static TextBlock Text(string text, double size, FontWeight weight, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush); return block;
    }
}
