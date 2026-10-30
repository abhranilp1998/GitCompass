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
        new("See a change", "Two files differ from your last saved version. Open Changed files and look at welcome.txt.", "Working copy = your files right now. Git compares them with the last saved version."),
        new("Stage", "Select welcome.txt for your next save. Leave ideas.txt unselected for now.", "Staging = choosing which changes go into the next save."),
        new("Commit", "Save a version with a short message, such as \"Add a line to welcome.txt\".", "Commit = a saved snapshot on your computer. Nothing is uploaded."),
        new("Branch and merge", "Create a branch called try-idea, save ideas.txt there, switch back to main, then merge try-idea.", "Branch = a separate line of work. Merge = combine its saved work into another branch."),
        new("Push to GitHub (optional)", "Connect your own empty GitHub repository and upload your saved versions, or finish without uploading.", "Push = upload your saved commits. Commit saves locally; push shares."),
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

    public static int ViewedChanges(int index) => Clamp(index) == SeeChange ? Stage : Clamp(index);
    public static int FinishWithoutPush(int index) => Clamp(index) == Push ? Done : Clamp(index);
}
