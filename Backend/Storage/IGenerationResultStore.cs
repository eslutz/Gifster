using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Storage;

public interface IGenerationResultStore
{
  Task<StoredGenerationResult> SaveAsync(
    string jobId,
    GeneratedMotionResult result,
    CancellationToken cancellationToken
  );

  Task<GeneratedMotionResult> ReadAsync(GenerationJob job, CancellationToken cancellationToken);
}

public sealed record StoredGenerationResult(string BlobName, string ContentType);
