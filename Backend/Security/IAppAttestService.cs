using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Security;

public interface IAppAttestService
{
  AppAttestChallengeResponse CreateChallenge();
  AppAttestSessionResponse? CreateSession(AppAttestAttestationRequest request);
  bool IsAuthorized(HttpContext context);
}
