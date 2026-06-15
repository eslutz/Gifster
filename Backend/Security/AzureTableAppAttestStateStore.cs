using Azure;
using Azure.Data.Tables;

namespace Gifster.Backend.Security;

public sealed class AzureTableAppAttestStateStore : IAppAttestStateStore
{
  private const string ChallengePartitionKey = "app-attest-challenge";
  private const string SessionPartitionKey = "app-attest-session";
  private const string ChallengeProperty = "Challenge";
  private const string ExpiresAtProperty = "ExpiresAt";

  private readonly TableClient tableClient;

  public AzureTableAppAttestStateStore(TableClient tableClient)
  {
    this.tableClient = tableClient;
  }

  public async Task SaveChallengeAsync(AppAttestChallengeResponse challenge, CancellationToken cancellationToken)
  {
    var entity = new TableEntity(ChallengePartitionKey, challenge.ChallengeId)
    {
      [ChallengeProperty] = challenge.Challenge,
      [ExpiresAtProperty] = challenge.ExpiresAt
    };

    await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken)
      .ConfigureAwait(false);
  }

  public async Task<AppAttestChallengeResponse?> ConsumeChallengeAsync(
    string challengeId,
    CancellationToken cancellationToken
  )
  {
    try
    {
      var response = await tableClient.GetEntityAsync<TableEntity>(
        ChallengePartitionKey,
        challengeId,
        cancellationToken: cancellationToken
      ).ConfigureAwait(false);

      await tableClient.DeleteEntityAsync(
        ChallengePartitionKey,
        challengeId,
        response.Value.ETag,
        cancellationToken
      ).ConfigureAwait(false);

      return new AppAttestChallengeResponse(
        response.Value.RowKey,
        RequiredString(response.Value, ChallengeProperty),
        RequiredDateTimeOffset(response.Value, ExpiresAtProperty)
      );
    }
    catch (RequestFailedException error) when (error.Status is 404 or 412)
    {
      return null;
    }
  }

  public async Task SaveSessionAsync(
    string sessionToken,
    DateTimeOffset expiresAt,
    CancellationToken cancellationToken
  )
  {
    var entity = new TableEntity(SessionPartitionKey, sessionToken)
    {
      [ExpiresAtProperty] = expiresAt
    };

    await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken)
      .ConfigureAwait(false);
  }

  public async Task<DateTimeOffset?> GetSessionExpiresAtAsync(
    string sessionToken,
    CancellationToken cancellationToken
  )
  {
    try
    {
      var response = await tableClient.GetEntityAsync<TableEntity>(
        SessionPartitionKey,
        sessionToken,
        cancellationToken: cancellationToken
      ).ConfigureAwait(false);

      var expiresAt = RequiredDateTimeOffset(response.Value, ExpiresAtProperty);
      if (expiresAt <= DateTimeOffset.UtcNow)
      {
        await tableClient.DeleteEntityAsync(
          SessionPartitionKey,
          sessionToken,
          response.Value.ETag,
          cancellationToken
        ).ConfigureAwait(false);
        return null;
      }

      return expiresAt;
    }
    catch (RequestFailedException error) when (error.Status is 404 or 412)
    {
      return null;
    }
  }

  private static string RequiredString(TableEntity entity, string propertyName)
  {
    if (entity.TryGetValue(propertyName, out var value) &&
        value is string text &&
        !string.IsNullOrWhiteSpace(text))
    {
      return text;
    }

    throw new InvalidOperationException($"App Attest state row did not contain required '{propertyName}'.");
  }

  private static DateTimeOffset RequiredDateTimeOffset(TableEntity entity, string propertyName)
  {
    if (!entity.TryGetValue(propertyName, out var value))
    {
      throw new InvalidOperationException($"App Attest state row did not contain required '{propertyName}'.");
    }

    return value switch
    {
      DateTimeOffset dateTimeOffset => dateTimeOffset,
      DateTime dateTime => new DateTimeOffset(dateTime),
      _ => throw new InvalidOperationException($"App Attest state row contained invalid '{propertyName}'.")
    };
  }
}
