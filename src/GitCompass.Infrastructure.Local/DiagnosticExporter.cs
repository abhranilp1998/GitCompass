using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitCompass.Infrastructure.Local;

public sealed record DiagnosticBundle(string Markdown, string Json);
public static partial class Redactor
{
    public static string Redact(string text)
    {
        var result = SecretRegex().Replace(text, "[credential hidden]");
        result = HeaderRegex().Replace(result, "$1[hidden]");
        result = UrlCredentialsRegex().Replace(result, "$1[credential hidden]@");
        result = QuerySecretRegex().Replace(result, "$1[hidden]");
        result = WindowsPathRegex().Replace(result, "[private path]");
        result = UnixHomeRegex().Replace(result, "[private path]");
        var user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length > 2)
            result = Regex.Replace(result, Regex.Escape(user), "[user]", RegexOptions.IgnoreCase);
        return result;
    }
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]{16,})\b")] private static partial Regex SecretRegex();
    [GeneratedRegex(@"(?im)\b(Authorization\s*:\s*(?:(?:Bearer|Basic)\s+)?|(?:access_token|refresh_token|password|client_secret)\s*[=:]\s*)[^\s,;]+") ] private static partial Regex HeaderRegex();
    [GeneratedRegex(@"(https?://)[^\s/@]+(?::[^\s/@]*)?@", RegexOptions.IgnoreCase)] private static partial Regex UrlCredentialsRegex();
    [GeneratedRegex(@"([?&](?:token|access_token|key|password)=)[^&#\s]+", RegexOptions.IgnoreCase)] private static partial Regex QuerySecretRegex();
    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\)[^\r\n\t<>""|]+") ] private static partial Regex WindowsPathRegex();
    [GeneratedRegex(@"/(?:home|Users)/[^\s<>""']+", RegexOptions.IgnoreCase)] private static partial Regex UnixHomeRegex();
}
public sealed class DiagnosticExporter
{
    public DiagnosticBundle Prepare(string projectAlias, string? branch, string access, string gitVersion,
        DateTimeOffset? lastRemoteCheck, IEnumerable<string> boundedEvents)
    {
        var events = boundedEvents.TakeLast(500).Select(e => Redactor.Redact(e.Length > 4000 ? e[..4000] + " [truncated]" : e)).ToArray();
        var summary = new
        {
            SchemaVersion = 1, Application = "GitCompass 0.1.0", OS = Environment.OSVersion.VersionString,
            Project = Redactor.Redact(projectAlias), Branch = Redactor.Redact(branch ?? "unknown"), Access = access,
            Git = gitVersion, LastSuccessfulRemoteCheck = lastRemoteCheck, PreparedAt = DateTimeOffset.UtcNow, Events = events,
            Excluded = "Source, diffs, environment, repository contents and terminal scrollback. Redaction is best effort; review before sharing."
        };
        var markdown = $"# GitCompass support summary\n\nPrepared: {summary.PreparedAt:O}\n\n" +
            $"Project: {summary.Project}\n\nBranch: {summary.Branch}\n\nAccess: {access}\n\nGit: {gitVersion}\n\nOS: {summary.OS}\n\n" +
            $"Last successful remote check: {lastRemoteCheck?.ToString("O") ?? "not checked"}\n\n" +
            "Review every field before sharing. Source, diffs, repository files, environment and terminal scrollback are excluded.\n\n```text\n" +
            string.Join('\n', events).Replace("```", "'''", StringComparison.Ordinal) + "\n```\n";
        return new(markdown, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
    public async Task SaveAsync(DiagnosticBundle bundle, string selectedDirectory, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetFullPath(selectedDirectory), "GitCompass-support-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.md"), bundle.Markdown, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "events.json"), bundle.Json, cancellationToken);
    }
}
