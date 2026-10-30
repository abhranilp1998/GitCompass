using System.Text.RegularExpressions;

namespace GitCompass.Domain;

public sealed record GlossaryTerm(string Term, string LessonId, string Plain);

/// <summary>Everyday definitions of Git words, each linked to the offline lesson (LearningCatalog id) that explains it in depth.</summary>
public static partial class GitGlossary
{
    public static readonly GlossaryTerm[] Terms =
    [
        new("commit", "git-commit", "A saved version of your project, stored on this computer."),
        new("stage", "git-add", "Choosing which changes go into your next commit."),
        new("branch", "git-branch", "A separate line of work, so you can try ideas without touching main."),
        new("merge", "git-merge", "Combining the saved work of one branch into another."),
        new("push", "git-push", "Uploading your saved commits to an online copy (a remote)."),
        new("pull", "git-pull", "Downloading new commits from a remote and combining them into your branch."),
        new("fetch", "git-fetch", "Checking a remote for new commits without changing your files."),
        new("remote", "remote", "A named online (or other) copy of the repository, such as origin on GitHub."),
        new("clone", "git-clone", "Downloading a full copy of an existing repository into a new folder."),
        new("stash", "git-stash", "Putting unfinished changes aside temporarily so you can come back to them."),
        new("diff", "git-diff", "The difference between two versions: removed lines vs. added lines."),
        new("conflict", "conflicts", "When two changes touch the same lines and Git needs you to choose."),
        new("repository", "basics", "A project folder whose history Git keeps track of."),
        new("HEAD", "basics", "Where you are right now: usually the latest commit on your current branch."),
    ];

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
