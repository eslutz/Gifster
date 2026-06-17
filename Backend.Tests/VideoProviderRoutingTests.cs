using System.Net;
using GifForge.Backend.Models;
using GifForge.Backend.Jobs;
using GifForge.Backend.Providers;

namespace GifForge.Backend.Tests;

public sealed class VideoProviderRoutingTests
{
  [Fact]
  public async Task HttpProviderPreservesFalModelPathAndEscapesProviderJobId()
  {
    var model = new VideoGenerationModel(
      "FAL_WAN22_TEXT_TO_VIDEO",
      "fal-ai/wan/v2.2-a14b/text-to-video",
      VideoGenerationCapability.TextToVideo,
      0.03m,
      true
    );
    var handler = new RecordingHttpMessageHandler(
      request => request.RequestUri?.AbsolutePath.Contains("/requests/", StringComparison.Ordinal) == true
        ? Mp4Response()
        : JsonResponse("""{"request_id":"req/with space"}""")
    );
    var provider = new FalVideoProvider(
      new HttpVideoGenerationProviderOptions(
        "fal.ai",
        "https://queue.fal.run/{modelId}",
        "https://queue.fal.run/{modelId}/requests/{providerJobId}",
        null,
        [model]
      ),
      new HttpClient(handler)
    );

    var providerJob = await provider.GenerateFromTextAsync(
      TestGenerationRequests.Valid(),
      model,
      CancellationToken.None
    );
    var job = GenerationJob.Create(
      TestGenerationRequests.Valid(),
      providerJob with { ModelId = model.ModelId },
      TimeSpan.FromHours(1)
    );

    await provider.GetResultAsync(job, CancellationToken.None);

    Assert.Equal(
      "https://queue.fal.run/fal-ai/wan/v2.2-a14b/text-to-video",
      handler.RequestUris[0].AbsoluteUri
    );
    Assert.Equal(
      "https://queue.fal.run/fal-ai/wan/v2.2-a14b/text-to-video/requests/req%2Fwith%20space",
      handler.RequestUris[1].AbsoluteUri
    );
  }

  [Fact]
  public async Task SubmitGenerationAsyncRoutesImageInputsToCheapestImageToVideoModel()
  {
    var fal = new RecordingVideoGenerationProvider(
      "fal.ai",
      [
        new VideoGenerationModel("fal-wan-2.2-i2v", VideoGenerationCapability.ImageToVideo, 0.04m, true)
      ]
    );
    var luma = new RecordingVideoGenerationProvider(
      "luma",
      [
        new VideoGenerationModel("ray-3.2-i2v", VideoGenerationCapability.ImageToVideo, 0.18m, true)
      ]
    );
    var router = new RoutedVideoGenerationProvider([fal, luma]);
    var request = TestGenerationRequests.Valid() with
    {
      Mode = "image_to_gif",
      SourceMedia = TestSourceMedia.Png()
    };

    var providerJob = await router.SubmitGenerationAsync(request, CancellationToken.None);

    Assert.Equal("fal.ai", providerJob.Provider);
    Assert.Equal("fal-wan-2.2-i2v", fal.LastModelId);
    Assert.Equal(VideoGenerationCapability.ImageToVideo, fal.LastCapability);
    Assert.Null(luma.LastModelId);
  }

  [Fact]
  public async Task SubmitGenerationAsyncDoesNotTryFallbackWithoutClientRetry()
  {
    var fal = new RecordingVideoGenerationProvider(
      "fal.ai",
      [
        new VideoGenerationModel("fal-wan-2.2-t2v", VideoGenerationCapability.TextToVideo, 0.03m, true)
      ],
      new HttpRequestException("fal temporarily unavailable")
    );
    var luma = new RecordingVideoGenerationProvider(
      "luma",
      [
        new VideoGenerationModel("ray-3.2-t2v", VideoGenerationCapability.TextToVideo, 0.16m, true)
      ]
    );
    var router = new RoutedVideoGenerationProvider([fal, luma]);

    await Assert.ThrowsAsync<HttpRequestException>(
      () => router.SubmitGenerationAsync(TestGenerationRequests.Valid(), CancellationToken.None)
    );

    Assert.Equal(1, fal.SubmissionAttempts);
    Assert.Equal(0, luma.SubmissionAttempts);
  }

