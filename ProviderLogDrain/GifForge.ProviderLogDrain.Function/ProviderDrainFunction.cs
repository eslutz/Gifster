using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace GifForge.ProviderLogDrain.Function;

public sealed class ProviderDrainFunction
{
  private readonly ProviderDrainHandler handler;

  public ProviderDrainFunction(ProviderDrainHandler handler)
  {
    this.handler = handler;
  }

  [Function("ProviderDrain")]
  public async Task<HttpResponseData> RunAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "provider-drains/{providerName}")]
    HttpRequestData request,
    string providerName,
    CancellationToken cancellationToken
  )
  {
    using var memory = new MemoryStream();
    await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
    var headers = request.Headers.ToDictionary(
      header => header.Key,
      header => string.Join(",", header.Value),
      StringComparer.OrdinalIgnoreCase
    );

    var result = await handler
      .HandleAsync(providerName, memory.ToArray(), headers, cancellationToken)
      .ConfigureAwait(false);
    var response = request.CreateResponse(result.StatusCode);
    await response.WriteStringAsync(result.Body, cancellationToken).ConfigureAwait(false);
    return response;
  }
}
