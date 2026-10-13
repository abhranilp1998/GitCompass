using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GitCompass.Application;
using GitCompass.Domain;
using GitCompass.Infrastructure.Git;

var gitPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? @"C:\Program Files\Git\cmd\git.exe";
// Ordinary folders must be outside the checkout so Git discovery cannot inherit this project's history.
var fixtureBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GitCompass-tests", "integration"));
Directory.CreateDirectory(fixtureBase);
var fixture = Path.Combine(fixtureBase, "core-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
Console.WriteLine("Disposable fixture: " + fixture);
var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); Console.WriteLine($"PASS {++count}: {label}"); }
async Task Expect<T>(Func<Task> action, string label) where T : Exception { try { await action(); throw new Exception("Expected " + typeof(T).Name + ": " + label); } catch (T) { Check(true, label); } }
async Task<string> Git(string cwd, params string[] command)
{
    var psi = new ProcessStartInfo(gitPath) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
    foreach (var arg in new[] { "-c", "core.autocrlf=false", "-c", "commit.gpgSign=false", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false" }.Concat(command)) psi.ArgumentList.Add(arg);
    foreach (var key in psi.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) psi.Environment.Remove(key);
    psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
    using var process = Process.Start(psi)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
    var result = await stdout; var error = await stderr;
    if (process.ExitCode != 0) throw new Exception("Fixture Git failed: " + string.Join(' ', command) + "\n" + error);
    return result.TrimEnd('\r', '\n');
}
string TreeHash(string path)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) { hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(path, file))); hash.AppendData(File.ReadAllBytes(file)); }
    return Convert.ToHexString(hash.GetHashAndReset());
}
var workspace = GitWorkspaceFactory.Create(gitPath);
var events = new List<OperationEvent>(); workspace.Activity += e => events.Add(e);
async Task<OperationResult> Operate(Guid id, OperationKind kind, ImmutableArray<string> paths = default, string? message = null, string? remote = null, string? url = null, string? branch = null, string? target = null, bool untracked = false, bool upstream = false)
{
    var preview = await workspace.PreviewAsync(new(id, kind, paths, message, remote, url, branch, target, IncludeUntracked: untracked, SetUpstream: upstream));
    return await workspace.ExecuteAsync(preview);
}
async Task Success(Guid id, OperationKind kind, ImmutableArray<string> paths = default, string? message = null, string? remote = null, string? url = null, string? branch = null, string? target = null, bool untracked = false, bool upstream = false)
{
    var result = await Operate(id, kind, paths, message, remote, url, branch, target, untracked, upstream);
    Check(result.State == OperationState.Succeeded, kind + " succeeds: " + result.Summary + (result.State == OperationState.Succeeded ? "" : "\n" + result.Error));
}
void Write(string root, string file, string content) { var path = Path.Combine(root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content, new UTF8Encoding(false)); }

