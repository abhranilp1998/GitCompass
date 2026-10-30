namespace GitCompass.Domain;

public sealed record FriendlyGitError(string Title, string WhatHappened, string WhatToDo, string? DocUrl = null);

/// <summary>Plain-language explanations for the Git problems beginners hit most. Returns null when nothing matches, so the original message stays authoritative.</summary>
public static class GitErrorExplainer
{
    public static FriendlyGitError? Explain(string? message)
    {
        var text = (message ?? "").ToLowerInvariant();
        if (text.Length == 0) return null;
        if (text.Contains("conflict"))
            return new("Merge conflict", "Two changes touched the same lines, and Git can't pick one for you. Nothing is lost: both are kept in the file between <<<<<<< and >>>>>>> marker lines.",
                "Open each file with clashing changes. Keep the lines you want and delete the marker lines. Then choose the file for saving (stage) and continue, or abort to go back to how things were.", "https://git-scm.com/book/en/v2/Git-Branching-Basic-Branching-and-Merging");
        if (text.Contains("detached"))
            return new("Detached HEAD", "You're looking at a specific saved version rather than a branch. New commits made here aren't on any branch, so they're easy to lose.",
                "Switch back to a branch (for example main) before saving new work. If you already saved something here, create a branch at this version first so it has a name.", "https://git-scm.com/docs/git-switch");
        if (text.Contains("rejected") || text.Contains("non-fast-forward") || text.Contains("fetch first"))
            return new("Upload rejected", "The online copy has saved versions that yours doesn't have yet, so Git refused to overwrite them. Your local commits are safe.",
                "Check for updates (fetch), bring the new versions in (merge), then upload again. GitCompass never overwrites the online copy (no force-push).", "https://git-scm.com/docs/git-push");
        if (text.Contains("authentication") || text.Contains("could not read username") || text.Contains("permission denied") || text.Contains("403") || text.Contains("401"))
            return new("Sign-in or permission problem", "The website didn't accept your sign-in, or your account isn't allowed to change this project.",
                "Sign in again when Git asks (it opens a sign-in window). Make sure your account is allowed to change this project. Ask the project owner if you are not sure.", "https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/about-authentication-to-github");
        if (text.Contains("not a git repository"))
            return new("No saved versions here yet", "This folder isn't set up to keep saved versions (it isn't a Git repository).",
                "Open the Overview page and choose \"Start saving versions here\", or pick a different folder.", "https://git-scm.com/book/en/v2/Git-Basics-Getting-a-Git-Repository");
        if (text.Contains("index.lock") || text.Contains("another git process"))
            return new("Git is busy", "Another program (maybe your editor or a terminal) is using Git in this folder right now.",
                "Wait a moment and try again. If it keeps happening, close other programs that use this folder, then try again.");
        if (text.Contains("nothing to commit") || text.Contains("no changes added to commit"))
            return new("Nothing to save", "No files are chosen for the next saved version, or nothing has changed.",
                "Edit a file, tick it on the Changes page, choose it for saving (stage), then save again.", "https://git-scm.com/docs/git-add");
        if (text.Contains("could not resolve host") || text.Contains("unable to access") || text.Contains("timed out") || text.Contains("network is unreachable"))
            return new("Can't reach the online copy", "Your computer couldn't connect to the server. You may be offline, or the web address may be wrong.",
                "Check your internet connection and the project's web address (in Project setup), then try again. Your work on this computer is safe.", "https://git-scm.com/book/en/v2/Git-Basics-Working-with-Remotes");
        if (text.Contains("author") && text.Contains("email"))
            return new("Git needs your name", "Each saved version is labelled with a name and email, and Git doesn't have yours yet.",
                "Choose \"Set author details\", type your name and email, then save again.", "https://git-scm.com/book/en/v2/Getting-Started-First-Time-Git-Setup");
        return null;
    }

    /// <summary>Shown when no specific explanation matches: honest, calm, and gives one next step.</summary>
    public static readonly FriendlyGitError Fallback = new("Something went wrong",
        "GitCompass couldn't finish this step. It checks your project again afterwards, so the screen shows what really happened.",
        "Read the message above, then try again. If it happens again, choose \"Get help: make a summary to share\" and send it to someone who can help.");
}
