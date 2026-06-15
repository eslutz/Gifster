using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;
using Gifster.Backend.Queueing;
using Gifster.Backend.Security;

namespace Gifster.Backend.Tests;

public sealed class AppAttestRouteTests
{
  [Fact]
  public async Task GenerationRoutesRequireAppAttestWhenConfigured()
  {
    await using var app = GifsterBackendApp.Create(
      args: ["--GIFSTER_APP_ATTEST_REQUIRED=true"],
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: new NoopGenerationJobDispatcher()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };

    var response = await client.PostAsync("/v1/generations", GenerationRequestContent());

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task DemoAppAttestSessionAllowsGenerationRequestWhenDemoBypassIsEnabled()
  {
    await using var app = GifsterBackendApp.Create(
      args: [
        "--GIFSTER_APP_ATTEST_REQUIRED=true",
        "--GIFSTER_APP_ATTEST_DEMO_BYPASS=true"
      ],
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: new NoopGenerationJobDispatcher()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var challengeResponse = await client.PostAsync("/v1/app-attest/challenges", null);
    var challenge = JsonSerializer.Deserialize<AppAttestChallengeResponse>(
      await challengeResponse.Content.ReadAsStringAsync(),
      JsonOptions()
    );
    Assert.NotNull(challenge);
    var attestationJson = JsonSerializer.Serialize(
      new AppAttestAttestationRequest(
        "test-key-id",
        challenge.ChallengeId,
        "test-attestation-object",
        "test-client-data-hash"
      ),
      JsonOptions()
    );
    var attestationResponse = await client.PostAsync(
      "/v1/app-attest/attestations",
      new StringContent(attestationJson, Encoding.UTF8, "application/json")
    );
    var session = JsonSerializer.Deserialize<AppAttestSessionResponse>(
      await attestationResponse.Content.ReadAsStringAsync(),
      JsonOptions()
    );
    Assert.NotNull(session);

    var request = new HttpRequestMessage(HttpMethod.Post, "/v1/generations")
    {
      Content = GenerationRequestContent()
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);

    var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task ProductionAppAttestRejectsDemoAttestationWhenDemoBypassIsDisabled()
  {
    await using var app = GifsterBackendApp.Create(
      args: ["--GIFSTER_APP_ATTEST_REQUIRED=true"],
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: new NoopGenerationJobDispatcher()
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var challengeResponse = await client.PostAsync("/v1/app-attest/challenges", null);
    var challenge = JsonSerializer.Deserialize<AppAttestChallengeResponse>(
      await challengeResponse.Content.ReadAsStringAsync(),
      JsonOptions()
    );
    Assert.NotNull(challenge);
    var attestationJson = JsonSerializer.Serialize(
      new AppAttestAttestationRequest(
        "test-key-id",
        challenge.ChallengeId,
        "test-attestation-object",
        "test-client-data-hash"
      ),
      JsonOptions()
    );

    var attestationResponse = await client.PostAsync(
      "/v1/app-attest/attestations",
      new StringContent(attestationJson, Encoding.UTF8, "application/json")
    );

    Assert.Equal(HttpStatusCode.Unauthorized, attestationResponse.StatusCode);
  }

  [Fact]
  public async Task ProductionAppAttestSessionAllowsGenerationRequestWhenVerifierAccepts()
  {
    var verifier = new AcceptingAppAttestVerifier();
    await using var app = GifsterBackendApp.Create(
      args: ["--GIFSTER_APP_ATTEST_REQUIRED=true"],
      provider: new FakeFrameSequenceProvider(),
      jobStore: new MemoryJobStore(),
      jobDispatcher: new NoopGenerationJobDispatcher(),
      appAttestVerifier: verifier
    );
    var baseAddress = await BackendRouteTestHost.StartAsync(app);
    using var client = new HttpClient { BaseAddress = baseAddress };
    var challengeResponse = await client.PostAsync("/v1/app-attest/challenges", null);
    var challenge = JsonSerializer.Deserialize<AppAttestChallengeResponse>(
      await challengeResponse.Content.ReadAsStringAsync(),
      JsonOptions()
    );
    Assert.NotNull(challenge);
    var attestationJson = JsonSerializer.Serialize(
      new AppAttestAttestationRequest(
        "test-key-id",
        challenge.ChallengeId,
        "verified-attestation-object",
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Challenge)))
      ),
      JsonOptions()
    );
    var attestationResponse = await client.PostAsync(
      "/v1/app-attest/attestations",
      new StringContent(attestationJson, Encoding.UTF8, "application/json")
    );
    var session = JsonSerializer.Deserialize<AppAttestSessionResponse>(
      await attestationResponse.Content.ReadAsStringAsync(),
      JsonOptions()
    );
    Assert.NotNull(session);

    var request = new HttpRequestMessage(HttpMethod.Post, "/v1/generations")
    {
      Content = GenerationRequestContent()
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);

    var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Equal("test-key-id", verifier.VerifiedKeyId);
  }

  private static StringContent GenerationRequestContent()
  {
    var requestJson = JsonSerializer.Serialize(TestGenerationRequests.Valid(), JsonOptions());
    return new StringContent(requestJson, Encoding.UTF8, "application/json");
  }

  private static JsonSerializerOptions JsonOptions() =>
    new(JsonSerializerDefaults.Web);
}

internal sealed class AcceptingAppAttestVerifier : IAppAttestVerifier
{
  public string? VerifiedKeyId { get; private set; }

  public AppAttestVerificationResult? Verify(
    AppAttestAttestationRequest request,
    AppAttestChallengeResponse challenge
  )
  {
    VerifiedKeyId = request.KeyId;
    return new AppAttestVerificationResult(request.KeyId);
  }
}
