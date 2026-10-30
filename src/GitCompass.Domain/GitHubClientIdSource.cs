namespace GitCompass.Domain;

/// <summary>
/// Chooses the GitHub App public client ID: a value the user saved in Settings wins, otherwise the ID
/// shipped with the build (MSBuild property GitCompassGitHubClientId / env GITCOMPASS_GITHUB_CLIENT_ID).
/// Client IDs are public identifiers for the device flow; no client secret is ever shipped or accepted.
/// </summary>
public static class GitHubClientIdSource
{
    public static string? Resolve(string? fromSettings, string? shipped) =>
        Normalize(fromSettings) ?? Normalize(shipped);

    /// <summary>Accepts only the character set GitHub uses for client IDs (letters, digits, '.', '_', '-'), up to 64 chars.</summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 64) return null;
        return trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? trimmed : null;
    }
}
