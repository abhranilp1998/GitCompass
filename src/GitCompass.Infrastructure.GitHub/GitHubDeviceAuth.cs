using System.Text.Json;

namespace GitCompass.Infrastructure.GitHub;

/// <summary>GitHub App device flow. No password or client secret is collected.</summary>
public sealed class GitHubDeviceAuth
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly string _clientId;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _rotation = new(1, 1);

    public GitHubDeviceAuth(HttpClient http, ITokenStore store, string clientId,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _http = http; _store = store; _clientId = clientId;
        _delay = delay ?? ((time, token) => Task.Delay(time, token));
    }

    public async Task<DeviceAuthorization> BeginAsync(CancellationToken cancellationToken = default)
    {
        using var json = await PostAsync("https://github.com/login/device/code", new()
        { ["client_id"] = _clientId, ["scope"] = "offline_access" }, cancellationToken);
        var data = json.RootElement;
        CheckError(data);
        var uri = new Uri(Required(data, "verification_uri"));
        if (uri.Scheme != "https" || uri.Host != "github.com")
            throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub returned an unexpected sign-in address.");
        return new(Required(data, "user_code"), uri, Required(data, "device_code"),
            DateTimeOffset.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32()),
            Math.Max(1, data.GetProperty("interval").GetInt32()));
    }

    public async Task<TokenSet> CompleteAsync(DeviceAuthorization authorization, CancellationToken cancellationToken = default)
    {
        var interval = authorization.IntervalSeconds;
        while (DateTimeOffset.UtcNow < authorization.ExpiresAt)
        {
            await _delay(TimeSpan.FromSeconds(interval), cancellationToken);
            if (DateTimeOffset.UtcNow >= authorization.ExpiresAt) break;
            using var json = await PostAsync("https://github.com/login/oauth/access_token", new()
            {
                ["client_id"] = _clientId, ["device_code"] = authorization.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, cancellationToken);
            var data = json.RootElement;
            if (data.TryGetProperty("error", out var error))
            {
                if (error.GetString() == "authorization_pending") continue;
                if (error.GetString() == "slow_down")
                { interval = Math.Max(interval + 5, OptionalInt(data, "interval") ?? 0); continue; }
                CheckError(data);
            }
            var tokens = ReadTokens(data);
            await _store.SaveAsync(tokens, cancellationToken);
            return tokens;
        }
        throw new GitHubException(GitHubFailure.SignInRequired, "The sign-in code expired. Start sign-in again.");
    }

    public async Task<TokenSet> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _rotation.WaitAsync(cancellationToken);
        try
        {
            var tokens = await _store.LoadAsync(cancellationToken);
            if (tokens is null) throw new GitHubException(GitHubFailure.SignInRequired, "Sign in to GitHub API access first.");
            if (tokens.ExpiresAt is null || tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return tokens;
            if (tokens.RefreshToken is null || tokens.RefreshExpiresAt <= DateTimeOffset.UtcNow)
            { await _store.ClearAsync(cancellationToken); throw new GitHubException(GitHubFailure.SignInRequired, "GitHub sign-in expired. Authorize again."); }
            using var json = await PostAsync("https://github.com/login/oauth/access_token", new()
            {
                ["client_id"] = _clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.RefreshToken
            }, cancellationToken);
            if (json.RootElement.TryGetProperty("error", out _))
            { await _store.ClearAsync(cancellationToken); CheckError(json.RootElement); }
            var rotated = ReadTokens(json.RootElement);
            await _store.SaveAsync(rotated, cancellationToken);
            return rotated;
        }
        finally { _rotation.Release(); }
    }

    private async Task<JsonDocument> PostAsync(string uri, Dictionary<string, string> fields, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("GitCompass/0.1");
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new GitHubException(GitHubFailure.SignInRequired, "GitHub could not complete this authorization. Check the App registration and try user-initiated sign-in.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
        catch (HttpRequestException) { throw new GitHubException(GitHubFailure.Connectivity, "GitHub sign-in could not reach the server. Your local Git work remains available."); }
        catch (JsonException) { throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub returned an unreadable authorization response."); }
    }
    private static string Required(JsonElement data, string key) => data.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new GitHubException(GitHubFailure.InvalidResponse, "GitHub authorization response is incomplete.");
    private static int? OptionalInt(JsonElement data, string key) => data.TryGetProperty(key, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static TokenSet ReadTokens(JsonElement data) => new(Required(data, "access_token"), data.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
        OptionalInt(data, "expires_in") is int expiry ? DateTimeOffset.UtcNow.AddSeconds(expiry) : null,
        OptionalInt(data, "refresh_token_expires_in") is int refreshExpiry ? DateTimeOffset.UtcNow.AddSeconds(refreshExpiry) : null);
    private static void CheckError(JsonElement data)
    {
        if (!data.TryGetProperty("error", out var error)) return;
        var message = error.GetString() switch
        {
            "access_denied" => "GitHub authorization was declined.",
            "expired_token" => "The authorization code expired. Start sign-in again.",
            "incorrect_client_credentials" => "Check the registered GitHub App public client ID.",
            _ => "GitHub authorization cannot continue. Start user-initiated sign-in again."
        };
        throw new GitHubException(GitHubFailure.SignInRequired, message);
    }
}
