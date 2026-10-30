namespace GitCompass.Domain;

public sealed record FriendlyGitError(string Title, string WhatHappened, string WhatToDo);

/// <summary>Plain-language explanations for the Git problems beginners hit most. Returns null when nothing matches, so the original message stays authoritative.</summary>
public static class GitErrorExplainer
{
    public static FriendlyGitError? Explain(string? message)
    {
        var text = (message ?? "").ToLowerInvariant();
        if (text.Length == 0) return null;
        if (text.Contains("conflict"))
            return new("Merge conflict", "Two versions changed the same lines, and Git can't choose between them for you. Nothing is lost: both versions are kept inside the file between <<<<<<< and >>>>>>> markers.",
                "Open each conflicted file and keep the lines you want (delete the markers). Then stage the file and continue the merge, or abort the merge to go back to how things were.");
        if (text.Contains("detached"))
            return new("Detached HEAD", "You're looking at a specific saved version rather than a branch. New commits made here aren't on any branch, so they're easy to lose.",
                "Switch back to a branch (for example main) before saving new work. If you already saved something here, create a branch at this version first so it has a name.");
        if (text.Contains("rejected") || text.Contains("non-fast-forward") || text.Contains("fetch first"))
            return new("Upload rejected", "The online copy has saved versions that yours doesn't have yet, so Git refused to overwrite them. Your local commits are safe.",
                "Check the remote, bring in (merge) the new history, then upload again. GitCompass never force-pushes.");
        if (text.Contains("authentication") || text.Contains("could not read username") || text.Contains("permission denied") || text.Contains("403") || text.Contains("401"))
            return new("Sign-in or permission problem", "The server didn't accept who you are, or your account can't write to this repository.",
                "Sign in again through Git Credential Manager (or check your SSH key), and make sure your account has write access to the repository.");
        return null;
    }
}