if (args.Contains("--focused"))
{
    var local = Path.Combine(fixture, "focused local"); var remote = Path.Combine(fixture, "focused remote.git"); Directory.CreateDirectory(local); Directory.CreateDirectory(remote);
    await Git(local, "init", "--initial-branch=main"); await Git(local, "config", "user.name", "Focused Fixture"); await Git(local, "config", "user.email", "focused@example.test"); await Git(local, "config", "commit.gpgSign", "false"); await Git(local, "config", "core.autocrlf", "false");
    Write(local, "version.txt", "initial\n"); await Git(local, "add", "--", "version.txt"); await Git(local, "commit", "-m", "Base"); await Git(remote, "init", "--bare", "--initial-branch=main"); await Git(local, "remote", "add", "origin", remote); await Git(local, "push", "--set-upstream", "origin", "main:main"); await Git(local, "push", "origin", "main:topic");
    var opened = await workspace.OpenAsync(local); var localId = opened.ProjectId; workspace.SetTrusted(localId, true); workspace.Access.SetLevel(localId, AccessLevel.MonitorOnly);
    await Success(localId, OperationKind.Fetch, remote: "origin"); Check((await workspace.BranchesAsync(localId)).Any(b => b.Name == "origin/topic"), "Approved tracking branch exists before scoped pruning");
    await Git(local, "branch", "do-not-touch"); await Git(local, "tag", "do-not-touch"); await Git(local, "config", "--add", "remote.origin.fetch", "+refs/heads/*:refs/heads/unsafe/*"); await Git(local, "config", "--add", "remote.origin.fetch", "+refs/heads/*:refs/tags/unsafe/*"); await Git(local, "config", "remote.origin.pruneTags", "true");
    var headsAndTags = await Git(local, "show-ref", "--heads", "--tags"); var filesBefore = File.ReadAllText(Path.Combine(local, "version.txt")); await Git(remote, "update-ref", "-d", "refs/heads/topic");
    await Success(localId, OperationKind.Fetch, remote: "origin"); Check((await workspace.BranchesAsync(localId)).All(b => b.Name != "origin/topic") && await Git(local, "show-ref", "--heads", "--tags") == headsAndTags && File.ReadAllText(Path.Combine(local, "version.txt")) == filesBefore, "Scoped pruning removes only approved tracking refs despite local/tag refspecs and pruneTags configuration");
    workspace.Access.SetLevel(localId, AccessLevel.AllowChanges);
    Write(local, "upload.txt", "explicit push review\n"); await Success(localId, OperationKind.Stage, ["upload.txt"]); await Success(localId, OperationKind.Commit, message: "Publish selected work safely");
    var serverChangedPreview = await workspace.PreviewAsync(new(localId, OperationKind.Push));
    await Git(local, "push", "--", remote, "HEAD:refs/heads/main");
    var serverChangedResult = await workspace.ExecuteAsync(serverChangedPreview);
    Check(serverChangedResult.State == OperationState.Stale && events.Any(e => e.OperationId == serverChangedPreview.Id && e.State == OperationState.Stale), "A server advance within reviewed local ancestry still invalidates the original outgoing review and emits a truthful stale result");
    var refsBeforePush = await Git(local, "show-ref", "--heads", "--tags"); await Success(localId, OperationKind.Push);
    Check(await Git(local, "show-ref", "--heads", "--tags") == refsBeforePush, "Direct reviewed push URL prevents inherited fetch mappings from creating local branches/tags during push bookkeeping");
    var intermediateUrl = Path.Combine(fixture, "reviewed intermediate.git"); var secondRewrite = Path.Combine(fixture, "unreviewed target.git");
    await Git(local, "config", "url." + intermediateUrl + ".pushInsteadOf", remote); await Git(local, "config", "url." + secondRewrite + ".pushInsteadOf", intermediateUrl);
    var rewriteHash = TreeHash(local);
    await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(localId, OperationKind.Push)), "A second URL rewrite is blocked before any upload instead of retargeting the reviewed destination");
    Check(TreeHash(local) == rewriteHash, "URL rewrite validation uses process-only overrides and changes no repository config or files");
    await Git(local, "config", "--unset-all", "url." + intermediateUrl + ".pushInsteadOf"); await Git(local, "config", "--unset-all", "url." + secondRewrite + ".pushInsteadOf");
    var collaborator = Path.Combine(fixture, "focused collaborator"); await Git(fixture, "clone", "--", remote, collaborator); await Git(collaborator, "config", "user.name", "Collaborator"); await Git(collaborator, "config", "user.email", "collaborator@example.test"); await Git(collaborator, "config", "commit.gpgSign", "false");
    Write(collaborator, "incoming.txt", "incoming\n"); await Git(collaborator, "add", "--", "incoming.txt"); await Git(collaborator, "commit", "-m", "Incoming"); await Git(collaborator, "push", "origin", "main:main"); await Success(localId, OperationKind.Fetch, remote: "origin"); workspace.Access.SetLevel(localId, AccessLevel.AllowChanges);
    var reviewedUpdate = await workspace.PreviewAsync(new(localId, OperationKind.Update)); var originalHead = workspace.GetSnapshot(localId)!.HeadId;
    var offline = remote + "-offline";
    if (!Path.GetFullPath(remote).StartsWith(fixture + Path.DirectorySeparatorChar) || !Path.GetFullPath(offline).StartsWith(fixture + Path.DirectorySeparatorChar)) throw new Exception("Fixture move escaped dedicated test root.");
    Directory.Move(remote, offline);
    try { var failedFetch = await Operate(localId, OperationKind.Fetch, remote: "origin"); Check(failedFetch.State == OperationState.Failed, "Unavailable local transport records a failed fetch"); }
    finally { Directory.Move(offline, remote); }
    var staleRemoteUpdate = await workspace.ExecuteAsync(reviewedUpdate);
    Check(staleRemoteUpdate.State == OperationState.Failed && staleRemoteUpdate.Snapshot.HeadId == originalHead, "A failed remote check after preview blocks update even when byte fingerprint stays unchanged");
    await Success(localId, OperationKind.Fetch, remote: "origin"); await Success(localId, OperationKind.Update);
    await Git(remote, "update-ref", "-d", "refs/heads/main"); await Success(localId, OperationKind.Fetch, remote: "origin"); var removed = workspace.GetSnapshot(localId)!;
    Check(removed.UpstreamId == null && removed.Ahead == null && removed.Behind == null, "Removed upstream is unknown after approved pruning, never a false aligned zero");
    Write(local, "version.txt", "cancellation check\n"); await Success(localId, OperationKind.Stage, ["version.txt"]);
    Write(local, ".git/hooks/pre-commit", "#!/bin/sh\nprintf 'entered cancellation fixture\\n'\nsleep 10\n");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var sawLiveOutput = false;
    workspace.Activity += e => { if (e.Text.Contains("entered cancellation fixture")) { sawLiveOutput = true; cancellation.Cancel(); } };
    var cancellable = await workspace.PreviewAsync(new(localId, OperationKind.Commit, Message: "Cancellable fixture"));
    var canceled = await workspace.ExecuteAsync(cancellable, cancellation.Token);
    Check(sawLiveOutput && canceled.State == OperationState.OutcomeUnknown && File.ReadAllText(Path.Combine(local, "version.txt")) == "cancellation check\n", "Live stdout arrives before exit; process cancellation preserves work and reports uncertainty without generic lock deletion");
    Write(local, ".git/hooks/pre-commit", "#!/bin/sh\nprintf 'background pipe holder fixture\\n'\nsleep 30 &\nexit 0\n");
    var orphanPreview = await workspace.PreviewAsync(new(localId, OperationKind.Commit, Message: "Orphan pipe fixture")); var duration = Stopwatch.StartNew();
    var orphanResult = await workspace.ExecuteAsync(orphanPreview); duration.Stop();
    Check(duration.Elapsed < TimeSpan.FromSeconds(10) && orphanResult.State == OperationState.OutcomeUnknown && orphanResult.OutputTruncated, "Root Git exit cannot hang on a background helper's inherited pipes; owned descendants are closed and uncertainty/truncation recorded");
    Check(workspace.Access.Get(localId).PendingLevel == null, "Owned orphan cleanup releases the access lease");
    File.Delete(Path.Combine(local, ".git/hooks/pre-commit"));
    Write(local, "version.txt", "preserved stash side\n"); Write(local, "restoration-note.txt", "preserved untracked note\n");
    var conflictStash = await Operate(localId, OperationKind.Stash, untracked: true);
    Check(conflictStash.State == OperationState.Succeeded && conflictStash.PreservedStashId != null, "Conflict fixture preserves tracked and explicitly included untracked work by exact stash ID");
    Write(local, "version.txt", "new committed side\n"); await Success(localId, OperationKind.Stage, ["version.txt"]); await Success(localId, OperationKind.Commit, message: "Commit a different restoration base");
    var restoredConflict = await Operate(localId, OperationKind.ApplyStash, target: conflictStash.PreservedStashId);
    Check(restoredConflict.State == OperationState.Failed && restoredConflict.Snapshot.HasConflicts && restoredConflict.Snapshot.ActiveOperation == GitOperation.None && await Git(local, "rev-parse", "refs/stash") == conflictStash.PreservedStashId && await Git(local, "show", conflictStash.PreservedStashId + "^3:restoration-note.txt") == "preserved untracked note", "Real stash restoration conflict lists unmerged paths and retains the exact stash including untracked contents");
    await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(localId, OperationKind.Push)), "Unmerged stash-restoration paths pause publication even without a recorded merge operation");
    Console.WriteLine($"{count} focused real-Git assertions passed."); return;
}

