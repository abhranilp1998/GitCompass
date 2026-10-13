using System.Collections.Immutable;

namespace GitCompass.Domain;

public enum AccessLevel { ReadOnly, MonitorOnly, AllowChanges }
public enum RepositoryKind { Candidate, WorkingTree, LinkedWorktree, Bare, Invalid }
public enum BranchState { Unborn, Attached, Detached, Unknown }
public enum FileKind { Tracked, Untracked, Conflicted, Ignored }
public enum GitOperation { None, Merge, Rebase, CherryPick, Revert, Bisect }
public enum RemoteCheckState { NotChecked, Succeeded, Failed, Unknown }
public enum UserGoal { Understand, UploadWork, GetLatest, StartTask, PreparePullRequest, UnderstandError }
public enum OperationKind { Initialize, Stage, Unstage, Commit, Fetch, Update, Push, ConfigureRemote, ApplyIgnore, Stash, ApplyStash, Merge, ContinueMerge, AbortMerge, CreateBranch, SwitchBranch, Terminal, GitHubRead, GitHubWrite, ConfigureIdentity, Clone, Inspect }
public enum OperationState { Queued, Starting, Running, Succeeded, Failed, Canceled, OutcomeUnknown, Denied, Stale }

public sealed record ProjectRegistration(Guid Id, string DisplayName, string SelectedFolder, string RootPath, string CommonGitDirectory, RepositoryKind Kind, bool Trusted = false, bool MonitoringEnabled = false, string? IntendedBranch = null);
public sealed record FileState(string Path, string? OriginalPath, char IndexStatus, char WorktreeStatus, FileKind Kind, string Submodule = "N...", long? Size = null, bool? Binary = null)
{
    public bool IsStaged => Kind != FileKind.Untracked && IndexStatus != '.' && IndexStatus != '?';
    public bool IsUnstaged => Kind == FileKind.Untracked || (WorktreeStatus != '.' && WorktreeStatus != '?');
}
public sealed record PushDestination(string Remote, string Url, string SourceRef, string DestinationRef, string? Problem = null);
public sealed record RemoteObservation(RemoteCheckState State, DateTimeOffset? LastSuccessfulCheck = null, string? Remote = null, string? Error = null);
public sealed record RepositorySnapshot(Guid ProjectId, long Generation, DateTimeOffset ObservedAt, ProjectRegistration Project, string? HeadId, string? Branch, BranchState BranchState, string? Upstream, string? UpstreamId, PushDestination? Push, ImmutableArray<FileState> Files, GitOperation ActiveOperation, int? Ahead, int? Behind, bool IsShallow, RemoteObservation Remote, string Fingerprint, string GitVersion, ImmutableArray<string> Problems)
{
    public string? AuthorName { get; init; }
    public string? AuthorEmail { get; init; }
    public bool HasAuthorIdentity => !string.IsNullOrWhiteSpace(AuthorName) && !string.IsNullOrWhiteSpace(AuthorEmail);
    public ImmutableArray<string> RemoteNames { get; init; } = [];
    public bool HasRemotes => !RemoteNames.IsDefaultOrEmpty;
    public bool IsClean => Files.Length == 0;
    public bool HasConflicts => Files.Any(f => f.Kind == FileKind.Conflicted);
    public bool HasLocalEdits => Files.Any(f => f.IsStaged || f.IsUnstaged);
}
public sealed record Recommendation(string Code, string Title, string Reason, string Effects, string Prerequisites, string Recovery, OperationKind? Action, AccessLevel RequiredAccess, string Lesson);
public sealed record RecommendationPlan(long SnapshotGeneration, UserGoal Goal, ImmutableArray<Recommendation> Steps);
public sealed record CommitNode(string Id, ImmutableArray<string> Parents, string Author, string Email, DateTimeOffset Date, string Subject, ImmutableArray<string> Refs);
public sealed record HistoryRequest(string? LeftCommitId = null, string? RightCommitId = null, int Skip = 0, int Take = 200);
public sealed record HistoryComparison(string LeftCommitId, string? RightCommitId, int? LeftOnly, int? RightOnly);
public sealed record HistoryPage(ImmutableArray<CommitNode> Commits, bool HasMore, bool IsShallow, string? Anchor, HistoryComparison? Comparison = null);
public sealed record BranchInfo(string Name, string Id, bool IsCurrent, string? Upstream, bool IsRemote);
public enum DiffKind { Worktree, Staged, Commit, Comparison }
public sealed record DiffRequest(DiffKind Kind = DiffKind.Worktree, string? Path = null, string? CommitId = null, string? CompareToId = null);
public sealed record DiffResult(string Text, bool Truncated, ImmutableArray<string> Paths, string? Problem = null);
public sealed record IgnoreSuggestion(string Pattern, string Reason, ImmutableArray<string> AffectedUntracked, ImmutableArray<string> AlreadyTracked);
public sealed record OperationRequest(Guid ProjectId, OperationKind Kind, ImmutableArray<string> Paths = default, string? Message = null, string? Remote = null, string? RemoteUrl = null, string? Branch = null, string? TargetId = null, ImmutableArray<string> IgnorePatterns = default, bool IncludeUntracked = false, bool SetUpstream = false, string? AuthorName = null, string? AuthorEmail = null);
public sealed record OperationPreview(Guid Id, OperationRequest Request, long SnapshotGeneration, string Fingerprint, long AccessGeneration, AccessLevel AccessLevel, string Title, string Explanation, string CommandDisplay, ImmutableArray<string> Targets, string? ReviewedHeadId, string? ReviewedUpstreamId, DateTimeOffset CreatedAt, string? ReviewedIndexFingerprint = null);
public sealed record OperationEvent(Guid OperationId, Guid ProjectId, DateTimeOffset Time, OperationState State, string Stream, string Text, OperationKind Kind);
public sealed record OperationResult(Guid OperationId, OperationState State, int? ExitCode, string Summary, string Output, string Error, RepositorySnapshot Snapshot, string? PreservedStashId = null, bool OutputTruncated = false);
public sealed record AccessGrant(Guid ProjectId, AccessLevel Level, Guid SessionId, long Generation, AccessLevel? PendingLevel = null);
public sealed class AccessDeniedException(string message) : InvalidOperationException(message);
public sealed class StalePreviewException(string message) : InvalidOperationException(message);
