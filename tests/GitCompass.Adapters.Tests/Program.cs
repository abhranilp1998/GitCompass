using System.Net;
using System.Text;
using System.Text.Json;
using GitCompass.Infrastructure.GitHub;
using GitCompass.Infrastructure.Local;

var count = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); count++; }
async Task<T> Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
var root = Path.Combine(Path.GetFullPath(".artifacts"), "adapter-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var store = new AppStateStore(root);
await store.SaveSettingsAsync(new AppSettings { Theme = "Dark", FetchIntervalMinutes = 3 });
Check((await store.LoadSettingsAsync()).Theme == "Dark", "settings persist atomically outside repository");
var quiet = new AppSettings { QuietHoursStart = 22, QuietHoursEnd = 7 };
var localOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
DateTimeOffset LocalHour(int hour) => new(2026, 10, 12, hour, 0, 0, localOffset);
Check(!MonitoringNotificationPolicy.ShouldNotify(quiet, LocalHour(23)) && !MonitoringNotificationPolicy.ShouldNotify(quiet, LocalHour(6)) && MonitoringNotificationPolicy.ShouldNotify(quiet, LocalHour(7)), "quiet hours suppress notices across midnight with an exclusive end");
Check(!MonitoringNotificationPolicy.ShouldNotify(quiet with { MuteMonitoringNotifications = true }, LocalHour(12)), "notification mute leaves monitoring permission separate");
await store.SaveSettingsAsync(quiet);
Check((await store.LoadSettingsAsync()).QuietHoursStart == 22, "quiet settings persist outside repositories");
await Throws<ArgumentException>(() => store.SaveSettingsAsync(quiet with { QuietHoursStart = 7 }));
Check(true, "invalid equal quiet hours are rejected before persistence");
await store.RememberProjectAsync(root, "Sandbox"); await store.RememberProjectAsync(root, "Sandbox updated");
Check((await store.LoadProjectsAsync()).Count == 1, "recent projects deduplicate canonical folders");
var atomicStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var atomicReads = 0;
var atomicReaders = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
{
    await atomicStart.Task;
    for (var iteration = 0; iteration < 50; iteration++)
    {
        var observed = await store.LoadSettingsAsync();
        if (observed.SchemaVersion != 1 || observed.Theme is not ("System" or "Light" or "Dark") || observed.FetchIntervalMinutes is < 1 or > 1440)
            throw new InvalidOperationException("A concurrent reader observed incomplete or invalid settings.");
        Interlocked.Increment(ref atomicReads);
        await Task.Delay(1);
    }
})).ToArray();
var atomicWriter = Task.Run(async () =>
{
    await atomicStart.Task;
    for (var iteration = 0; iteration < 40; iteration++)
        await store.SaveSettingsAsync(new AppSettings { Theme = iteration % 2 == 0 ? "Light" : "Dark", FetchIntervalMinutes = 3 });
});
atomicStart.SetResult();
await Task.WhenAll(atomicReaders.Append(atomicWriter)).WaitAsync(TimeSpan.FromSeconds(30));
Check(atomicReads == 400 && (await store.LoadSettingsAsync()).Theme == "Dark" && !Directory.EnumerateFiles(root, "settings.json.*.tmp").Any(),
    "400 concurrent settings reads observe complete snapshots during 40 atomic saves and retain the exact final theme");
await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{ broken");
var corrupt = await Throws<AppStateException>(async () => await store.LoadSettingsAsync());
Check(File.ReadAllText(corrupt.PreservedPath) == "{ broken", "corrupt settings are preserved and reported");
var tokenStore = new WindowsTokenStore(root);
var tokens = new TokenSet("ghu_disposable_test_token", "ghr_disposable_test_refresh", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(5));
await tokenStore.SaveAsync(tokens);
Check((await tokenStore.LoadAsync()) == tokens, "Windows user-bound protected tokens round-trip");
Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(root, "credentials", "github.bin"))).Contains(tokens.AccessToken), "token file excludes plaintext credentials");
var redacted = Redactor.Redact("Authorization: Bearer opaque-header-secret\nhttps://alice:password@example.com/repo?token=123\nC:\\Users\\private\\work\nghp_veryprivateCredential\n/home/alice/project");
Check(!redacted.Contains("opaque-header-secret") && !redacted.Contains("password") && !redacted.Contains("ghp_") && !redacted.Contains("/home/alice"), "diagnostics redact headers, URLs, tokens and private paths");
var bundle = new DiagnosticExporter().Prepare("Sandbox", "main", "ReadOnly", "git 2.54", null, [redacted]);
await new DiagnosticExporter().SaveAsync(bundle, root);
Check(Directory.GetDirectories(root, "GitCompass-support-*").Length == 1, "reviewed local diagnostic bundle saves without transmission");