// Direct parser injection exercises filename delimiters that cannot be created on Windows.
var raw = "# branch.oid (initial)\0# branch.head main\0? space\tquote\"\n雪.txt\0";
var parsed = PorcelainV2Parser.Parse(raw);
Check(parsed.BranchState == BranchState.Unborn && parsed.Files[0].Path == "space\tquote\"\n雪.txt", "NUL parser preserves tabs/newlines/quotes/Unicode paths");
var rename = "2 R. N... 100644 100644 100644 aaaa bbbb R100 renamed path\0old path\0";
Check(PorcelainV2Parser.Parse(rename).Files[0] is { Path: "renamed path", OriginalPath: "old path" }, "NUL parser preserves rename pair order");
await Expect<FormatException>(() => Task.Run(() => PorcelainV2Parser.Parse("? missing-terminator")), "Incomplete machine records are rejected");
await Expect<FormatException>(() => Task.Run(() => PorcelainV2Parser.Parse("2 R. N... 100644 100644 100644 aaaa bbbb R100 missing old\0")), "Incomplete rename record is rejected");
var unmerged = PorcelainV2Parser.Parse("u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflicted path\0");
Check(unmerged.Files.Single().Kind == FileKind.Conflicted, "Unmerged record preserves conflict classification");

var first = Path.Combine(fixture, "project one"); Directory.CreateDirectory(first); Write(first, "hello 雪.txt", "original\n");
var candidateHash = TreeHash(first); var snapshot = await workspace.OpenAsync(first); var id = snapshot.ProjectId;
Check(snapshot.Project.Kind == RepositoryKind.Candidate && TreeHash(first) == candidateHash && !Directory.Exists(Path.Combine(first, ".git")), "Opening candidate is strictly nonmutating");
await Expect<AccessDeniedException>(() => workspace.PreviewAsync(new(id, OperationKind.Initialize, Branch: "main")), "Read-only initialization denied centrally");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges);
await Expect<AccessDeniedException>(() => workspace.PreviewAsync(new(id, OperationKind.Initialize, Branch: "main")), "Untrusted configured effects require deliberate trust");
workspace.SetTrusted(id, true);
await Success(id, OperationKind.Initialize, branch: "main");
// Empty local values override any developer-wide identity so this prerequisite fixture is deterministic.
await Git(first, "config", "--local", "user.name", ""); await Git(first, "config", "--local", "user.email", "");
var beforeIdentityInspection = TreeHash(first);
snapshot = await workspace.RefreshAsync(id);
Check(!snapshot.HasAuthorIdentity && string.IsNullOrWhiteSpace(snapshot.AuthorName) && string.IsNullOrWhiteSpace(snapshot.AuthorEmail) && !snapshot.HasRemotes && snapshot.Push is null && TreeHash(first) == beforeIdentityInspection, "Missing author and absent remote are inspected without changing any repository bytes or inventing an upload error");
Check(workspace.Plan(id).Steps[0].Code == "author-missing" && workspace.Plan(id).Steps.All(step => step.Action != OperationKind.Fetch), "Actual first-save guidance surfaces author setup before an impossible remote check");
var identity = await workspace.PreviewAsync(new(id, OperationKind.ConfigureIdentity, AuthorName: "Fixture Author", AuthorEmail: "fixture@example.test"));
Check((await workspace.ExecuteAsync(identity)).State == OperationState.Succeeded, "Author identity changes only reviewed local config");
snapshot = workspace.GetSnapshot(id)!;
Check(snapshot.HasAuthorIdentity && snapshot.AuthorName == "Fixture Author" && snapshot.AuthorEmail == "fixture@example.test" && workspace.Plan(id).Steps.All(step => step.Code != "author-missing"), "Refreshed author details complete the actual first-save prerequisite");
await Git(first, "config", "commit.gpgSign", "false");
await Git(first, "config", "core.autocrlf", "false");
Write(first, ".gitattributes", "* text=auto eol=lf\n");
Write(first, "-leading.txt", "literal\n"); Write(first, "[wildcard].txt", "exact\n"); Write(first, "not-selected.txt", "left untracked\n");
await Success(id, OperationKind.Stage, ["hello 雪.txt", "-leading.txt", "[wildcard].txt"]);
snapshot = workspace.GetSnapshot(id)!;
Check(snapshot.Files.Count(f => f.IsStaged) == 3 && snapshot.Files.Single(f => f.Path == "not-selected.txt").Kind == FileKind.Untracked, "Staging uses exact Unicode/leading-hyphen/literal wildcard paths");
Write(first, "[wildcard].txt", "edited again after staging\n");
await Success(id, OperationKind.Unstage, ["[wildcard].txt"]);
Check(workspace.GetSnapshot(id)!.Files.Single(f => f.Path == "[wildcard].txt").Kind == FileKind.Untracked && File.Exists(Path.Combine(first, "[wildcard].txt")), "Unstage on unborn branch preserves working copy");
await Success(id, OperationKind.Stage, ["[wildcard].txt", "not-selected.txt", ".gitattributes"]);
await Success(id, OperationKind.Commit, message: "First local version\n\nRecorded from fixture files.");
var initial = workspace.GetSnapshot(id)!.HeadId!;
Check(await Git(first, "rev-parse", "HEAD") == initial, "Commit result is a real recorded Git object");
Check(workspace.Plan(id).Steps.Any(step => step.Code == "local-only") && workspace.Plan(id).Steps.All(step => step.Action is not (OperationKind.Fetch or OperationKind.ConfigureRemote)) && workspace.Plan(id, UserGoal.UploadWork).Steps.Any(step => step.Action == OperationKind.ConfigureRemote), "A real local-only saved project makes sharing optional until the user chooses upload");

