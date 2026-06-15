using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Security;

public interface IAppAttestService
{
  Task<AppAttestChallengeResponse> CreateChallengeAsync(CancellationToken cancellationToken);
  Task<AppAttestSessionResponse?> CreateSessionAsync(
    AppAttestAttestationRequest request,
    CancellationToken cancellationToken
  );
  Task<bool> IsAuthorizedAsync(HttpContext context, CancellationToken cancellationToken);
}
