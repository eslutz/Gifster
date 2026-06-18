using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Security;

public sealed record AppAttestOptions(
  bool Required,
  bool DemoBypassEnabled,
  string? AppIdentifier,
  IReadOnlyList<string> AppIdentifiers,
  string? RootCertificatePem
)
{
  public static AppAttestOptions FromConfiguration(IConfiguration configuration) =>
    new(
      string.Equals(
        configuration["GIFFORGE_APP_ATTEST_REQUIRED"],
        "true",
        StringComparison.OrdinalIgnoreCase
      ),
      string.Equals(
        configuration["GIFFORGE_APP_ATTEST_DEMO_BYPASS"],
        "true",
        StringComparison.OrdinalIgnoreCase
      ),
      configuration["GIFFORGE_APP_ATTEST_APP_IDENTIFIER"],
      AppIdentifiersFromConfiguration(configuration),
      RootCertificatePemFromConfiguration(configuration)
    );

  private static IReadOnlyList<string> AppIdentifiersFromConfiguration(IConfiguration configuration)
  {
    var values = new List<string>();
    AddIdentifiers(values, configuration["GIFFORGE_APP_ATTEST_APP_IDENTIFIERS"]);
    AddIdentifiers(values, configuration["GIFFORGE_APP_ATTEST_APP_IDENTIFIER"]);
    return values.Distinct(StringComparer.Ordinal).ToArray();
  }

  private static void AddIdentifiers(List<string> values, string? rawValue)
  {
    if (string.IsNullOrWhiteSpace(rawValue))
    {
      return;
    }

    foreach (var value in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      if (!string.IsNullOrWhiteSpace(value))
      {
        values.Add(value);
      }
    }
  }

  private static string? RootCertificatePemFromConfiguration(IConfiguration configuration)
  {
    var inlinePem = configuration["GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM"];
    if (!string.IsNullOrWhiteSpace(inlinePem))
    {
      return inlinePem;
    }

    var pemPath = configuration["GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PATH"];
    return string.IsNullOrWhiteSpace(pemPath)
      ? null
      : File.ReadAllText(pemPath);
  }
}
