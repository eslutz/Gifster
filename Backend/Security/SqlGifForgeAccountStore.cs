using Microsoft.Data.SqlClient;

namespace GifForge.Backend.Security;

public sealed class SqlGifForgeAccountStore : IGifForgeAccountStore
{
  private const string Schema = "gifforge";
  private readonly string connectionString;

  public SqlGifForgeAccountStore(string server, string database)
  {
    var builder = new SqlConnectionStringBuilder
    {
      DataSource = server,
      InitialCatalog = database,
      Encrypt = true,
      TrustServerCertificate = false,
      ConnectTimeout = 30,
      Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault
    };
    connectionString = builder.ConnectionString;
  }

  public async Task<GifForgeUser> UpsertAppleUserAsync(AppleIdentity identity, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      BEGIN TRANSACTION;

      DECLARE @user_id uniqueidentifier;
      SELECT @user_id = user_id
      FROM {{Schema}}.users WITH (UPDLOCK, HOLDLOCK)
      WHERE apple_subject = @apple_subject AND deleted_at IS NULL;

      IF @user_id IS NULL
      BEGIN
        SET @user_id = NEWID();
        INSERT INTO {{Schema}}.users (user_id, apple_subject, app_account_token, private_relay_email, created_at, updated_at)
        VALUES (@user_id, @apple_subject, NEWID(), @email, SYSUTCDATETIME(), SYSUTCDATETIME());
      END
      ELSE
      BEGIN
        UPDATE {{Schema}}.users
        SET private_relay_email = COALESCE(@email, private_relay_email),
            updated_at = SYSUTCDATETIME()
        WHERE user_id = @user_id;
      END

      SELECT user_id, apple_subject, app_account_token, created_at, updated_at, deleted_at
      FROM {{Schema}}.users
      WHERE user_id = @user_id;

      COMMIT TRANSACTION;
      """;
    command.Parameters.AddWithValue("@apple_subject", identity.Subject);
    command.Parameters.AddWithValue("@email", (object?)identity.Email ?? DBNull.Value);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      throw new InvalidOperationException("SQL user upsert did not return a user row.");
    }

    return ReadUser(reader);
  }

  public async Task<GifForgeUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SELECT user_id, apple_subject, app_account_token, created_at, updated_at, deleted_at
      FROM {{Schema}}.users
      WHERE user_id = @user_id AND deleted_at IS NULL;
      """;
    command.Parameters.AddWithValue("@user_id", userId);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
      ? ReadUser(reader)
      : null;
  }

