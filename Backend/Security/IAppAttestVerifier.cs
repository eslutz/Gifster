namespace Gifster.Backend.Security;

public interface IAppAttestVerifier
{
  AppAttestVerificationResult? Verify(
    AppAttestAttestationRequest request,
    AppAttestChallengeResponse challenge
  );
}

public sealed record AppAttestVerificationResult(string KeyId);
