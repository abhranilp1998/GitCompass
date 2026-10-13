namespace GitCompass.Infrastructure.Local;

public static class DependencyProbe
{
    public static string? FindGitExecutable()
    {
        var known = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe")
        };
        return known.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p.Trim('"'), "git.exe")))
            .FirstOrDefault(File.Exists);
    }
}
