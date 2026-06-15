using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Gifster.Backend.Jobs;

public sealed class GenerationJobTableEntity : ITableEntity
{
  public const string JobPartitionKey = "generation";

  public string PartitionKey { get; set; } = JobPartitionKey;
  public string RowKey { get; set; } = string.Empty;
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public string RequestJson { get; set; } = "{}";
  public string Provider { get; set; } = string.Empty;
  public string ProviderJobId { get; set; } = string.Empty;
  public string Status { get; set; } = GenerationJobStatus.Queued.JsonValue();
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset ExpiresAt { get; set; }
  public string? ResultBlobName { get; set; }
  public string? ResultContentType { get; set; }
  public string? FailedMessage { get; set; }

  public GenerationJobTableEntity()
  {
  }

  public static GenerationJobTableEntity FromJob(GenerationJob job) =>
    new()
    {
      PartitionKey = JobPartitionKey,
      RowKey = job.Id,
      RequestJson = JsonSerializer.Serialize(job.Request, GifsterJsonSerializerContext.Default.GenerationRequest),
      Provider = job.Provider,
      ProviderJobId = job.ProviderJobId,
      Status = job.Status.JsonValue(),
      CreatedAt = job.CreatedAt,
      UpdatedAt = job.UpdatedAt,
      ExpiresAt = job.ExpiresAt,
      ResultBlobName = job.ResultBlobName,
      ResultContentType = job.ResultContentType,
      FailedMessage = job.FailedMessage
    };

  public TableEntity ToTableEntity()
  {
    var entity = new TableEntity(PartitionKey, RowKey)
    {
      [nameof(RequestJson)] = RequestJson,
      [nameof(Provider)] = Provider,
      [nameof(ProviderJobId)] = ProviderJobId,
      [nameof(Status)] = Status,
      [nameof(CreatedAt)] = CreatedAt,
      [nameof(UpdatedAt)] = UpdatedAt,
      [nameof(ExpiresAt)] = ExpiresAt
    };

    if (ResultBlobName is not null)
    {
      entity[nameof(ResultBlobName)] = ResultBlobName;
    }

    if (ResultContentType is not null)
    {
      entity[nameof(ResultContentType)] = ResultContentType;
    }

    if (FailedMessage is not null)
    {
      entity[nameof(FailedMessage)] = FailedMessage;
    }

    return entity;
  }

  public static GenerationJobTableEntity FromTableEntity(TableEntity entity) =>
    new()
    {
      PartitionKey = entity.PartitionKey,
      RowKey = entity.RowKey,
      Timestamp = entity.Timestamp,
      ETag = entity.ETag,
      RequestJson = RequiredString(entity, nameof(RequestJson)),
      Provider = RequiredString(entity, nameof(Provider)),
      ProviderJobId = RequiredString(entity, nameof(ProviderJobId)),
      Status = RequiredString(entity, nameof(Status)),
      CreatedAt = RequiredDateTimeOffset(entity, nameof(CreatedAt)),
      UpdatedAt = RequiredDateTimeOffset(entity, nameof(UpdatedAt)),
      ExpiresAt = RequiredDateTimeOffset(entity, nameof(ExpiresAt)),
      ResultBlobName = OptionalString(entity, nameof(ResultBlobName)),
      ResultContentType = OptionalString(entity, nameof(ResultContentType)),
      FailedMessage = OptionalString(entity, nameof(FailedMessage))
    };

  public GenerationJob ToJob()
  {
    var request = JsonSerializer.Deserialize(RequestJson, GifsterJsonSerializerContext.Default.GenerationRequest)
      ?? throw new InvalidOperationException("Generation job row did not contain a valid generation request.");

    return new GenerationJob(
      RowKey,
      request,
      Provider,
      ProviderJobId,
      GenerationJobStatusExtensions.FromJsonValue(Status),
      CreatedAt,
      UpdatedAt,
      ExpiresAt,
      ResultBlobName,
      ResultContentType,
      FailedMessage
    );
  }

  private static string RequiredString(TableEntity entity, string propertyName)
  {
    if (entity.TryGetValue(propertyName, out var value) &&
        value is string text &&
        !string.IsNullOrWhiteSpace(text))
    {
      return text;
    }

    throw new InvalidOperationException($"Generation job row did not contain required '{propertyName}'.");
  }

  private static string? OptionalString(TableEntity entity, string propertyName) =>
    entity.TryGetValue(propertyName, out var value) ? value as string : null;

  private static DateTimeOffset RequiredDateTimeOffset(TableEntity entity, string propertyName)
  {
    if (!entity.TryGetValue(propertyName, out var value))
    {
      throw new InvalidOperationException($"Generation job row did not contain required '{propertyName}'.");
    }

    return value switch
    {
      DateTimeOffset dateTimeOffset => dateTimeOffset,
      DateTime dateTime => new DateTimeOffset(dateTime),
      _ => throw new InvalidOperationException($"Generation job row contained invalid '{propertyName}'.")
    };
  }
}