  [Fact]
  public async Task SubmitRetryGenerationAsyncSkipsPreviouslyAttemptedProvider()
  {
    var fal = new RecordingVideoGenerationProvider(
      "fal.ai",
      [
        new VideoGenerationModel("fal-wan-2.2-t2v", VideoGenerationCapability.TextToVideo, 0.03m, true)
      ]
    );
    var luma = new RecordingVideoGenerationProvider(
      "luma",
      [
        new VideoGenerationModel("ray-3.2-t2v", VideoGenerationCapability.TextToVideo, 0.16m, true)
      ]
    );
    var router = new RoutedVideoGenerationProvider([fal, luma]);

    var providerJob = await router.SubmitRetryGenerationAsync(
      TestGenerationRequests.Valid() with { RetryOfJobId = "failed-job" },
      new HashSet<string>(["fal.ai"], StringComparer.OrdinalIgnoreCase),
      new HashSet<string>(["fal-wan-2.2-t2v"], StringComparer.OrdinalIgnoreCase),
      CancellationToken.None
    );

    Assert.Equal("luma", providerJob.Provider);
    Assert.Equal("ray-3.2-t2v", providerJob.ModelId);
    Assert.Equal(0, fal.SubmissionAttempts);
    Assert.Equal(1, luma.SubmissionAttempts);
  }

  [Fact]
  public void ClassifyMapsGifMp4MovAndLivePhotoMovToVideoToVideo()
  {
    Assert.Equal(
      VideoGenerationCapability.VideoToVideo,
      VideoGenerationInputClassifier.RequiredCapability(TestGenerationRequests.Valid() with
      {
        Mode = "video_to_gif",
        SourceMedia = TestSourceMedia.Gif()
      })
    );
    Assert.Equal(
      VideoGenerationCapability.VideoToVideo,
      VideoGenerationInputClassifier.RequiredCapability(TestGenerationRequests.Valid() with
      {
        Mode = "video_to_gif",
        SourceMedia = TestSourceMedia.Mp4()
      })
    );
    Assert.Equal(
      VideoGenerationCapability.VideoToVideo,
      VideoGenerationInputClassifier.RequiredCapability(TestGenerationRequests.Valid() with
      {
        Mode = "video_to_gif",
        SourceMedia = TestSourceMedia.LivePhotoMov()
      })
    );
  }

  private static HttpResponseMessage JsonResponse(string json) =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

  private static HttpResponseMessage Mp4Response()
  {
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new ByteArrayContent([0x00, 0x00, 0x00, 0x18])
    };
    response.Content.Headers.ContentType = new("video/mp4");
    return response;
  }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
  private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;

  public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
  {
    this.responseFactory = responseFactory;
  }

  public List<Uri> RequestUris { get; } = [];

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken
  )
  {
    RequestUris.Add(request.RequestUri ?? throw new InvalidOperationException("Request URI is required."));
    return Task.FromResult(responseFactory(request));
  }
}

internal sealed class RecordingVideoGenerationProvider : IVideoGenerationProvider
{
  private readonly Exception? submitError;

  public RecordingVideoGenerationProvider(
    string name,
    IReadOnlyList<VideoGenerationModel> models,
    Exception? submitError = null
  )
  {
    Name = name;
    Models = models;
    this.submitError = submitError;
  }

  public string Name { get; }

  public IReadOnlyList<VideoGenerationModel> Models { get; }

  public string? LastModelId { get; private set; }

  public VideoGenerationCapability? LastCapability { get; private set; }

  public int SubmissionAttempts { get; private set; }

  public Task<ProviderJob> GenerateFromTextAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(model);

  public Task<ProviderJob> GenerateFromImageAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(model);

  public Task<ProviderJob> TransformVideoAsync(
    GenerationRequest request,
    VideoGenerationModel model,
    CancellationToken cancellationToken
  ) =>
    SubmitAsync(model);

  public Task<GeneratedMotionResult> GetResultAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  ) =>
    throw new NotSupportedException();

  private Task<ProviderJob> SubmitAsync(VideoGenerationModel model)
  {
    SubmissionAttempts++;
    LastModelId = model.ModelId;
    LastCapability = model.Capability;
    return submitError is null
      ? Task.FromResult(new ProviderJob(Name, $"{Name}:{model.ModelId}:job"))
      : Task.FromException<ProviderJob>(submitError);
  }
}

internal static class TestSourceMedia
{
  public static SourceMediaRequest Png() =>
    new(Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]), "image/png", "photo.png", "image", null);

  public static SourceMediaRequest Gif() =>
    new(Convert.ToBase64String("GIF89a"u8.ToArray()), "image/gif", "source.gif", "video", null);

  public static SourceMediaRequest Mp4() =>
    new(Convert.ToBase64String([0x00, 0x00, 0x00, 0x18]), "video/mp4", "source.mp4", "video", null);

  public static SourceMediaRequest LivePhotoMov() =>
    new(Convert.ToBase64String([0x00, 0x00, 0x00, 0x18]), "video/quicktime", "IMG_0001.MOV", "livePhotoPairedVideo", "live-photo-1");
}
