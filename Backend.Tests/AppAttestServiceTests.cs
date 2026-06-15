using System.Net.Http.Headers;
using Gifster.Backend.Security;
using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Tests;

public sealed class AppAttestServiceTests
{
  [Fact]
  public async Task SharedStateStoreAuthorizesAcrossServiceInstances()
  {
    var stateStore = new MemoryAppAttestStateStore();
    var firstReplica = CreateService(stateStore);
    var secondReplica = CreateService(stateStore);
    var challenge = await firstReplica.CreateChallengeAsync(CancellationToken.None);
    var session = await secondReplica.CreateSessionAsync(
      new AppAttestAttestationRequest(
        "test-key-id",
        challenge.ChallengeId,
        "test-attestation-object",
        "test-client-data-hash"
      ),
      CancellationToken.None
    );
    Assert.NotNull(session);
    var context = new DefaultHttpContext();
    context.Request.Headers.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      session.SessionToken
    ).ToString();

    var authorized = await firstReplica.IsAuthorizedAsync(context, CancellationToken.None);

    Assert.True(authorized);
  }

  [Fact]
  public async Task ChallengeCanOnlyBeConsumedOnce()
  {
    var stateStore = new MemoryAppAttestStateStore();
    var service = CreateService(stateStore);
    var challenge = await service.CreateChallengeAsync(CancellationToken.None);
    var request = new AppAttestAttestationRequest(
      "test-key-id",
      challenge.ChallengeId,
      "test-attestation-object",
      "test-client-data-hash"
    );

    var firstSession = await service.CreateSessionAsync(request, CancellationToken.None);
    var secondSession = await service.CreateSessionAsync(request, CancellationToken.None);

    Assert.NotNull(firstSession);
    Assert.Null(secondSession);
  }

  private static AppAttestService CreateService(IAppAttestStateStore stateStore) =>
    new(
      new AppAttestOptions(
        Required: true,
        DemoBypassEnabled: true,
        AppIdentifier: null,
        RootCertificatePem: null
      ),
      new UnavailableAppAttestVerifier(),
      stateStore
    );
}
