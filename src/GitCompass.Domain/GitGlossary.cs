using System.Text.RegularExpressions;

namespace GitCompass.Domain;

public sealed record GlossaryTerm(string Term, string LessonId, string Plain, string Everyday);

/// <summary>The single source of Git words for the app and README: an everyday name for each term, a short grade-6 definition,
/// and the offline lesson (LearningCatalog id) that explains it in depth. UI labels use <see cref="Label"/> so people learn the real word.</summary>
public static partial class GitGlossary
{
    public static readonly GlossaryTerm[] Terms =
    [
        new("commit", "git-commit", "A saved version of your project, stored on this computer.", "Save a version"),
        new("stage", "git-add", "Picking which changed files go into your next saved version.", "Choose files to save"),
        new("branch", "git-branch", "A separate line of work, so you can try ideas without changing the main one.", "Line of work"),
        new("merge", "git-merge", "Joining the saved work from one line of work into another.", "Combine work"),
        new("push", "git-push", "Sending your saved versions to the online copy.", "Upload"),
        new("pull", "git-pull", "Getting new saved versions from the online copy and adding them to your work.", "Get updates"),
        new("fetch", "git-fetch", "Checking the online copy for new saved versions, without changing your files.", "Check for updates"),
        new("remote", "remote", "The online copy of your project, for example on GitHub. It is often named origin.", "Online copy"),
        new("clone", "git-clone", "Downloading a full copy of a project, with all its history, into a new folder.", "Download a copy"),
        new("stash", "git-stash", "Putting unfinished edits aside for a while so you can come back to them.", "Set work aside"),
        new("diff", "git-diff", "What changed between two versions: lines taken out and lines added.", "What changed"),
        new("conflict", "conflicts", "Two changes touched the same lines, so Git needs you to pick what to keep.", "Clashing changes"),
        new("repository", "basics", "A project folder where Git keeps every saved version. Often called a repo.", "Project with history"),
        new("HEAD", "basics", "Where you are right now: usually the newest saved version on your current line of work.", "You are here"),
    ];

    public static GlossaryTerm Get(string term) => Terms.First(t => t.Term == term);

    /// <summary>"Everyday name (git word)", e.g. "Save a version (commit)". Keeps the real term visible so beginners learn it.</summary>
    public static string Label(string term) => Get(term) is var t ? t.Everyday + " (" + t.Term + ")" : term;

    /// <summary>Markdown table for README/docs; a test keeps README's copy identical.</summary>
    public static string MarkdownTable() =>
        "| Everyday words | Git word | What it means |\n|---|---|---|\n" + string.Concat(Terms.Select(t => $"| {t.Everyday} | {t.Term} | {t.Plain} |\n"));

    [GeneratedRegex(@"\b(commit|commits|committed|stage|staged|staging|unstage|branch|branches|merge|merged|merging|push|pushed|pull|pulled|fetch|fetched|remote|remotes|clone|cloned|stash|stashed|diff|diffs|conflict|conflicts|conflicted|repository|repositories|repo|HEAD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Words();

    /// <summary>Glossary terms mentioned in the text, in first-mention order, without duplicates. "HEAD" only matches in capitals.</summary>
    public static IReadOnlyList<GlossaryTerm> TermsIn(string? text)
    {
        var found = new List<GlossaryTerm>();
        if (string.IsNullOrEmpty(text)) return found;
        foreach (Match match in Words().Matches(text))
        {
            var word = match.Value;
            if (word.Equals("head", StringComparison.OrdinalIgnoreCase) && word != "HEAD") continue;
            var root = word.ToLowerInvariant() switch
            {
                "commits" or "committed" => "commit", "staged" or "staging" or "unstage" => "stage", "branches" => "branch",
                "merged" or "merging" => "merge", "pushed" => "push", "pulled" => "pull", "fetched" => "fetch", "remotes" => "remote",
                "cloned" => "clone", "stashed" => "stash", "diffs" => "diff", "conflicts" or "conflicted" => "conflict",
                "repositories" or "repo" => "repository", "head" => "HEAD", var w => w,
            };
            var term = Terms.First(t => t.Term == root);
            if (!found.Contains(term)) found.Add(term);
        }
        return found;
    }

    /// <summary>Tooltip text for up to three terms; empty when none are mentioned.</summary>
    public static string Tooltip(string? text, int max = 3) =>
        string.Join("\n", TermsIn(text).Take(max).Select(t => t.Term + ": " + t.Plain));
}
