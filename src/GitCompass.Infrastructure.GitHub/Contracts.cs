namespace GitCompass.Infrastructure.GitHub;

public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, DateTimeOffset? RefreshExpiresAt);
public interface ITokenStore
{
    Task<TokenSet?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
public sealed record DeviceAuthorization(string UserCode, Uri VerificationUri, string DeviceCode, DateTimeOffset ExpiresAt, int IntervalSeconds);
public sealed record PullRequestDraft(string Title, string Body, string Head, string Base, bool Draft, string? HeadRepository = null);
public sealed record PullRequestInfo(int Number, string Title, string State, string HtmlUrl, string Head, string Base, bool Draft, bool? Mergeable, string? HeadRepository = null);
public sealed record PullRequestStatus(PullRequestInfo Request, string? MergeState, IReadOnlyList<string> Reviews, IReadOnlyList<string> Checks, IReadOnlyList<string> Unavailable);
public enum GitHubFailure { SignInRequired, Forbidden, Inaccessible, RateLimited, Validation, Connectivity, OutcomeUnknown, InvalidResponse }
public sealed class GitHubException(GitHubFailure failure, string message, DateTimeOffset? retryAfter = null) : Exception(message)
{
    public GitHubFailure Failure { get; } = failure;
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}
