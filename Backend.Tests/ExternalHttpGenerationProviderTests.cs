using System.Net;
using System.Text;
using Gifster.Backend.Jobs;
using Gifster.Backend.Models;
using Gifster.Backend.Providers;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Tests;

public sealed class ExternalHttpGenerationProviderTests
{
  [Fact]
  public async Task SubmitGenerationPostsRequestAndParsesProviderJob()
  {
    var handler = new RecordingHttpMessageHandler(request =>
    {
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("https://provider.example.test/jobs", request.RequestUri?.ToString());
      Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());

      return new HttpResponseMessage(HttpStatusCode.Accepted)
      {
        Content = new StringContent(
          """
          {
            "providerJobId": "provider-job-123"
          }
          """,
          Encoding.UTF8,
          "application/json"
        )
      };
    });
    var provider = new ExternalHttpGenerationProvider(
      new ExternalHttpProviderOptions(
        "external-http",
        new Uri("https://provider.example.test/jobs"),
        "https://provider.example.test/jobs/{providerJobId}/result",
        "Bearer test-token"
      ),
      new HttpClient(handler)
    );

    var request = TestGenerationRequests.Valid("raw uncleaned prompt") with
    {
      Mode = "image_to_gif",
      CleanedPrompt = "cat in sunglasses",
      ExpandedPrompt = "Create a short looping animated scene. Prompt: cat in sunglasses.",
      SourceImage = new SourceImageRequest(
        Convert.ToBase64String("processed jpeg"u8.ToArray()),
        "image/jpeg",
        320,
        240
      ),
      SourceImageContext = new SourceImageContextRequest(
        320,
        240,
        "landscape",
        "4:3",
        "User-selected landscape JPEG source image, 320x240, aspect 4:3."
      ),
      Caption = new CaptionRequest("userText", "private caption text")
    };

    var providerJob = await provider.SubmitGenerationAsync(request, CancellationToken.None);

    Assert.Equal("external-http", provider.Name);
    Assert.Equal("external-http", providerJob.Provider);
    Assert.Equal("provider-job-123", providerJob.ProviderJobId);
    Assert.Contains("\"cleanedPrompt\":\"cat in sunglasses\"", handler.LastRequestBody);
    Assert.Contains("\"sourceImageContext\"", handler.LastRequestBody);
    Assert.Contains("\"orientation\":\"landscape\"", handler.LastRequestBody);
    Assert.Contains("\"aspectRatio\":\"4:3\"", handler.LastRequestBody);
    Assert.Contains("\"captionMode\":\"userText\"", handler.LastRequestBody);
    Assert.Contains("\"renderCaptionLocally\":true", handler.LastRequestBody);
    Assert.DoesNotContain("private caption text", handler.LastRequestBody);
    Assert.DoesNotContain("raw uncleaned prompt", handler.LastRequestBody);
    Assert.DoesNotContain("\"caption\":", handler.LastRequestBody);
    Assert.DoesNotContain("\"originalPrompt\":", handler.LastRequestBody);
  }

  [Fact]
  public async Task GetResultDownloadsProviderMotionAssetBytes()
  {
    var mp4Bytes = "fake mp4 bytes"u8.ToArray();
    var handler = new RecordingHttpMessageHandler(request =>
    {
      Assert.Equal(HttpMethod.Get, request.Method);
      Assert.Equal("https://provider.example.test/jobs/provider-job-456/result", request.RequestUri?.ToString());

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new ByteArrayContent(mp4Bytes)
        {
          Headers =
          {
            ContentType = new("video/mp4")
          }
        }
      };
    });
    var provider = new ExternalHttpGenerationProvider(
      new ExternalHttpProviderOptions(
        "external-http",
        new Uri("https://provider.example.test/jobs"),
        "https://provider.example.test/jobs/{providerJobId}/result",
        null
      ),
      new HttpClient(handler)
    );
    var job = new GenerationJob(
      "job-1",
      TestGenerationRequests.Valid(),
      "external-http",
      "provider-job-456",
      GenerationJobStatus.Succeeded,
      DateTimeOffset.UtcNow,
      DateTimeOffset.UtcNow,
      DateTimeOffset.UtcNow
    );

    var result = await provider.GetResultAsync(job, CancellationToken.None);

    Assert.Equal(GenerationResultContentTypes.Mp4, result.ContentType);
    Assert.Equal(mp4Bytes, result.Bytes);
  }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
  private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

  public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
  {
    this.responder = responder;
  }

  public string LastRequestBody { get; private set; } = string.Empty;

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken
  )
  {
    LastRequestBody = request.Content is null
      ? string.Empty
      : await request.Content.ReadAsStringAsync(cancellationToken);

    return responder(request);
  }
}