// Helpers are configured to leave evidence if inspection executes them.
Write(first, ".git/forbidden-helper.sh", "#!/bin/sh\nprintf forbidden > helper-was-run\n");
await Git(first, "config", "core.fsmonitor", ".git/forbidden-helper.sh"); await Git(first, "config", "diff.external", ".git/forbidden-helper.sh");
Write(first, "hello 雪.txt", "modified same-length?\n");
workspace.Access.SetLevel(id, AccessLevel.ReadOnly); var hashBefore = TreeHash(first);
snapshot = await workspace.RefreshAsync(id); var history = await workspace.HistoryAsync(id); var diff = await workspace.DiffAsync(id, new(Path: "hello 雪.txt")); var branchList = await workspace.BranchesAsync(id);
Check(TreeHash(first) == hashBefore && !File.Exists(Path.Combine(first, "helper-was-run")), "Read-only status/history/diff/branches change no repository content and invoke no configured helper");
// A blob-filtered clone must report unavailable contents rather than fetching them during inspection.
var partialSource = Path.Combine(fixture, "partial source.git");
await Git(fixture, "clone", "--bare", "--no-local", "--", first, partialSource);
await Git(partialSource, "config", "uploadpack.allowFilter", "true");
var partialClient = Path.Combine(fixture, "partial inspection");
await Git(fixture, "clone", "--no-checkout", "--filter=blob:none", "--", new Uri(partialSource).AbsoluteUri, partialClient);
var missingBlob = await Git(partialClient, "rev-parse", "HEAD:hello 雪.txt");
var missingObjects = await Git(partialClient, "--no-lazy-fetch", "rev-list", "--objects", "--missing=print", "HEAD");
Check(missingObjects.Split('\n').Contains("?" + missingBlob), "Controlled partial-clone fixture truly lacks a committed blob locally");
var partialSnapshot = await workspace.OpenAsync(partialClient); var partialBefore = TreeHash(partialClient);
var partialDiff = await workspace.DiffAsync(partialSnapshot.ProjectId, new(DiffKind.Commit, CommitId: partialSnapshot.HeadId));
Check(partialDiff.Problem is not null, "Partial-clone inspection reports unavailable committed contents instead of silently fetching");
Check(TreeHash(partialClient) == partialBefore && (await Git(partialClient, "--no-lazy-fetch", "rev-list", "--objects", "--missing=print", "HEAD")).Split('\n').Contains("?" + missingBlob), "Reading missing partial-clone contents preserves all repository bytes and leaves the blob unfetched");
Check(history.Commits.Single().Id == initial && diff.Text.Contains("modified") && branchList.Single(b => b.IsCurrent).Name == "main", "Actual history, safe diff and branch inventory agree with Git");
await Expect<AccessDeniedException>(() => workspace.PreviewAsync(new(id, OperationKind.Fetch, Remote: "team")), "Read-only fetch denied before remote process");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges);
var stale = await workspace.PreviewAsync(new(id, OperationKind.Stage, Paths: ["hello 雪.txt"])); Write(first, "hello 雪.txt", "changed after preview\n");
await Expect<StalePreviewException>(() => workspace.ExecuteAsync(stale), "A content change after review invalidates preview even when status letters match");
var revoked = await workspace.PreviewAsync(new(id, OperationKind.Stage, Paths: ["hello 雪.txt"])); workspace.Access.SetLevel(id, AccessLevel.ReadOnly);
await Expect<AccessDeniedException>(() => workspace.ExecuteAsync(revoked), "Grant revocation rejects an already reviewed queued action");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges); await Success(id, OperationKind.Stage, ["hello 雪.txt"]); await Success(id, OperationKind.Commit, message: "Review changed contents");

