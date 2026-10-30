using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using GitCompass.Application;
using GitCompass.Domain;

namespace GitCompass.Infrastructure.Git;

internal sealed partial class GitBackend
{
    private readonly HashSet<string> ownedStashes = new(StringComparer.Ordinal);
    public async Task<OperationPreview> PlanAsync(OperationRequest request, RepositorySnapshot s, AccessGrant grant, CancellationToken cancellation)
    {
        if (s.Project.Kind is RepositoryKind.Invalid or RepositoryKind.Bare) throw new InvalidOperationException("Guided operations require a safely resolved working repository or candidate folder.");
        if (!s.Project.Trusted) throw new AccessDeniedException("Review and trust this project's Git configuration before allowing helpers, hooks, filters or transports to run.");
        if (request.Kind is not (OperationKind.Initialize or OperationKind.Clone) && s.Project.Kind == RepositoryKind.Candidate) throw new InvalidOperationException("Initialize or clone a repository first.");
        if (s.HasConflicts && request.Kind is not (OperationKind.Stage or OperationKind.Unstage or OperationKind.ContinueMerge or OperationKind.AbortMerge)) throw new InvalidOperationException("Resolve and deliberately stage every unmerged path before ordinary operations. The preserved stash or committed histories remain available.");
        if (s.ActiveOperation != GitOperation.None && request.Kind is not (OperationKind.Stage or OperationKind.ContinueMerge or OperationKind.AbortMerge)) throw new InvalidOperationException($"{s.ActiveOperation} is active. Finish or review that operation first.");
        var paths = request.Paths.IsDefault ? ImmutableArray<string>.Empty : request.Paths.Distinct(StringComparer.Ordinal).ToImmutableArray();
        foreach (var path in paths)
        {
            ValidatePath(s.Project, path);
            var state = s.Files.FirstOrDefault(f => f.Path == path);
            if (state == null) throw new InvalidOperationException("Only exact paths displayed in the reviewed change list may be staged or unstaged.");
            if (state.Submodule != "N...") throw new InvalidOperationException("Submodule paths require separate developer review.");
        }
        request = request with { Paths = paths };
        var targets = ImmutableArray.CreateBuilder<string>(); var title = request.Kind.ToString(); string explanation; string display;
        switch (request.Kind)
        {
            case OperationKind.Initialize:
                if (s.Project.Kind != RepositoryKind.Candidate) throw new InvalidOperationException("The selected folder already belongs to a repository. No nested repository will be initialized.");
                await ValidBranch(s.Project.RootPath, request.Branch, cancellation);
                title = "Start local version history"; explanation = "Creates .git metadata in this exact selected folder with the chosen initial branch. Files are not staged, committed or uploaded.";
                display = $"git init --initial-branch={Quote(request.Branch!)} -- <selected-folder>"; targets.Add(s.Project.RootPath); break;
            case OperationKind.Clone:
                if (s.Project.Kind != RepositoryKind.Candidate || Directory.EnumerateFileSystemEntries(s.Project.RootPath).Any()) throw new InvalidOperationException("Clone requires this exact candidate folder to be empty. Preserve your ZIP copy and select a separate empty folder.");
                ValidateUrl(request.RemoteUrl);
                explanation = "Downloads a repository into the exact selected empty folder. Your original ZIP copy is preserved. Cloning trusts configured transport helpers.";
                display = $"git clone -- {SafeUrl(request.RemoteUrl!)} <selected-empty-folder>"; targets.Add(s.Project.RootPath); targets.Add(request.RemoteUrl!); break;
            case OperationKind.Stage:
            case OperationKind.Unstage:
                if (paths.Length == 0) throw new InvalidOperationException("Choose at least one exact file from Changes.");
                explanation = request.Kind == OperationKind.Stage ? "Stages only these reviewed saved file contents, including deletions. Previously staged contents remain visible." : "Removes only these paths from the next commit; your working files are preserved.";
                display = request.Kind == OperationKind.Stage ? "git --literal-pathspecs add -- <reviewed-paths>" : s.HeadId == null ? "git rm --cached -- <reviewed-paths>" : "git restore --staged --source=<reviewed-HEAD> -- <reviewed-paths>";
                targets.AddRange(paths); break;
            case OperationKind.Commit:
                if (!s.Files.Any(f => f.IsStaged)) throw new InvalidOperationException("There are no staged contents to commit.");
                if (s.HasConflicts) throw new InvalidOperationException("Resolve every unmerged path before committing.");
                ValidateMessage(request.Message);
                if (string.IsNullOrWhiteSpace(await Config(s.Project.RootPath, "user.name", cancellation)) || string.IsNullOrWhiteSpace(await Config(s.Project.RootPath, "user.email", cancellation))) throw new InvalidOperationException("Set your own local author name and email first; GitHub sign-in is separate.");
                title = "Save this version locally"; explanation = "Records the whole reviewed index, including pre-existing staged files. Uses your identity, hooks and signing configuration. It does not upload.";
                display = "git commit --file=- (reviewed message via standard input)"; targets.AddRange(s.Files.Where(f => f.IsStaged).Select(f => f.Path)); break;
            case OperationKind.Fetch:
                var fetchRemote = request.Remote ?? (s.Branch == null ? null : await Config(s.Project.RootPath, $"branch.{s.Branch}.remote", cancellation));
                if (fetchRemote == null) throw new InvalidOperationException("Choose one configured remote to check; none is assumed.");
                await ValidateRemote(s.Project.RootPath, fetchRemote, cancellation);
                request = request with { Remote = fetchRemote };
                title = "Check the remote"; explanation = "Downloads objects and updates only refs/remotes/" + fetchRemote + "/ plus FETCH_HEAD. Does not change local branches, tags, index or working files. Configured fetch mappings are overridden.";
                display = $"git fetch --no-tags --prune --no-prune-tags --no-auto-maintenance --recurse-submodules=no --refmap= {Quote(fetchRemote)} +refs/heads/*:refs/remotes/{fetchRemote}/*";
                targets.Add($"refs/remotes/{fetchRemote}/*"); break;
            case OperationKind.Update:
                RequireClean(s); RequireAttached(s); RequireFresh(s, await Config(s.Project.RootPath, $"branch.{s.Branch}.remote", cancellation));
                if (s.Ahead != 0 || !(s.Behind > 0) || s.UpstreamId == null) throw new InvalidOperationException("Simple update requires a known behind-only branch. Diverged or unknown history needs a separate reviewed choice.");
                request = request with { TargetId = s.UpstreamId };
                title = "Download reviewed changes into this branch"; explanation = "Fast-forwards the current branch to the reviewed fetched commit. Files change; untracked or ignored collisions stop the merge.";
                display = $"git merge --ff-only --no-overwrite-ignore {s.UpstreamId}"; targets.Add(s.UpstreamId); break;
            case OperationKind.Merge:
                RequireClean(s); RequireAttached(s); RequireFresh(s, await Config(s.Project.RootPath, $"branch.{s.Branch}.remote", cancellation));
                var mergeTarget = await RequiredCommit(s.Project.RootPath, request.TargetId ?? s.UpstreamId, cancellation);
                if (mergeTarget == s.HeadId) throw new InvalidOperationException("Selected histories already match.");
                if (await Value(s.Project.RootPath, cancellation, "merge-base", s.HeadId!, mergeTarget) == null) throw new InvalidOperationException("These histories have no known common ancestor. Unrelated histories are never combined automatically.");
                request = request with { TargetId = mergeTarget };
                explanation = "Merges the reviewed committed history into the current branch. A clean state was required; conflicts pause ordinary synchronization. No automatic ours/theirs choice.";
                display = $"git merge --no-edit --no-overwrite-ignore {mergeTarget}"; targets.Add(mergeTarget); break;
            case OperationKind.ContinueMerge:
                if (s.ActiveOperation != GitOperation.Merge || s.HasConflicts) throw new InvalidOperationException("Continue supports a recorded merge with every conflict resolved and deliberately staged.");
                explanation = "Completes the recorded merge using the whole reviewed index and saved merge message. Check the resulting project before uploading."; display = "git commit --no-edit"; targets.AddRange(s.Files.Where(f => f.IsStaged).Select(f => f.Path)); break;
            case OperationKind.AbortMerge:
                if (s.ActiveOperation != GitOperation.Merge) throw new InvalidOperationException("Only the recorded merge can be aborted here.");
                explanation = "Requests Git to restore the state before this merge. Review the result: abort is not a universal backup for edits made during conflict resolution."; display = "git merge --abort"; targets.Add(s.HeadId ?? "unborn"); break;
            case OperationKind.Push:
                RequireAttached(s);
                if (s.HeadId == null) throw new InvalidOperationException("Save a local commit before uploading.");
                var push = s.Push;
                if (request.Remote != null || request.Branch != null)
                {
                    var selectedRemote = request.Remote ?? push?.Remote ?? throw new InvalidOperationException("Choose a push remote.");
                    await ValidateRemote(s.Project.RootPath, selectedRemote, cancellation);
                    var selectedBranch = request.Branch ?? (push?.DestinationRef.StartsWith("refs/heads/") == true ? push.DestinationRef[11..] : null);
                    await ValidBranch(s.Project.RootPath, selectedBranch, cancellation);
                    var urls = await RemoteUrls(s.Project.RootPath, selectedRemote, true, cancellation);
                    if (urls.Length != 1) throw new InvalidOperationException("Select a remote with exactly one actual push URL.");
                    push = new(selectedRemote, urls[0], "refs/heads/" + s.Branch, "refs/heads/" + selectedBranch);
                }
                if (push == null || push.Problem != null || push.DestinationRef.Length == 0) throw new InvalidOperationException(push?.Problem ?? "Select an explicit push destination.");
                await ValidateRemote(s.Project.RootPath, push.Remote, cancellation);
                RequireFresh(s, push.Remote);
                ValidateUrl(push.Url);
                await EnsureDirectUrl(s.Project.RootPath, push.Remote, push.Url, cancellation);
                // The actual server ref, not the fetched upstream, determines publication safety and outgoing review.
                var remoteId = await ReadRemoteRef(s.Project, push.Url, push.DestinationRef, cancellation);
                if (remoteId != null)
                {
                    if (await ResolveCommit(s.Project.RootPath, remoteId, cancellation) == null) throw new InvalidOperationException("The push destination has commits not yet fetched locally. Check that remote before reviewing an upload.");
                    var relation = await Read(s.Project.RootPath, cancellation, "merge-base", "--is-ancestor", remoteId, s.HeadId);
                    if (relation.ExitCode != 0) throw new InvalidOperationException("The actual push branch contains history not in your branch. Fetch and review integration; a force push is never substituted.");
                }
                request = request with { Remote = push.Remote, RemoteUrl = push.Url, Branch = push.DestinationRef[11..], TargetId = remoteId };
                title = "Upload reviewed local commits";
                explanation = $"Uploads {s.Branch} to {push.Remote}/{request.Branch} at {SafeUrl(push.Url)}. Only committed history is uploaded; unstaged work and stashes remain local. Normal push rejects a conflicting server change. Cached tracking comparisons need another permitted check." + (request.SetUpstream ? " After verification, sets only this branch's reviewed tracking mapping." : "");
                display = $"git push --porcelain --recurse-submodules=no -- {SafeUrl(push.Url)} refs/heads/{s.Branch}:{push.DestinationRef}" + (request.SetUpstream ? "; then set the reviewed local branch tracking keys" : "");
                targets.Add(push.Url); targets.Add(push.SourceRef); targets.Add(push.DestinationRef); targets.Add(remoteId ?? "empty-destination"); break;
            case OperationKind.ConfigureRemote:
                ValidateRemoteName(request.Remote); ValidateUrl(request.RemoteUrl);
                if (await Config(s.Project.RootPath, $"remote.{request.Remote}.url", cancellation) != null) throw new InvalidOperationException("This remote name already exists. Review existing configuration; it is not overwritten automatically.");
                if (request.SetUpstream) { RequireAttached(s); await ValidBranch(s.Project.RootPath, request.Branch, cancellation); }
                title = "Connect this project to a chosen repository";
                explanation = "Adds only the explicitly selected remote URL to local configuration." + (request.SetUpstream ? " Sets this current branch's reviewed upstream mapping. It does not fetch or push." : " It does not fetch, upload or assume branch tracking.");
                display = $"git remote add -- {Quote(request.Remote!)} {SafeUrl(request.RemoteUrl!)}";
                targets.Add(request.Remote!); targets.Add(request.RemoteUrl!); if (request.SetUpstream) targets.Add("refs/heads/" + request.Branch); break;
            case OperationKind.ConfigureIdentity:
                if (string.IsNullOrWhiteSpace(request.AuthorName) || string.IsNullOrWhiteSpace(request.AuthorEmail) || !request.AuthorEmail.Contains('@') || (request.AuthorName + request.AuthorEmail).Any(c => c is '\0' or '\r' or '\n')) throw new InvalidOperationException("Enter your own author name and email on one line. This is separate from sign-in.");
                explanation = "Sets only this repository's author name and email for new commits. It does not change old commits or global Git settings."; display = "git config --local user.name <your-name>; git config --local user.email <your-email>"; targets.Add(request.AuthorName); targets.Add(request.AuthorEmail); break;
            case OperationKind.ApplyIgnore:
                if (request.IgnorePatterns.IsDefaultOrEmpty) throw new InvalidOperationException("Select reviewed ignore patterns.");
                foreach (var pattern in request.IgnorePatterns) if (string.IsNullOrWhiteSpace(pattern) || pattern.Any(c => c is '\0' or '\r' or '\n')) throw new InvalidOperationException("Each ignore pattern must be a separate nonempty line.");
                var ignore = Path.Combine(s.Project.RootPath, ".gitignore");
                if (File.Exists(ignore) && (File.GetAttributes(ignore) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The ignore file is a link; it cannot be safely edited by this control.");
                explanation = "Appends the selected patterns while preserving existing rules/comments. Already tracked files remain tracked. Existing ignore contents are backed up outside the project."; display = "Append reviewed patterns atomically to <project>/.gitignore"; targets.AddRange(request.IgnorePatterns); break;
            case OperationKind.Stash:
                if (!s.HasLocalEdits) throw new InvalidOperationException("No local edits are available to preserve.");
                explanation = "Stores tracked staged/unstaged edits temporarily." + (request.IncludeUntracked ? " Selected inclusion also saves untracked files." : " Untracked files remain in the working folder.") + " Ignored files remain outside the stash. The exact stash ID is retained for a later reviewed apply.";
                display = "git stash push " + (request.IncludeUntracked ? "--include-untracked " : "") + "--message <GitCompass-operation-id>"; targets.AddRange(s.Files.Select(f => f.Path)); break;
            case OperationKind.ApplyStash:
                var stashId = await RequiredCommit(s.Project.RootPath, request.TargetId, cancellation);
                lock (ownedStashes) if (!ownedStashes.Contains(stashId)) throw new InvalidOperationException("Select the exact stash created by GitCompass in this session. Never apply an entry by list position.");
                RequireClean(s); request = request with { TargetId = stashId };
                explanation = "Applies this exact preserved stash including its index state. It may conflict. The stash is retained until you verify restoration; the app never drops it automatically."; display = $"git stash apply --index {stashId}"; targets.Add(stashId); break;
            case OperationKind.CreateBranch:
                await ValidBranch(s.Project.RootPath, request.Branch, cancellation);
                if (await ResolveCommit(s.Project.RootPath, "refs/heads/" + request.Branch, cancellation) != null) throw new InvalidOperationException("That branch already exists.");
                var baseId = await RequiredCommit(s.Project.RootPath, request.TargetId ?? s.HeadId, cancellation);
                request = request with { TargetId = baseId };
                explanation = "Creates the named local branch at the exact reviewed base commit. Your current branch and working files stay as they are; switching is a separate review.";
                display = $"git branch -- {Quote(request.Branch!)} {baseId}"; targets.Add(request.Branch!); targets.Add(baseId); break;
            case OperationKind.SwitchBranch:
                RequireClean(s); await ValidBranch(s.Project.RootPath, request.Branch, cancellation);
                var switchId = await ResolveCommit(s.Project.RootPath, "refs/heads/" + request.Branch, cancellation) ?? throw new InvalidOperationException("Choose an existing local branch; no remote branch is guessed or created.");
                request = request with { TargetId = switchId };
                explanation = "Switches to the exact existing local branch after preserving local changes. Git stops if ignored or untracked files would be overwritten."; display = $"git switch --no-guess --no-overwrite-ignore -- {Quote(request.Branch!)}"; targets.Add(request.Branch!); targets.Add(switchId); break;
            case OperationKind.UndoCommit:
                RequireAttached(s);
                if (s.HeadId == null) throw new InvalidOperationException("There is no saved version to undo.");
                if (request.TargetId != null && request.TargetId != s.HeadId) throw new InvalidOperationException("The latest saved version changed after you chose to undo it. Review again.");
                var parents = (await Value(s.Project.RootPath, cancellation, "rev-list", "--parents", "--max-count=1", s.HeadId, "--"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parents is not { Length: 2 }) throw new InvalidOperationException("Only an ordinary saved version with one earlier version can be undone here. First versions and merges need a separate review.");
                // Undo is offered only before upload: no remote-tracking ref may contain this commit.
                var published = await Read(s.Project.RootPath, cancellation, "for-each-ref", "--contains", s.HeadId, "--format=%(refname)", "refs/remotes/");
                if (published.ExitCode != 0 || published.Output.Trim().Length > 0) throw new InvalidOperationException("This version may already be uploaded. Undoing it locally would rewrite shared history, so it isn't offered. Make a new commit that fixes it instead.");
                // Tracking refs can lag (GitCompass pushes by URL), so also ask the actual push destination. Unknown => refuse.
                if (s.Push is { Problem: null } undoPush && undoPush.Url.Length > 0 && undoPush.DestinationRef.Length > 0)
                {
                    var serverId = await ReadRemoteRef(s.Project, undoPush.Url, undoPush.DestinationRef, cancellation);
                    if (serverId != null)
                    {
                        var contained = serverId == s.HeadId ? 0 : (await Read(s.Project.RootPath, cancellation, "merge-base", "--is-ancestor", s.HeadId, serverId)).ExitCode;
                        if (contained != 1) throw new InvalidOperationException("This version is already uploaded, or the online copy couldn't be compared. Undoing it locally would rewrite shared history, so it isn't offered. Make a new commit that fixes it instead.");
                    }
                }
                request = request with { TargetId = s.HeadId };
                title = "Undo the last saved version";
                explanation = "Moves " + s.Branch + " back to the version before " + s.HeadId[..7] + ". Your files are not changed and the undone changes stay staged, ready to save again. Only possible because this version was never uploaded.";
                display = $"git reset --soft {parents[1]}"; targets.Add(s.HeadId); targets.Add(parents[1]); break;
            default: throw new InvalidOperationException("This capability has its own typed adapter and is not a public Git command.");
        }
        var indexFingerprint = request.Kind is OperationKind.Commit or OperationKind.ContinueMerge ? await IndexFingerprint(s.Project.RootPath, cancellation) : null;
        return new(Guid.NewGuid(), request, s.Generation, s.Fingerprint, grant.Generation, grant.Level, title, explanation, display, targets.ToImmutable(), s.HeadId, s.UpstreamId, DateTimeOffset.UtcNow, indexFingerprint);
    }

    public async Task<BackendResult> ExecuteAsync(OperationPreview preview, RepositorySnapshot before, CancellationToken cancellation)
    {
        var request = preview.Request; var project = before.Project;
        var output = new StringBuilder(); var error = new StringBuilder(); var truncated = false; string? stash = null; ProcessResult? last = null;
        Emit(OperationState.Starting, "status", preview.Title + ": " + preview.CommandDisplay);
        try
        {
            // Discovery is also compared by the coordinator. Validate paths and branch/object inputs again immediately before mutation.
            foreach (var path in request.Paths) ValidatePath(project, path);
            // Recompute preconditions even if byte evidence is unchanged: remote freshness may expire or fail after review.
            var revalidated = await PlanAsync(request, before, new(before.ProjectId, preview.AccessLevel, Guid.Empty, preview.AccessGeneration), cancellation);
            if (!revalidated.Targets.SequenceEqual(preview.Targets) || revalidated.ReviewedIndexFingerprint != preview.ReviewedIndexFingerprint) throw new StalePreviewException("The actual remote destination or reviewed index changed after review. Create a new preview.");
            switch (request.Kind)
            {
                case OperationKind.Initialize: await Run(["init", "--initial-branch=" + request.Branch, "--", project.RootPath]); break;
                case OperationKind.Clone: await Run(["clone", "--no-recurse-submodules", "--", request.RemoteUrl!, project.RootPath]); break;
                case OperationKind.Stage: await Run(new[] { "--literal-pathspecs", "add", "--" }.Concat(request.Paths)); break;
                case OperationKind.Unstage:
                    await Run(before.HeadId == null ? new[] { "--literal-pathspecs", "rm", "--cached", "-f", "--" }.Concat(request.Paths) : new[] { "--literal-pathspecs", "restore", "--staged", "--source=" + before.HeadId, "--" }.Concat(request.Paths)); break;
                case OperationKind.Commit: await Run(["commit", "--file=-"], request.Message + "\n"); break;
                case OperationKind.Fetch:
                    await Run(["-c", "fetch.pruneTags=false", "-c", "fetch.writeCommitGraph=false", "fetch", "--no-tags", "--prune", "--no-prune-tags", "--no-auto-maintenance", "--no-write-commit-graph", "--recurse-submodules=no", "--refmap=", "--", request.Remote!, $"+refs/heads/*:refs/remotes/{request.Remote}/*"]); break;
                case OperationKind.Update: await Run(["merge", "--ff-only", "--no-overwrite-ignore", request.TargetId!]); break;
                case OperationKind.Merge: await Run(["merge", "--no-edit", "--no-overwrite-ignore", request.TargetId!]); break;
                case OperationKind.ContinueMerge: await Run(["commit", "--no-edit"]); break;
                case OperationKind.AbortMerge: await Run(["merge", "--abort"]); break;
                case OperationKind.Push:
                    // The reviewed URL avoids named-remote fetch mappings that could otherwise update unintended local branches/tags during push bookkeeping.
                    var pushArgs = new List<string> { "-c", "push.followTags=false", "push", "--porcelain", "--recurse-submodules=no", "--", request.RemoteUrl!, $"refs/heads/{before.Branch}:refs/heads/{request.Branch}" };
                    await Run(pushArgs);
                    if (last is { ExitCode: 0, Canceled: false })
                    {
                        var published = await ReadRemoteRef(project, request.RemoteUrl!, "refs/heads/" + request.Branch, cancellation);
                        if (published != preview.ReviewedHeadId) { Emit(OperationState.OutcomeUnknown, "status", "Push finished but destination verification did not confirm the reviewed commit."); return new(OperationState.OutcomeUnknown, last.ExitCode, "Upload outcome needs another permitted remote verification.", output.ToString(), error.ToString(), Truncated: truncated); }
                        if (request.SetUpstream)
                        {
                            await Run(["config", "--local", $"branch.{before.Branch}.remote", request.Remote!]);
                            if (last?.ExitCode == 0) await Run(["config", "--local", $"branch.{before.Branch}.merge", "refs/heads/" + request.Branch]);
                            if (last?.ExitCode != 0) return new(OperationState.OutcomeUnknown, last?.ExitCode, "The reviewed commit is published, but local tracking setup did not finish. Review tracking settings instead of blindly repeating the upload.", output.ToString(), error.ToString(), Truncated: truncated);
                        }
                    }
                    break;
                case OperationKind.ConfigureRemote:
                    await Run(["remote", "add", "--", request.Remote!, request.RemoteUrl!]);
                    if (last?.ExitCode == 0 && request.SetUpstream)
                    {
                        await Run(["config", "--local", $"branch.{before.Branch}.remote", request.Remote!]);
                        if (last?.ExitCode == 0) await Run(["config", "--local", $"branch.{before.Branch}.merge", "refs/heads/" + request.Branch]);
                    }
                    break;
                case OperationKind.ConfigureIdentity:
                    await Run(["config", "--local", "user.name", request.AuthorName!]);
                    if (last?.ExitCode == 0) await Run(["config", "--local", "user.email", request.AuthorEmail!]); break;
                case OperationKind.ApplyIgnore:
                    cancellation.ThrowIfCancellationRequested(); await ApplyIgnore(project, request.IgnorePatterns, cancellation);
                    last = new(0, "Ignore rules appended; original contents backed up in GitCompass application data. Already tracked files remain tracked.\n", "", false, false); output.Append(last.Output); break;
                case OperationKind.Stash:
                    var stashArgs = new List<string> { "stash", "push", "--message", "GitCompass " + preview.Id.ToString("N") }; if (request.IncludeUntracked) stashArgs.Add("--include-untracked");
                    await Run(stashArgs);
                    if (last?.ExitCode == 0) { stash = await ResolveCommit(project.RootPath, "refs/stash", cancellation); if (stash != null) lock (ownedStashes) ownedStashes.Add(stash); }
                    break;
                case OperationKind.ApplyStash: await Run(["stash", "apply", "--index", request.TargetId!]); stash = request.TargetId; break;
                case OperationKind.CreateBranch: await Run(["branch", "--", request.Branch!, request.TargetId!]); break;
                case OperationKind.UndoCommit: await Run(["reset", "--soft", preview.Targets[1]]); break;
                case OperationKind.SwitchBranch: await Run(["switch", "--no-guess", "--no-overwrite-ignore", "--", request.Branch!]); break;
            }
            if (last == null) throw new InvalidOperationException("No typed operation was selected.");
            var state = last.Canceled ? OperationState.OutcomeUnknown : last.ExitCode == 0 ? OperationState.Succeeded : OperationState.Failed;
            var summary = state == OperationState.Succeeded ? Success(request.Kind) : state == OperationState.OutcomeUnknown ? "Operation was interrupted after starting; inspect and verify its effects before retrying." : Failure(last.Error);
            if (state == OperationState.Succeeded && !await Verify(request, before, stash, preview.ReviewedIndexFingerprint, cancellation)) { state = OperationState.OutcomeUnknown; summary = "Git returned success but the expected result could not be verified. Hooks may have changed reviewed contents. Refresh and review before another action."; }
            Emit(state, "status", summary);
            return new(state, last.ExitCode, summary, output.ToString(), error.ToString(), stash, truncated);
        }
        catch (OperationCanceledException)
        {
            var state = last == null ? OperationState.Canceled : OperationState.OutcomeUnknown;
            var summary = last == null ? "Canceled before a Git result was available; refresh before retrying." : "Canceled after a repository or network operation started. Its outcome needs verification.";
            Emit(state, "status", summary); return new(state, last?.ExitCode, summary, output.ToString(), error.ToString(), stash, truncated);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var state = ex is StalePreviewException ? OperationState.Stale : last == null ? OperationState.Failed : OperationState.OutcomeUnknown;
            var message = Redact(ex.Message, project); Emit(state, "status", message);
            return new(state, last?.ExitCode, message, output.ToString(), error.ToString(), stash, truncated);
        }

        async Task Run(IEnumerable<string> args, string? input = null)
        {
            cancellation.ThrowIfCancellationRequested(); Emit(OperationState.Running, "status", "Git operation running");
            // Human output alone reaches Activity; bounded buffers and secret/path redaction apply before publication.
            var streamSync = new object();
            var lines = new Dictionary<string, StringBuilder> { ["stdout"] = new(), ["stderr"] = new() };
            var discard = new HashSet<string>(); var emitted = new Dictionary<string, int> { ["stdout"] = 0, ["stderr"] = 0 };
            last = await git.RunAsync(project.RootPath, args, cancellation, input, false, (stream, chunk) =>
            {
                lock (streamSync)
                {
                // Delay partial lines so redaction can see a token/path that spans process chunks. Oversized lines are withheld.
                foreach (var character in chunk)
                {
                    if (character is '\n' or '\r')
                    {
                        if (discard.Remove(stream)) { Emit(OperationState.Running, stream, "[Oversized output line withheld; output truncated]"); truncated = true; }
                        else if (lines[stream].Length > 0 && emitted[stream] < 512 * 1024) { var safeLine = Redact(lines[stream].ToString(), project); Emit(OperationState.Running, stream, safeLine); emitted[stream] += safeLine.Length; }
                        lines[stream].Clear();
                    }
                    else if (!discard.Contains(stream))
                    {
                        if (lines[stream].Length >= 8192) { lines[stream].Clear(); discard.Add(stream); }
                        else lines[stream].Append(character);
                    }
                }
                }
            });
            foreach (var (stream, line) in lines) if (line.Length > 0 && !discard.Contains(stream) && emitted[stream] < 512 * 1024) Emit(OperationState.Running, stream, line.ToString());
            var safeOutput = Redact(last.Output, project); var safeError = Redact(last.Error, project);
            Append(output, safeOutput); Append(error, safeError); truncated |= last.Truncated;
            if (last.Canceled) throw new OperationCanceledException(cancellation);
        }
        void Append(StringBuilder buffer, string text) { var room = Math.Max(0, 512 * 1024 - buffer.Length); buffer.Append(text.AsSpan(0, Math.Min(room, text.Length))); if (text.Length > room) truncated = true; }
        void Emit(OperationState state, string stream, string text) => Publish(new(preview.Id, project.Id, DateTimeOffset.UtcNow, state, stream, Redact(text, project), request.Kind));
    }
    private async Task<bool> Verify(OperationRequest r, RepositorySnapshot before, string? stash, string? indexFingerprint, CancellationToken cancellation)
    {
        var root = before.Project.RootPath;
        switch (r.Kind)
        {
            case OperationKind.Initialize: case OperationKind.Clone: return (await DiscoverAsync(root, cancellation)).Kind == RepositoryKind.WorkingTree;
            case OperationKind.Update: return await ResolveCommit(root, "HEAD", cancellation) == r.TargetId;
            case OperationKind.Commit: return await ResolveCommit(root, "HEAD", cancellation) is { } head && head != before.HeadId && await ReviewedCommitIdentity(root, head, before, false, cancellation) && indexFingerprint == await CommittedFingerprint(root, head, cancellation);
            case OperationKind.UndoCommit: return await Value(root, cancellation, "symbolic-ref", "--short", "HEAD") == before.Branch && await ResolveCommit(root, "HEAD", cancellation) is { } undone && undone != r.TargetId && await Value(root, cancellation, "rev-parse", r.TargetId + "^") == undone;
            case OperationKind.CreateBranch: return await ResolveCommit(root, "refs/heads/" + r.Branch, cancellation) == r.TargetId;
            case OperationKind.SwitchBranch: return await Value(root, cancellation, "symbolic-ref", "--short", "HEAD") == r.Branch && await ResolveCommit(root, "HEAD", cancellation) == r.TargetId;
            case OperationKind.ConfigureRemote: return await Config(root, $"remote.{r.Remote}.url", cancellation) == r.RemoteUrl;
            case OperationKind.ConfigureIdentity: return await Config(root, "user.name", cancellation) == r.AuthorName && await Config(root, "user.email", cancellation) == r.AuthorEmail;
            case OperationKind.Stash:
                if (stash == null || await ResolveCommit(root, "refs/stash", cancellation) != stash) return false;
                var status = await Read(root, cancellation, "status", "--porcelain=v2", "-z", "--untracked-files=all");
                return status.ExitCode == 0 && PorcelainV2Parser.Parse(status.Output).Files.All(f => f.Kind == FileKind.Untracked && !r.IncludeUntracked);
            case OperationKind.ContinueMerge:
                var continued = await ResolveCommit(root, "HEAD", cancellation);
                if (continued == null || !await ReviewedCommitIdentity(root, continued, before, true, cancellation) || indexFingerprint != await CommittedFingerprint(root, continued, cancellation)) return false;
                goto case OperationKind.AbortMerge;
            case OperationKind.AbortMerge: return await Value(root, cancellation, "rev-parse", "--absolute-git-dir") is { } gitdir && !File.Exists(Path.Combine(gitdir, "MERGE_HEAD"));
            default: return true; // staged files/index and conflict result are independently inspected immediately by the coordinator.
        }
    }
    private async Task<bool> ReviewedCommitIdentity(string root, string head, RepositorySnapshot before, bool merge, CancellationToken cancellation)
    {
        if (await Value(root, cancellation, "symbolic-ref", "--short", "HEAD") != before.Branch) return false;
        var ancestry = (await Value(root, cancellation, "rev-list", "--parents", "--max-count=1", head, "--"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ancestry == null || ancestry[0] != head) return false;
        return before.HeadId == null ? ancestry.Length == 1 : ancestry.Length >= 2 && ancestry[1] == before.HeadId && (merge || ancestry.Length == 2);
    }
    private async Task<string> IndexFingerprint(string root, CancellationToken cancellation)
    {
        var result = await Read(root, cancellation, "ls-files", "--stage", "-z");
        if (result.ExitCode != 0) throw new InvalidOperationException("Cannot review the staged index.");
        var records = new List<string>();
        foreach (var record in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t'); var fields = tab < 0 ? [] : record[..tab].Split(' ');
            if (fields.Length != 3 || fields[2] != "0") throw new InvalidOperationException("The index contains unresolved or malformed entries.");
            records.Add(fields[0] + " " + fields[1] + "\t" + record[(tab + 1)..]);
        }
        return ManifestHash(records);
    }
    private async Task<string> CommittedFingerprint(string root, string head, CancellationToken cancellation)
    {
        var result = await Read(root, cancellation, "ls-tree", "-r", "--full-tree", "-z", head);
        if (result.ExitCode != 0) throw new InvalidOperationException("Cannot verify committed contents.");
        var records = new List<string>();
        foreach (var record in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t'); var fields = tab < 0 ? [] : record[..tab].Split(' ');
            if (fields.Length != 3) throw new FormatException("Malformed committed tree.");
            records.Add(fields[0] + " " + fields[2] + "\t" + record[(tab + 1)..]);
        }
        return ManifestHash(records);
    }
    private static string ManifestHash(IEnumerable<string> records) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', records.Order(StringComparer.Ordinal)))));
    private async Task ValidateRemote(string root, string remote, CancellationToken cancellation)
    {
        ValidateRemoteName(remote);
        var urls = await RemoteUrls(root, remote, false, cancellation);
        if (urls.Length != 1) throw new InvalidOperationException("Choose a configured remote with one fetch URL.");
        ValidateUrl(urls[0]);
        if (await Config(root, $"remote.{remote}.mirror", cancellation) == "true") throw new InvalidOperationException("Mirroring remotes are outside guided operations.");
    }
    private async Task ValidBranch(string root, string? branch, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch == "HEAD" || branch.Any(c => c is '\0' or '\r' or '\n')) throw new InvalidOperationException("Choose an explicit valid branch name.");
        var result = await Read(root, cancellation, "check-ref-format", "--branch", branch);
        if (result.ExitCode != 0 || result.Output.Trim() != branch) throw new InvalidOperationException("Git rejected this branch name. Previous-checkout shortcuts are not allowed.");
    }
    private async Task<string?> ReadRemoteRef(ProjectRegistration project, string url, string destination, CancellationToken cancellation)
    {
        var result = await Read(project.RootPath, cancellation, "ls-remote", "--refs", "--", url, destination);
        if (result.ExitCode != 0) throw new InvalidOperationException("Remote verification failed: " + Redact(result.Error, project));
        var matches = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Split('\t')).Where(p => p.Length == 2 && p[1] == destination).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || !ObjectId(matches[0][0])) throw new InvalidOperationException("The remote returned an ambiguous branch identity.");
        return matches[0][0];
    }
    private async Task EnsureDirectUrl(string root, string remote, string url, CancellationToken cancellation)
    {
        // get-url accepts only repository-configured remote names. Override that remote's URL arrays for this
        // inspection process only; the resulting resolution matches using the reviewed URL directly in push.
        // Empty values clear earlier URL entries. No config file is written and no transport is started.
        var result = await Read(root, cancellation, "-c", $"remote.{remote}.url=", "-c", $"remote.{remote}.pushurl=", "-c", $"remote.{remote}.url={url}", "remote", "get-url", "--push", "--all", "--", remote);
        if (result.ExitCode != 0 || result.Output.TrimEnd('\r', '\n') != url) throw new InvalidOperationException("The resolved push URL would be rewritten again. Multi-step transport rewrites need a separate explicit configuration review.");
    }
    private static void ValidateRemoteName(string? remote)
    {
        if (remote == null || !Regex.IsMatch(remote, "^[A-Za-z0-9][A-Za-z0-9_-]*$")) throw new InvalidOperationException("Choose a simple remote name containing letters, numbers, underscores or hyphens.");
    }
    private static void ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('-') || url.Any(c => c is '\0' or '\r' or '\n') || url.Contains("::")) throw new InvalidOperationException("Choose one explicit supported repository URL or local fixture path.");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !Path.IsPathFullyQualified(url) && uri.Scheme is not ("https" or "http" or "ssh" or "git" or "file")) throw new InvalidOperationException("This repository URL transport is not supported by guided operations.");
    }
    private static void ValidateMessage(string? message) { if (string.IsNullOrWhiteSpace(message) || message.Length > 64 * 1024 || message.Contains('\0')) throw new InvalidOperationException("Write a meaningful commit message (up to 64 KB)."); }
    private static void RequireClean(RepositorySnapshot s) { if (!s.IsClean) throw new InvalidOperationException("Preserve and review staged, unstaged and untracked files before integration or switching branches."); }
    private static void RequireAttached(RepositorySnapshot s) { if (s.BranchState != BranchState.Attached) throw new InvalidOperationException("This action requires a committed current branch. Detached or unborn work needs a separate review."); }
    private static void RequireFresh(RepositorySnapshot s, string? remote) { if (remote == null || s.Remote.Remote != remote || s.Remote.LastSuccessfulCheck == null || s.Remote.State != RemoteCheckState.Succeeded || DateTimeOffset.UtcNow - s.Remote.LastSuccessfulCheck > TimeSpan.FromMinutes(10)) throw new InvalidOperationException("Check the relevant remote first. Cached, different-remote or failed observations are not a fresh review."); }
    private static string Quote(string text) => '"' + text.Replace("\"", "\\\"") + '"';
    private static string SafeUrl(string url) => Regex.Replace(url, @"(https?://)[^\s/@]+:[^\s/@]+@", "$1<credentials>@", RegexOptions.IgnoreCase);
    private static string Success(OperationKind kind) => kind switch
    {
        OperationKind.Commit => "Version saved locally. Upload is a separate reviewed action.",
        OperationKind.Fetch => "Remote check completed. Working files and local branches were not integrated.",
        OperationKind.Push => "The reviewed commit was confirmed at the selected remote branch.",
        OperationKind.Stash => "Work preserved in the exact recorded stash. Verify restoration before any later deletion.",
        OperationKind.ApplyStash => "Stash applied; the stored stash is retained. Review the restored files.",
        OperationKind.UndoCommit => "Last saved version undone. Its changes are still here and staged, ready to save again.",
        OperationKind.Update => "Current branch fast-forwarded to the reviewed incoming commit.",
        _ => "Operation completed; refreshed project facts show the result."
    };
    private static string Failure(string error)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("conflict")) return "Git stopped with conflicts. Review the recorded operation and affected paths; your committed histories remain available.";
        if (text.Contains("rejected") || text.Contains("non-fast-forward")) return "Upload was rejected. Local commits remain; check the remote and review incoming history before another normal push.";
        if (text.Contains("authentication") || text.Contains("could not read username") || text.Contains("permission denied")) return "Git transport authentication or permission failed. Configure your existing credential helper or SSH access through a deliberate sign-in.";
        if (text.Contains("lock")) return "Git metadata is busy or locked. Check the other process; no lock files were removed.";
        if (text.Contains("hook") || text.Contains("sign")) return "Git's hook or signing check failed. Review its output and correct the configured setup; it was not bypassed.";
        if (text.Contains("overwrite")) return "Git stopped to preserve local files that would be overwritten. Review and preserve those paths before trying again.";
        return "Git did not complete this operation. Read the output, inspect the current project and review a new plan.";
    }
    private static async Task ApplyIgnore(ProjectRegistration project, ImmutableArray<string> patterns, CancellationToken cancellation)
    {
        var path = Path.Combine(project.RootPath, ".gitignore"); var original = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellation) : "";
        var backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitCompass", "ignore-backups"); Directory.CreateDirectory(backupDirectory);
        if (File.Exists(path)) await File.WriteAllTextAsync(Path.Combine(backupDirectory, Guid.NewGuid().ToString("N") + ".gitignore"), original, new UTF8Encoding(false), cancellation);
        var lines = original.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var append = patterns.Where(p => !lines.Contains(p)).ToArray(); if (append.Length == 0) return;
        var content = original + (original.Length > 0 && !original.EndsWith('\n') ? Environment.NewLine : "") + "# GitCompass: reviewed project patterns" + Environment.NewLine + string.Join(Environment.NewLine, append) + Environment.NewLine;
        var temporary = Path.Combine(project.RootPath, ".gitignore.gitcompass-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellation); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
