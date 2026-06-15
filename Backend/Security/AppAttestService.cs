using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Security;

public sealed class AppAttestService : IAppAttestService
{
  private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

  private readonly AppAttestOptions options;
  private readonly IAppAttestVerifier verifier;
  private readonly IAppAttestStateStore stateStore;

  public AppAttestService(
    AppAttestOptions options,
    IAppAttestVerifier verifier,
    IAppAttestStateStore stateStore
  )
  {
    this.options = options;
    this.verifier = verifier;
    this.stateStore = stateStore;
  }

  public async Task<AppAttestChallengeResponse> CreateChallengeAsync(CancellationToken cancellationToken)
  {
    var challengeId = Guid.NewGuid().ToString("D");
    var expiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime);
    var challenge = new AppAttestChallengeResponse(challengeId, Token(32), expiresAt);
    await stateStore.SaveChallengeAsync(challenge, cancellationToken).ConfigureAwait(false);

    return challenge;
  }

  public async Task<AppAttestSessionResponse?> CreateSessionAsync(
    AppAttestAttestationRequest request,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(request.KeyId) ||
        string.IsNullOrWhiteSpace(request.ChallengeId) ||
        string.IsNullOrWhiteSpace(request.AttestationObject) ||
        string.IsNullOrWhiteSpace(request.ClientDataHash))
    {
      return null;
    }

    var challenge = await stateStore.ConsumeChallengeAsync(request.ChallengeId, cancellationToken)
      .ConfigureAwait(false);
    if (challenge is null || challenge.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      return null;
    }

    if (!options.DemoBypassEnabled &&
        verifier.Verify(request, challenge) is null)
    {
      return null;
    }

    var sessionToken = Token(48);
    var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
    await stateStore.SaveSessionAsync(sessionToken, expiresAt, cancellationToken).ConfigureAwait(false);

    return new AppAttestSessionResponse(sessionToken, expiresAt);
  }

  public async Task<bool> IsAuthorizedAsync(HttpContext context, CancellationToken cancellationToken)
  {
    if (!options.Required)
    {
      return true;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    const string bearerPrefix = "Bearer ";
    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var token = authorization[bearerPrefix.Length..].Trim();
    var expiresAt = await stateStore.GetSessionExpiresAtAsync(token, cancellationToken).ConfigureAwait(false);
    return expiresAt is not null && expiresAt > DateTimeOffset.UtcNow;
  }

  private static string Token(int byteCount) =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
}
