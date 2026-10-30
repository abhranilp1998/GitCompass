using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using GitCompass.Domain;

namespace GitCompass.Desktop;

public enum SkipQuizOutcome { Cancelled, OpenProject, CloneRepository, StartWalkthrough }

/// <summary>Modal skip check. Passing or "Skip anyway" both lead to opening a project; Esc returns with no change.</summary>
public sealed class SkipQuizDialog : Window
{
    private readonly int?[] answers = new int?[SkipQuiz.Questions.Length];
    private readonly StackPanel body = new() { Margin = new Thickness(24) };
    public SkipQuizOutcome Outcome { get; private set; } = SkipQuizOutcome.Cancelled;

    public SkipQuizDialog(Window owner, Random? random = null)
    {
        Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Fit inside the shell's 640 × 560 minimum with a margin on every side.
        (Width, MaxHeight) = FitWithin(owner.ActualWidth, owner.ActualHeight);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = owner.FontFamily;
        var greeting = SkipQuiz.PickGreeting(random ?? Random.Shared);
        Title = greeting.Title;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Outcome = SkipQuizOutcome.Cancelled; Close(); } };
        BuildQuestions(greeting);
    }

    /// <summary>Preferred 560 × 720, shrunk to the owner's size minus 48 px, never below 320 × 320.</summary>
    public static (double Width, double MaxHeight) FitWithin(double ownerWidth, double ownerHeight) =>
        (Math.Max(320, Math.Min(560, (ownerWidth > 0 ? ownerWidth : 640) - 48)), Math.Max(320, Math.Min(720, (ownerHeight > 0 ? ownerHeight : 560) - 48)));

    private void BuildQuestions(QuizGreeting greeting)
    {
        body.Children.Clear();
        Add(greeting.Title, 22, FontWeights.SemiBold); Add(greeting.Subtitle, 14, FontWeights.Normal, 0.75);
        for (var q = 0; q < SkipQuiz.Questions.Length; q++)
        {
            var question = SkipQuiz.Questions[q]; var index = q;
            Add($"{q + 1}. {question.Prompt}", 15, FontWeights.SemiBold);
            for (var c = 0; c < question.Choices.Length; c++)
            {
                var choice = c;
                var radio = new RadioButton { Content = question.Choices[c], GroupName = "q" + q, Margin = new Thickness(8, 4, 0, 0) };
                AutomationProperties.SetName(radio, $"Question {q + 1}: {question.Choices[c]}");
                radio.SetResourceReference(ForegroundProperty, "TextBrush");
                radio.Checked += (_, _) => answers[index] = choice;
                body.Children.Add(radio);
            }
        }
        var row = Row();
        row.Children.Add(Button("Check my answers", ShowResult, true));
        row.Children.Add(Button("Back", () => { Outcome = SkipQuizOutcome.Cancelled; Close(); }));
        body.Children.Add(row);
    }

    private void ShowResult()
    {
        var missed = SkipQuiz.Missed(answers);
        body.Children.Clear();
        var row = Row();
        if (missed.Length == 0)
        {
            Add("Okay, you clearly know your stuff.", 22, FontWeights.SemiBold);
            Add("Choose a folder or project to open. It opens in Read-only first.", 14, FontWeights.Normal, 0.75);
            row.Children.Add(Button("Open folder or project", () => Finish(SkipQuizOutcome.OpenProject), true));
            row.Children.Add(Button("Clone a repo", () => Finish(SkipQuizOutcome.CloneRepository)));
        }
        else
        {
            Add("Close! Git trips up everyone at first.", 22, FontWeights.SemiBold);
            foreach (var i in missed) { var q = SkipQuiz.Questions[i]; Add(q.Prompt, 15, FontWeights.SemiBold); Add(q.Explanation, 14, FontWeights.Normal, 0.75); }
            Add("The walkthrough takes about 10 minutes in a practice project you can delete later.", 14, FontWeights.Normal, 0.75);
            row.Children.Add(Button("Take the walkthrough", () => Finish(SkipQuizOutcome.StartWalkthrough), true));
            row.Children.Add(Button("Skip anyway", () => Finish(SkipQuizOutcome.OpenProject)));
            row.Children.Add(Button("Clone a repo", () => Finish(SkipQuizOutcome.CloneRepository)));
        }
        body.Children.Add(row);
    }

    private void Finish(SkipQuizOutcome outcome) { Outcome = outcome; DialogResult = true; }
    private void Add(string text, double size, FontWeight weight, double opacity = 1)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, opacity < 1 ? "MutedBrush" : "TextBrush");
        body.Children.Add(block);
    }
    // WrapPanel lets buttons stack on narrow windows instead of clipping.
    private static WrapPanel Row() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
    private static Button Button(string title, Action action, bool primary = false)
    {
        var button = new Button { Content = title, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 8), IsDefault = primary, MinHeight = 36 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action();
        return button;
    }
}
