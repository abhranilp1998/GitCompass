using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GitCompass.Application;
using GitCompass.Domain;

namespace GitCompass.Infrastructure.Git;

internal sealed partial class GitBackend(string executable) : IGitBackend
{
    private readonly GitProcess git = new(executable);
    public event Action<OperationEvent>? Activity;
    private void Publish(OperationEvent entry)
    {
        if (Activity == null) return;
        foreach (var sink in Activity.GetInvocationList().Cast<Action<OperationEvent>>()) try { sink(entry); } catch (Exception) { }
    }
    private async Task<ProcessResult> Read(string cwd, CancellationToken cancellation, params string[] args)
    {
        var result = await git.RunAsync(cwd, args, cancellation);
        if (result.Canceled) { if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation); throw new InvalidOperationException(result.Error); }
        if (result.Truncated) throw new InvalidOperationException("Machine output exceeded the inspection limit. Narrow the request; incomplete output cannot support a write.");
        return result;
    }
    private async Task<string?> Value(string cwd, CancellationToken cancellation, params string[] args)
    {
        var result = await Read(cwd, cancellation, args); return result.ExitCode == 0 ? result.Output.TrimEnd('\r', '\n', '\0') : null;
    }
    public async Task<ProjectRegistration> DiscoverAsync(string selectedFolder, CancellationToken cancellation)
    {
        var selected = CanonicalFolder(selectedFolder);
        var bare = await Read(selected, cancellation, "rev-parse", "--is-bare-repository");
        if (bare.ExitCode != 0)
        {
            var marker = HasGitMarkerInParents(selected);
            return new(Guid.NewGuid(), new DirectoryInfo(selected).Name, selected, selected, "", marker ? RepositoryKind.Invalid : RepositoryKind.Candidate);
        }
        var gitDirectory = await Value(selected, cancellation, "rev-parse", "--absolute-git-dir") ?? "";
        var common = await Value(selected, cancellation, "rev-parse", "--path-format=absolute", "--git-common-dir") ?? gitDirectory;
        var isBare = bare.Output.Trim() == "true";
        var root = isBare ? gitDirectory : await Value(selected, cancellation, "rev-parse", "--show-toplevel");
        if (string.IsNullOrEmpty(root)) return new(Guid.NewGuid(), new DirectoryInfo(selected).Name, selected, selected, common, RepositoryKind.Invalid);
        root = CanonicalFolder(root);
        var kind = isBare ? RepositoryKind.Bare : string.Equals(Path.GetFullPath(gitDirectory), Path.GetFullPath(common), StringComparison.OrdinalIgnoreCase) ? RepositoryKind.WorkingTree : RepositoryKind.LinkedWorktree;
        return new(Guid.NewGuid(), new DirectoryInfo(root).Name, selected, root, Path.GetFullPath(common), kind);
    }
    public async Task<RepositorySnapshot> InspectAsync(ProjectRegistration registration, long generation, RemoteObservation remote, CancellationToken cancellation)
    {
        var discovered = await DiscoverAsync(registration.SelectedFolder, cancellation);
        if (!string.Equals(discovered.RootPath, registration.RootPath, StringComparison.OrdinalIgnoreCase) || (registration.Kind != RepositoryKind.Candidate && !string.Equals(discovered.CommonGitDirectory, registration.CommonGitDirectory, StringComparison.OrdinalIgnoreCase)))
            discovered = discovered with { Kind = RepositoryKind.Invalid, RootPath = registration.RootPath, CommonGitDirectory = registration.CommonGitDirectory };
        var project = discovered with { Id = registration.Id, DisplayName = registration.DisplayName, Trusted = registration.Trusted, IntendedBranch = registration.IntendedBranch, MonitoringEnabled = registration.MonitoringEnabled };
        var version = (await Value(project.RootPath, cancellation, "--version")) ?? "Git unavailable";
        var problems = ImmutableArray.CreateBuilder<string>();
        if (project.Kind is RepositoryKind.Candidate or RepositoryKind.Bare or RepositoryKind.Invalid)
        {
            if (project.Kind == RepositoryKind.Bare) problems.Add("Bare repositories contain history without editable project files. Guided writes are unavailable here.");
            if (project.Kind == RepositoryKind.Invalid) problems.Add("Git metadata could not be safely resolved. Check ownership, the .git target and folder access. The app never changes global trust.");
            return new(project.Id, generation, DateTimeOffset.UtcNow, project, null, null, BranchState.Unknown, null, null, null, [], GitOperation.None, null, null, false, remote, await Fingerprint(project, "", cancellation), version, problems.ToImmutable());
        }
        var statusResult = await Read(project.RootPath, cancellation, "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all", "--ignore-submodules=none");
        if (statusResult.ExitCode != 0) throw new InvalidOperationException(Redact(statusResult.Error, project));
        var status = PorcelainV2Parser.Parse(statusResult.Output);
        var gitDir = await Value(project.RootPath, cancellation, "rev-parse", "--absolute-git-dir") ?? project.CommonGitDirectory;
        var op = DetectOperation(gitDir);
        var shallow = await Value(project.RootPath, cancellation, "rev-parse", "--is-shallow-repository") == "true";
        string? upstreamId = status.Upstream == null ? null : await ResolveCommit(project.RootPath, status.Upstream, cancellation);
        int? ahead = null, behind = null;
        if (status.HeadId != null && upstreamId != null)
        {
            var counts = await Value(project.RootPath, cancellation, "rev-list", "--left-right", "--count", status.HeadId + "..." + upstreamId, "--");
            var pieces = counts?.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (pieces?.Length == 2 && int.TryParse(pieces[0], out var a) && int.TryParse(pieces[1], out var b)) { ahead = a; behind = b; }
        }
        var push = status.Branch == null ? null : await ResolvePush(project, status.Branch, cancellation);
        if (push?.Problem != null) problems.Add(push.Problem);
        var superproject = await Value(project.RootPath, cancellation, "rev-parse", "--show-superproject-working-tree");
        if (!string.IsNullOrEmpty(superproject)) problems.Add("This folder is a submodule. Guided branch/integration management needs a separate parent-project review.");
        if (status.Files.Any(f => f.Submodule != "N...")) problems.Add("Submodule changes are shown separately; guided staging of submodule paths requires developer review.");
        var fingerprint = await Fingerprint(project, statusResult.Output, cancellation);
        return new(project.Id, generation, DateTimeOffset.UtcNow, project, status.HeadId, status.Branch, status.BranchState, status.Upstream, upstreamId, push, status.Files, op, ahead, behind, shallow, remote, fingerprint, version, problems.ToImmutable());
    }
    public Task<HistoryPage> HistoryAsync(RepositorySnapshot s, int skip, int take, CancellationToken cancellation) => ReadHistory(s, s.HeadId, s.UpstreamId, skip, take, false, cancellation);
    public async Task<HistoryPage> HistoryAsync(RepositorySnapshot s, HistoryRequest request, CancellationToken cancellation)
    {
        var left = request.LeftCommitId ?? s.HeadId; var right = request.RightCommitId ?? s.UpstreamId;
        var available = (await BranchesAsync(s, cancellation)).Select(branch => branch.Id).ToHashSet(StringComparer.Ordinal);
        if (s.HeadId != null) available.Add(s.HeadId);
        foreach (var selected in new[] { left, right }.Where(id => id != null))
        {
            if (!ObjectId(selected!) || !available.Contains(selected!)) throw new InvalidOperationException("Select an exact commit from the current branch inventory. Refresh when a selected branch changes.");
            await RequiredCommit(s.Project.RootPath, selected, cancellation);
        }
        return await ReadHistory(s, left, right, request.Skip, request.Take, true, cancellation);
    }
    private async Task<HistoryPage> ReadHistory(RepositorySnapshot s, string? left, string? right, int skip, int take, bool comparisonRequested, CancellationToken cancellation)
    {
        if (left == null) return new([], false, s.IsShallow, null);
        var args = new List<string> { "log", "--topo-order", "--no-show-signature", "--no-notes", "--decorate=full", "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%D", "-z", $"--skip={skip}", $"--max-count={take + 1}", left };
        if (right != null) args.Add(right); args.Add("--");
        var r = await Read(s.Project.RootPath, cancellation, args.ToArray());
        if (r.ExitCode != 0) throw new InvalidOperationException(Redact(r.Error, s.Project));
        var fields = r.Output.Split('\0'); var nodes = ImmutableArray.CreateBuilder<CommitNode>();
        for (var i = 0; i + 6 < fields.Length; i += 7)
        {
            if (!ObjectId(fields[i]) || !DateTimeOffset.TryParse(fields[i + 4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new FormatException("Malformed commit history.");
            var parents = fields[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray();
            if (parents.Any(p => !ObjectId(p))) throw new FormatException("Malformed parent ID.");
            nodes.Add(new(fields[i], parents, fields[i + 2], fields[i + 3], date, fields[i + 5], fields[i + 6].Split(", ", StringSplitOptions.RemoveEmptyEntries).ToImmutableArray()));
        }
        HistoryComparison? comparison = null;
        if (comparisonRequested)
        {
            int? leftOnly = null, rightOnly = null;
            if (right != null)
            {
                var counts = await Read(s.Project.RootPath, cancellation, "rev-list", "--left-right", "--count", left + "..." + right, "--");
                var parts = counts.Output.Trim().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (counts.ExitCode != 0 || parts.Length != 2 || !int.TryParse(parts[0], out var l) || !int.TryParse(parts[1], out var rcount)) throw new InvalidOperationException("The selected branch relationship could not be read. It remains unknown.");
                leftOnly = l; rightOnly = rcount;
            }
            comparison = new(left, right, leftOnly, rightOnly);
        }
        return new(nodes.Take(take).ToImmutableArray(), nodes.Count > take, s.IsShallow, comparisonRequested ? left + ":" + right : left, comparison);
    }
    public async Task<ImmutableArray<BranchInfo>> BranchesAsync(RepositorySnapshot s, CancellationToken cancellation)
    {
        if (s.Project.Kind is RepositoryKind.Candidate or RepositoryKind.Invalid) return [];
        var result = await Read(s.Project.RootPath, cancellation, "for-each-ref", "--format=%(refname)%00%(objectname)%00%(upstream:short)", "refs/heads/", "refs/remotes/");
        if (result.ExitCode != 0) throw new InvalidOperationException(Redact(result.Error, s.Project));
        var branches = ImmutableArray.CreateBuilder<BranchInfo>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.TrimEnd('\r').Split('\0'); if (p.Length != 3) throw new FormatException("Malformed branch list.");
            var remote = p[0].StartsWith("refs/remotes/"); var name = p[0][(remote ? 13 : 11)..];
            branches.Add(new(name, p[1], !remote && name == s.Branch, p[2].Length == 0 ? null : p[2], remote));
        }
        return branches.ToImmutable();
    }
    public async Task<DiffResult> DiffAsync(RepositorySnapshot s, DiffRequest request, CancellationToken cancellation)
    {
        if (s.Project.Kind is RepositoryKind.Candidate or RepositoryKind.Invalid or RepositoryKind.Bare) return new("Select an existing working repository to inspect diffs.", false, []);
        if (request.Path != null) ValidatePath(s.Project, request.Path);
        if (request.Path != null && s.Files.Any(f => f.Path == request.Path && f.Kind == FileKind.Untracked))
        {
            var absolute = Path.Combine(s.Project.RootPath, request.Path);
            if (!File.Exists(absolute)) return new("Untracked path is no longer present.", false, [request.Path]);
            var fileInfo = new FileInfo(absolute);
            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0) return new("Linked file: " + (fileInfo.LinkTarget ?? "target unavailable") + ". External target contents are not read by the diff viewer.", false, [request.Path]);
            await using var file = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
            var length = file.Length; var bytes = new byte[(int)Math.Min(length, 256 * 1024)]; var used = 0;
            while (used < bytes.Length) { var read = await file.ReadAsync(bytes.AsMemory(used), cancellation); if (read == 0) break; used += read; }
            if (bytes.Take(Math.Min(used, 8192)).Contains((byte)0)) return new($"Binary/untracked file: {length:N0} bytes. It has no recorded Git version.", false, [request.Path]);
            var clipped = length > bytes.Length; var text = Encoding.UTF8.GetString(bytes.AsSpan(0, used));
            return new(text, clipped, [request.Path]);
        }
        var args = new List<string> { "--literal-pathspecs", "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--ignore-submodules=none" };
        switch (request.Kind)
        {
            case DiffKind.Staged: args.Add("--cached"); break;
            case DiffKind.Commit:
                var commit = await RequiredCommit(s.Project.RootPath, request.CommitId, cancellation);
                args = ["--literal-pathspecs", "show", "--format=fuller", "--no-show-signature", "--no-notes", "--no-ext-diff", "--no-textconv", "--no-color", commit]; break;
            case DiffKind.Comparison:
                args.Add(await RequiredCommit(s.Project.RootPath, request.CompareToId, cancellation)); args.Add(await RequiredCommit(s.Project.RootPath, request.CommitId, cancellation)); break;
        }
        args.Add("--"); if (request.Path != null) args.Add(request.Path);
        var result = await git.RunAsync(s.Project.RootPath, args, cancellation, limit: 256 * 1024);
        return new(result.Output, result.Truncated, request.Path == null ? s.Files.Select(f => f.Path).ToImmutableArray() : [request.Path], result.ExitCode == 0 ? null : Redact(result.Error, s.Project));
    }
    private async Task<string?> ResolveCommit(string root, string refName, CancellationToken cancellation)
    {
        if (refName.Length == 0 || refName.StartsWith('-') || refName.Any(c => c is '\0' or '\r' or '\n')) return null;
        var id = await Value(root, cancellation, "rev-parse", "--verify", "--end-of-options", refName + "^{commit}"); return id != null && ObjectId(id) ? id : null;
    }
    private async Task<string> RequiredCommit(string root, string? id, CancellationToken cancellation)
    {
        if (id == null || !ObjectId(id)) throw new InvalidOperationException("Select an exact commit object ID.");
        return await ResolveCommit(root, id, cancellation) ?? throw new InvalidOperationException("The reviewed commit is unavailable.");
    }
    private async Task<PushDestination?> ResolvePush(ProjectRegistration project, string branch, CancellationToken cancellation)
    {
        var root = project.RootPath;
        var remote = await Config(root, $"branch.{branch}.pushRemote", cancellation) ?? await Config(root, "remote.pushDefault", cancellation) ?? await Config(root, $"branch.{branch}.remote", cancellation);
        if (remote == null)
        {
            var names = (await Value(root, cancellation, "remote"))?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (names.Length == 1) remote = names[0];
            else return new("", "", "refs/heads/" + branch, "", "Push destination is ambiguous. Select one explicit remote and branch.");
        }
        if (remote == ".") return new(remote, root, "refs/heads/" + branch, "", "A local-dot upstream needs an explicit network/publication destination.");
        var urls = await RemoteUrls(root, remote, true, cancellation);
        var pushSpecs = await Values(root, $"remote.{remote}.push", cancellation);
        if (urls.Length != 1) return new(remote, "", "refs/heads/" + branch, "", "Guided push requires one reviewed push URL; multiple or missing URLs are blocked.");
        var source = "refs/heads/" + branch; string? destination = null; string? problem = null;
        if (pushSpecs.Length > 0)
        {
            var matching = pushSpecs.Where(p => !p.StartsWith('+') && !p.Contains('*')).Select(p => p.Split(':')).Where(p => p.Length == 2 && (p[0] == source || p[0] == branch)).ToArray();
            if (pushSpecs.Length == 1 && matching.Length == 1) destination = matching[0][1].StartsWith("refs/") ? matching[0][1] : "refs/heads/" + matching[0][1];
            else problem = "Configured push mappings are ambiguous, forced or multiple. Select a single explicit destination.";
        }
        else
        {
            var mode = await Config(root, "push.default", cancellation) ?? "simple";
            var upstreamRemote = await Config(root, $"branch.{branch}.remote", cancellation);
            var merge = await Config(root, $"branch.{branch}.merge", cancellation);
            switch (mode)
            {
                case "current": destination = source; break;
                case "upstream": if (remote == upstreamRemote && merge?.StartsWith("refs/heads/") == true) destination = merge; else problem = "Upstream push requires a matching configured upstream remote and branch."; break;
                case "simple":
                    if (remote != upstreamRemote) destination = source;
                    else if (merge == source) destination = source;
                    else problem = "Simple push needs a same-name upstream branch, or an explicitly selected publication target.";
                    break;
                default: problem = $"push.default={mode} does not identify one safe guided destination."; break;
            }
        }
        if (destination != null && !destination.StartsWith("refs/heads/")) problem = "Guided push destinations must be branch refs.";
        return new(remote, urls[0], source, destination ?? "", problem);
    }
    private async Task<string?> Config(string root, string key, CancellationToken cancellation) => (await Values(root, key, cancellation)).LastOrDefault();
    private async Task<string[]> Values(string root, string key, CancellationToken cancellation)
    {
        var r = await Read(root, cancellation, "config", "--null", "--get-all", key); return r.ExitCode == 0 ? r.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
    }
    private async Task<string[]> RemoteUrls(string root, string remote, bool push, CancellationToken cancellation)
    {
        var args = new List<string> { "remote", "get-url", "--all" }; if (push) args.Add("--push"); args.Add("--"); args.Add(remote);
        var result = await Read(root, cancellation, args.ToArray()); return result.ExitCode == 0 ? result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];
    }
    private async Task<string> Fingerprint(ProjectRegistration project, string status, CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value + "\0"));
        Append(project.RootPath); Append(project.CommonGitDirectory); Append(project.Kind.ToString()); Append(project.Trusted.ToString()); Append(project.IntendedBranch ?? ""); Append(status);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (project.Kind == RepositoryKind.Candidate)
        {
            foreach (var path in SafeFiles(project.RootPath)) paths.Add(path);
        }
        else if (project.Kind != RepositoryKind.Invalid)
        {
            var refsTask = Value(project.RootPath, cancellation, "show-ref", "--head");
            var configTask = Value(project.RootPath, cancellation, "config", "--null", "--list", "--show-origin");
            var filesTask = Value(project.RootPath, cancellation, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
            var directoryTask = Value(project.RootPath, cancellation, "rev-parse", "--absolute-git-dir");
            await Task.WhenAll(refsTask, configTask, filesTask, directoryTask);
            Append((await refsTask) ?? ""); Append((await configTask) ?? "");
            var listed = await filesTask;
            if (listed != null) foreach (var path in listed.Split('\0', StringSplitOptions.RemoveEmptyEntries)) paths.Add(Path.Combine(project.RootPath, path));
            var gitdir = await directoryTask ?? project.CommonGitDirectory;
            foreach (var name in new[] { "index", "HEAD", "MERGE_HEAD", "MERGE_MSG", "CHERRY_PICK_HEAD", "REVERT_HEAD", "ORIG_HEAD" }) paths.Add(Path.Combine(gitdir, name));
            paths.Add(Path.Combine(project.CommonGitDirectory, "config")); paths.Add(Path.Combine(project.CommonGitDirectory, "packed-refs")); paths.Add(Path.Combine(project.RootPath, ".gitignore"));
        }
        var evidence = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(paths, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellation }, async (path, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { evidence[path] = "link:" + info.LinkTarget; return; }
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
                using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var bytes = ArrayPool<byte>.Shared.Rent(65536);
                try { int count; while ((count = await file.ReadAsync(bytes.AsMemory(), token)) > 0) contentHash.AppendData(bytes.AsSpan(0, count)); }
                finally { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
                evidence[path] = "file:" + Convert.ToHexString(contentHash.GetHashAndReset());
            }
            else evidence[path] = "missing";
        });
        // Concurrency only changes how bytes are read. Aggregation remains ordered and every byte is still hashed.
        foreach (var path in paths.Order(StringComparer.Ordinal)) { Append(path); Append(evidence[path]); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static IEnumerable<string> SafeFiles(string root)
    {
        foreach (var item in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if (item.Name == ".git" || (item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if ((item.Attributes & FileAttributes.Directory) != 0) { foreach (var nested in SafeFiles(item.FullName)) yield return nested; }
            else yield return item.FullName;
        }
    }
    private static GitOperation DetectOperation(string gitdir)
    {
        if (File.Exists(Path.Combine(gitdir, "MERGE_HEAD"))) return GitOperation.Merge;
        if (Directory.Exists(Path.Combine(gitdir, "rebase-merge")) || Directory.Exists(Path.Combine(gitdir, "rebase-apply"))) return GitOperation.Rebase;
        if (File.Exists(Path.Combine(gitdir, "CHERRY_PICK_HEAD"))) return GitOperation.CherryPick;
        if (File.Exists(Path.Combine(gitdir, "REVERT_HEAD"))) return GitOperation.Revert;
        return File.Exists(Path.Combine(gitdir, "BISECT_START")) ? GitOperation.Bisect : GitOperation.None;
    }
    private static string CanonicalFolder(string path)
    {
        var full = Path.GetFullPath(path); if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Select an existing project folder.");
        var info = new DirectoryInfo(full); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) full = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Cannot resolve selected folder link.");
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static bool HasGitMarkerInParents(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, ".git")) || Directory.Exists(Path.Combine(directory.FullName, ".git"))) return true;
        return false;
    }
    private static bool ObjectId(string value) => value.Length >= 4 && value.All(Uri.IsHexDigit);
    private static void ValidatePath(ProjectRegistration project, string path)
    {
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Contains('\0') || path.Split(['/', '\\']).Any(p => p is ".git" or ".." or ".")) throw new InvalidOperationException("Select an exact project-relative file path, outside Git metadata.");
        var root = Path.GetFullPath(project.RootPath); var absolute = Path.GetFullPath(Path.Combine(root, path));
        if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Selected path escaped the reviewed project.");
        var parent = new FileInfo(absolute).Directory;
        while (parent != null && !string.Equals(parent.FullName, root, StringComparison.OrdinalIgnoreCase)) { if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("A selected path crosses a folder link. Select its real project separately."); parent = parent.Parent; }
    }
    private static string Redact(string text, ProjectRegistration project)
    {
        text = text.Replace(project.RootPath, "<project>", StringComparison.OrdinalIgnoreCase).Replace(project.SelectedFolder, "<selected-folder>", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(https?://)[^\s/@]+:[^\s/@]+@", "$1<credentials>@", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"\b(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|Bearer\s+[^\s]+)\b", "<redacted>", RegexOptions.IgnoreCase);
    }
}
