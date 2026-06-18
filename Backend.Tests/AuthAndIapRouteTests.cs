using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GifForge.Backend.Jobs;
using GifForge.Backend.Providers;
using GifForge.Backend.Queueing;

namespace GifForge.Backend.Tests;

public sealed class AuthAndIapRouteTests
{
  [Fact]
  public async Task AppleAuthCreatesBackendSessionAndAuthenticatedProfile()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var auth = await SignInAsync(client);

    Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
    Assert.False(string.IsNullOrWhiteSpace(auth.UserId));
    Assert.False(string.IsNullOrWhiteSpace(auth.AppAccountToken));

    using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
    profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    var profileResponse = await client.SendAsync(profileRequest);

    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
    using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
    Assert.Equal(auth.UserId, profile.RootElement.GetProperty("userId").GetString());
    Assert.Equal(auth.AppAccountToken, profile.RootElement.GetProperty("appAccountToken").GetString());
  }

  [Fact]
  public async Task StoreKitConsumableTransactionGrantsCreditsIdempotently()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);

    using var productsRequest = AuthorizedRequest(HttpMethod.Get, "/v1/iap/products", auth.AccessToken);
    var productsResponse = await client.SendAsync(productsRequest);
    Assert.Equal(HttpStatusCode.OK, productsResponse.StatusCode);
    var productsBody = await productsResponse.Content.ReadAsStringAsync();
    Assert.Contains("dev.ericslutz.gifforge.credits.10", productsBody);

    var transactionBody = JsonSerializer.Serialize(new
    {
      productId = "dev.ericslutz.gifforge.credits.10",
      signedTransaction = $"demo:transaction-1:dev.ericslutz.gifforge.credits.10:{auth.AppAccountToken}"
    }, JsonOptions());

    using var firstTransaction = AuthorizedRequest(
      HttpMethod.Post,
      "/v1/iap/transactions",
      auth.AccessToken,
      transactionBody
    );
    var firstResponse = await client.SendAsync(firstTransaction);

    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
    Assert.Equal(10, first.RootElement.GetProperty("grantedCredits").GetInt32());
    Assert.Equal(10, first.RootElement.GetProperty("availableCredits").GetInt32());

    using var duplicateTransaction = AuthorizedRequest(
      HttpMethod.Post,
      "/v1/iap/transactions",
      auth.AccessToken,
      transactionBody
    );
    var duplicateResponse = await client.SendAsync(duplicateTransaction);

    Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
    using var duplicate = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
    Assert.True(duplicate.RootElement.GetProperty("alreadyProcessed").GetBoolean());
    Assert.Equal(10, duplicate.RootElement.GetProperty("availableCredits").GetInt32());
  }

  [Fact]
  public async Task StoreKitTransactionRejectsProductIdMismatch()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);
    var transactionBody = JsonSerializer.Serialize(new
    {
      productId = "dev.ericslutz.gifforge.credits.10",
      signedTransaction = $"demo:transaction-mismatch:dev.ericslutz.gifforge.credits.25:{auth.AppAccountToken}"
    }, JsonOptions());

    using var transaction = AuthorizedRequest(
      HttpMethod.Post,
      "/v1/iap/transactions",
      auth.AccessToken,
      transactionBody
    );
    var response = await client.SendAsync(transaction);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task StoreKitVerificationFailsClosedWithoutPinnedAppleRoot()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs("--GIFFORGE_IAP_DEMO_BYPASS=false"),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);
    var transactionBody = JsonSerializer.Serialize(new
    {
      productId = "dev.ericslutz.gifforge.credits.10",
      signedTransaction = $"demo:transaction-unpinned:dev.ericslutz.gifforge.credits.10:{auth.AppAccountToken}"
    }, JsonOptions());

    using var transaction = AuthorizedRequest(
      HttpMethod.Post,
      "/v1/iap/transactions",
      auth.AccessToken,
      transactionBody
    );
    var response = await client.SendAsync(transaction);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task RefreshTokenReuseRevokesRotatedFamily()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);
    var refreshed = await RefreshAsync(client, auth.RefreshToken);

    var reusedOldToken = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken = auth.RefreshToken })
    );
    Assert.Equal(HttpStatusCode.Unauthorized, reusedOldToken.StatusCode);

    var rotatedFamilyToken = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken = refreshed.RefreshToken })
    );
    Assert.Equal(HttpStatusCode.Unauthorized, rotatedFamilyToken.StatusCode);
  }

  [Fact]
  public async Task SignInWithAppleNotificationRejectsInvalidPayload()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.PostAsync(
      "/v1/apple/sign-in-server-notifications",
      JsonBody(new { payload = "not-a-valid-notification" })
    );

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Theory]
  [InlineData("consent-revoked")]
  [InlineData("account-delete")]
  [InlineData("account-deleted")]
  public async Task SignInWithAppleDeletionNotificationRevokesUserSessions(string eventType)
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client, "demo.apple-delete-subject");

    var notification = await client.PostAsync(
      "/v1/apple/sign-in-server-notifications",
      JsonBody(new { payload = $"demo:{eventType}:demo.apple-delete-subject" })
    );

    Assert.Equal(HttpStatusCode.OK, notification.StatusCode);

    using var profileRequest = AuthorizedRequest(HttpMethod.Get, "/v1/me", auth.AccessToken);
    var profileResponse = await client.SendAsync(profileRequest);
    Assert.Equal(HttpStatusCode.Unauthorized, profileResponse.StatusCode);

    var refreshResponse = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken = auth.RefreshToken })
    );
    Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
  }

  [Fact]
  public async Task SignInWithAppleEmailNotificationDoesNotRevokeSession()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client, "demo.apple-email-subject");

    var notification = await client.PostAsync(
      "/v1/apple/sign-in-server-notifications",
      JsonBody(new { payload = "demo:email-disabled:demo.apple-email-subject:relay@example.com" })
    );

    Assert.Equal(HttpStatusCode.OK, notification.StatusCode);

    using var profileRequest = AuthorizedRequest(HttpMethod.Get, "/v1/me", auth.AccessToken);
    var profileResponse = await client.SendAsync(profileRequest);
    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
  }

  [Fact]
  public async Task SignInWithAppleDeletedUserCanSignInAgainWithSameSubject()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    _ = await SignInAsync(client, "demo.apple-relink-subject");

    var notification = await client.PostAsync(
      "/v1/apple/sign-in-server-notifications",
      JsonBody(new { payload = "demo:consent-revoked:demo.apple-relink-subject" })
    );
    Assert.Equal(HttpStatusCode.OK, notification.StatusCode);

    var relinked = await SignInAsync(client, "demo.apple-relink-subject");

    using var profileRequest = AuthorizedRequest(HttpMethod.Get, "/v1/me", relinked.AccessToken);
    var profileResponse = await client.SendAsync(profileRequest);
    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
  }

  [Fact]
  public async Task ConcurrentRefreshOnlyIssuesOneReplacementAndRevokesFamily()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);

    var refreshes = await Task.WhenAll(
      PostRefreshAsync(client, auth.RefreshToken),
      PostRefreshAsync(client, auth.RefreshToken)
    );

    var success = Assert.Single(refreshes, item => item.Response.StatusCode == HttpStatusCode.OK);
    Assert.Single(refreshes, item => item.Response.StatusCode == HttpStatusCode.Unauthorized);
    var replacement = AuthFromJson(await success.Response.Content.ReadAsStringAsync());

    var replacementAfterFamilyRevocation = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken = replacement.RefreshToken })
    );
    Assert.Equal(HttpStatusCode.Unauthorized, replacementAfterFamilyRevocation.StatusCode);
  }

  [Fact]
  public async Task AuthEndpointIsRateLimited()
  {
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(
        "--GIFFORGE_RATE_LIMIT_AUTH_MAX=1",
        "--GIFFORGE_RATE_LIMIT_WINDOW_SECONDS=60"
      ),
      provider: new FakeFrameSequenceProvider()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    _ = await SignInAsync(client);

    var response = await client.PostAsync(
      "/v1/auth/apple",
      JsonBody(new
      {
        identityToken = "demo.rate-limit",
        nonce = "test-nonce"
      })
    );

    Assert.Equal((HttpStatusCode)429, response.StatusCode);
    Assert.True(response.Headers.RetryAfter?.Delta?.TotalSeconds > 0);
  }

  [Fact]
  public async Task StatusPollingDoesNotConsumeGenerationSubmissionQuota()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(
        "--GIFFORGE_RATE_LIMIT_GENERATION_MAX=1",
        "--GIFFORGE_RATE_LIMIT_GENERATION_STATUS_MAX=5",
        "--GIFFORGE_RATE_LIMIT_WINDOW_SECONDS=60"
      ),
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var auth = await SignInAsync(client);
    await GrantTenCreditsAsync(client, auth);
    var generationJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var generationRequest = AuthorizedRequest(HttpMethod.Post, "/v1/generations", auth.AccessToken, generationJson);
    var generationResponse = await client.SendAsync(generationRequest);
    Assert.Equal(HttpStatusCode.Accepted, generationResponse.StatusCode);
    var jobId = Assert.Single(dispatcher.JobIds);

    for (var index = 0; index < 3; index++)
    {
      using var statusRequest = AuthorizedRequest(HttpMethod.Get, $"/v1/generations/{jobId}", auth.AccessToken);
      var statusResponse = await client.SendAsync(statusRequest);
      Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
    }

    using var secondGenerationRequest = AuthorizedRequest(HttpMethod.Post, "/v1/generations", auth.AccessToken, generationJson);
    var secondGenerationResponse = await client.SendAsync(secondGenerationRequest);
    Assert.Equal((HttpStatusCode)429, secondGenerationResponse.StatusCode);
  }

  [Fact]
  public async Task GenerationRequiresBackendAuthAndReservesCredit()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var generationJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());

    var unauthenticated = await client.PostAsync(
      "/v1/generations",
      new StringContent(generationJson, Encoding.UTF8, "application/json")
    );
    Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

    var auth = await SignInAsync(client);
    using var noCreditsRequest = AuthorizedRequest(HttpMethod.Post, "/v1/generations", auth.AccessToken, generationJson);
    var noCredits = await client.SendAsync(noCreditsRequest);
    Assert.Equal(HttpStatusCode.PaymentRequired, noCredits.StatusCode);

    await GrantTenCreditsAsync(client, auth);

    using var generationRequest = AuthorizedRequest(HttpMethod.Post, "/v1/generations", auth.AccessToken, generationJson);
    var generationResponse = await client.SendAsync(generationRequest);

    Assert.Equal(HttpStatusCode.Accepted, generationResponse.StatusCode);
    var jobId = Assert.Single(dispatcher.JobIds);
    Assert.False(string.IsNullOrWhiteSpace(jobId));

    using var creditsRequest = AuthorizedRequest(HttpMethod.Get, "/v1/me/credits", auth.AccessToken);
    var creditsResponse = await client.SendAsync(creditsRequest);
    Assert.Equal(HttpStatusCode.OK, creditsResponse.StatusCode);
    using var credits = JsonDocument.Parse(await creditsResponse.Content.ReadAsStringAsync());
    Assert.Equal(10, credits.RootElement.GetProperty("grantedCredits").GetInt32());
    Assert.Equal(0, credits.RootElement.GetProperty("capturedDebits").GetInt32());
    Assert.Equal(1, credits.RootElement.GetProperty("reservedCredits").GetInt32());
    Assert.Equal(9, credits.RootElement.GetProperty("availableCredits").GetInt32());
  }

  [Fact]
  public async Task GenerationStatusRequiresOwningUser()
  {
    var dispatcher = new RecordingGenerationJobDispatcher();
    var jobStore = new MemoryJobStore();
    await using var app = GifForgeBackendApp.Create(
      args: AuthArgs(),
      provider: new FakeFrameSequenceProvider(),
      jobStore: jobStore,
      jobDispatcher: dispatcher
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var owner = await SignInAsync(client, "demo.owner");
    var otherUser = await SignInAsync(client, "demo.other");
    await GrantTenCreditsAsync(client, owner, "transaction-owner");
    var generationJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    using var generationRequest = AuthorizedRequest(HttpMethod.Post, "/v1/generations", owner.AccessToken, generationJson);
    var generationResponse = await client.SendAsync(generationRequest);
    Assert.Equal(HttpStatusCode.Accepted, generationResponse.StatusCode);
    var jobId = Assert.Single(dispatcher.JobIds);

    using var otherStatus = AuthorizedRequest(HttpMethod.Get, $"/v1/generations/{jobId}", otherUser.AccessToken);
    var otherStatusResponse = await client.SendAsync(otherStatus);

    Assert.Equal(HttpStatusCode.Forbidden, otherStatusResponse.StatusCode);
  }

  private static string[] AuthArgs(params string[] extraArgs) =>
  [
    "--GIFFORGE_AUTH_REQUIRED=true",
    "--GIFFORGE_AUTH_DEMO_BYPASS=true",
    "--GIFFORGE_IAP_DEMO_BYPASS=true",
    ..extraArgs
  ];

  private static async Task<AuthResponse> SignInAsync(HttpClient client, string identityToken = "demo.apple-subject")
  {
    var requestJson = JsonSerializer.Serialize(new
    {
      identityToken,
      nonce = "test-nonce"
    }, JsonOptions());

    var response = await client.PostAsync(
      "/v1/auth/apple",
      new StringContent(requestJson, Encoding.UTF8, "application/json")
    );
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return new AuthResponse(
      document.RootElement.GetProperty("userId").GetString() ?? string.Empty,
      document.RootElement.GetProperty("appAccountToken").GetString() ?? string.Empty,
      document.RootElement.GetProperty("accessToken").GetString() ?? string.Empty,
      document.RootElement.GetProperty("refreshToken").GetString() ?? string.Empty
    );
  }

  private static async Task<AuthResponse> RefreshAsync(HttpClient client, string refreshToken)
  {
    var response = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken })
    );
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return AuthFromJson(await response.Content.ReadAsStringAsync());
  }

  private static async Task<(HttpResponseMessage Response, string RefreshToken)> PostRefreshAsync(
    HttpClient client,
    string refreshToken
  )
  {
    var response = await client.PostAsync(
      "/v1/auth/refresh",
      JsonBody(new { refreshToken })
    );
    return (response, refreshToken);
  }

  private static async Task GrantTenCreditsAsync(
    HttpClient client,
    AuthResponse auth,
    string transactionId = "transaction-credits"
  )
  {
    var transactionBody = JsonSerializer.Serialize(new
    {
      productId = "dev.ericslutz.gifforge.credits.10",
      signedTransaction = $"demo:{transactionId}:dev.ericslutz.gifforge.credits.10:{auth.AppAccountToken}"
    }, JsonOptions());
    using var transactionRequest = AuthorizedRequest(
      HttpMethod.Post,
      "/v1/iap/transactions",
      auth.AccessToken,
      transactionBody
    );
    var response = await client.SendAsync(transactionRequest);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private static HttpRequestMessage AuthorizedRequest(
    HttpMethod method,
    string url,
    string accessToken,
    string? body = null
  )
  {
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    if (body is not null)
    {
      request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    return request;
  }

  private static JsonSerializerOptions JsonOptions() =>
    new(JsonSerializerDefaults.Web);

  private static StringContent JsonBody<T>(T body) =>
    new(JsonSerializer.Serialize(body, JsonOptions()), Encoding.UTF8, "application/json");

  private static AuthResponse AuthFromJson(string json)
  {
    using var document = JsonDocument.Parse(json);
    return new AuthResponse(
      document.RootElement.GetProperty("userId").GetString() ?? string.Empty,
      document.RootElement.GetProperty("appAccountToken").GetString() ?? string.Empty,
      document.RootElement.GetProperty("accessToken").GetString() ?? string.Empty,
      document.RootElement.GetProperty("refreshToken").GetString() ?? string.Empty
    );
  }

  private sealed record AuthResponse(
    string UserId,
    string AppAccountToken,
    string AccessToken,
    string RefreshToken
  );
}
