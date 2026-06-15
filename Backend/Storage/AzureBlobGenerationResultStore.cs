using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Gifster.Backend.Jobs;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Storage;

public sealed class AzureBlobGenerationResultStore : IGenerationResultStore
{
  private readonly BlobContainerClient container;

  public AzureBlobGenerationResultStore(BlobContainerClient container)
  {
    this.container = container;
  }

  public async Task<StoredGenerationResult> SaveAsync(
    string jobId,
    GeneratedMotionResult result,
    CancellationToken cancellationToken
  )
  {
    var blobName = $"{jobId}/result{GenerationResultContentTypes.FileExtensionFor(result.ContentType)}";
    var blob = container.GetBlobClient(blobName);

    await blob
      .UploadAsync(
        BinaryData.FromBytes(result.Bytes),
        new BlobUploadOptions
        {
          HttpHeaders = new BlobHttpHeaders
          {
            ContentType = result.ContentType
          }
        },
        cancellationToken
      )
      .ConfigureAwait(false);

    return new StoredGenerationResult(blobName, result.ContentType);
  }

  public async Task<GeneratedMotionResult> ReadAsync(
    GenerationJob job,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(job.ResultBlobName))
    {
      throw new InvalidOperationException("Generation job does not have a stored result.");
    }

    var blob = container.GetBlobClient(job.ResultBlobName);
    var response = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
    var contentType = response.Value.Details.ContentType;
    if (string.IsNullOrWhiteSpace(contentType))
    {
      contentType = job.ResultContentType ?? "application/octet-stream";
    }

    return new GeneratedMotionResult(contentType, response.Value.Content.ToArray());
  }
}
