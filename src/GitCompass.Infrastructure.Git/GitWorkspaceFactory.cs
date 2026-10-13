using GitCompass.Application;

namespace GitCompass.Infrastructure.Git;

public static class GitWorkspaceFactory
{
    public static GitWorkspace Create(string gitExecutablePath) => new(new GitBackend(gitExecutablePath));
}
