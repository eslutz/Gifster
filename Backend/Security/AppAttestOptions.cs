using Microsoft.Extensions.Configuration;

namespace Gifster.Backend.Security;

public sealed record AppAttestOptions(
  bool Required,
  bool DemoBypassEnabled,
  string? AppIdentifier,
  string? RootCertificatePem
)
{
  public static AppAttestOptions FromConfiguration(IConfiguration configuration) =>
    new(
      string.Equals(
        configuration["GIFSTER_APP_ATTEST_REQUIRED"],
        "true",
        StringComparison.OrdinalIgnoreCase
      ),
      string.Equals(
        configuration["GIFSTER_APP_ATTEST_DEMO_BYPASS"],
        "true",
        StringComparison.OrdinalIgnoreCase
      ),
      configuration["GIFSTER_APP_ATTEST_APP_IDENTIFIER"],
      RootCertificatePemFromConfiguration(configuration)
    );

  private static string? RootCertificatePemFromConfiguration(IConfiguration configuration)
  {
    var inlinePem = configuration["GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PEM"];
    if (!string.IsNullOrWhiteSpace(inlinePem))
    {
      return inlinePem;
    }

    var pemPath = configuration["GIFSTER_APP_ATTEST_ROOT_CERTIFICATE_PATH"];
    return string.IsNullOrWhiteSpace(pemPath)
      ? null
      : File.ReadAllText(pemPath);
  }
}
