namespace GitCompass.Domain;

public enum ExperienceLevel { BrandNew, KnowALittle, KnowGit }
public enum WelcomeRoute { FullWalkthrough, ShortWalkthrough, SkipQuiz }
public sealed record ExperienceOption(ExperienceLevel Level, string Label, string Detail);

/// <summary>First-launch welcome: what GitCompass does in plain words, and an experience picker that decides the walkthrough length.</summary>
public static class Welcome
{
    public const string Title = "Welcome to GitCompass";
    public static readonly string[] Lines =
    [
        "GitCompass keeps a history of your project, so you can go back to any earlier version.",
        "It explains each step in plain words and asks before it changes anything.",
        "Sharing online with GitHub is optional. You can start on this computer only.",
    ];
    public const string Question = "How much do you know about Git?";
    public static readonly ExperienceOption[] Options =
    [
        new(ExperienceLevel.BrandNew, "I'm brand new", "Show me everything, step by step, with practice missions."),
        new(ExperienceLevel.KnowALittle, "I know a little", "Just the key steps, please."),
        new(ExperienceLevel.KnowGit, "I know Git", "Let me prove it and skip ahead."),
    ];

    public static WelcomeRoute Route(ExperienceLevel level) => level switch
    {
        ExperienceLevel.BrandNew => WelcomeRoute.FullWalkthrough,
        ExperienceLevel.KnowALittle => WelcomeRoute.ShortWalkthrough,
        _ => WelcomeRoute.SkipQuiz,
    };

    /// <summary>Value stored in the walkthrough record ("New", "Some", "Git").</summary>
    public static string ToState(ExperienceLevel level) => level switch { ExperienceLevel.BrandNew => "New", ExperienceLevel.KnowALittle => "Some", _ => "Git" };
    public static bool IsValidState(string? value) => value is "" or "New" or "Some" or "Git";
    public static bool ShowsShortWalkthrough(string? experience) => experience == "Some";
}
