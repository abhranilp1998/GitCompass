using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GitCompass.Infrastructure.Local;

public sealed record InstalledGitCommand(string Name, string Section, string Description, bool IsDeclaredSupported);
public sealed record GitDocumentationInterface(string Name, string Section, string Description);
public sealed record GitCommandInventoryResult(string Version, ImmutableArray<InstalledGitCommand> Commands,
    ImmutableArray<string> UnsupportedInstalledCommands, ImmutableArray<string> DeclaredCommandsMissingFromInventory,
    ImmutableArray<GitDocumentationInterface> DocumentationInterfaces, string? Error = null)
{
    public int SupportedInstalledCount => Commands.Count(command => command.IsDeclaredSupported);
}

/// <summary>Offline reconciliation of installed Git's built-in command inventory with the
/// explicitly declared teaching catalog. It never executes a listed command or alias.</summary>
public static class GitCommandInventory
{
    private const int MaximumOutputCharacters = 256 * 1024;
    private static readonly Regex Item = new(@"^\s{2,}([a-z][a-z0-9-]*)\s{2,}(.*)$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> CommandSections = new(StringComparer.Ordinal)
    {
        "Main Porcelain Commands", "Ancillary Commands / Manipulators", "Ancillary Commands / Interrogators",
        "Interacting with Others", "Low-level Commands / Manipulators", "Low-level Commands / Interrogators",
        "Low-level Commands / Syncing Repositories", "Low-level Commands / Internal Helpers"
    };
    private static readonly HashSet<string> DocumentationSections = new(StringComparer.Ordinal)
    {
        "User-facing repository, command and file interfaces", "Developer-facing file formats, protocols and other interfaces"
    };

    public static async Task<GitCommandInventoryResult> ReadAsync(string gitExecutable,
        IEnumerable<string> declaredSupportedCoreCommands, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(declaredSupportedCoreCommands);
        if (!Path.IsPathFullyQualified(gitExecutable) || !File.Exists(gitExecutable))
            throw new FileNotFoundException("Git inventory requires an existing absolute Git executable path.", gitExecutable);
        var declared = declaredSupportedCoreCommands.ToImmutableHashSet(StringComparer.Ordinal);
        if (declared.Count > 1000 || declared.Any(name => !Regex.IsMatch(name, "^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)))
            throw new ArgumentException("The declared inventory contains an invalid command name.", nameof(declaredSupportedCoreCommands));
        try
        {
            var version = await RunAsync(["--version"]);
            var help = await RunAsync(["help", "-a", "--no-external-commands", "--no-aliases"]);
            return Parse(version.Trim(), help, declared);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or FormatException)
        {
            // A failed enumeration is an unknown inventory, never zero installed commands.
            return new("Unknown", [], [], [], [], "Installed Git command inventory could not be read: " + exception.Message);
        }

        async Task<string> RunAsync(string[] arguments)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var info = new ProcessStartInfo(Path.GetFullPath(gitExecutable))
            {
                WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, false)
            };
            info.Environment.Clear();
            foreach (var name in new[] { "SystemRoot", "SystemDrive", "WINDIR", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "ComSpec" })
                if (Environment.GetEnvironmentVariable(name) is { } value) info.Environment[name] = value;
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
            info.Environment["GIT_CONFIG_SYSTEM"] = "NUL";
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GIT_PAGER"] = "cat";
            info.Environment["LC_ALL"] = "C";
            foreach (var argument in new[] { "--no-pager", "-c", "color.ui=false", "-c", "help.autoCorrect=0" }.Concat(arguments)) info.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = info };
            deadline.Token.ThrowIfCancellationRequested(); process.Start(); process.StandardInput.Close();
            using var registration = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
            var stdout = Capture(process.StandardOutput); var stderr = Capture(process.StandardError);
            await process.WaitForExitAsync(CancellationToken.None);
            var streams = await Task.WhenAll(stdout, stderr);
            cancellation.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new InvalidOperationException("The offline Git inventory query timed out.");
            if (streams.Any(stream => stream.Truncated)) throw new InvalidOperationException("The installed command inventory exceeds the bounded inspection limit.");
            if (process.ExitCode != 0) throw new InvalidOperationException("Git did not support the offline command-inventory query (exit " + process.ExitCode + ").");
            return streams[0].Text;
        }
    }

    public static GitCommandInventoryResult Parse(string version, string helpOutput, IEnumerable<string> declaredSupportedCoreCommands)
    {
        if (helpOutput.Length > MaximumOutputCharacters) throw new FormatException("The command inventory exceeds its bounded limit.");
        var declared = declaredSupportedCoreCommands.ToImmutableHashSet(StringComparer.Ordinal);
        var commands = ImmutableArray.CreateBuilder<InstalledGitCommand>();
        var documentation = ImmutableArray.CreateBuilder<GitDocumentationInterface>();
        var seen = new HashSet<string>(StringComparer.Ordinal); string? section = null; var foundSection = false;
        foreach (var raw in helpOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!char.IsWhiteSpace(line[0]))
            {
                section = CommandSections.Contains(line) || DocumentationSections.Contains(line) ? line : null;
                if (section is not null) foundSection = true;
                else if (foundSection) throw new FormatException("Git returned an unfamiliar inventory section; completeness cannot be established.");
                continue;
            }
            if (section is null) continue;
            var match = Item.Match(line);
            if (!match.Success) throw new FormatException("Git returned an unfamiliar command-inventory entry format.");
            var name = match.Groups[1].Value; var description = match.Groups[2].Value;
            if (DocumentationSections.Contains(section)) { documentation.Add(new(name, section, description)); continue; }
            if (!seen.Add(name)) throw new FormatException("Git returned a duplicate core command entry.");
            commands.Add(new(name, section, description, declared.Contains(name)));
        }
        if (commands.Count == 0) throw new FormatException("No recognized command sections were returned by installed Git.");
        var all = commands.ToImmutable();
        return new(version, all, all.Where(command => !command.IsDeclaredSupported).Select(command => command.Name).Order(StringComparer.Ordinal).ToImmutableArray(),
            declared.Except(seen).Order(StringComparer.Ordinal).ToImmutableArray(), documentation.ToImmutable());
    }

    private static async Task<(string Text, bool Truncated)> Capture(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; var truncated = false; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            var remaining = Math.Max(0, MaximumOutputCharacters - text.Length);
            if (count > remaining) truncated = true;
            text.Append(buffer.AsSpan(0, Math.Min(remaining, count)));
        }
        return (text.ToString(), truncated);
    }
}
