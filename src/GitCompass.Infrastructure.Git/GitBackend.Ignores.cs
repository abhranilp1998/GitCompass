using System.Collections.Immutable;
using GitCompass.Domain;

namespace GitCompass.Infrastructure.Git;

internal sealed partial class GitBackend
{
    public async Task<ImmutableArray<IgnoreSuggestion>> IgnoreSuggestionsAsync(RepositorySnapshot snapshot, CancellationToken cancellation)
    {
        var root = snapshot.Project.RootPath;
        var allPaths = snapshot.Project.Kind == RepositoryKind.Candidate ? SafeFiles(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray() : (await Value(root, cancellation, "ls-files", "-z", "--cached", "--others", "--exclude-standard"))?.Split('\0', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var candidates = new List<(string Pattern, string Reason)>();
        if (allPaths.Any(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(("bin/", "Compiled .NET output can be rebuilt from source.")); candidates.Add(("obj/", "Intermediate .NET build files can be regenerated.")); candidates.Add((".vs/", "Visual Studio's local workspace settings are machine-specific."));
        }
        if (allPaths.Any(p => Path.GetFileName(p) == "package.json")) candidates.Add(("node_modules/", "Installed JavaScript dependencies are restored from the package manifests and lockfile."));
        if (allPaths.Any(p => Path.GetFileName(p) is "pyproject.toml" or "requirements.txt")) { candidates.Add(("__pycache__/", "Python bytecode is generated locally.")); candidates.Add((".venv/", "A Python virtual environment is restored locally.")); }
        candidates.Add((".env", "A local environment file may contain private settings. Review it separately; ignoring is not secret removal."));
        var existing = File.Exists(Path.Combine(root, ".gitignore")) ? await File.ReadAllTextAsync(Path.Combine(root, ".gitignore"), cancellation) : "";
        var existingLines = existing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var tracked = snapshot.Project.Kind == RepositoryKind.Candidate ? new HashSet<string>() : ((await Value(root, cancellation, "ls-files", "-z", "--cached"))?.Split('\0', StringSplitOptions.RemoveEmptyEntries) ?? []).ToHashSet(StringComparer.Ordinal);
        var suggestions = ImmutableArray.CreateBuilder<IgnoreSuggestion>();
        foreach (var (pattern, reason) in candidates.Where(c => !existingLines.Contains(c.Pattern)))
        {
            bool Matches(string path) => pattern.EndsWith('/') ? path.Split('/').SkipLast(1).Contains(pattern.TrimEnd('/'), StringComparer.Ordinal) : Path.GetFileName(path) == pattern;
            var affected = allPaths.Where(Matches).ToArray();
            suggestions.Add(new(pattern, reason, affected.Where(p => !tracked.Contains(p)).ToImmutableArray(), affected.Where(tracked.Contains).ToImmutableArray()));
        }
        return suggestions.ToImmutable();
    }
}
