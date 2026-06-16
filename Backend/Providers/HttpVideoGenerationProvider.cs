using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GifForge.Backend.Jobs;
using GifForge.Backend.Models;
using GifForge.Backend.Storage;

namespace GifForge.Backend.Providers;

public abstract class HttpVideoGenerationProvider : IVideoGenerationProvider
{
  private readonly HttpClient httpClient;
  private readonly HttpVideoGenerationProviderOptions options;

  protected HttpVideoGenerationProvider(
    HttpVideoGenerationProviderOptions options,
    HttpClient httpClient
  )
  {
    this.options = options;
    this.httpClient = httpClient;
  }

  public string Name => options.Name;

  public IReadOnlyList<VideoGenerationModel> Models => options.Models;

  public Task<ProviderJob> GenerateFromTextAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(VideoGenerationCapability.TextToVideo, request, model, cancellationToken);

  public Task<ProviderJob> GenerateFromImageAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(VideoGenerationCapability.ImageToVideo, request, model, cancellationToken);

  public Task<ProviderJob> TransformVideoAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(VideoGenerationCapability.VideoToVideo, request, model, cancellationToken);

  public async Task<GeneratedMotionResult> GetResultAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  )
  {
    using var request = new HttpRequestMessage(
      HttpMethod.Get,
      TemplateUri(options.ResultUrlTemplate, job.ProviderModelId, job.ProviderJobId)
    );
    ApplyAuthorization(request);

    using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    if (response.StatusCode is System.Net.HttpStatusCode.Accepted or System.Net.HttpStatusCode.NoContent)
    {
      throw new HttpRequestException($"{Name} generation result is not ready yet.");
    }

    if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden or
        System.Net.HttpStatusCode.NotFound or
        System.Net.HttpStatusCode.UnprocessableEntity)
    {
      throw new GenerationPermanentFailureException(
        $"{Name} rejected result retrieval with HTTP {(int)response.StatusCode}."
      );
    }

    response.EnsureSuccessStatusCode();
    var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
    if (contentType == GenerationResultContentTypes.Mp4)
    {
      var directBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
      return directBytes.Length == 0
        ? throw new GenerationPermanentFailureException($"{Name} returned an empty MP4.")
        : new GeneratedMotionResult(GenerationResultContentTypes.Mp4, directBytes);
    }

    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var document = JsonDocument.Parse(body);
    if (IsStillProcessing(document.RootElement))
    {
      throw new HttpRequestException($"{Name} generation result is not ready yet.");
    }

    var assetUrl = FindString(document.RootElement, "url") ??
      FindString(document.RootElement, "download_url") ??
      FindNestedString(document.RootElement, ["video", "url"]) ??
      FindNestedString(document.RootElement, ["asset", "url"]) ??
      FindNestedString(document.RootElement, ["assets", "video"]) ??
      FindNestedString(document.RootElement, ["generation", "assets", "video"]);

    if (string.IsNullOrWhiteSpace(assetUrl))
    {
      throw new GenerationPermanentFailureException($"{Name} result did not include a downloadable video URL.");
    }

    using var assetRequest = new HttpRequestMessage(HttpMethod.Get, assetUrl);
    using var assetResponse = await httpClient.SendAsync(assetRequest, cancellationToken).ConfigureAwait(false);
    assetResponse.EnsureSuccessStatusCode();
    var bytes = await assetResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    if (bytes.Length == 0)
    {
      throw new GenerationPermanentFailureException($"{Name} returned an empty generated video.");
    }

    return new GeneratedMotionResult(GenerationResultContentTypes.Mp4, bytes);
  }

  protected abstract VideoProviderSubmissionRequest BuildSubmissionRequest(
    VideoGenerationCapability capability,
    GenerationRequest request,
    VideoGenerationModel model
  );

  private async Task<ProviderJob> SubmitAsync(
    VideoGenerationCapability capability,
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  )
  {
    using var httpRequest = new HttpRequestMessage(
      HttpMethod.Post,
      TemplateUri(options.SubmitUrlTemplate, model.ModelId, null)
    )
    {
      Content = new StringContent(
        JsonSerializer.Serialize(
          BuildSubmissionRequest(capability, request, model),
          HttpVideoProviderJsonSerializerContext.Default.VideoProviderSubmissionRequest
        ),
        Encoding.UTF8,
        "application/json"
      )
    };
    ApplyAuthorization(httpRequest);

    using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
    if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or
        System.Net.HttpStatusCode.Unauthorized or
        System.Net.HttpStatusCode.Forbidden or
        System.Net.HttpStatusCode.UnprocessableEntity)
    {
      throw new GenerationPermanentFailureException(
        $"{Name} rejected generation submission with HTTP {(int)response.StatusCode}."
      );
    }

    response.EnsureSuccessStatusCode();
    var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    var submission = await JsonSerializer.DeserializeAsync(
      stream,
      HttpVideoProviderJsonSerializerContext.Default.VideoProviderSubmissionResponse,
      cancellationToken
    ).ConfigureAwait(false);

    var providerJobId = submission?.RequestId ?? submission?.Id ?? submission?.GenerationId;
    if (string.IsNullOrWhiteSpace(providerJobId))
    {
      throw new GenerationPermanentFailureException($"{Name} did not return a provider job id.");
    }

    return new ProviderJob(Name, providerJobId);
  }

  private void ApplyAuthorization(HttpRequestMessage request)
  {
    if (string.IsNullOrWhiteSpace(options.AuthorizationHeader))
    {
      return;
    }

    request.Headers.TryAddWithoutValidation("Authorization", options.AuthorizationHeader);
  }

  private static Uri TemplateUri(string template, string? modelId, string? providerJobId)
  {
    var uri = template;
    if (uri.Contains("{modelId}", StringComparison.Ordinal))
    {
      if (string.IsNullOrWhiteSpace(modelId))
      {
        throw new InvalidOperationException("Video provider URL template requires a model id.");
      }

      uri = uri.Replace("{modelId}", EscapePathSegments(modelId), StringComparison.Ordinal);
    }

    if (uri.Contains("{providerJobId}", StringComparison.Ordinal))
    {
      if (string.IsNullOrWhiteSpace(providerJobId))
      {
        throw new InvalidOperationException("Video provider URL template requires a provider job id.");
      }

      uri = uri.Replace("{providerJobId}", Uri.EscapeDataString(providerJobId), StringComparison.Ordinal);
    }

    return new Uri(uri);
  }

  private static string EscapePathSegments(string value) =>
    string.Join(
      '/',
      value.Split('/').Select(segment => Uri.EscapeDataString(segment))
    );

  protected static string? SourceMediaDataUrl(GenerationRequest request)
  {
    if (request.SourceMedia is { } sourceMedia)
    {
      return $"data:{sourceMedia.MimeType};base64,{sourceMedia.DataBase64}";
    }

    if (request.SourceImage is { } sourceImage)
    {
      return $"data:{sourceImage.MimeType};base64,{sourceImage.DataBase64}";
    }

    return null;
  }

  protected static int DurationSeconds(GenerationRequest request)
  {
    var requested = request.Options?.LoopSeconds is { } loopSeconds
      ? (int)Math.Round(loopSeconds, MidpointRounding.AwayFromZero)
      : 4;
    return Math.Clamp(requested, 3, 5);
  }

  private static bool IsStillProcessing(JsonElement element)
  {
    var status = FindString(element, "status") ?? FindNestedString(element, ["generation", "status"]);
    return status?.Trim().ToLowerInvariant() is "queued" or "pending" or "processing" or "running" or "in_progress";
  }

  private static string? FindNestedString(JsonElement element, IReadOnlyList<string> path)
  {
    var current = element;
    foreach (var segment in path)
    {
      if (!current.TryGetProperty(segment, out current))
      {
        return null;
      }
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
  }

  private static string? FindString(JsonElement element, string propertyName)
  {
    if (element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String)
    {
      return property.GetString();
    }

    return null;
  }
}

public sealed record HttpVideoGenerationProviderOptions(
  string Name,
  string SubmitUrlTemplate,
  string ResultUrlTemplate,
  string? AuthorizationHeader,
  IReadOnlyList<VideoGenerationModel> Models
);

public sealed record VideoProviderSubmissionRequest(
  string Model,
  string Prompt,
  string? NegativePrompt,
  string InputType,
  int DurationSeconds,
  int? Width,
  int? Height,
  string? SourceMediaDataUrl
);

public sealed record VideoProviderSubmissionResponse(
  string? RequestId,
  string? Id,
  string? GenerationId,
  string? StatusUrl,
  string? ResponseUrl
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VideoProviderSubmissionRequest))]
[JsonSerializable(typeof(VideoProviderSubmissionResponse))]
internal partial class HttpVideoProviderJsonSerializerContext : JsonSerializerContext;
