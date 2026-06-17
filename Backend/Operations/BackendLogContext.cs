using System.Reflection;

namespace GifForge.Backend.Operations;

public sealed record BackendLogContext(
  string EnvironmentName,
  string Component,
  string Service,
  string Version
)
{
  public static BackendLogContext FromConfiguration(IConfiguration configuration)
  {
    var environmentName = Normalized(configuration["GIFFORGE_ENVIRONMENT_NAME"]);
    var component = Normalized(configuration["GIFFORGE_LOG_COMPONENT"]);
    var requireStructuredContext = string.Equals(
      configuration["GIFFORGE_REQUIRE_STRUCTURED_LOG_CONTEXT"],
      "true",
      StringComparison.OrdinalIgnoreCase
    );

    if (requireStructuredContext)
    {
      var missing = new List<string>();
      if (environmentName is null)
      {
        missing.Add("GIFFORGE_ENVIRONMENT_NAME");
      }
      if (component is null)
      {
        missing.Add("GIFFORGE_LOG_COMPONENT");
      }
      if (missing.Count > 0)
      {
        throw new InvalidOperationException(
          $"Structured log context is required, but {string.Join(" and ", missing)} is not configured."
        );
      }
    }

    var service = Normalized(configuration["OTEL_SERVICE_NAME"]) ?? "GifForge.Backend";
    var version = Normalized(configuration["GIFFORGE_VERSION"])
      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
      ?? "unknown";

    return new BackendLogContext(
      environmentName ?? "local",
      component ?? "backend-test",
      service,
      version
    );
  }

  public IReadOnlyDictionary<string, object> ToScope() => new Dictionary<string, object>
  {
    ["GifForgeEnvironment"] = EnvironmentName,
    ["GifForgeComponent"] = Component,
    ["GifForgeService"] = Service,
    ["GifForgeVersion"] = Version
  };

  private static string? Normalized(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }
}
