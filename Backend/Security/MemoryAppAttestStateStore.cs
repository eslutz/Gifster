using System.Collections.Concurrent;

namespace Gifster.Backend.Security;

public sealed class MemoryAppAttestStateStore : IAppAttestStateStore
{
  private readonly ConcurrentDictionary<string, AppAttestChallengeResponse> challenges = new();
  private readonly ConcurrentDictionary<string, DateTimeOffset> sessions = new();

  public Task SaveChallengeAsync(AppAttestChallengeResponse challenge, CancellationToken cancellationToken)
  {
    challenges[challenge.ChallengeId] = challenge;
    return Task.CompletedTask;
  }

  public Task<AppAttestChallengeResponse?> ConsumeChallengeAsync(
    string challengeId,
    CancellationToken cancellationToken
  )
  {
    challenges.TryRemove(challengeId, out var challenge);
    return Task.FromResult(challenge);
  }

  public Task SaveSessionAsync(
    string sessionToken,
    DateTimeOffset expiresAt,
    CancellationToken cancellationToken
  )
  {
    sessions[sessionToken] = expiresAt;
    return Task.CompletedTask;
  }

  public Task<DateTimeOffset?> GetSessionExpiresAtAsync(
    string sessionToken,
    CancellationToken cancellationToken
  )
  {
    if (!sessions.TryGetValue(sessionToken, out var expiresAt))
    {
      return Task.FromResult<DateTimeOffset?>(null);
    }

    if (expiresAt <= DateTimeOffset.UtcNow)
    {
      sessions.TryRemove(sessionToken, out _);
      return Task.FromResult<DateTimeOffset?>(null);
    }

    return Task.FromResult<DateTimeOffset?>(expiresAt);
  }
}
