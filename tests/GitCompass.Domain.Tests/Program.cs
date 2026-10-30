using System.Collections.Immutable;
using GitCompass.Application;
using GitCompass.Domain;

var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); Console.WriteLine($"PASS {++count}: {label}"); }
void Denied(Action action, string label) { try { action(); throw new Exception("Permission unexpectedly allowed: " + label); } catch (AccessDeniedException) { Check(true, label); } }
var project = new ProjectRegistration(Guid.NewGuid(), "Practice", "folder", "folder", "common", RepositoryKind.WorkingTree);
RepositorySnapshot Snapshot(int? ahead = 0, int? behind = 0, ImmutableArray<FileState> files = default, GitOperation operation = GitOperation.None) => new(project.Id, 1, DateTimeOffset.UtcNow, project, "abcd", "main", BranchState.Attached, "team/main", "bcde", new("team", "https://example.test/repo", "refs/heads/main", "refs/heads/main"), files.IsDefault ? [] : files, operation, ahead, behind, false, new(RemoteCheckState.Succeeded, DateTimeOffset.UtcNow), "fingerprint", "test", []) { AuthorName = "Practice Author", AuthorEmail = "practice@example.test", RemoteNames = ["team"] };
var behindDirty = Snapshot(0, 2, [new("a", null, '.', 'M', FileKind.Tracked)]);
var plan = RecommendationEngine.Build(behindDirty);
Check(plan.Steps[0].Code == "preserve-work" && plan.Steps.Any(s => s.Code == "behind"), "Local preservation precedes incoming review and preserves both facts");
Check(RecommendationEngine.Build(Snapshot(2, 2)).Steps[0].Code == "diverged", "Divergence uses ancestry counts");
Check(RecommendationEngine.Build(Snapshot(operation: GitOperation.Merge)).Steps[0].Code == "operation-recovery", "Interrupted operation takes priority");
Check(!RecommendationEngine.Build(Snapshot()).Steps.Any(s => s.Code == "branch-intent"), "No branch mismatch is invented when intent is unset");
Check(RecommendationEngine.Build(Snapshot() with { Project = project with { IntendedBranch = "task" } }).Steps[0].Code == "branch-intent", "Explicit branch intent mismatch is actionable");
Check(!RecommendationEngine.Build(Snapshot(null, null) with { BranchState = BranchState.Unborn, HeadId = null, UpstreamId = null }).Steps.Any(s => s.Code is "known-aligned" or "ahead" or "behind"), "Unknown/unborn comparison never becomes zero");
Check(RecommendationEngine.Build(Snapshot() with { BranchState = BranchState.Detached, Branch = null }).Steps[0].Code == "detached", "Detached work suppresses ordinary branch publication");
Check(RecommendationEngine.Build(Snapshot() with { UpstreamId = null, Ahead = null, Behind = null }).Steps.Any(s => s.Code == "upstream-missing"), "Removed upstream stays explicitly unknown");
Check(RecommendationEngine.Build(Snapshot() with { IsShallow = true }).Steps.Any(s => s.Code == "shallow"), "Limited history is explained");
Check(RecommendationEngine.Build(Snapshot() with { Remote = new(RemoteCheckState.Failed, DateTimeOffset.UtcNow.AddDays(-1), "team", "offline") }).Steps.Any(s => s.Code == "remote-stale"), "Failed remote check keeps stale evidence visible");
Check(RecommendationEngine.Build(Snapshot(2, 0), UserGoal.GetLatest).Steps.All(s => s.Action != OperationKind.Push), "Get latest retains outgoing facts without recommending an unrequested upload");
Check(RecommendationEngine.Build(Snapshot(), UserGoal.StartTask).Steps.Any(s => s.Code == "start-task" && s.Action == OperationKind.CreateBranch) && !RecommendationEngine.Build(Snapshot(), UserGoal.StartTask).Steps.Any(s => s.Code == "branch-intent"), "Starting a task asks for an explicit branch/base without inventing intent");
Check(RecommendationEngine.Build(Snapshot(), UserGoal.PreparePullRequest).Steps.Any(s => s.Code == "prepare-pr" && s.Lesson == "pr"), "PR goal explains chosen hosted source/target after publication prerequisites");
var localOnly = Snapshot(null, null) with { Upstream = null, UpstreamId = null, Push = null, RemoteNames = [], Remote = new(RemoteCheckState.NotChecked) };
var localPlan = RecommendationEngine.Build(localOnly);
Check(localPlan.Steps.Any(s => s.Code == "local-only" && s.Action is null) && localPlan.Steps.All(s => s.Action is not (OperationKind.Fetch or OperationKind.ConfigureRemote)), "Local-only understanding explains saved history without requiring online setup or an impossible check");
Check(RecommendationEngine.Build(localOnly, UserGoal.UploadWork).Steps.Any(s => s.Code == "no-upstream" && s.Action == OperationKind.ConfigureRemote) && RecommendationEngine.Build(localOnly, UserGoal.UploadWork).Steps.All(s => s.Action != OperationKind.Fetch), "An explicit upload goal asks for a connection before checking a nonexistent remote");
Check(RecommendationEngine.Build(localOnly with { Files = [new("a", null, '.', 'M', FileKind.Tracked)] }).Steps[0].Code == "preserve-work", "A local-only project still prioritizes saving edited files over optional sharing");
var missingIdentity = localOnly with { BranchState = BranchState.Unborn, HeadId = null, AuthorName = null, AuthorEmail = null, Files = [new("new.txt", null, '?', '?', FileKind.Untracked)] };
var missingPlan = RecommendationEngine.Build(missingIdentity);
Check(!missingIdentity.HasAuthorIdentity && missingPlan.Steps[0].Code == "author-missing" && missingPlan.Steps[0].Action == OperationKind.ConfigureIdentity && missingPlan.Steps.Any(s => s.Code == "first-commit"), "Missing author details are a direct prerequisite before a beginner's first save");
Check(RecommendationEngine.Build(missingIdentity with { AuthorName = "Name", AuthorEmail = " " }).Steps[0].Code == "author-missing", "Whitespace or partial author details do not mark the prerequisite complete");
Check(RecommendationEngine.Build(localOnly with { AuthorName = null, AuthorEmail = null }).Steps.All(s => s.Code != "author-missing"), "Inspecting clean saved history does not demand author setup unnecessarily");
Check(RecommendationEngine.Build(missingIdentity with { ActiveOperation = GitOperation.Merge }).Steps.Single().Code == "operation-recovery", "Operation recovery remains above author setup and optional sharing");
Check(RecommendationEngine.Build(localOnly with { Project = project with { Kind = RepositoryKind.Candidate }, HeadId = null, AuthorName = null, AuthorEmail = null }).Steps.Single().Code == "no-repository", "An ordinary folder starts with local-history guidance before author configuration");
Check(RecommendationEngine.Build(Snapshot() with { RemoteNames = [], Remote = new(RemoteCheckState.NotChecked) }).Steps.Any(s => s.Code == "remote-stale"), "A known connected push mapping retains team checks when older callers omit the remote inventory");
var access = new AccessRegistry(); access.Register(project.Id);
Check(access.Get(project.Id).Level == AccessLevel.ReadOnly, "New projects begin Read-only");
foreach (var kind in Enum.GetValues<OperationKind>().Except([OperationKind.Inspect])) Denied(() => access.Authorize(project.Id, kind), "Read-only blocks " + kind);
access.Authorize(project.Id, OperationKind.Inspect); Check(true, "Read-only allows truthful local inspection capability");
access.SetLevel(project.Id, AccessLevel.MonitorOnly); access.Authorize(project.Id, OperationKind.Fetch); access.Authorize(project.Id, OperationKind.GitHubRead);
Check(true, "Monitor permits only fetch and hosted reads");
foreach (var kind in Enum.GetValues<OperationKind>().Except([OperationKind.Fetch, OperationKind.GitHubRead, OperationKind.Inspect])) Denied(() => access.Authorize(project.Id, kind), "Monitor blocks " + kind);
access.SetLevel(project.Id, AccessLevel.AllowChanges); var generation = access.Get(project.Id).Generation;
using (var lease = access.Acquire(project.Id, OperationKind.Terminal, generation))
{
    var grant = access.SetLevel(project.Id, AccessLevel.ReadOnly);
    Check(grant.Level == AccessLevel.AllowChanges && grant.PendingLevel == AccessLevel.ReadOnly, "Downgrade is pending while a terminal lease owns activity");
    Check(lease.Revoked.IsCancellationRequested, "Downgrade asks active owned activity to cancel");
    Denied(() => access.Authorize(project.Id, OperationKind.Terminal), "Pending downgrade denies new input");
    Denied(() => access.Authorize(project.Id, OperationKind.Stage, generation), "Queued old grant is immediately revoked");
}
Check(access.Get(project.Id).Level == AccessLevel.ReadOnly && access.Get(project.Id).PendingLevel == null, "Read-only becomes active after owned activity is closed");
var restarted = new AccessRegistry(); restarted.Register(project.Id);
Check(restarted.Get(project.Id).Level == AccessLevel.ReadOnly && restarted.SessionId != access.SessionId, "Restart does not preserve unrestricted access");
var linkedId = Guid.NewGuid(); access.Register(linkedId, repositoryIdentity: "same-common"); access.Register(project.Id, repositoryIdentity: "same-common");
access.SetLevel(project.Id, AccessLevel.AllowChanges); access.SetLevel(linkedId, AccessLevel.AllowChanges);
using (var lease = access.Acquire(project.Id, OperationKind.Commit)) Denied(() => access.Acquire(linkedId, OperationKind.Terminal), "Shared-worktree managed lease atomically blocks another project's terminal");
using (var lease = access.Acquire(linkedId, OperationKind.Terminal)) Denied(() => access.Acquire(project.Id, OperationKind.Fetch), "Shared-worktree terminal lease atomically blocks another project's metadata operation");