var bare = Path.Combine(fixture, "team remote.git"); Directory.CreateDirectory(bare); await Git(bare, "init", "--bare", "--initial-branch=main");
await Success(id, OperationKind.ConfigureRemote, remote: "team", url: bare, branch: "main", upstream: true);
Check(workspace.GetSnapshot(id)!.HasRemotes && workspace.GetSnapshot(id)!.RemoteNames.SequenceEqual(["team"]) && workspace.Plan(id).Steps.Any(step => step.Action == OperationKind.Fetch), "Configured named remote is inspected and restores permitted team-check guidance");
workspace.Access.SetLevel(id, AccessLevel.MonitorOnly);
await Success(id, OperationKind.Fetch, remote: "team");
await Expect<AccessDeniedException>(() => workspace.PreviewAsync(new(id, OperationKind.Push)), "Monitor-only push denied");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges); await Success(id, OperationKind.Push, upstream: true);
Check(await Git(bare, "rev-parse", "refs/heads/main") == workspace.GetSnapshot(id)!.HeadId, "Initial publication verified against local bare remote");
await Git(first, "config", "remote.team.mirror", "true");
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Push)), "Mirroring configuration cannot turn guided push into broad ref writes");
await Git(first, "config", "remote.team.mirror", "false");
await Git(first, "config", "--add", "remote.team.pushurl", bare); await Git(first, "config", "--add", "remote.team.pushurl", bare);
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Push)), "Multiple actual push URLs are blocked instead of duplicating upload effects");
await Git(first, "config", "--unset-all", "remote.team.pushurl");

