using System.Text.Json;

namespace GitCompass.Infrastructure.Local;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string Theme { get; init; } = "System";
    public string WindowMaterial { get; init; } = "Mica";
    public string? GitExecutablePath { get; init; }
    public string? GitHubClientId { get; init; }
    public int FetchIntervalMinutes { get; init; } = 5;
    public bool EnableMonitoring { get; init; }
    public bool MuteMonitoringNotifications { get; init; }
    public int? QuietHoursStart { get; init; }
    public int? QuietHoursEnd { get; init; }
}
public sealed record RecentProject(string Folder, string DisplayName, DateTimeOffset LastOpened);
public sealed record ProjectListRecord(int SchemaVersion, RecentProject[] Projects);
public sealed record BeginnerGuideState
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool WelcomeDismissed { get; init; }
    public bool Active { get; init; }
    public string? ProjectFolder { get; init; }
    public string Intent { get; init; } = "";
    public string? TeamCopyFolder { get; init; }
    public string? PracticeFolder { get; init; }
    public string? SavedCommitId { get; init; }
    /// <summary>Practice mission index (0–5, see BeginnerMissions). Added in schema 2.</summary>
    public int MissionIndex { get; init; }
    public bool PushOffered { get; init; }
    /// <summary>True only for a practice repository created by the explicitly reviewed guided action.</summary>
    public bool GuidedPractice { get; init; }

    /// <summary>Upgrades a schema 1 record. A recorded first save means the stage and commit missions are complete.</summary>
    public static BeginnerGuideState Migrate(BeginnerGuideState guide) => guide.SchemaVersion == 1
        ? guide with { SchemaVersion = CurrentSchemaVersion, MissionIndex = guide.SavedCommitId is null ? 0 : 3 }
        : guide;
}
public sealed class AppStateException(string message, string preservedPath) : Exception(message)
{
    public string PreservedPath { get; } = preservedPath;
}

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writes = new(1, 1);
    public string Root { get; }
    public AppStateStore(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitCompass");
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadAsync("settings.json", new AppSettings(), cancellationToken).ConfigureAwait(false);
        if (!ValidSettings(settings)) throw new AppStateException("These settings or their schema are unsupported. The original file is preserved.", Path.Combine(Root, "settings.json"));
        return settings;
    }
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!ValidSettings(settings))
            throw new ArgumentException("Unsupported settings schema, window material, monitoring interval or quiet hours. Quiet hours use two different local hours from 0 to 23.");
        return WriteAsync("settings.json", settings, cancellationToken);
    }
    private static bool ValidSettings(AppSettings settings) => settings.SchemaVersion == 1 &&
        settings.WindowMaterial is "Mica" or "Acrylic" or "Solid" &&
        settings.FetchIntervalMinutes is >= 1 and <= 1440 &&
        (settings.QuietHoursStart is null && settings.QuietHoursEnd is null ||
         settings.QuietHoursStart is >= 0 and <= 23 && settings.QuietHoursEnd is >= 0 and <= 23 && settings.QuietHoursStart != settings.QuietHoursEnd);
    public async Task<IReadOnlyList<RecentProject>> LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await ReadAsync("repositories.json", new ProjectListRecord(1, []), cancellationToken).ConfigureAwait(false);
        if (projects.SchemaVersion != 1 || projects.Projects is null) throw new AppStateException("This project-list schema is unsupported. The original file is preserved.", Path.Combine(Root, "repositories.json"));
        return projects.Projects;
    }
    public async Task<BeginnerGuideState> LoadBeginnerGuideAsync(CancellationToken cancellationToken = default)
    {
        var guide = BeginnerGuideState.Migrate(await ReadAsync("beginner-guide.json", new BeginnerGuideState(), cancellationToken).ConfigureAwait(false));
        if (!ValidGuide(guide)) throw new AppStateException("This walkthrough record is unsupported. The original file is preserved.", Path.Combine(Root, "beginner-guide.json"));
        return guide;
    }
    public Task SaveBeginnerGuideAsync(BeginnerGuideState guide, CancellationToken cancellationToken = default)
    {
        if (!ValidGuide(guide)) throw new ArgumentException("Unsupported walkthrough schema or project intent.");
        return WriteAsync("beginner-guide.json", guide, cancellationToken);
    }
    private static bool ValidGuide(BeginnerGuideState guide) => guide.SchemaVersion == BeginnerGuideState.CurrentSchemaVersion && guide.MissionIndex is >= 0 and <= 5 && (guide.Intent is "" or "OwnFiles" or "TeamCopy") &&
        ValidGuidePath(guide.ProjectFolder) && ValidGuidePath(guide.TeamCopyFolder) && ValidGuidePath(guide.PracticeFolder);
    private static bool ValidGuidePath(string? path)
    {
        if (path is null) return true;
        try { return Path.IsPathFullyQualified(path) && Path.GetFullPath(path).Length > 0; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
    public async Task RememberProjectAsync(string folder, string displayName, CancellationToken cancellationToken = default)
    {
        var absolute = Path.GetFullPath(folder);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var projects = await LoadProjectsAsync(cancellationToken).ConfigureAwait(false);
            var next = projects.Where(p => !Path.GetFullPath(p.Folder).Equals(absolute, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentProject(absolute, displayName, DateTimeOffset.UtcNow)).Take(20).ToArray();
            await WriteCoreAsync("repositories.json", new ProjectListRecord(1, next), cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }
    private async Task<T> ReadAsync<T>(string name, T fallback, CancellationToken token)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return fallback;
        try
        {
            // Readers keep their snapshot while an atomic save replaces the pathname on Windows.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(file, JsonOptions, token).ConfigureAwait(false) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            // Never overwrite the damaged file as if settings were successfully read.
            throw new AppStateException($"The {name} file is unreadable. It has been preserved; choose recovery before saving defaults.", path);
        }
    }
    private async Task WriteAsync<T>(string name, T value, CancellationToken token)
    {
        // Storage must finish independently of a caller's UI/theme synchronization context.
        await _writes.WaitAsync(token).ConfigureAwait(false);
        try { await WriteCoreAsync(name, value, token).ConfigureAwait(false); }
        finally { _writes.Release(); }
    }
    private async Task WriteCoreAsync<T>(string name, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        var destination = Path.Combine(Root, name);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { File.Move(temporary, destination, overwrite: true); break; }
                catch (Exception error) when (attempt < 6 && WindowsReplaceContention(error) && File.Exists(temporary))
                {
                    // Windows can require exclusive destination access even for delete-sharing readers.
                    // Keep the complete temporary file; never delete or rewrite the saved destination.
                    await Task.Delay(Math.Min(20 << attempt, 160), token).ConfigureAwait(false);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool WindowsReplaceContention(Exception error) => OperatingSystem.IsWindows() &&
        (error is UnauthorizedAccessException && (error.HResult & 0xFFFF) == 5 ||
         error is IOException && (error.HResult & 0xFFFF) is 32 or 33);
}