var memory = new MemoryTokens(tokens);
var handler = new FakeHandler();
using var http = new HttpClient(handler);
bool read = true, write = false;
var client = new GitHubClient(http, memory, () => read, () => write);
var denied = await Throws<UnauthorizedAccessException>(async () => await client.CreatePullRequestAsync("owner", "repo", new("Title", "Body", "feature", "main", true)));
Check(handler.Calls == 0 && denied.Message.Contains("Allow changes"), "hosted write denied before HTTP in Monitor only");
handler.Enqueue(HttpStatusCode.OK, "[]", "\"v1\"");
handler.Enqueue(HttpStatusCode.NotModified, "");
await client.ReadPullRequestsAsync("owner", "repo"); await client.ReadPullRequestsAsync("owner", "repo");
Check(handler.LastRequestHeaders.Contains("If-None-Match") && handler.LastRequestHeaders.Contains(GitHubClient.ApiVersion), "conditional reads use pinned REST API version");
handler.Enqueue(HttpStatusCode.OK, "[" + FakeHandler.PullRequest + "]", "\"page1\"", link: "<https://api.github.com/repos/owner/paged/pulls?page=2>; rel=\"next\"");
handler.Enqueue(HttpStatusCode.OK, "[" + FakeHandler.PullRequest.Replace("\"number\":7", "\"number\":8") + "]", "\"page2\"");
Check((await client.ReadPullRequestsAsync("owner", "paged")).Count == 2 && handler.LastRequestUri.EndsWith("page=2"), "hosted list reads all pages through fixed same-origin paths");
handler.Enqueue(HttpStatusCode.NotModified, ""); handler.Enqueue(HttpStatusCode.NotModified, "");
Check((await client.ReadPullRequestsAsync("owner", "paged")).Count == 2 && handler.LastRequestHeaders.Contains("If-None-Match"), "per-page conditional cache retains pagination continuation");
handler.Enqueue(HttpStatusCode.OK, FakeHandler.PullRequest.Replace("\"head\":{", "\"merged\":true,\"mergeable_state\":\"clean\",\"head\":{\"sha\":\"abc123\","));
handler.Enqueue(HttpStatusCode.OK, "[{\"user\":{\"login\":\"reviewer\"},\"state\":\"APPROVED\"}]");
handler.Enqueue(HttpStatusCode.OK, "{\"check_runs\":[{\"name\":\"Build\",\"status\":\"completed\",\"conclusion\":\"success\"}]}");
handler.Enqueue(HttpStatusCode.OK, "{\"statuses\":[{\"context\":\"Legacy CI\",\"state\":\"pending\"}]}");
var status = await client.ReadPullRequestStatusAsync("owner", "repo", 7);
Check(status.Request.State == "merged" && status.Reviews.Single().Contains("APPROVED") && status.Checks.Count == 2 && status.Checks.Any(check => check.Contains("pending")), "PR status reads merged state, reviews, check runs and pending commit status");
handler.Enqueue(HttpStatusCode.OK, FakeHandler.PullRequest.Replace("\"head\":{", "\"head\":{\"sha\":\"abc123\","));
handler.Enqueue(HttpStatusCode.Forbidden, "{}"); handler.Enqueue(HttpStatusCode.Forbidden, "{}"); handler.Enqueue(HttpStatusCode.OK, "{\"statuses\":[]}");
var restrictedStatus = await client.ReadPullRequestStatusAsync("owner", "repo", 7);
Check(restrictedStatus.Request.Mergeable is null && restrictedStatus.Unavailable.Count == 2 && restrictedStatus.Checks.Count == 0, "restricted reviews/checks are unavailable rather than reported passing");
handler.Enqueue(HttpStatusCode.NotFound, "{}");
Check((await Throws<GitHubException>(async () => await client.ReadPullRequestsAsync("owner", "private"))).Failure == GitHubFailure.Inaccessible, "404 means inaccessible, not conclusively absent");
handler.Enqueue(HttpStatusCode.OK, "{\"object\":{\"sha\":\"abc123\"}}");
Check(await client.VerifyPublishedCommitAsync("fork", "renamed-repo", "feature/report", "abc123"), "PR publication verification binds explicit fork repository, branch and commit");
handler.Enqueue(HttpStatusCode.OK, "{\"object\":{\"sha\":\"new-server-commit\"}}");
Check(!await client.VerifyPublishedCommitAsync("fork", "renamed-repo", "feature/report", "abc123"), "a changed published source invalidates PR preview evidence");
handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfter: "12");
var limited = await Throws<GitHubException>(async () => await client.ReadPullRequestsAsync("owner", "repo"));
Check(limited.Failure == GitHubFailure.RateLimited && limited.RetryAfter.HasValue, "rate limit preserves retry deadline without automatic writes");
write = true;
handler.Enqueue(HttpStatusCode.OK, "[]"); handler.EnqueueFailure(new HttpRequestException("lost response"));
var draft = new PullRequestDraft("Title", "Body", "fork:feature", "main", true);
Check((await Throws<GitHubException>(async () => await client.CreatePullRequestAsync("owner", "repo", draft))).Failure == GitHubFailure.OutcomeUnknown, "lost PR response reports uncertain outcome");
handler.Enqueue(HttpStatusCode.OK, "[" + FakeHandler.PullRequest + "]");
var existing = await client.CreatePullRequestAsync("owner", "repo", draft);
Check(existing.Number == 7 && existing.Mergeable is null && handler.Posts == 1, "uncertain PR submission reconciles matching fork source and pending mergeability without duplicate");
handler.Enqueue(HttpStatusCode.OK, "[]"); handler.Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
var uncertainDraft = draft with { Head = "fork:other" };
Check((await Throws<GitHubException>(async () => await client.CreatePullRequestAsync("owner", "repo", uncertainDraft))).Failure == GitHubFailure.OutcomeUnknown, "server error on submission preserves uncertain outcome");
handler.Enqueue(HttpStatusCode.OK, "[]");
Check((await Throws<GitHubException>(async () => await client.CreatePullRequestAsync("owner", "repo", uncertainDraft))).Failure == GitHubFailure.OutcomeUnknown && handler.Posts == 2, "empty reconciliation after uncertainty does not blindly resubmit");
read = false;
var callsBefore = handler.Calls;
await Throws<UnauthorizedAccessException>(async () => await client.ReadPullRequestsAsync("owner", "repo"));
Check(handler.Calls == callsBefore, "hosted reads recheck revoked permission");
read = true;
handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
Check((await Throws<GitHubException>(async () => await client.ReadPullRequestsAsync("owner", "repo"))).Failure == GitHubFailure.SignInRequired && memory.Value is null, "revoked token clears protected auth state");