var second = Path.Combine(fixture, "colleague two"); Directory.CreateDirectory(second); var secondCandidate = await workspace.OpenAsync(second); var secondId = secondCandidate.ProjectId; workspace.SetTrusted(secondId, true); workspace.Access.SetLevel(secondId, AccessLevel.AllowChanges);
await Success(secondId, OperationKind.Clone, url: bare);
await Git(second, "config", "user.name", "Second Fixture"); await Git(second, "config", "user.email", "second@example.test"); await Git(second, "config", "commit.gpgSign", "false");
await Git(second, "config", "core.autocrlf", "false");
Write(second, "incoming.txt", "from colleague\n"); await Git(second, "add", "--", "incoming.txt"); await Git(second, "commit", "-m", "Incoming work"); await Git(second, "push", "origin", "refs/heads/main:refs/heads/main");
workspace.Access.SetLevel(id, AccessLevel.MonitorOnly);
// An unsafe inherited fetch refspec must not touch local branch refs or tags.
await Git(first, "config", "--add", "remote.team.fetch", "+refs/heads/*:refs/heads/unsafe/*"); await Git(first, "config", "--add", "remote.team.fetch", "+refs/heads/*:refs/tags/unsafe/*");
var localBefore = await Git(first, "show-ref", "--heads", "--tags"); var workBefore = File.ReadAllText(Path.Combine(first, "hello 雪.txt"));
await Success(id, OperationKind.Fetch, remote: "team");
Check(await Git(first, "show-ref", "--heads", "--tags") == localBefore && File.ReadAllText(Path.Combine(first, "hello 雪.txt")) == workBefore, "Monitor fetch overrides malicious local-branch/tag ref mappings and preserves worktree");
snapshot = workspace.GetSnapshot(id)!; Check(snapshot.Ahead == 0 && snapshot.Behind == 1, "Fetched ahead/behind reflect actual ancestry");
await Git(first, "remote", "add", "other", bare); await Success(id, OperationKind.Fetch, remote: "other");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges);
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Update)), "A different remote's recent fetch does not authorize updating the configured upstream");
await Success(id, OperationKind.Fetch, remote: "team"); Write(first, "hello 雪.txt", "unsaved local work\n");
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Update)), "Dirty behind-only update is blocked");
Write(first, "untracked preservation.txt", "keep me\n"); var stashResult = await Operate(id, OperationKind.Stash, untracked: true);
Check(stashResult.State == OperationState.Succeeded && stashResult.PreservedStashId != null && workspace.GetSnapshot(id)!.IsClean, "Reviewed stash preserves tracked and explicitly included untracked work: " + stashResult.State + " " + stashResult.Error + " files=" + string.Join(',', stashResult.Snapshot.Files.Select(f => f.Path)));
await Success(id, OperationKind.Update); Check(workspace.GetSnapshot(id)!.HeadId == workspace.GetSnapshot(id)!.UpstreamId, "Fast-forward reaches exactly the reviewed upstream ID");
await Success(id, OperationKind.ApplyStash, target: stashResult.PreservedStashId);
Check(File.ReadAllText(Path.Combine(first, "untracked preservation.txt")) == "keep me\n" && await Git(first, "rev-parse", "refs/stash") == stashResult.PreservedStashId, "Stash restoration uses exact identity and keeps stash available");
await Success(id, OperationKind.Stage, ["hello 雪.txt", "untracked preservation.txt"]); await Success(id, OperationKind.Commit, message: "Preserved local work");

// Distinct push remote and source/destination branch are represented explicitly.
await Git(first, "config", "branch.main.pushRemote", "team"); await Git(first, "config", "push.default", "current");
await Success(id, OperationKind.Fetch, remote: "team"); await Success(id, OperationKind.Push);
var firstHead = workspace.GetSnapshot(id)!.HeadId;
await Git(second, "fetch", "origin"); await Git(second, "merge", "--ff-only", "origin/main");
Write(second, "from second.txt", "remote version\n"); await Git(second, "add", "--", "from second.txt"); await Git(second, "commit", "-m", "Second concurrent work"); await Git(second, "push", "origin", "refs/heads/main:refs/heads/main");
Write(first, "local branch work.txt", "local version\n"); await Success(id, OperationKind.Stage, ["local branch work.txt"]); await Success(id, OperationKind.Commit, message: "Local concurrent work"); await Success(id, OperationKind.Fetch, remote: "team");
snapshot = workspace.GetSnapshot(id)!; Check(snapshot.Ahead == 1 && snapshot.Behind == 1 && workspace.Plan(id).Steps.Any(p => p.Code == "diverged"), "Two-clone divergence has one real commit on each side");
workspace.Access.SetLevel(id, AccessLevel.ReadOnly); var comparisonHash = TreeHash(first);
var selectedHistory = await workspace.HistoryAsync(id, new HistoryRequest(snapshot.HeadId, snapshot.UpstreamId, Take: 1));
var selectedNextPage = await workspace.HistoryAsync(id, new HistoryRequest(snapshot.HeadId, snapshot.UpstreamId, Skip: 1, Take: 1));
Check(selectedHistory.Comparison is { LeftOnly: 1, RightOnly: 1 } && selectedHistory.HasMore && selectedHistory.Anchor == selectedNextPage.Anchor && selectedHistory.Commits[0].Id != selectedNextPage.Commits[0].Id && TreeHash(first) == comparisonHash, "Selected real branch comparison and exact-anchor pagination read actual ancestry without changing worktree/index/refs/objects/config");
await Expect<InvalidOperationException>(() => workspace.HistoryAsync(id, new HistoryRequest("HEAD; unsafe text", snapshot.UpstreamId)), "History comparison rejects arbitrary shell/revision text instead of interpolating it");
await Expect<InvalidOperationException>(() => workspace.HistoryAsync(id, new HistoryRequest(initial, snapshot.UpstreamId)), "Selected comparison IDs must belong to the current branch inventory, even when an older commit exists");
workspace.Access.SetLevel(id, AccessLevel.AllowChanges);
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Update)), "Divergence never uses simple update");
await Success(id, OperationKind.Merge);
history = await workspace.HistoryAsync(id); Check(history.Commits[0].Parents.Length == 2, "Graph records both real merge parents");
await Success(id, OperationKind.CreateBranch, branch: "feature/lesson", target: firstHead);
await Success(id, OperationKind.SwitchBranch, branch: "feature/lesson");
Check(workspace.GetSnapshot(id)!.Branch == "feature/lesson", "Reviewed branch switch uses explicit existing local branch");
await Success(id, OperationKind.SwitchBranch, branch: "main");

