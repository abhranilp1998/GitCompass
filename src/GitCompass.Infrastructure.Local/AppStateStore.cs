using System.Text.Json;

namespace GitCompass.Infrastructure.Local;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string Theme { get; init; } = "System";
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
        var settings = await ReadAsync("settings.json", new AppSettings(), cancellationToken);
        if (!ValidSettings(settings)) throw new AppStateException("These settings or their schema are unsupported. The original file is preserved.", Path.Combine(Root, "settings.json"));
        return settings;
    }
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!ValidSettings(settings))
            throw new ArgumentException("Unsupported settings schema, monitoring interval or quiet hours. Quiet hours use two different local hours from 0 to 23.");
        return WriteAsync("settings.json", settings, cancellationToken);
    }
    private static bool ValidSettings(AppSettings settings) => settings.SchemaVersion == 1 &&
        settings.FetchIntervalMinutes is >= 1 and <= 1440 &&
        (settings.QuietHoursStart is null && settings.QuietHoursEnd is null ||
         settings.QuietHoursStart is >= 0 and <= 23 && settings.QuietHoursEnd is >= 0 and <= 23 && settings.QuietHoursStart != settings.QuietHoursEnd);
    public async Task<IReadOnlyList<RecentProject>> LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await ReadAsync("repositories.json", new ProjectListRecord(1, []), cancellationToken);
        if (projects.SchemaVersion != 1 || projects.Projects is null) throw new AppStateException("This project-list schema is unsupported. The original file is preserved.", Path.Combine(Root, "repositories.json"));
        return projects.Projects;
    }
    public async Task RememberProjectAsync(string folder, string displayName, CancellationToken cancellationToken = default)
    {
        var absolute = Path.GetFullPath(folder);
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var projects = await LoadProjectsAsync(cancellationToken);
            var next = projects.Where(p => !Path.GetFullPath(p.Folder).Equals(absolute, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentProject(absolute, displayName, DateTimeOffset.UtcNow)).Take(20).ToArray();
            await WriteCoreAsync("repositories.json", new ProjectListRecord(1, next), cancellationToken);
        }
        finally { _writes.Release(); }
    }
    private async Task<T> ReadAsync<T>(string name, T fallback, CancellationToken token)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return fallback;
        try
        {
            using var file = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(file, JsonOptions, token) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            // Never overwrite the damaged file as if settings were successfully read.
            throw new AppStateException($"The {name} file is unreadable. It has been preserved; choose recovery before saving defaults.", path);
        }
    }
    private async Task WriteAsync<T>(string name, T value, CancellationToken token)
    {
        await _writes.WaitAsync(token);
        try { await WriteCoreAsync(name, value, token); }
        finally { _writes.Release(); }
    }
    private async Task WriteCoreAsync<T>(string name, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        var destination = Path.Combine(Root, name);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
