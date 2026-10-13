using System.Text.Json;

namespace GitCompass.Infrastructure.Local;

public sealed class StructuredLogStore(string root)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly string _folder = Path.Combine(root, "logs");
    public async Task AppendAsync(string project, string operation, string state, string text, CancellationToken cancellationToken = default)
    {
        var item = new { Time = DateTimeOffset.UtcNow, Project = Redactor.Redact(project), Operation = operation, State = state,
            Text = Redactor.Redact(text.Length > 16000 ? text[..16000] + " [event truncated]" : text) };
        await _writes.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_folder);
            var files = new DirectoryInfo(_folder).GetFiles("*.jsonl").OrderBy(f => f.LastWriteTimeUtc).ToList();
            long retained = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7) || retained > 50L * 1024 * 1024)
                { retained -= file.Length; file.Delete(); }
            }
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var current = new DirectoryInfo(_folder).GetFiles(today + "*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                ?? Path.Combine(_folder, today + ".jsonl");
            if (File.Exists(current) && new FileInfo(current).Length > 8 * 1024 * 1024)
                current = Path.Combine(_folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".jsonl");
            await File.AppendAllTextAsync(current, JsonSerializer.Serialize(item) + Environment.NewLine, cancellationToken);
        }
        finally { _writes.Release(); }
    }
}