// A stale server changes after a valid push preview; normal Git rejection preserves local work.
await Success(id, OperationKind.Fetch, remote: "team"); var pushPreview = await workspace.PreviewAsync(new(id, OperationKind.Push));
Write(second, "server raced.txt", "server changed after review\n"); await Git(second, "add", "--", "server raced.txt"); await Git(second, "commit", "-m", "Server race"); await Git(second, "push", "origin", "refs/heads/main:refs/heads/main");
var localCommit = workspace.GetSnapshot(id)!.HeadId; var rejection = await workspace.ExecuteAsync(pushPreview);
Check(rejection.State is OperationState.Failed or OperationState.Stale && rejection.Snapshot.HeadId == localCommit, "Concurrent server push invalidates/rejects publication without force or local commit loss");
await Success(id, OperationKind.Fetch, remote: "team"); await Success(id, OperationKind.Merge); await Success(id, OperationKind.Push);

// Resolve a genuine content conflict, including a plain abort path.
await Git(second, "fetch", "origin"); await Git(second, "merge", "--ff-only", "origin/main");
Write(first, "hello 雪.txt", "first conflict side\n"); await Success(id, OperationKind.Stage, ["hello 雪.txt"]); await Success(id, OperationKind.Commit, message: "Local conflicting change");
Write(second, "hello 雪.txt", "second conflict side\n"); await Git(second, "add", "--", "hello 雪.txt"); await Git(second, "commit", "-m", "Remote conflicting change"); await Git(second, "push", "origin", "refs/heads/main:refs/heads/main");
await Success(id, OperationKind.Fetch, remote: "team"); var conflict = await Operate(id, OperationKind.Merge);
Check(conflict.State == OperationState.Failed && conflict.Snapshot.ActiveOperation == GitOperation.Merge && conflict.Snapshot.HasConflicts, "Real merge conflict pauses ordinary operations and lists conflicted exact paths");
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(id, OperationKind.Push)), "Push paused during recorded conflict");
await Success(id, OperationKind.AbortMerge); Check(!workspace.GetSnapshot(id)!.HasConflicts, "Reviewed abort clears recorded merge without generic reset");
await Operate(id, OperationKind.Merge); Write(first, "hello 雪.txt", "combined reviewed result\n"); await Success(id, OperationKind.Stage, ["hello 雪.txt"]); await Success(id, OperationKind.ContinueMerge);
Check(workspace.GetSnapshot(id)!.ActiveOperation == GitOperation.None, "Deliberately staged resolution continues supported merge");

var secondBare = Path.Combine(fixture, "alternate push.git"); Directory.CreateDirectory(secondBare); await Git(secondBare, "init", "--bare", "--initial-branch=main");
var urlAlias = "https://fixture.invalid/rewritten.git";
await Git(first, "remote", "add", "publication", urlAlias);
await Git(first, "config", "url." + bare.Replace('\\', '/') + ".insteadOf", urlAlias);
await Git(first, "config", "url." + secondBare.Replace('\\', '/') + ".pushInsteadOf", urlAlias);
await Git(first, "config", "branch.main.pushRemote", "publication");
await Success(id, OperationKind.Fetch, remote: "publication"); snapshot = workspace.GetSnapshot(id)!;
Check(snapshot.Push!.Url.Replace('\\', '/') == secondBare.Replace('\\', '/'), "Actual push URL resolves pushInsteadOf independently from fetch rewriting");
await Success(id, OperationKind.Push);
Check(await Git(secondBare, "rev-parse", "refs/heads/main") == workspace.GetSnapshot(id)!.HeadId, "Verified publication uses the rewritten actual push destination");
await Git(first, "config", "branch.main.pushRemote", "team");

var candidateParent = Path.Combine(fixture, "candidate parent"); var candidateChild = Path.Combine(candidateParent, "selected child"); Directory.CreateDirectory(candidateChild);
var registeredChild = await workspace.OpenAsync(candidateChild); workspace.SetTrusted(registeredChild.ProjectId, true); workspace.Access.SetLevel(registeredChild.ProjectId, AccessLevel.AllowChanges);
await Git(candidateParent, "init", "--initial-branch=main"); var parentHash = TreeHash(candidateParent);
var retargeted = await workspace.RefreshAsync(registeredChild.ProjectId);
Check(retargeted.Project.Kind == RepositoryKind.Invalid && retargeted.Project.RootPath == candidateChild && TreeHash(candidateParent) == parentHash, "External parent initialization cannot retarget a candidate's identity or grant");
await Expect<InvalidOperationException>(() => workspace.PreviewAsync(new(registeredChild.ProjectId, OperationKind.Initialize, Branch: "main")), "Retargeted candidate writes are blocked centrally");

