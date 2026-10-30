namespace GitCompass.Domain;

public sealed record QuizQuestion(string Prompt, string[] Choices, int Correct, string Explanation);
public sealed record QuizGreeting(string Title, string Subtitle);

/// <summary>Light-hearted Git check shown when a beginner skips the walkthrough. Never blocks; a score is not stored.</summary>
public static class SkipQuiz
{
    public static readonly QuizGreeting[] Greetings =
    [
        new("Do you really know Git?", "Three quick questions. No pressure. (Some pressure.)"),
        new("Whoa there, Git wizard", "Prove it and the door is yours."),
        new("Skipping already? Bold.", "Answer three and we'll get out of your way."),
        new("git gud check", "If you can answer these, you don't need us."),
        new("Trust, but verify", "Three questions between you and your project."),
        new("Is this your first rodeo?", "Let's find out in about 20 seconds."),
    ];

    public static readonly QuizQuestion[] Questions =
    [
        new("What does staging (git add) do?",
            ["Chooses which changes go into the next commit", "Uploads files to GitHub", "Saves a permanent version", "Makes a backup copy of the folder"], 0,
            "Staging chooses which changes go into your next saved version."),
        new("Commit vs push?",
            ["Commit saves locally; push uploads commits to a remote", "They are the same thing", "Push saves locally; commit uploads", "Commit needs an internet connection"], 0,
            "A commit is saved on your computer. A push uploads saved commits."),
        new("What is a branch?",
            ["A separate line of work you can switch between and merge later", "A copy of the folder on GitHub", "A deleted commit", "A file Git ignores"], 0,
            "A branch is a separate line of work. You can merge it back later."),
    ];

    public static QuizGreeting PickGreeting(Random random) => Greetings[random.Next(Greetings.Length)];

    /// <summary>Returns indices of questions answered incorrectly (unanswered counts as incorrect).</summary>
    public static int[] Missed(IReadOnlyList<int?> answers) =>
        Enumerable.Range(0, Questions.Length).Where(i => i >= answers.Count || answers[i] != Questions[i].Correct).ToArray();
}