// Skip quiz: never blocks, scores only by correct choice, unanswered counts as missed.
Check(SkipQuiz.Questions.Length == 3 && SkipQuiz.Greetings.Length >= 5, "Skip quiz has three questions and several playful greetings");
Check(SkipQuiz.Missed(SkipQuiz.Questions.Select(q => (int?)q.Correct).ToArray()).Length == 0, "All correct answers pass the skip quiz");
Check(SkipQuiz.Missed([null, null, null]).Length == 3 && SkipQuiz.Missed([]).Length == 3, "Unanswered questions count as missed");
Check(SkipQuiz.Missed([SkipQuiz.Questions[0].Correct, (SkipQuiz.Questions[1].Correct + 1) % 4, SkipQuiz.Questions[2].Correct]).SequenceEqual([1]), "Only the wrong answer is reported for explanation");
Check(SkipQuiz.Questions.All(q => q.Correct >= 0 && q.Correct < q.Choices.Length && q.Explanation.Length > 0), "Every quiz question has a valid answer and an explanation");
Check(Enumerable.Range(0, 50).Select(i => SkipQuiz.PickGreeting(new Random(i))).All(g => SkipQuiz.Greetings.Contains(g)), "Greeting picker only returns defined title/subtitle pairs");

// Practice missions advance only on succeeded reviewed operations, in order.
var mission = BeginnerMissions.SeeChange;
Check(BeginnerMissions.Advance(mission, OperationKind.Stage, OperationState.Failed) == BeginnerMissions.SeeChange, "Failed staging does not advance a mission");
mission = BeginnerMissions.ViewedChanges(mission); Check(mission == BeginnerMissions.Stage, "Viewing changes completes mission 1");
mission = BeginnerMissions.Advance(mission, OperationKind.Stage, OperationState.Succeeded); Check(mission == BeginnerMissions.Commit, "Successful staging completes mission 2");
Check(BeginnerMissions.Advance(mission, OperationKind.Merge, OperationState.Succeeded) == BeginnerMissions.Commit, "A merge cannot skip the commit mission");
mission = BeginnerMissions.Advance(mission, OperationKind.Commit, OperationState.Succeeded); Check(mission == BeginnerMissions.BranchMerge, "Successful commit completes mission 3");
Check(BeginnerMissions.Advance(mission, OperationKind.Commit, OperationState.Succeeded) == BeginnerMissions.BranchMerge && BeginnerMissions.Advance(mission, OperationKind.CreateBranch, OperationState.Succeeded) == BeginnerMissions.BranchMerge, "Commits and branch creation on the side branch keep mission 4 open until merge");
Check(BeginnerMissions.Advance(mission, OperationKind.Merge, OperationState.Canceled) == BeginnerMissions.BranchMerge, "A canceled merge keeps mission 4 open");
mission = BeginnerMissions.Advance(mission, OperationKind.Merge, OperationState.Succeeded); Check(mission == BeginnerMissions.Push, "Successful merge completes mission 4");
Check(BeginnerMissions.FinishWithoutPush(mission) == BeginnerMissions.Done && BeginnerMissions.Advance(mission, OperationKind.Push, OperationState.Succeeded) == BeginnerMissions.Done, "Mission 5 finishes by push or by choosing not to upload");
Check(BeginnerMissions.FinishWithoutPush(BeginnerMissions.Stage) == BeginnerMissions.Stage && BeginnerMissions.Clamp(99) == BeginnerMissions.Done && BeginnerMissions.Clamp(-1) == BeginnerMissions.SeeChange, "Out-of-order skips and out-of-range indices are contained");
Check(BeginnerMissions.Missions.Length == BeginnerMissions.Count && BeginnerMissions.Missions.All(m => m.Concept.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 20), "Five missions, each concept line at most 20 words");
Console.WriteLine($"{count} domain assertions passed.");
