using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GitCompass.Desktop;
using GitCompass.Infrastructure.Local;

static class Program
{
    [STAThread]
    static int Main(string[] arguments)
    {
        try { Run(arguments); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Run(string[] arguments)
    {
        if (arguments.Contains("--liquid-only"))
        {
            var liquidRoot = Path.Combine(Environment.CurrentDirectory, ".artifacts", "desktop-liquid-tests", Guid.NewGuid().ToString("N"));
            LiquidGlassChecks.Run(liquidRoot); return;
        }
        if (arguments.Contains("--responsive-only"))
        {
            var responsiveRoot = Path.Combine(Environment.CurrentDirectory, ".artifacts", "desktop-responsive-tests", Guid.NewGuid().ToString("N"));
            ResponsiveLayoutChecks.Run(responsiveRoot); return;
        }
        var app = arguments.FirstOrDefault() ?? Path.Combine(Environment.CurrentDirectory, "src", "GitCompass.Desktop", "bin", "Debug", "net10.0-windows10.0.17763.0", "GitCompass.exe");
        if (!File.Exists(app)) throw new FileNotFoundException("Build the desktop executable before its render tests.", app);
        if (arguments.Contains("--startup-only"))
        {
            var startupRoot = Path.Combine(Environment.CurrentDirectory, ".artifacts", "desktop-startup-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(startupRoot);
            StartupCloseChecks.Run(app, startupRoot); return;
        }
        var git = DependencyProbe.FindGitExecutable() ?? throw new InvalidOperationException("Git is needed for real UI fixtures.");
        var root = Path.Combine(Environment.CurrentDirectory, ".artifacts", "desktop-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var a = Path.Combine(root, "project-a"); var b = Path.Combine(root, "project-b"); var remote = Path.Combine(root, "team.git"); Directory.CreateDirectory(a);
        Git(git, root, "init", "--bare", "--initial-branch=main", remote);
        Git(git, a, "init", "--initial-branch=main"); Identity(git, a);
        File.WriteAllText(Path.Combine(a, "practice.txt"), "Initial practice version\n"); Git(git, a, "add", "--", "practice.txt"); Git(git, a, "commit", "-m", "Initial fixture"); Git(git, a, "remote", "add", "origin", remote); Git(git, a, "push", "--set-upstream", "origin", "main");
        Git(git, root, "clone", remote, b); Identity(git, b); File.WriteAllText(Path.Combine(b, "team.txt"), "Team history\n"); Git(git, b, "add", "--", "team.txt"); Git(git, b, "commit", "-m", "Add team version"); Git(git, b, "push", "origin", "main");
        Git(git, a, "fetch", "origin"); File.WriteAllText(Path.Combine(a, "local.txt"), "Local history\n"); Git(git, a, "add", "--", "local.txt"); Git(git, a, "commit", "-m", "Add local version"); File.AppendAllText(Path.Combine(a, "practice.txt"), "Unstaged work\n"); File.WriteAllText(Path.Combine(a, "new 雪.txt"), "New untracked content\n");
        var fingerprint = Fingerprint(a); var pages = arguments.Contains("--interactions-only") ? [] : new[] { "Overview", "Changes", "History graph", "Branches", "Pull requests", "Learn", "Settings" }; var passed = 0;
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var page in pages)
        {
            var capture = Path.Combine(root, theme.ToLowerInvariant() + "-" + page.Replace(' ', '-') + ".png");
            var start = new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--smoke", "--capture", capture, "--state-root", Path.Combine(root, "state-" + theme + "-" + page.Replace(' ', '-')), "--project", a, "--theme", theme, "--page", page }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch the built desktop app.");
            if (!process.WaitForExit(25000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Render test timed out: " + page); }
            Assert(process.ExitCode == 0, "Desktop startup returned failure for " + page);
            Assert(File.Exists(capture), "The real WPF capture exists for " + page);
            var description = File.ReadAllText(Path.ChangeExtension(capture, ".txt")); Assert(description.Contains("Page: " + page), "Correct page context is recorded"); Assert(description.Contains("Real observed branch: main"), "Real branch is displayed"); Assert(description.Contains("Files: 2"), "Tracked and untracked changes are counted separately from commits"); Assert(description.Contains("Level = ReadOnly"), "New project access stays Read-only");
            using var imageStream = File.OpenRead(capture); var bitmap = BitmapFrame.Create(imageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Assert(bitmap.PixelWidth >= 900 && bitmap.PixelHeight >= 600, "Client-area image has the expected usable size");
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); Assert(pixels.Where((_, i) => i % 4 == 3).All(p => p == 255), "The client-area render is opaque with no black transparency edge");
            Assert(Fingerprint(a) == fingerprint, "Opening and rendering preserves worktree/index/refs/objects/config content");
            Console.WriteLine($"PASS {theme} {page}: actual WPF page rendered; Read-only fingerprints unchanged. Capture: {capture}"); passed++;
        }
        var manifestPath = Path.Combine(Path.GetDirectoryName(app)!, "assets", "learning", "manifest.json"); using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var ids = json.RootElement.GetProperty("Entries").EnumerateArray().Select(e => e.GetProperty("Id").GetString()!).ToHashSet(); var commands = json.RootElement.GetProperty("SupportedCoreCommands").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert(commands.All(c => ids.Contains("git-" + c)), "Every declared core command has an original bundled reference entry"); Assert(ids.Contains("journey") && ids.Contains("conflicts") && ids.Contains("pr"), "Daily workflow and conflict/PR concepts ship offline");
        Console.WriteLine($"PASS learning manifest: {commands.Length} declared command entries; {ids.Count} total original lessons/reference entries. {passed} real WPF page/theme render scenarios passed. Fixture preserved: {root}");
        if (!arguments.Contains("--renders-only")) UiEvidenceChecks.Run(a, root);
        Assert(Fingerprint(a) == fingerprint, "Native comparison, filtering, diff/review cancellation, settings and output controls preserve repository content");
    }
    static void Identity(string git, string path) { Git(git, path, "config", "user.name", "GitCompass Desktop Fixture"); Git(git, path, "config", "user.email", "fixture@example.invalid"); }
    static void Git(string executable, string folder, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_GLOBAL"] = "NUL"; start.Environment["GIT_CONFIG_SYSTEM"] = "NUL";
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.WaitForExit(); Task.WaitAll(output, error); if (process.ExitCode != 0) throw new InvalidOperationException("Fixture command failed: " + string.Join(' ', args) + "\n" + error.Result);
    }
    internal static string Fingerprint(string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) { hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file))); hash.AppendData(File.ReadAllBytes(file)); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    static void Assert(bool condition, string meaning) { if (!condition) throw new InvalidOperationException("ASSERTION: " + meaning); }
}
