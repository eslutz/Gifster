using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gifster.Backend.Jobs;
using Gifster.Backend.Models;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Providers;

public sealed class ExternalHttpGenerationProvider : IGenerationProvider
{
  private readonly ExternalHttpProviderOptions options;
  private readonly HttpClient httpClient;

  public ExternalHttpGenerationProvider(ExternalHttpProviderOptions options, HttpClient httpClient)
  {
    this.options = options;
    this.httpClient = httpClient;
  }

  public string Name => options.Name;

  public string Mode => "external";

  public async Task<ProviderJob> SubmitGenerationAsync(
    GenerationRequest request,
    CancellationToken cancellationToken
  )
  {
    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.SubmitUrl)
    {
      Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(
        ExternalProviderGenerationRequest.From(request),
        ExternalHttpProviderJsonSerializerContext.Default.ExternalProviderGenerationRequest
      ))
    };
    httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
    {
      CharSet = Encoding.UTF8.WebName
    };
    ApplyAuthorization(httpRequest);

    using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
    EnsureSubmissionAccepted(response);
    var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    var providerJob = await JsonSerializer.DeserializeAsync(
      stream,
      ExternalHttpProviderJsonSerializerContext.Default.ExternalProviderJobResponse,
      cancellationToken
    ).ConfigureAwait(false);

    if (string.IsNullOrWhiteSpace(providerJob?.ProviderJobId))
    {
      throw new InvalidOperationException("External provider did not return a providerJobId.");
    }

    return new ProviderJob(Name, providerJob.ProviderJobId);
  }

  public async Task<GeneratedMotionResult> GetResultAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  )
  {
    using var httpRequest = new HttpRequestMessage(HttpMethod.Get, ResultUrlFor(job));
    ApplyAuthorization(httpRequest);

    using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
    if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden)
    {
      throw new GenerationPermanentFailureException(
        $"External provider rejected result retrieval with HTTP {(int)response.StatusCode}."
      );
    }

    EnsureResultReadyAndAccepted(response);
    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    if (bytes.Length == 0)
    {
      throw new GenerationPermanentFailureException("External provider returned an empty motion asset.");
    }

    var contentType = response.Content.Headers.ContentType?.MediaType;
    if (contentType is not (GenerationResultContentTypes.FrameSequence or GenerationResultContentTypes.Mp4))
    {
      throw new GenerationPermanentFailureException(
        $"External provider returned unsupported result content type '{contentType ?? "unknown"}'."
      );
    }

    return new GeneratedMotionResult(contentType, bytes);
  }

  private Uri ResultUrlFor(GenerationJob job)
  {
    var url = options.ResultUrlTemplate
      .Replace("{providerJobId}", Uri.EscapeDataString(job.ProviderJobId), StringComparison.Ordinal)
      .Replace("{jobId}", Uri.EscapeDataString(job.Id), StringComparison.Ordinal);
    return new Uri(url);
  }

  private static void EnsureSubmissionAccepted(HttpResponseMessage response)
  {
    if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden or
        System.Net.HttpStatusCode.UnprocessableEntity)
    {
      throw new GenerationPermanentFailureException(
        $"External provider rejected generation submission with HTTP {(int)response.StatusCode}."
      );
    }

    response.EnsureSuccessStatusCode();
  }

  private static void EnsureResultReadyAndAccepted(HttpResponseMessage response)
  {
    if (response.StatusCode is System.Net.HttpStatusCode.Accepted or
        System.Net.HttpStatusCode.NoContent)
    {
      throw new HttpRequestException(
        $"External provider result is not ready yet; HTTP {(int)response.StatusCode}."
      );
    }

    response.EnsureSuccessStatusCode();
  }

  private void ApplyAuthorization(HttpRequestMessage request)
  {
    if (string.IsNullOrWhiteSpace(options.AuthorizationHeader))
    {
      return;
    }

    request.Headers.TryAddWithoutValidation("Authorization", options.AuthorizationHeader);
  }
}

public sealed record ExternalProviderGenerationRequest(
  string? Id,
  string Mode,
  string CleanedPrompt,
  string? ExpandedPrompt,
  string? NegativePrompt,
  string CaptionMode,
  bool RenderCaptionLocally,
  SourceImageRequest? SourceImage,
  SourceImageContextRequest? SourceImageContext,
  GenerationOptions? Options,
  string? ClientTraceId
)
{
  public static ExternalProviderGenerationRequest From(GenerationRequest request) =>
    new(
      request.Id,
      request.Mode,
      request.CleanedPrompt,
      request.ExpandedPrompt,
      request.NegativePrompt,
      request.Caption?.Mode ?? "none",
      true,
      request.SourceImage,
      request.SourceImageContext,
      request.Options,
      request.ClientTraceId
    );
}

public sealed record ExternalProviderJobResponse(string ProviderJobId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExternalProviderGenerationRequest))]
[JsonSerializable(typeof(SourceImageRequest))]
[JsonSerializable(typeof(SourceImageContextRequest))]
[JsonSerializable(typeof(GenerationOptions))]
[JsonSerializable(typeof(ExternalProviderJobResponse))]
internal partial class ExternalHttpProviderJsonSerializerContext : JsonSerializerContext;
