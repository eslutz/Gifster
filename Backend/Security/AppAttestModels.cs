namespace Gifster.Backend.Security;

public sealed record AppAttestChallengeResponse(string ChallengeId, string Challenge, DateTimeOffset ExpiresAt);

public sealed record AppAttestAttestationRequest(
  string KeyId,
  string ChallengeId,
  string AttestationObject,
  string ClientDataHash
);

public sealed record AppAttestSessionResponse(string SessionToken, DateTimeOffset ExpiresAt);
