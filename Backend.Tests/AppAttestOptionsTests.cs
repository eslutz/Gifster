using GifForge.Backend.Security;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Tests;

public sealed class AppAttestOptionsTests
{
  [Fact]
  public void FromConfigurationParsesCommaSeparatedAppIdentifierAllowlist()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GIFFORGE_APP_ATTEST_APP_IDENTIFIER"] =
          "TEAMID.dev.ericslutz.gifforge, TEAMID.dev.ericslutz.gifforge.messagesextension",
        ["GIFFORGE_APP_ATTEST_APP_IDENTIFIERS"] =
          "TEAMID.dev.ericslutz.gifforge.messagesextension,TEAMID.dev.ericslutz.gifforge.beta"
      })
      .Build();

    var options = AppAttestOptions.FromConfiguration(configuration);

    Assert.Equal(
      [
        "TEAMID.dev.ericslutz.gifforge.messagesextension",
        "TEAMID.dev.ericslutz.gifforge.beta",
        "TEAMID.dev.ericslutz.gifforge"
      ],
      options.AppIdentifiers
    );
    Assert.Equal(
      "TEAMID.dev.ericslutz.gifforge, TEAMID.dev.ericslutz.gifforge.messagesextension",
      options.AppIdentifier
    );
  }
}
