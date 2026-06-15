using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Security;

public sealed class MemoryAppAttestService : IAppAttestService
{
  private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

  private readonly AppAttestOptions options;
  private readonly IAppAttestVerifier verifier;
  private readonly ConcurrentDictionary<string, AppAttestChallengeResponse> challenges = new();
  private readonly ConcurrentDictionary<string, DateTimeOffset> sessions = new();

  public MemoryAppAttestService(AppAttestOptions options, IAppAttestVerifier verifier)
  {
    this.options = options;
    this.verifier = verifier;
  }

  public AppAttestChallengeResponse CreateChallenge()
  {
    var challengeId = Guid.NewGuid().ToString("D");
    var expiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime);
    var challenge = new AppAttestChallengeResponse(challengeId, Token(32), expiresAt);
    challenges[challengeId] = challenge;

    return challenge;
  }

  public AppAttestSessionResponse? CreateSession(AppAttestAttestationRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.KeyId) ||
        string.IsNullOrWhiteSpace(request.ChallengeId) ||
        string.IsNullOrWhiteSpace(request.AttestationObject) ||
        string.IsNullOrWhiteSpace(request.ClientDataHash))
    {
      return null;
    }

    if (!challenges.TryRemove(request.ChallengeId, out var challenge) ||
        challenge.ExpiresAt <= DateTimeOffset.UtcNow)
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
    sessions[sessionToken] = expiresAt;
    return new AppAttestSessionResponse(sessionToken, expiresAt);
  }

  public bool IsAuthorized(HttpContext context)
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
    return sessions.TryGetValue(token, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow;
  }

  private static string Token(int byteCount) =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
}
