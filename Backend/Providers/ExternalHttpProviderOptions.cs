using Microsoft.Extensions.Configuration;

namespace Gifster.Backend.Providers;

public sealed record ExternalHttpProviderOptions(
  string Name,
  Uri SubmitUrl,
  string ResultUrlTemplate,
  string? AuthorizationHeader
)
{
  public static ExternalHttpProviderOptions FromConfiguration(IConfiguration configuration)
  {
    var submitUrl = configuration["GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL"];
    var resultUrlTemplate = configuration["GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE"];
    if (string.IsNullOrWhiteSpace(submitUrl) || string.IsNullOrWhiteSpace(resultUrlTemplate))
    {
      throw new InvalidOperationException(
        "External HTTP provider requires GIFSTER_EXTERNAL_PROVIDER_SUBMIT_URL and GIFSTER_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE."
      );
    }

    return new ExternalHttpProviderOptions(
      configuration["GIFSTER_EXTERNAL_PROVIDER_NAME"] ?? "external-http",
      new Uri(submitUrl),
      resultUrlTemplate,
      configuration["GIFSTER_EXTERNAL_PROVIDER_AUTHORIZATION"]
    );
  }
}
