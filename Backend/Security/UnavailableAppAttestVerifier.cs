namespace Gifster.Backend.Security;

public sealed class UnavailableAppAttestVerifier : IAppAttestVerifier
{
  public AppAttestVerificationResult? Verify(
    AppAttestAttestationRequest request,
    AppAttestChallengeResponse challenge
  ) => null;
}
