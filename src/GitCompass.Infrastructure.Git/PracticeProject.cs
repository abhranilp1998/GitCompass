namespace GitCompass.Infrastructure.Git;

/// <summary>C# port of scripts/create-practice.ps1: a disposable local repository with one saved version, one edit and one new file, and no remote.</summary>
public static class PracticeProject
{
    public const string FolderName = "GitCompass-Practice";
    public const string Instructions = """
        GitCompass practice project

        This folder is disposable and has no remote or online connection configured.
        Its practice author identity applies only to this folder.

        Follow the missions in GitCompass: see a change, stage, commit,
        branch and merge, and optionally push to your own GitHub repository.

        A commit saves selected contents locally. A push is a separate upload;
        this practice project has no upload destination until you add one.
        You may remove this entire practice folder later without affecting another project.

        """;

    /// <summary>Creates the practice folder under <paramref name="parent"/>. Refuses to replace an existing folder.</summary>
    public static async Task<string> CreateAsync(string gitExecutable, string parent, CancellationToken cancellation = default)
    {
        var folder = Path.Combine(Path.GetFullPath(parent), FolderName);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("A practice folder already exists at " + folder + ". It has not been changed. Choose another location.");
        var git = new GitProcess(gitExecutable);
        Directory.CreateDirectory(folder);
        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        async Task Run(params string[] arguments)
        {
            var result = await git.RunAsync(folder, new[] { "-c", "core.hooksPath=" + nullDevice, "-c", "commit.gpgsign=false" }.Concat(arguments), cancellation, inspection: false);
            if (result.ExitCode != 0 || result.Canceled) throw new InvalidOperationException("Creating the practice project failed at git " + arguments[0] + ": " + result.Error.Trim());
        }
        var utf8 = new System.Text.UTF8Encoding(false);
        await Run("init", "--initial-branch=main");
        await Run("config", "--local", "user.name", "GitCompass Practice");
        await Run("config", "--local", "user.email", "practice@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(folder, "welcome.txt"), "This is a disposable GitCompass practice project.\nThe first version is saved locally.\n", utf8, cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, ".gitignore"), "scratch/\n", utf8, cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, "READ-ME-FIRST.txt"), Instructions, utf8, cancellation);
        await Run("add", "--", ".gitignore", "welcome.txt", "READ-ME-FIRST.txt");
        await Run("commit", "-m", "Save the first local practice version");
        await File.AppendAllTextAsync(Path.Combine(folder, "welcome.txt"), "This extra line is an edit after the saved version.\n", utf8, cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, "ideas.txt"), "This new file has not been saved in a Git version yet.\n", utf8, cancellation);
        var remotes = await git.RunAsync(folder, ["remote"], cancellation);
        if (remotes.Output.Trim().Length != 0) throw new InvalidOperationException("The practice project must have no configured remotes.");
        return folder;
    }
}
