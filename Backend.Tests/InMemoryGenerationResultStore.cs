using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;
using Gifster.Backend.Storage;

namespace Gifster.Backend.Tests;

internal sealed class InMemoryGenerationResultStore : IGenerationResultStore
{
  private readonly Dictionary<string, GeneratedMotionResult> results = [];

  public Task<StoredGenerationResult> SaveAsync(
    string jobId,
    GeneratedMotionResult result,
    CancellationToken cancellationToken
  )
  {
    var blobName = $"provider-results/{jobId}/result{GenerationResultContentTypes.FileExtensionFor(result.ContentType)}";
    results[blobName] = result;
    return Task.FromResult(new StoredGenerationResult(
      blobName,
      result.ContentType
    ));
  }

  public Task<GeneratedMotionResult> ReadAsync(GenerationJob job, CancellationToken cancellationToken)
  {
    if (job.ResultBlobName is null || !results.TryGetValue(job.ResultBlobName, out var result))
    {
      throw new InvalidOperationException("Result was not found.");
    }

    return Task.FromResult(result);
  }
}