Write(first, "project.csproj", "<Project />\n"); Write(first, "obj/generated.cache", "generated\n");
var ignoreSuggestions = await workspace.IgnoreSuggestionsAsync(id);
Check(ignoreSuggestions.Any(s => s.Pattern == "obj/" && s.AffectedUntracked.Contains("obj/generated.cache")), "Project-specific ignore preview identifies generated untracked files");
Write(first, ".gitignore", "# existing comment\nlocal-private/\n"); await workspace.RefreshAsync(id);
var ignorePreview = await workspace.PreviewAsync(new(id, OperationKind.ApplyIgnore, IgnorePatterns: ["obj/", "bin/"]));
Check((await workspace.ExecuteAsync(ignorePreview)).State == OperationState.Succeeded && File.ReadAllText(Path.Combine(first, ".gitignore")).StartsWith("# existing comment\nlocal-private/\n"), "Ignore application preserves existing comments/rules and appends reviewed patterns");
await Success(id, OperationKind.Stage, ["project.csproj", ".gitignore"]); await Success(id, OperationKind.Commit, message: "Reviewed project ignores");

// A hook may emit both streams, but a throwing sink cannot stop pipe draining; changed index is detected after commit.
Write(first, ".git/hooks/pre-commit", "#!/bin/sh\nprintf 'stdout progress\\n'\nprintf 'stderr progress\\n' >&2\nprintf 'added by trusted hook\\n' > hook-added.txt\ngit add -- hook-added.txt\n");
Write(first, "local branch work.txt", "hook validation change\n"); await Success(id, OperationKind.Stage, ["local branch work.txt"]);
workspace.Activity += _ => throw new InvalidOperationException("Injected failing observer");
var hookedCommit = await Operate(id, OperationKind.Commit, message: "Hook verification fixture");
Check(hookedCommit.State == OperationState.OutcomeUnknown && File.Exists(Path.Combine(first, "hook-added.txt")), "Hook-altered committed contents cannot be labeled the reviewed commit; subscriber errors do not deadlock pipes");
File.Delete(Path.Combine(first, ".git/hooks/pre-commit"));
Write(first, "local branch work.txt", "reviewed branch verification\n"); await Success(id, OperationKind.Stage, ["local branch work.txt"]);
Write(first, ".git/hooks/post-commit", "#!/bin/sh\ngit switch --quiet -c hook-switched-branch\n");
var switchedCommit = await Operate(id, OperationKind.Commit, message: "Verify hook cannot silently change reviewed branch");
Check(switchedCommit.State == OperationState.OutcomeUnknown && switchedCommit.Snapshot.Branch == "hook-switched-branch", "A hook that changes the reviewed branch after commit produces an explained uncertain result");
File.Delete(Path.Combine(first, ".git/hooks/post-commit")); await Git(first, "switch", "main"); await workspace.RefreshAsync(id);

await Success(id, OperationKind.Fetch, remote: "team"); await Success(id, OperationKind.Push);
await Git(second, "fetch", "origin"); await Git(second, "merge", "--ff-only", "origin/main");
Write(second, "private collision.txt", "incoming tracked version\n"); await Git(second, "add", "--", "private collision.txt"); await Git(second, "commit", "-m", "Incoming path collides with a private ignored file"); await Git(second, "push", "origin", "refs/heads/main:refs/heads/main");
File.AppendAllText(Path.Combine(first, ".git/info/exclude"), "\nprivate collision.txt\n"); Write(first, "private collision.txt", "local private contents must survive\n");
await Success(id, OperationKind.Fetch, remote: "team"); var collidingUpdate = await Operate(id, OperationKind.Update);
Check(collidingUpdate.State == OperationState.Failed && File.ReadAllText(Path.Combine(first, "private collision.txt")) == "local private contents must survive\n", "Fast-forward stops at ignored-file overwrite collision and preserves private contents");
File.Delete(Path.Combine(first, "private collision.txt")); await Success(id, OperationKind.Update);

// Nested folder discovery and linked worktree ownership are real Git paths.
Directory.CreateDirectory(Path.Combine(first, "nested folder")); var nested = await workspace.OpenAsync(Path.Combine(first, "nested folder"));
Check(nested.Project.RootPath == first && nested.ProjectId == id, "Selecting a nested folder resolves the registered real root");
var worktree = Path.Combine(fixture, "linked worktree"); await Git(first, "worktree", "add", "-b", "linked/test", worktree);
var linked = await workspace.OpenAsync(worktree); Check(linked.Project.Kind == RepositoryKind.LinkedWorktree && linked.Project.CommonGitDirectory == workspace.GetSnapshot(id)!.Project.CommonGitDirectory, "Linked worktree shares canonical common-directory identity");
var bareInspection = await workspace.OpenAsync(bare); Check(bareInspection.Project.Kind == RepositoryKind.Bare, "Bare repository has an explained nonediting state");
Check(events.Any(e => e.Stream == "stderr") && events.All(e => !e.Text.Contains(first, StringComparison.OrdinalIgnoreCase)), "Operation events preserve stderr without labeling it failure and redact private project paths");
Console.WriteLine($"{count} real-Git integration assertions passed. Fixtures retained for inspection at {fixture}.");
