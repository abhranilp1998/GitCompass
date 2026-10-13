using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitCompass.Infrastructure.GitHub;

public sealed class GitHubClient(HttpClient http, ITokenStore tokenStore, Func<bool> canRead, Func<bool> canWrite)
{
    public const string ApiVersion = "2026-03-10";
    private readonly Dictionary<string, (string? ETag, IReadOnlyList<PullRequestInfo> Rows, bool HasNext)> _cache = new();
    private readonly HashSet<string> _uncertainSubmissions = new();
    private readonly SemaphoreSlim _requests = new(1, 1);
    public Func<CancellationToken, Task<TokenSet>>? RefreshTokensAsync { get; set; }

    public async Task<bool> VerifyPublishedCommitAsync(string sourceOwner, string sourceRepository, string sourceBranch, string expectedCommitId, CancellationToken cancellationToken = default)
    {
        Authorize(false);
        if (string.IsNullOrWhiteSpace(sourceBranch) || string.IsNullOrWhiteSpace(expectedCommitId))
            throw new GitHubException(GitHubFailure.Validation, "Select a recorded source commit and published branch.");
        await _requests.WaitAsync(cancellationToken);
        try
        {
            using var request = await RequestAsync(HttpMethod.Get, RepoPath(sourceOwner, sourceRepository) + "/git/ref/heads/" + Uri.EscapeDataString(sourceBranch), cancellationToken);
            Authorize(false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await CheckResponseAsync(response, cancellationToken);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.GetProperty("object").GetProperty("sha").GetString() == expectedCommitId;
        }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Connectivity, "The published source could not be verified. Check GitHub before preparing this request."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub returned unreadable source-reference information."); }
        finally { _requests.Release(); }
    }

    public async Task<IReadOnlyList<PullRequestInfo>> ReadPullRequestsAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        Authorize(false);
        await _requests.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(owner, repository, cancellationToken); }
        finally { _requests.Release(); }
    }

    public async Task<PullRequestStatus> ReadPullRequestStatusAsync(string owner, string repository, int number, CancellationToken cancellationToken = default)
    {
        Authorize(false);
        if (number <= 0) throw new GitHubException(GitHubFailure.Validation, "Choose a positive pull request number.");
        await _requests.WaitAsync(cancellationToken);
        try
        {
            var repoPath = RepoPath(owner, repository);
            using var data = await ReadJsonAsync(repoPath + "/pulls/" + number, cancellationToken);
            var request = ParsePullRequest(data.RootElement);
            var sha = data.RootElement.GetProperty("head").GetProperty("sha").GetString();
            if (string.IsNullOrWhiteSpace(sha)) throw new JsonException("Missing source commit.");
            var unavailable = new List<string>();
            IReadOnlyList<string> reviews = [], checks = [], statuses = [];
            try { reviews = await ReadPagedSummariesAsync(repoPath + "/pulls/" + number + "/reviews", null,
                row => (row.GetProperty("user").ValueKind == JsonValueKind.Object ? row.GetProperty("user").GetProperty("login").GetString() : "Former user") + ": " + row.GetProperty("state").GetString(), cancellationToken); }
            catch (GitHubException error) when (error.Failure is GitHubFailure.Forbidden or GitHubFailure.Inaccessible) { unavailable.Add("Reviews: " + error.Message); }
            try { checks = await ReadPagedSummariesAsync(repoPath + "/commits/" + Uri.EscapeDataString(sha) + "/check-runs", "check_runs",
                row => row.GetProperty("name").GetString() + ": " + row.GetProperty("status").GetString() + " / " + (row.GetProperty("conclusion").GetString() ?? "pending"), cancellationToken); }
            catch (GitHubException error) when (error.Failure is GitHubFailure.Forbidden or GitHubFailure.Inaccessible) { unavailable.Add("Check runs: " + error.Message); }
            try { statuses = await ReadPagedSummariesAsync(repoPath + "/commits/" + Uri.EscapeDataString(sha) + "/status", "statuses",
                row => row.GetProperty("context").GetString() + ": " + row.GetProperty("state").GetString(), cancellationToken); }
            catch (GitHubException error) when (error.Failure is GitHubFailure.Forbidden or GitHubFailure.Inaccessible) { unavailable.Add("Commit statuses: " + error.Message); }
            return new(request, data.RootElement.TryGetProperty("mergeable_state", out var state) ? state.GetString() : null, reviews, checks.Concat(statuses).ToArray(), unavailable);
        }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Connectivity, "GitHub review or check status could not be reached. Refresh explicitly when connectivity is restored."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub returned unreadable review or check data."); }
        finally { _requests.Release(); }
    }

    private async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken token)
    {
        using var request = await RequestAsync(HttpMethod.Get, path, token);
        Authorize(false);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await CheckResponseAsync(response, token);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Connectivity, "GitHub status could not be reached. Previously shown results remain cached."); }
    }
    private async Task<IReadOnlyList<string>> ReadPagedSummariesAsync(string path, string? property, Func<JsonElement, string> summarize, CancellationToken token)
    {
        var result = new List<string>();
        for (var page = 1; page <= 100; page++)
        {
            using var request = await RequestAsync(HttpMethod.Get, path + "?per_page=100&page=" + page, token);
            Authorize(false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await CheckResponseAsync(response, token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var array = property is null ? json.RootElement : json.RootElement.GetProperty(property);
            result.AddRange(array.EnumerateArray().Select(summarize));
            if (!HasNextPage(response)) return result;
        }
        throw new GitHubException(GitHubFailure.InvalidResponse, "This status exceeds the bounded listing. Review the complete result on GitHub.");
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(string owner, string repository, PullRequestDraft draft, CancellationToken cancellationToken = default)
    {
        Authorize(true);
        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Head) || string.IsNullOrWhiteSpace(draft.Base))
            throw new GitHubException(GitHubFailure.Validation, "Review the title, published source branch, and target branch before submitting.");
        var identity = $"{owner}/{repository}/{draft.HeadRepository}/{draft.Head}/{draft.Base}";
        await _requests.WaitAsync(cancellationToken);
        try
        {
            // Check for an existing matching request, including after an uncertain response.
            var existing = (await ReadCoreAsync(owner, repository, cancellationToken, useCache: false, draft))
                .FirstOrDefault(pr => NormalizeHead(pr.Head, owner) == NormalizeHead(draft.Head, owner) && pr.Base == draft.Base && pr.State == "open" &&
                    (draft.HeadRepository is null || pr.HeadRepository == draft.HeadRepository));
            if (existing is not null) { _uncertainSubmissions.Remove(identity); return existing; }
            if (_uncertainSubmissions.Contains(identity))
                throw new GitHubException(GitHubFailure.OutcomeUnknown, "The earlier submission is still uncertain. Verify it on GitHub before starting another submission.");
            Authorize(true);
            using var request = await RequestAsync(HttpMethod.Post, RepoPath(owner, repository) + "/pulls", cancellationToken);
            request.Content = JsonContent.Create(new { title = draft.Title, body = draft.Body, head = draft.Head, head_repo = draft.HeadRepository, @base = draft.Base, draft = draft.Draft },
                options: new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            Authorize(true);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await CheckResponseAsync(response, cancellationToken);
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return ParsePullRequest(data.RootElement);
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                _uncertainSubmissions.Add(identity);
                throw new GitHubException(GitHubFailure.OutcomeUnknown, "The server may have accepted the pull request. Check existing requests before submitting again.");
            }
            catch (GitHubException error) when (error.Failure == GitHubFailure.Connectivity)
            {
                _uncertainSubmissions.Add(identity);
                throw new GitHubException(GitHubFailure.OutcomeUnknown, "GitHub returned an uncertain server result. Verify existing pull requests before another submission.");
            }
        }
        finally { _requests.Release(); }
    }

    private async Task<IReadOnlyList<PullRequestInfo>> ReadCoreAsync(string owner, string repository, CancellationToken token, bool useCache = true, PullRequestDraft? match = null)
    {
        Authorize(false);
        var path = RepoPath(owner, repository) + "/pulls?state=open&per_page=100";
        if (match is not null) path += "&head=" + Uri.EscapeDataString(match.Head.Contains(':') ? match.Head : owner + ":" + match.Head) + "&base=" + Uri.EscapeDataString(match.Base);
        try
        {
            var rows = new List<PullRequestInfo>();
            for (var page = 1; page <= 100; page++)
            {
                // Use fixed same-origin typed paths; never follow a server-provided URL with credentials.
                var pagePath = path + "&page=" + page;
                using var request = await RequestAsync(HttpMethod.Get, pagePath, token);
                var key = pagePath + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers.Authorization!.Parameter!)));
                if (useCache && _cache.TryGetValue(key, out var saved) && saved.ETag is not null)
                    request.Headers.TryAddWithoutValidation("If-None-Match", saved.ETag);
                Authorize(false);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                bool hasNext;
                if (response.StatusCode == HttpStatusCode.NotModified && _cache.TryGetValue(key, out saved))
                { rows.AddRange(saved.Rows); hasNext = saved.HasNext; }
                else
                {
                    await CheckResponseAsync(response, token);
                    using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    var result = data.RootElement.EnumerateArray().Select(ParsePullRequest).ToArray();
                    hasNext = HasNextPage(response);
                    _cache[key] = (response.Headers.ETag?.ToString(), result, hasNext);
                    rows.AddRange(result);
                }
                if (!hasNext) return rows.DistinctBy(row => row.Number).ToArray();
            }
            throw new GitHubException(GitHubFailure.InvalidResponse, "This repository exceeds the bounded 10,000-request listing. Open GitHub to inspect the complete list; no partial list was used to decide whether to submit.");
        }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Connectivity, "GitHub could not be reached. Hosted information remains stale; local Git work is available."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub returned unreadable pull request data."); }
    }
    private static bool HasNextPage(HttpResponseMessage response) => response.Headers.TryGetValues("Link", out var links) &&
        links.Any(link => link.Split(',').Any(part => part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)));

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, CancellationToken token)
    {
        var tokens = await tokenStore.LoadAsync(token);
        if (tokens is not null && tokens.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1) && RefreshTokensAsync is not null)
            tokens = await RefreshTokensAsync(token);
        if (tokens is null || tokens.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new GitHubException(GitHubFailure.SignInRequired, "Sign in for GitHub API features. Git transport credentials are separate.");
        var request = new HttpRequestMessage(method, "https://api.github.com" + path);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GitCompass/0.1");
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return request;
    }

    private async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        { await tokenStore.ClearAsync(token); throw new GitHubException(GitHubFailure.SignInRequired, "GitHub authorization expired or was revoked. Sign in again."); }
        var limited = response.StatusCode == HttpStatusCode.TooManyRequests || response.Headers.Contains("Retry-After") ||
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0";
        if (limited)
        {
            DateTimeOffset? retry = response.Headers.RetryAfter?.Date;
            if (response.Headers.RetryAfter?.Delta is TimeSpan delay) retry = DateTimeOffset.UtcNow + delay;
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) && long.TryParse(reset.FirstOrDefault(), out var seconds))
                retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
            throw new GitHubException(GitHubFailure.RateLimited, "GitHub asked GitCompass to wait. No write was retried automatically.", retry);
        }
        throw response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new GitHubException(GitHubFailure.Forbidden, "GitHub does not permit this action. Check App installation, organization policy, and your access."),
            HttpStatusCode.NotFound => new GitHubException(GitHubFailure.Inaccessible, "This repository or pull request is inaccessible. A private repository may still exist."),
            HttpStatusCode.UnprocessableEntity => new GitHubException(GitHubFailure.Validation, "GitHub rejected these source/target or title details. Review them before another submission."),
            _ => new GitHubException(GitHubFailure.Connectivity, "GitHub could not complete the request. Check hosted state before any repeat submission.")
        };
    }
    private void Authorize(bool write)
    {
        if (!(write ? canWrite() : canRead())) throw new UnauthorizedAccessException(write
            ? "Pull request creation requires Allow changes for this project."
            : "Hosted reads require Monitor only or Allow changes for this project.");
    }
    private static string RepoPath(string owner, string repository)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) || owner.Contains('/') || repository.Contains('/'))
            throw new GitHubException(GitHubFailure.Validation, "Choose one GitHub owner and repository.");
        return $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}";
    }
    private static PullRequestInfo ParsePullRequest(JsonElement data)
    {
        var head = data.GetProperty("head");
        var sourceRepo = head.GetProperty("repo");
        var targetRepo = data.GetProperty("base").GetProperty("repo");
        var headOwner = sourceRepo.ValueKind == JsonValueKind.Object ? sourceRepo.GetProperty("owner").GetProperty("login").GetString() : "unavailable-source";
        var targetOwner = targetRepo.GetProperty("owner").GetProperty("login").GetString();
        var sourceName = sourceRepo.ValueKind == JsonValueKind.Object && sourceRepo.TryGetProperty("name", out var source) ? source.GetString() : null;
        var targetName = targetRepo.TryGetProperty("name", out var target) ? target.GetString() : null;
        var branch = head.GetProperty("ref").GetString()!;
        var state = data.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True ? "merged" : data.GetProperty("state").GetString()!;
        return new(data.GetProperty("number").GetInt32(), data.GetProperty("title").GetString()!, state,
            data.GetProperty("html_url").GetString()!, headOwner != targetOwner || sourceName != targetName ? $"{headOwner}:{branch}" : branch,
            data.GetProperty("base").GetProperty("ref").GetString()!, data.TryGetProperty("draft", out var d) && d.GetBoolean(),
            data.TryGetProperty("mergeable", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False ? m.GetBoolean() : null, sourceName);
    }
    private static string NormalizeHead(string head, string owner) => head.Contains(':') ? head : owner + ":" + head;
}
