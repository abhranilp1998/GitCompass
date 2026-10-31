namespace GitCompass.Domain;

public sealed record BeginnerMissionInfo(string Title, string Goal, string Concept);

/// <summary>Hands-on practice walkthrough progress. Only reviewed operations that succeed advance a mission.</summary>
public static class BeginnerMissions
{
    public const int SeeChange = 0, Stage = 1, Commit = 2, BranchMerge = 3, Push = 4, Done = 5;
    public const int Count = 5;
    public const string PracticeBranch = "try-idea";

    public static readonly BeginnerMissionInfo[] Missions =
    [
        new("See a change", "Two files differ from your last saved version. Open Changed files and look at what changed in welcome.txt.", "Your files right now are compared with your last saved version. The comparison is called a diff."),
        new("Stage", "Select welcome.txt for your next save. Leave ideas.txt unselected for now.", "Staging = choosing which changes go into the next save."),
        new("Commit", "Save a version with a short message, such as \"Add a line to welcome.txt\".", "Commit = a saved snapshot on your computer. Nothing is uploaded."),
        new("Branch and merge", "Try an idea on a separate line of work, then bring it back. Four small steps below.", "Branch = a separate line of work. Merge = combine its saved work into another branch."),
        new("Push to GitHub (optional)", "Connect your own empty GitHub repository and upload your saved versions, or finish without uploading.", "Push = upload your saved versions. Saving (commit) keeps them on your computer; pushing shares them."),
    ];

    public static int Clamp(int index) => Math.Clamp(index, SeeChange, Done);

    public static int Advance(int index, OperationKind kind, OperationState state)
    {
        index = Clamp(index);
        if (state != OperationState.Succeeded) return index;
        return (index, kind) switch
        {
            (SeeChange or Stage, OperationKind.Stage) => Commit,
            (SeeChange or Stage or Commit, OperationKind.Commit) => BranchMerge,
            (BranchMerge, OperationKind.Merge) => Push,
            (Push, OperationKind.Push) => Done,
            _ => index,
        };
    }

    public const string PracticeChangedFile = "welcome.txt";

    /// <summary>Mission 1 completes only when the welcome.txt diff itself is opened, not merely the Changed files page.</summary>
    public static int ViewedDiff(int index, string? path) =>
        Clamp(index) == SeeChange && string.Equals(path?.Replace('\\', '/'), PracticeChangedFile, StringComparison.OrdinalIgnoreCase) ? Stage : Clamp(index);

    /// <summary>Short "you did it" line shown when a mission completes.</summary>
    public static string Celebration(int completed) => completed switch
    {
        SeeChange => "Nice eye! You just read a diff: red lines were removed, green lines were added.",
        Stage => "Staged! welcome.txt is lined up for your next save.",
        Commit => "Saved! That version now lives in your project's history.",
        BranchMerge => "Merged! Your idea from try-idea is now part of main.",
        Push => "Shared! Your saved versions are on GitHub.",
        _ => "Mission complete.",
    };

    /// <summary>Progress label such as "Mission 2 of 5", or "All 5 missions complete".</summary>
    public static string ProgressLabel(int index) => Clamp(index) >= Done ? $"All {Count} missions complete" : $"Mission {Clamp(index) + 1} of {Count}";
    /// <summary>Mission 4 sub-steps (0–3). <paramref name="baseBranch"/> is the project's real starting branch (the practice project is created with main).</summary>
    public static string[] BranchSteps(string baseBranch) =>
    [
        $"Create a new line of work (branch) called {PracticeBranch}",
        $"Save ideas.txt while you're on {PracticeBranch}",
        $"Switch back to {baseBranch}",
        $"Combine {PracticeBranch} into {baseBranch} (merge)",
    ];
    public const int BranchStepCount = 4;

    /// <summary>Advances a Mission 4 sub-step only after the matching operation succeeded on the expected branch.</summary>
    public static int AdvanceBranchStep(int step, OperationKind kind, string? currentBranch, string baseBranch) => (Math.Clamp(step, 0, 3), kind) switch
    {
        (0, OperationKind.CreateBranch) => 1,
        (0 or 1, OperationKind.Commit) when currentBranch == PracticeBranch => 2,
        (2, OperationKind.SwitchBranch) when currentBranch == baseBranch => 3,
        _ => Math.Clamp(step, 0, 3),
    };

    /// <summary>Saved practice permission is reused only when the marker inside the practice repository matches the token saved with it.</summary>
    public static bool PracticeMarkerMatches(string? savedToken, string? markerContent) =>
        !string.IsNullOrWhiteSpace(savedToken) && savedToken.Length >= 16 && string.Equals(savedToken.Trim(), markerContent?.Trim(), StringComparison.Ordinal);
    public const string PracticeMarkerFile = "gitcompass-practice";

    public static int FinishWithoutPush(int index) => Clamp(index) == Push ? Done : Clamp(index);
}
