using System.Collections.Immutable;

namespace GitCompass.Domain;

public static class RecommendationEngine
{
    public static RecommendationPlan Build(RepositorySnapshot s, UserGoal goal = UserGoal.Understand)
    {
        var steps = ImmutableArray.CreateBuilder<Recommendation>();
        void Add(string code, string title, string reason, OperationKind? action, string effects, string lesson, string prerequisite = "Review the project, branch and selected contents.", string recovery = "Read the operation result, refresh this project and review a new plan.") => steps.Add(new(code, title, reason, effects, prerequisite, recovery, action, action is OperationKind.Fetch or OperationKind.GitHubRead ? AccessLevel.MonitorOnly : action == null ? AccessLevel.ReadOnly : AccessLevel.AllowChanges, lesson));
        if (s.ActiveOperation != GitOperation.None || s.HasConflicts)
        {
            Add("operation-recovery", "Finish the recorded operation", $"{s.ActiveOperation} is active; {s.Files.Count(f => f.Kind == FileKind.Conflicted)} paths are conflicted.", s.ActiveOperation == GitOperation.Merge ? OperationKind.ContinueMerge : null, "Review each conflict, edit the intended result and stage resolved paths before continuing.", "merge", "Do not update or push until the interrupted operation is understood.", "Abort only the recorded supported merge after reviewing what will be restored.");
            return new(s.Generation, goal, steps.ToImmutable());
        }
        if (s.Project.Kind is RepositoryKind.Invalid or RepositoryKind.Bare)
        {
            Add("unsupported-repository", "Review repository setup", string.Join(" ", s.Problems), null, "Inspection is available when Git can safely read this folder.", "init");
            return new(s.Generation, goal, steps.ToImmutable());
        }
        if (s.Project.Kind == RepositoryKind.Candidate)
        {
            Add("no-repository", "Start version history", "This selected folder has no Git history. Review private files and ignore rules first.", OperationKind.Initialize, "Creates Git metadata in this exact folder; it does not upload files.", "init");
            return new(s.Generation, goal, steps.ToImmutable());
        }
        if (s.BranchState == BranchState.Detached)
        {
            Add("detached", "Attach your work to a branch", "HEAD points to a commit without a current branch.", OperationKind.CreateBranch, "Creates a named branch at the reviewed commit.", "branches");
            return new(s.Generation, goal, steps.ToImmutable());
        }
        if (s.Project.IntendedBranch is { } intended && intended != s.Branch)
            Add("branch-intent", $"Review the intended branch: {intended}", $"The current branch is {s.Branch}; you explicitly chose {intended} for this task.", OperationKind.SwitchBranch, "Switching changes checked-out files; preserve local edits first.", "branches");
        if (s.BranchState == BranchState.Unborn)
            Add("first-commit", "Review files and save your first version", "This branch has no commits yet; incoming and outgoing counts are unknown.", s.Files.Any(f => f.IsStaged) ? OperationKind.Commit : OperationKind.Stage, "A commit records the whole reviewed index locally.", "commit");
        else if (s.HasLocalEdits)
            Add("preserve-work", "Review and save your changed files", $"{s.Files.Length} changed paths are separate from committed history.", s.Files.Any(f => f.IsStaged) ? OperationKind.Commit : OperationKind.Stage, "Stage selected contents, then record a local commit. An explained stash is another option.", "stage", "Save editor buffers and review both already staged and unstaged files.");
        if (s.HeadId != null && s.Upstream == null)
            Add("no-upstream", "Choose where this branch connects", "No upstream is configured. The app cannot assume origin or a branch name.", OperationKind.ConfigureRemote, "Sets the reviewed remote/tracking destination after an explicit choice.", "remote");
        else if (s.HeadId != null && s.UpstreamId == null)
            Add("upstream-missing", "Check the missing upstream", "The configured upstream has no readable fetched commit; comparison is unknown.", OperationKind.Fetch, "Checks only approved remote-tracking refs. It does not edit project files.", "fetch");
        else if (s.Ahead > 0 && s.Behind > 0)
            Add("diverged", "Review both histories before integrating", $"{s.Ahead} local commits and {s.Behind} fetched commits are on separate paths.", OperationKind.Merge, "A reviewed merge joins both committed histories and may create conflicts.", "merge", "Preserve local edits, check the remote and review the target commit.");
        else if (s.Behind > 0)
            Add("behind", $"Review {s.Behind} incoming commits", "These commits are in the fetched upstream and not reachable from your branch.", OperationKind.Update, "Fast-forwards to the reviewed incoming commit; project files change.", "update", "Working tree, index and possible untracked/ignored collisions must be safe.");
        else if (s.Ahead > 0)
            Add("ahead", $"Review {s.Ahead} outgoing commits", "These local recorded versions are not in the fetched upstream.", goal == UserGoal.GetLatest ? null : OperationKind.Push, goal == UserGoal.GetLatest ? "Your saved local commits remain available while you check for incoming history. Upload them through a separate reviewed action when ready." : "Uploads committed history to the exact reviewed push destination.", "push", "Check the actual push remote and destination, then refresh remote knowledge.");
        else if (s.Ahead == 0 && s.Behind == 0)
            Add("known-aligned", "No known commit synchronization needed", "Your branch matches the fetched upstream. This comparison is cached until another permitted check.", null, "Local changed files and unsaved editor work remain separate.", "status");
        if (s.IsShallow) Add("shallow", "History is limited", "This repository is shallow; relationships may omit earlier ancestors.", null, "Treat comparisons as limited evidence.", "history");
        if (s.Remote.State != RemoteCheckState.Succeeded || s.Remote.LastSuccessfulCheck == null || DateTimeOffset.UtcNow - s.Remote.LastSuccessfulCheck > TimeSpan.FromMinutes(10))
            Add("remote-stale", "Check the remote when ready", s.Remote.Error ?? "The displayed remote history may be older than the server.", OperationKind.Fetch, "Downloads objects and updates permitted remote-tracking refs; does not integrate or upload.", "fetch", "Choose Monitor only or Allow changes and trust the repository configuration.");
        if (goal == UserGoal.StartTask && s.HeadId != null && s.BranchState == BranchState.Attached)
            Add("start-task", "Choose a reviewed base and task branch", s.Project.IntendedBranch == null ? "No intended task branch is set. Choose it explicitly; the app cannot infer one." : $"You chose {s.Project.IntendedBranch} for this task. Review its base and current relationship.", OperationKind.CreateBranch, "Creates a branch at the explicitly selected commit. Switching remains a separate action.", "branches", "Resolve earlier preservation, tracking and incoming-history prerequisites, then choose a branch name and exact base.");
        if (goal == UserGoal.PreparePullRequest && s.HeadId != null && s.BranchState == BranchState.Attached)
            Add("prepare-pr", "Review pull request source and target", s.Ahead == 0 && s.UpstreamId != null ? "The fetched upstream contains this branch's saved commits. Verify publication, then choose the hosted source and target." : "Save and publish the intended source commits before requesting review.", null, "A pull request proposes review and a merge; it does not merge automatically or download project changes.", "pr", "Complete earlier steps. Confirm the hosted source repository/branch, target repository/branch, title and draft status.");
        if (goal == UserGoal.UnderstandError && s.Problems.Length > 0)
            Add("explain-problems", "Review the current problem and support context", string.Join(" ", s.Problems), null, "Prepare a reviewed diagnostic view without sending it automatically.", "status");
        return new(s.Generation, goal, steps.ToImmutable());
    }
}