  public async Task SaveRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      INSERT INTO {{Schema}}.refresh_tokens
        (token_hash, user_id, family_id, expires_at, created_at, revoked_at, replaced_by_token_hash)
      VALUES
        (@token_hash, @user_id, @family_id, @expires_at, @created_at, @revoked_at, @replaced_by_token_hash);
      """;
    command.Parameters.AddWithValue("@token_hash", token.TokenHash);
    command.Parameters.AddWithValue("@user_id", token.UserId);
    command.Parameters.AddWithValue("@family_id", token.FamilyId);
    command.Parameters.AddWithValue("@expires_at", token.ExpiresAt);
    command.Parameters.AddWithValue("@created_at", token.CreatedAt);
    command.Parameters.AddWithValue("@revoked_at", (object?)token.RevokedAt ?? DBNull.Value);
    command.Parameters.AddWithValue("@replaced_by_token_hash", (object?)token.ReplacedByTokenHash ?? DBNull.Value);
    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SELECT token_hash, user_id, family_id, expires_at, created_at, revoked_at, replaced_by_token_hash
      FROM {{Schema}}.refresh_tokens
      WHERE token_hash = @token_hash AND expires_at > SYSUTCDATETIME();
      """;
    command.Parameters.AddWithValue("@token_hash", tokenHash);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return ReadRefreshToken(reader);
  }

  public async Task<RefreshTokenClaimResult> RotateRefreshTokenAsync(
    string tokenHash,
    string replacementTokenHash,
    DateTimeOffset rotatedAt,
    DateTimeOffset replacementExpiresAt,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
      BEGIN TRANSACTION;

      DECLARE @user_id uniqueidentifier;
      DECLARE @family_id uniqueidentifier;
      DECLARE @expires_at datetimeoffset;
      DECLARE @created_at datetimeoffset;
      DECLARE @revoked_at datetimeoffset;
      DECLARE @replaced_by_token_hash nvarchar(128);

      SELECT
        @user_id = user_id,
        @family_id = family_id,
        @expires_at = expires_at,
        @created_at = created_at,
        @revoked_at = revoked_at,
        @replaced_by_token_hash = replaced_by_token_hash
      FROM {{Schema}}.refresh_tokens WITH (UPDLOCK, HOLDLOCK)
      WHERE token_hash = @token_hash AND expires_at > SYSUTCDATETIME();

      IF @user_id IS NULL
      BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS bit) AS found,
               CAST(0 AS bit) AS claimed,
               @token_hash AS token_hash,
               CAST(NULL AS uniqueidentifier) AS user_id,
               CAST(NULL AS uniqueidentifier) AS family_id,
               CAST(NULL AS datetimeoffset) AS expires_at,
               CAST(NULL AS datetimeoffset) AS created_at,
               CAST(NULL AS datetimeoffset) AS revoked_at,
               CAST(NULL AS nvarchar(128)) AS replaced_by_token_hash;
        RETURN;
      END

      IF @revoked_at IS NULL
      BEGIN
        UPDATE {{Schema}}.refresh_tokens
        SET revoked_at = @rotated_at,
            replaced_by_token_hash = @replacement_token_hash
        WHERE token_hash = @token_hash;

        INSERT INTO {{Schema}}.refresh_tokens
          (token_hash, user_id, family_id, expires_at, created_at)
        VALUES
          (@replacement_token_hash, @user_id, @family_id, @replacement_expires_at, @rotated_at);
      END

      COMMIT TRANSACTION;

      SELECT CAST(1 AS bit) AS found,
             CASE WHEN @revoked_at IS NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS claimed,
             @token_hash AS token_hash,
             @user_id AS user_id,
             @family_id AS family_id,
             @expires_at AS expires_at,
             @created_at AS created_at,
             @revoked_at AS revoked_at,
             @replaced_by_token_hash AS replaced_by_token_hash;
      """;
    command.Parameters.AddWithValue("@token_hash", tokenHash);
    command.Parameters.AddWithValue("@replacement_token_hash", replacementTokenHash);
    command.Parameters.AddWithValue("@rotated_at", rotatedAt);
    command.Parameters.AddWithValue("@replacement_expires_at", replacementExpiresAt);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
    {
      return new RefreshTokenClaimResult(false, null);
    }

    var token = new RefreshTokenRecord(
      reader.GetString(2),
      reader.GetGuid(3),
      reader.GetGuid(4),
      reader.GetDateTimeOffset(5),
      reader.GetDateTimeOffset(6),
      reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7),
      reader.IsDBNull(8) ? null : reader.GetString(8)
    );
    return new RefreshTokenClaimResult(reader.GetBoolean(1), token);
  }

  public async Task RevokeRefreshTokenAsync(
    string tokenHash,
    DateTimeOffset revokedAt,
    string? replacedByTokenHash,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      UPDATE {{Schema}}.refresh_tokens
      SET revoked_at = @revoked_at,
          replaced_by_token_hash = @replaced_by_token_hash
      WHERE token_hash = @token_hash AND revoked_at IS NULL;
      """;
    command.Parameters.AddWithValue("@token_hash", tokenHash);
    command.Parameters.AddWithValue("@revoked_at", revokedAt);
    command.Parameters.AddWithValue("@replaced_by_token_hash", (object?)replacedByTokenHash ?? DBNull.Value);
    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task RevokeRefreshTokenFamilyAsync(
    Guid familyId,
    DateTimeOffset revokedAt,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      UPDATE {{Schema}}.refresh_tokens
      SET revoked_at = COALESCE(revoked_at, @revoked_at)
      WHERE family_id = @family_id;
      """;
    command.Parameters.AddWithValue("@family_id", familyId);
    command.Parameters.AddWithValue("@revoked_at", revokedAt);
    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<StoreKitProduct>> GetProductsAsync(CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SELECT product_id, credits, active
      FROM {{Schema}}.iap_products
      WHERE active = 1
      ORDER BY credits;
      """;
    var products = new List<StoreKitProduct>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      products.Add(new StoreKitProduct(reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2)));
    }

    return products;
  }

  public async Task<StoreKitGrantResult?> GrantCreditsAsync(
    Guid userId,
    VerifiedStoreKitTransaction transaction,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
      BEGIN TRANSACTION;

      DECLARE @credits int;
      SELECT @credits = credits
      FROM {{Schema}}.iap_products WITH (UPDLOCK, HOLDLOCK)
      WHERE product_id = @product_id AND active = 1;

      IF @credits IS NULL
      BEGIN
        ROLLBACK TRANSACTION;
        SELECT CAST(0 AS bit) AS accepted, CAST(0 AS bit) AS already_processed, CAST(0 AS int) AS granted_credits;
        RETURN;
      END

      IF EXISTS (SELECT 1 FROM {{Schema}}.iap_transactions WHERE transaction_id = @transaction_id)
      BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(1 AS bit) AS accepted, CAST(1 AS bit) AS already_processed, CAST(0 AS int) AS granted_credits;
        RETURN;
      END

      INSERT INTO {{Schema}}.iap_transactions
        (transaction_id, original_transaction_id, user_id, product_id, app_account_token, environment, signed_payload_hash, status, created_at)
      VALUES
        (@transaction_id, @original_transaction_id, @user_id, @product_id, @app_account_token, @environment, @signed_payload_hash, 'granted', SYSUTCDATETIME());

      INSERT INTO {{Schema}}.usage_ledger
        (ledger_id, user_id, kind, credits, reference_id, created_at)
      VALUES
        (NEWID(), @user_id, 'grant', @credits, @transaction_id, SYSUTCDATETIME());

      COMMIT TRANSACTION;
      SELECT CAST(1 AS bit) AS accepted, CAST(0 AS bit) AS already_processed, @credits AS granted_credits;
      """;
    command.Parameters.AddWithValue("@transaction_id", transaction.TransactionId);
    command.Parameters.AddWithValue("@original_transaction_id", transaction.OriginalTransactionId);
    command.Parameters.AddWithValue("@user_id", userId);
    command.Parameters.AddWithValue("@product_id", transaction.ProductId);
    command.Parameters.AddWithValue("@app_account_token", transaction.AppAccountToken);
    command.Parameters.AddWithValue("@environment", transaction.Environment);
    command.Parameters.AddWithValue("@signed_payload_hash", transaction.SignedPayloadHash);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
    {
      return null;
    }

    var alreadyProcessed = reader.GetBoolean(1);
    var grantedCredits = reader.GetInt32(2);
    var balance = await GetCreditBalanceAsync(userId, cancellationToken).ConfigureAwait(false);
    return new StoreKitGrantResult(
      transaction.TransactionId,
      transaction.ProductId,
      grantedCredits,
      alreadyProcessed,
      balance
    );
  }

  public async Task<CreditBalance> GetCreditBalanceAsync(Guid userId, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    return await GetCreditBalanceAsync(connection, userId, cancellationToken).ConfigureAwait(false);
  }

  public async Task<CreditReservation?> TryReserveCreditsAsync(
    Guid userId,
    string jobId,
    int credits,
    DateTimeOffset expiresAt,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
      BEGIN TRANSACTION;

      DECLARE @granted int = 0;
      DECLARE @captured int = 0;
      DECLARE @reserved int = 0;
      DECLARE @reservation_id uniqueidentifier = NEWID();

      SELECT
        @granted = COALESCE(SUM(CASE WHEN kind = 'grant' THEN credits WHEN kind = 'reversal' THEN -credits ELSE 0 END), 0),
        @captured = COALESCE(SUM(CASE WHEN kind = 'capture' THEN credits ELSE 0 END), 0)
      FROM {{Schema}}.usage_ledger WITH (UPDLOCK, HOLDLOCK)
      WHERE user_id = @user_id;

      SELECT @reserved = COALESCE(SUM(credits), 0)
      FROM {{Schema}}.credit_reservations WITH (UPDLOCK, HOLDLOCK)
      WHERE user_id = @user_id AND status = 'reserved' AND expires_at > SYSUTCDATETIME();

      IF (@granted - @captured - @reserved) < @credits
      BEGIN
        ROLLBACK TRANSACTION;
        SELECT CAST(0 AS bit) AS reserved, @reservation_id AS reservation_id;
        RETURN;
      END

      INSERT INTO {{Schema}}.credit_reservations
        (reservation_id, user_id, job_id, credits, status, expires_at, created_at)
      VALUES
        (@reservation_id, @user_id, @job_id, @credits, 'reserved', @expires_at, SYSUTCDATETIME());

      INSERT INTO {{Schema}}.generation_ownership
        (job_id, user_id, reservation_id, status, created_at)
      VALUES
        (@job_id, @user_id, @reservation_id, 'queued', SYSUTCDATETIME());

      COMMIT TRANSACTION;
      SELECT CAST(1 AS bit) AS reserved, @reservation_id AS reservation_id;
      """;
    command.Parameters.AddWithValue("@user_id", userId);
    command.Parameters.AddWithValue("@job_id", jobId);
    command.Parameters.AddWithValue("@credits", credits);
    command.Parameters.AddWithValue("@expires_at", expiresAt);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !reader.GetBoolean(0))
    {
      return null;
    }

    return new CreditReservation(reader.GetGuid(1), userId, jobId, credits, "reserved", expiresAt, DateTimeOffset.UtcNow);
  }

  public Task CaptureReservationAsync(string jobId, CancellationToken cancellationToken) =>
    CompleteReservationAsync(jobId, "captured", "capture", cancellationToken);

  public Task ReleaseReservationAsync(string jobId, string reason, CancellationToken cancellationToken) =>
    CompleteReservationAsync(jobId, "released", "release", cancellationToken);

  public async Task<bool> UserOwnsGenerationAsync(Guid userId, string jobId, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SELECT COUNT_BIG(1)
      FROM {{Schema}}.generation_ownership
      WHERE user_id = @user_id AND job_id = @job_id;
      """;
    command.Parameters.AddWithValue("@user_id", userId);
    command.Parameters.AddWithValue("@job_id", jobId);
    var count = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    return count > 0;
  }

  public async Task ReverseTransactionAsync(string transactionId, CancellationToken cancellationToken)
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      BEGIN TRANSACTION;

      DECLARE @user_id uniqueidentifier;
      DECLARE @credits int;
      SELECT @user_id = tx.user_id, @credits = p.credits
      FROM {{Schema}}.iap_transactions tx
      INNER JOIN {{Schema}}.iap_products p ON p.product_id = tx.product_id
      WHERE tx.transaction_id = @transaction_id;

      IF @user_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM {{Schema}}.usage_ledger WHERE kind = 'reversal' AND reference_id = @transaction_id
      )
      BEGIN
        INSERT INTO {{Schema}}.usage_ledger
          (ledger_id, user_id, kind, credits, reference_id, created_at)
        VALUES
          (NEWID(), @user_id, 'reversal', @credits, @transaction_id, SYSUTCDATETIME());

        UPDATE {{Schema}}.iap_transactions
        SET status = 'reversed'
        WHERE transaction_id = @transaction_id;
      END

      COMMIT TRANSACTION;
      """;
    command.Parameters.AddWithValue("@transaction_id", transactionId);
    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  private async Task CompleteReservationAsync(
    string jobId,
    string reservationStatus,
    string ledgerKind,
    CancellationToken cancellationToken
  )
  {
    await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SET XACT_ABORT ON;
      BEGIN TRANSACTION;

      DECLARE @reservation_id uniqueidentifier;
      DECLARE @user_id uniqueidentifier;
      DECLARE @credits int;

      SELECT @reservation_id = reservation_id, @user_id = user_id, @credits = credits
      FROM {{Schema}}.credit_reservations WITH (UPDLOCK, HOLDLOCK)
      WHERE job_id = @job_id AND status = 'reserved';

      IF @reservation_id IS NOT NULL
      BEGIN
        UPDATE {{Schema}}.credit_reservations
        SET status = @reservation_status,
            captured_at = CASE WHEN @reservation_status = 'captured' THEN SYSUTCDATETIME() ELSE captured_at END,
            released_at = CASE WHEN @reservation_status = 'released' THEN SYSUTCDATETIME() ELSE released_at END
        WHERE reservation_id = @reservation_id;

        UPDATE {{Schema}}.generation_ownership
        SET status = @reservation_status
        WHERE job_id = @job_id;

        INSERT INTO {{Schema}}.usage_ledger
          (ledger_id, user_id, kind, credits, reference_id, created_at)
        VALUES
          (NEWID(), @user_id, @ledger_kind, CASE WHEN @ledger_kind = 'capture' THEN @credits ELSE 0 END, @job_id, SYSUTCDATETIME());
      END

      COMMIT TRANSACTION;
      """;
    command.Parameters.AddWithValue("@job_id", jobId);
    command.Parameters.AddWithValue("@reservation_status", reservationStatus);
    command.Parameters.AddWithValue("@ledger_kind", ledgerKind);
    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  private async Task<CreditBalance> GetCreditBalanceAsync(
    SqlConnection connection,
    Guid userId,
    CancellationToken cancellationToken
  )
  {
    await using var command = connection.CreateCommand();
    command.CommandText = $$"""
      SELECT
        COALESCE(SUM(CASE WHEN kind = 'grant' THEN credits WHEN kind = 'reversal' THEN -credits ELSE 0 END), 0) AS granted,
        COALESCE(SUM(CASE WHEN kind = 'capture' THEN credits ELSE 0 END), 0) AS captured
      FROM {{Schema}}.usage_ledger
      WHERE user_id = @user_id;

      SELECT COALESCE(SUM(credits), 0) AS reserved
      FROM {{Schema}}.credit_reservations
      WHERE user_id = @user_id AND status = 'reserved' AND expires_at > SYSUTCDATETIME();
      """;
    command.Parameters.AddWithValue("@user_id", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    var granted = 0;
    var captured = 0;
    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      granted = reader.GetInt32(0);
      captured = reader.GetInt32(1);
    }

    var reserved = 0;
    if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false) &&
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      reserved = reader.GetInt32(0);
    }

    return new CreditBalance(granted, captured, reserved, granted - captured - reserved);
  }

  private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
  {
    var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    return connection;
  }

  private static GifForgeUser ReadUser(SqlDataReader reader) =>
    new(
      reader.GetGuid(0),
      reader.GetString(1),
      reader.GetGuid(2),
      reader.GetDateTimeOffset(3),
      reader.GetDateTimeOffset(4),
      reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5)
    );

  private static RefreshTokenRecord ReadRefreshToken(SqlDataReader reader) =>
    new(
      reader.GetString(0),
      reader.GetGuid(1),
      reader.GetGuid(2),
      reader.GetDateTimeOffset(3),
      reader.GetDateTimeOffset(4),
      reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
      reader.IsDBNull(6) ? null : reader.GetString(6)
    );
}
