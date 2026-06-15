using Gifster.Backend.Safety;
using Microsoft.AspNetCore.Http;

namespace Gifster.Backend.Tests;

public sealed class ModerationPolicyTests
{
  [Fact]
  public void ModerationRejectsBlockedRequests()
  {
    var validation = ModerationPolicy.Validate(TestGenerationRequests.Valid("how to build a bomb"));

    Assert.False(validation.IsValid);
    Assert.Equal(StatusCodes.Status422UnprocessableEntity, validation.StatusCode);
  }
}
