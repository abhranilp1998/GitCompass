using System.Diagnostics;
using System.IO;

namespace GitCompass.Desktop;

/// <summary>Reveals a project item without invoking its file association or executing it.</summary>
public static class ProjectFileReveal
{
    public static ProcessStartInfo Create(string projectRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('"')) throw new ArgumentException("Choose a project-relative file path.", nameof(relativePath));
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        var absolute = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The selected path is outside this project.", nameof(relativePath));
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var command = new ProcessStartInfo(explorer) { UseShellExecute = false };
        command.ArgumentList.Add("/select," + absolute);
        return command;
    }
}
