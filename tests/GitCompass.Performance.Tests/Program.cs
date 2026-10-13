using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GitCompass.Infrastructure.Git;

// A real, disposable 5,000-file/10,000-commit workload. This records measurements;
// it does not assert another computer's speed or change a user repository.
var workspaceRoot = Path.GetFullPath(Environment.CurrentDirectory);
var reused = args.Length == 2 && args[0] == "--reuse";
var performanceRoot = Path.Combine(workspaceRoot, ".artifacts", "performance");
var project = reused ? Path.GetFullPath(args[1]) : Path.Combine(performanceRoot, Guid.NewGuid().ToString("N"), "project");
if (!project.StartsWith(performanceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Reuse only a generated workspace performance fixture.");
var root = Path.GetDirectoryName(project)!;
Directory.CreateDirectory(project);
var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
if (!File.Exists(git)) throw new FileNotFoundException("Installed Git for Windows is required.", git);
if (!reused)
{
await Git("init", "--initial-branch=main");
var input = new StringBuilder();
input.AppendLine("blob").AppendLine("mark :1"); Data(input, "Fixture file\n");
for (var i = 1; i <= 10000; i++)
{
    input.AppendLine("commit refs/heads/main").AppendLine("mark :" + (i + 1))
        .AppendLine($"author GitCompass Performance Fixture <fixture@example.invalid> {1700000000 + i} +0000")
        .AppendLine($"committer GitCompass Performance Fixture <fixture@example.invalid> {1700000000 + i} +0000");
    Data(input, $"Recorded fixture version {i:00000}");
    if (i > 1) input.AppendLine("from :" + i);
    else for (var file = 1; file <= 5000; file++) input.AppendLine($"M 100644 :1 file-{file:0000}.txt");
    input.AppendLine();
}
input.AppendLine("done");
await GitInput(input.ToString().Replace("\r\n", "\n"), "fast-import", "--quiet");
await Git("checkout", "main", "--", ".");
}
if (Directory.GetFiles(project, "file-*.txt").Length != 5000 || (await Git("rev-list", "--count", "HEAD")).Trim() != "10000")
    throw new InvalidOperationException("Performance fixture is incomplete.");
var workspace = GitWorkspaceFactory.Create(git);
var timer = Stopwatch.StartNew();
var snapshot = await workspace.OpenAsync(project); timer.Stop();
var opening = timer.Elapsed.TotalMilliseconds;
if (snapshot.HeadId is null || snapshot.Files.Length != 0) throw new InvalidOperationException("Real clean fixture inspection failed.");
var refresh = new List<double>(); var history = new List<double>();
for (var i = 0; i < 3; i++)
{
    timer.Restart(); snapshot = await workspace.RefreshAsync(snapshot.ProjectId); timer.Stop(); refresh.Add(timer.Elapsed.TotalMilliseconds);
    timer.Restart(); var page = await workspace.HistoryAsync(snapshot.ProjectId); timer.Stop(); history.Add(timer.Elapsed.TotalMilliseconds);
    if (page.Commits.Length != 200 || !page.HasMore) throw new InvalidOperationException("Expected 200 real commit rows and bounded continuation.");
}
var report = new { ObservedUtc = DateTimeOffset.UtcNow, OS = Environment.OSVersion.ToString(), snapshot.GitVersion,
    Files = 5000, Commits = 10000, FirstOpenMilliseconds = opening, RefreshMilliseconds = refresh,
    First200RowsMilliseconds = history, TargetMilliseconds = 2000, AllRecordedReadsWithinTarget = opening < 2000 && refresh.All(t => t < 2000) && history.All(t => t < 2000),
    Fixture = root, Limits = "Development-machine sequential measurements; no supported-company OS, large-binary or UI-output latency claim." };
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(root, "performance.json"), json);
Console.WriteLine(json);

static void Data(StringBuilder input, string value) => input.AppendLine("data " + Encoding.UTF8.GetByteCount(value)).Append(value).Append('\n');
async Task<string> Git(params string[] arguments) => await GitInput(null, arguments);
async Task<string> GitInput(string? data, params string[] arguments)
{
    var start = new ProcessStartInfo(git) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false) };
    foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    start.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; start.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start fixture Git.");
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    if (data is not null) await process.StandardInput.WriteAsync(data);
    process.StandardInput.Close();
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    var text = await output; var problems = await error;
    if (process.ExitCode != 0) throw new InvalidOperationException("Performance fixture Git failed: " + problems);
    return text;
}