var authorizationHandler = new FakeHandler();
using var authHttp = new HttpClient(authorizationHandler);
authorizationHandler.Enqueue(HttpStatusCode.OK, "{\"device_code\":\"test\",\"user_code\":\"ABCD-EFGH\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}");
authorizationHandler.Enqueue(HttpStatusCode.OK, "{\"error\":\"authorization_pending\"}");
authorizationHandler.Enqueue(HttpStatusCode.OK, "{\"error\":\"slow_down\"}");
authorizationHandler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"ghu_test\",\"refresh_token\":\"ghr_test\",\"expires_in\":100,\"refresh_token_expires_in\":200}");
var intervals = new List<TimeSpan>();
var auth = new GitHubDeviceAuth(authHttp, memory, "public-test-id", (delay, _) => { intervals.Add(delay); return Task.CompletedTask; });
var device = await auth.BeginAsync(); await auth.CompleteAsync(device);
Check(intervals.Select(i => i.TotalSeconds).SequenceEqual([5d, 5d, 10d]), "device polling honors interval and slow-down");
Check(!authorizationHandler.RequestBodies.Any(body => body.Contains("client_secret")), "device flow has no embedded client secret");
memory.Value = tokens with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
authorizationHandler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"ghu_rotated\",\"refresh_token\":\"ghr_rotated\",\"expires_in\":500,\"refresh_token_expires_in\":600}");
await auth.RefreshAsync();
Check(memory.Value!.AccessToken == "ghu_rotated" && !authorizationHandler.RequestBodies.Last().Contains("client_secret"), "expired device token rotates refresh atomically without secret");
await tokenStore.ClearAsync();
Console.WriteLine($"Adapters: {count} checks passed. Fixtures: {root}");

sealed class MemoryTokens(TokenSet? initial) : ITokenStore
{
    public TokenSet? Value { get; set; } = initial;
    public Task<TokenSet?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
    public Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default) { Value = tokens; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken = default) { Value = null; return Task.CompletedTask; }
}
sealed class FakeHandler : HttpMessageHandler
{
    public const string PullRequest = "{\"number\":7,\"title\":\"Title\",\"state\":\"open\",\"html_url\":\"https://github.com/owner/repo/pull/7\",\"draft\":true,\"mergeable\":null,\"head\":{\"ref\":\"feature\",\"repo\":{\"owner\":{\"login\":\"fork\"}}},\"base\":{\"ref\":\"main\",\"repo\":{\"owner\":{\"login\":\"owner\"}}}}";
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public int Calls { get; private set; }
    public int Posts { get; private set; }
    public string LastRequestHeaders { get; private set; } = "";
    public string LastRequestUri { get; private set; } = "";
    public List<string> RequestBodies { get; } = [];
    public void Enqueue(HttpStatusCode code, string json, string? etag = null, string? retryAfter = null, string? link = null) => _responses.Enqueue(() =>
    { var response = new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") }; if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag); if (retryAfter is not null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter); if (link is not null) response.Headers.TryAddWithoutValidation("Link", link); return response; });
    public void EnqueueFailure(Exception error) => _responses.Enqueue(() => throw error);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; if (request.Method == HttpMethod.Post && request.RequestUri!.Host == "api.github.com") Posts++;
        LastRequestHeaders = request.Headers.ToString();
        LastRequestUri = request.RequestUri!.AbsoluteUri;
        if (request.Content is not null) RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        return _responses.Dequeue()();
    }
}
