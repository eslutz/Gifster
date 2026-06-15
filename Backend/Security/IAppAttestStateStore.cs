namespace Gifster.Backend.Security;

public interface IAppAttestStateStore
{
  Task SaveChallengeAsync(AppAttestChallengeResponse challenge, CancellationToken cancellationToken);
  Task<AppAttestChallengeResponse?> ConsumeChallengeAsync(string challengeId, CancellationToken cancellationToken);
  Task SaveSessionAsync(string sessionToken, DateTimeOffset expiresAt, CancellationToken cancellationToken);
  Task<DateTimeOffset?> GetSessionExpiresAtAsync(string sessionToken, CancellationToken cancellationToken);
}
