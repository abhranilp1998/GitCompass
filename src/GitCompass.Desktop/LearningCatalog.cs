using System.IO;
using System.Text.Json;

namespace GitCompass.Desktop;

public sealed record LearningEntry(string Id, string Title, string Category, string Purpose, string Prerequisites, string Effects, string WhenToPause, string Example, string Outcome, string Practice);
public sealed record LearningManifest(int SchemaVersion, string Version, string[] SupportedCoreCommands, LearningEntry[] Entries);
public sealed class LearningCatalog
{
    public LearningManifest Manifest { get; }
    public LearningCatalog()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "assets", "learning", "manifest.json");
        Manifest = JsonSerializer.Deserialize<LearningManifest>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("The local learning manifest is invalid.");
    }
    public IEnumerable<LearningEntry> Search(string query) => Manifest.Entries.Where(e => string.IsNullOrWhiteSpace(query) || $"{e.Title} {e.Category} {e.Purpose}".Contains(query, StringComparison.OrdinalIgnoreCase));
    public LearningEntry? Find(string id)
    {
        id = id switch { "stage" or "unstage" => "add", "update" => "pull", "branches" => "branch", "history" => "log", "pull-requests" => "pr", _ => id };
        return Manifest.Entries.FirstOrDefault(e => e.Id == id || e.Id == "git-" + id);
    }
}
