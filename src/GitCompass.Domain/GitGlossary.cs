using System.Text.RegularExpressions;

namespace GitCompass.Domain;

public sealed record GlossaryTerm(string Term, string LessonId, string Plain, string Everyday, string DocUrl);

/// <summary>The single source of Git words for the app and README: an everyday name for each term, a short grade-6 definition,
/// and the offline lesson (LearningCatalog id) that explains it in depth. UI labels use <see cref="Label"/> so people learn the real word.</summary>
public static partial class GitGlossary
{
    public static readonly GlossaryTerm[] Terms =
    [
        new("commit", "git-commit", "A saved version of your project, stored on this computer.", "Save a version", "https://git-scm.com/docs/git-commit"),
        new("stage", "git-add", "Picking which changed files go into your next saved version.", "Choose files to save", "https://git-scm.com/docs/git-add"),
        new("branch", "git-branch", "A separate line of work, so you can try ideas without changing the main one.", "Line of work", "https://git-scm.com/docs/git-branch"),
        new("merge", "git-merge", "Joining the saved work from one line of work into another.", "Combine work", "https://git-scm.com/docs/git-merge"),
        new("push", "git-push", "Sending your saved versions to the online copy.", "Upload", "https://git-scm.com/docs/git-push"),
        new("pull", "git-pull", "Getting new saved versions from the online copy and adding them to your work.", "Get updates", "https://git-scm.com/docs/git-pull"),
        new("fetch", "git-fetch", "Checking the online copy for new saved versions, without changing your files.", "Check for updates", "https://git-scm.com/docs/git-fetch"),
        new("remote", "remote", "The online copy of your project, for example on GitHub. It is often named origin.", "Online copy", "https://git-scm.com/book/en/v2/Git-Basics-Working-with-Remotes"),
        new("clone", "git-clone", "Downloading a full copy of a project, with all its history, into a new folder.", "Download a copy", "https://git-scm.com/docs/git-clone"),
        new("stash", "git-stash", "Putting unfinished edits aside for a while so you can come back to them.", "Set work aside", "https://git-scm.com/docs/git-stash"),
        new("diff", "git-diff", "What changed between two versions: lines taken out and lines added.", "What changed", "https://git-scm.com/docs/git-diff"),
        new("conflict", "conflicts", "Two changes touched the same lines, so Git needs you to pick what to keep.", "Clashing changes", "https://git-scm.com/book/en/v2/Git-Branching-Basic-Branching-and-Merging"),
        new("repository", "basics", "A project folder where Git keeps every saved version. Often called a repo.", "Project with history", "https://git-scm.com/book/en/v2/Git-Basics-Getting-a-Git-Repository"),
        new("pull request", "pr", "Asking your team to look at your uploaded work and add it to theirs. Not the same as pull.", "Review request", "https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/proposing-changes-to-your-work-with-pull-requests/about-pull-requests"),
        new("fork", "remote", "Your own online copy of someone else's project, so you can suggest changes without changing theirs.", "Your own copy", "https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/working-with-forks/about-forks"),
        new("HEAD", "basics", "Where you are right now: usually the newest saved version on your current line of work.", "You are here", "https://git-scm.com/book/en/v2/Git-Internals-Git-References"),
    ];

    public static GlossaryTerm Get(string term) => Terms.First(t => t.Term == term);

    /// <summary>"Everyday name (git word)", e.g. "Save a version (commit)". Keeps the real term visible so beginners learn it.</summary>
    public static string Label(string term) => Get(term) is var t ? t.Everyday + " (" + t.Term + ")" : term;

    /// <summary>Short site name for a documentation link chip, e.g. "Learn more on git-scm.com ↗".</summary>
    public static string LinkText(string url) => "Learn more on " + (Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "the web") + " ↗";

    /// <summary>Official documentation for an offline lesson: git-scm.com/docs for command lessons, the glossary link for concept lessons.</summary>
    public static string? DocUrlForLesson(string lessonId) =>
        lessonId.StartsWith("git-", StringComparison.Ordinal) ? "https://git-scm.com/docs/" + lessonId : Terms.FirstOrDefault(t => t.LessonId == lessonId)?.DocUrl;

    /// <summary>Markdown table for README/docs; a test keeps README's copy identical.</summary>
    public static string MarkdownTable() =>
        "| Everyday words | Git word | What it means | Official docs |\n|---|---|---|---|\n" + string.Concat(Terms.Select(t => $"| {t.Everyday} | {t.Term} | {t.Plain} | [{new Uri(t.DocUrl).Host}]({t.DocUrl}) |\n"));

    [GeneratedRegex(@"\b(pull requests?|forks?|forked|commit|commits|committed|stage|staged|staging|unstage|branch|branches|merge|merged|merging|push|pushed|pull|pulled|fetch|fetched|remote|remotes|clone|cloned|stash|stashed|diff|diffs|conflict|conflicts|conflicted|repository|repositories|repo|HEAD)\b", RegexOptions.IgnoreCase)]
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
            var root = Regex.Replace(word.ToLowerInvariant(), @"\s+", " ") switch
            {
                "commits" or "committed" => "commit", "staged" or "staging" or "unstage" => "stage", "branches" => "branch",
                "merged" or "merging" => "merge", "pushed" => "push", "pulled" => "pull", "fetched" => "fetch", "remotes" => "remote",
                "cloned" => "clone", "stashed" => "stash", "diffs" => "diff", "conflicts" or "conflicted" => "conflict",
                "repositories" or "repo" => "repository", "pull requests" => "pull request", "forks" or "forked" => "fork", "head" => "HEAD", var w => w,
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
