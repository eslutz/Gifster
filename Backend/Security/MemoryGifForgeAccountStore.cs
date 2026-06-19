using System.Collections.Concurrent;

namespace GifForge.Backend.Security;

public sealed class MemoryGifForgeAccountStore : IGifForgeAccountStore
{
  private readonly Lock sync = new();
  private readonly Dictionary<string, Guid> userIdsByAppleSubject = new(StringComparer.Ordinal);
  private readonly Dictionary<Guid, GifForgeUser> users = [];
  private readonly Dictionary<string, RefreshTokenRecord> refreshTokens = [];
  private readonly Dictionary<string, VerifiedStoreKitTransaction> transactions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Guid> transactionUserIds = new(StringComparer.Ordinal);
  private readonly Dictionary<Guid, List<LedgerEntry>> ledgerByUser = [];
  private readonly Dictionary<string, CreditReservation> reservationsByJobId = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Guid> generationOwners = new(StringComparer.Ordinal);

  private static readonly StoreKitProduct[] Products =
  [
    new("dev.ericslutz.gifforge.credits.10", 10, true),
    new("dev.ericslutz.gifforge.credits.25", 25, true),
    new("dev.ericslutz.gifforge.credits.55", 55, true)
  ];

  public Task<GifForgeUser> CreateAnonymousUserAsync(CancellationToken cancellationToken)
  {
    lock (sync)
    {
      var now = DateTimeOffset.UtcNow;
      var user = new GifForgeUser(
        Guid.NewGuid(),
        null,
        Guid.NewGuid(),
        now,
        now
      );
      users[user.UserId] = user;
      ledgerByUser[user.UserId] = [];
      return Task.FromResult(user);
    }
  }

  public Task<GifForgeUser> UpsertAppleUserAsync(AppleIdentity identity, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (userIdsByAppleSubject.TryGetValue(identity.Subject, out var existingId) &&
          users.TryGetValue(existingId, out var existing))
      {
        var updated = existing with
        {
          AppleSubject = identity.Subject,
          UpdatedAt = DateTimeOffset.UtcNow,
          DeletedAt = null
        };
        users[existingId] = updated;
        return Task.FromResult(updated);
      }

      var now = DateTimeOffset.UtcNow;
      var user = new GifForgeUser(
        Guid.NewGuid(),
        identity.Subject,
        Guid.NewGuid(),
        now,
        now
      );
      userIdsByAppleSubject[identity.Subject] = user.UserId;
      users[user.UserId] = user;
      ledgerByUser[user.UserId] = [];
      return Task.FromResult(user);
    }
  }

  public Task<GifForgeUser?> LinkAppleUserAsync(
    Guid currentUserId,
    AppleIdentity identity,
    DateTimeOffset linkedAt,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (!users.TryGetValue(currentUserId, out var current) || current.DeletedAt is not null)
      {
        return Task.FromResult<GifForgeUser?>(null);
      }

      if (current.AppleSubject is not null &&
          !string.Equals(current.AppleSubject, identity.Subject, StringComparison.Ordinal))
      {
        return Task.FromResult<GifForgeUser?>(null);
      }

      if (!userIdsByAppleSubject.TryGetValue(identity.Subject, out var targetUserId) ||
          !users.TryGetValue(targetUserId, out var target))
      {
        var linked = current with
        {
          AppleSubject = identity.Subject,
          UpdatedAt = linkedAt,
          DeletedAt = null
        };
        users[currentUserId] = linked;
        userIdsByAppleSubject[identity.Subject] = currentUserId;
        return Task.FromResult<GifForgeUser?>(linked);
      }

      if (targetUserId == currentUserId)
      {
        var updated = target with { UpdatedAt = linkedAt, DeletedAt = null };
        users[targetUserId] = updated;
        return Task.FromResult<GifForgeUser?>(updated);
      }

      MergeUser(currentUserId, targetUserId, linkedAt);
      var restoredTarget = target with { UpdatedAt = linkedAt, DeletedAt = null };
      users[targetUserId] = restoredTarget;
      return Task.FromResult<GifForgeUser?>(restoredTarget);
    }
  }

  public Task<GifForgeUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      users.TryGetValue(userId, out var user);
      return Task.FromResult(user?.DeletedAt is null ? user : null);
    }
  }

  public Task SaveRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      refreshTokens[token.TokenHash] = token;
      return Task.CompletedTask;
    }
  }

  public Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      refreshTokens.TryGetValue(tokenHash, out var token);
      if (token is not null && token.ExpiresAt <= DateTimeOffset.UtcNow)
      {
        return Task.FromResult<RefreshTokenRecord?>(null);
      }

      return Task.FromResult(token);
    }
  }

  public Task<RefreshTokenClaimResult> RotateRefreshTokenAsync(
    string tokenHash,
    string replacementTokenHash,
    DateTimeOffset rotatedAt,
    DateTimeOffset replacementExpiresAt,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (!refreshTokens.TryGetValue(tokenHash, out var token) || token.ExpiresAt <= DateTimeOffset.UtcNow)
      {
        return Task.FromResult(new RefreshTokenClaimResult(false, null));
      }

      if (token.RevokedAt is not null)
      {
        return Task.FromResult(new RefreshTokenClaimResult(false, token));
      }

      refreshTokens[tokenHash] = token with
      {
        RevokedAt = rotatedAt,
        ReplacedByTokenHash = replacementTokenHash
      };
      refreshTokens[replacementTokenHash] = new RefreshTokenRecord(
        replacementTokenHash,
        token.UserId,
        token.FamilyId,
        replacementExpiresAt,
        rotatedAt
      );
      return Task.FromResult(new RefreshTokenClaimResult(true, token));
    }
  }

  public Task RevokeRefreshTokenAsync(
    string tokenHash,
    DateTimeOffset revokedAt,
    string? replacedByTokenHash,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (refreshTokens.TryGetValue(tokenHash, out var token))
      {
        refreshTokens[tokenHash] = token with
        {
          RevokedAt = revokedAt,
          ReplacedByTokenHash = replacedByTokenHash
        };
      }

      return Task.CompletedTask;
    }
  }

  public Task RevokeRefreshTokenFamilyAsync(
    Guid familyId,
    DateTimeOffset revokedAt,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      foreach (var item in refreshTokens.Where(item => item.Value.FamilyId == familyId).ToArray())
      {
        refreshTokens[item.Key] = item.Value with { RevokedAt = item.Value.RevokedAt ?? revokedAt };
      }

      return Task.CompletedTask;
    }
  }

  public Task<IReadOnlyList<StoreKitProduct>> GetProductsAsync(CancellationToken cancellationToken) =>
    Task.FromResult<IReadOnlyList<StoreKitProduct>>(Products);

  public Task<StoreKitGrantResult?> GrantCreditsAsync(
    Guid userId,
    VerifiedStoreKitTransaction transaction,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (!users.ContainsKey(userId))
      {
        return Task.FromResult<StoreKitGrantResult?>(null);
      }

      var product = Products.FirstOrDefault(item =>
        item.Active &&
        string.Equals(item.ProductId, transaction.ProductId, StringComparison.Ordinal)
      );
      if (product is null)
      {
        return Task.FromResult<StoreKitGrantResult?>(null);
      }

      if (transactions.ContainsKey(transaction.TransactionId))
      {
        return Task.FromResult<StoreKitGrantResult?>(new StoreKitGrantResult(
          transaction.TransactionId,
          transaction.ProductId,
          0,
          true,
          BalanceFor(userId)
        ));
      }

      transactions[transaction.TransactionId] = transaction;
      transactionUserIds[transaction.TransactionId] = userId;
      Ledger(userId).Add(new LedgerEntry("grant", product.Credits, transaction.TransactionId));

      return Task.FromResult<StoreKitGrantResult?>(new StoreKitGrantResult(
        transaction.TransactionId,
        transaction.ProductId,
        product.Credits,
        false,
        BalanceFor(userId)
      ));
    }
  }

  public Task<CreditBalance> GetCreditBalanceAsync(Guid userId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      return Task.FromResult(BalanceFor(userId));
    }
  }

  public Task<CreditReservation?> TryReserveCreditsAsync(
    Guid userId,
    string jobId,
    int credits,
    DateTimeOffset expiresAt,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (!users.TryGetValue(userId, out var user) ||
          user.DeletedAt is not null ||
          BalanceFor(userId).AvailableCredits < credits)
      {
        return Task.FromResult<CreditReservation?>(null);
      }

      var now = DateTimeOffset.UtcNow;
      var reservation = new CreditReservation(
        Guid.NewGuid(),
        userId,
        jobId,
        credits,
        "reserved",
        expiresAt,
        now
      );
      reservationsByJobId[jobId] = reservation;
      generationOwners[jobId] = userId;
      return Task.FromResult<CreditReservation?>(reservation);
    }
  }

  public Task CaptureReservationAsync(string jobId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (reservationsByJobId.TryGetValue(jobId, out var reservation) &&
          reservation.Status == "reserved")
      {
        reservationsByJobId[jobId] = reservation with
        {
          Status = "captured",
          CapturedAt = DateTimeOffset.UtcNow
        };
        Ledger(reservation.UserId).Add(new LedgerEntry("capture", reservation.Credits, jobId));
      }

      return Task.CompletedTask;
    }
  }

  public Task ReleaseReservationAsync(string jobId, string reason, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (reservationsByJobId.TryGetValue(jobId, out var reservation) &&
          reservation.Status == "reserved")
      {
        reservationsByJobId[jobId] = reservation with
        {
          Status = "released",
          ReleasedAt = DateTimeOffset.UtcNow
        };
        Ledger(reservation.UserId).Add(new LedgerEntry("release", 0, reason));
      }

      return Task.CompletedTask;
    }
  }

  public Task<bool> UserOwnsGenerationAsync(Guid userId, string jobId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      return Task.FromResult(
        users.TryGetValue(userId, out var user) &&
        user.DeletedAt is null &&
        generationOwners.TryGetValue(jobId, out var ownerId) &&
        ownerId == userId
      );
    }
  }

  public Task ReverseTransactionAsync(string transactionId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (!transactions.TryGetValue(transactionId, out var transaction))
      {
        return Task.CompletedTask;
      }

      var user = transactionUserIds.TryGetValue(transactionId, out var userId) &&
        users.TryGetValue(userId, out var owner)
          ? owner
          : users.Values.FirstOrDefault(item => item.AppAccountToken == transaction.AppAccountToken);
      var product = Products.FirstOrDefault(item => item.ProductId == transaction.ProductId);
      if (user is not null && product is not null)
      {
        Ledger(user.UserId).Add(new LedgerEntry("reversal", product.Credits, transactionId));
      }

      return Task.CompletedTask;
    }
  }

  public Task ApplySignInWithAppleNotificationAsync(
    SignInWithAppleNotification notification,
    DateTimeOffset receivedAt,
    CancellationToken cancellationToken
  )
  {
    lock (sync)
    {
      if (!userIdsByAppleSubject.TryGetValue(notification.Subject, out var userId) ||
          !users.TryGetValue(userId, out var user))
      {
        return Task.CompletedTask;
      }

      var eventType = notification.EventType.ToLowerInvariant();
      if (eventType is "email-enabled" or "email-disabled")
      {
        users[userId] = user with { UpdatedAt = receivedAt };
        return Task.CompletedTask;
      }

      if (eventType is "consent-revoked" or "account-delete" or "account-deleted")
      {
        users[userId] = user with
        {
          UpdatedAt = receivedAt,
          DeletedAt = user.DeletedAt ?? receivedAt
        };

        foreach (var item in refreshTokens.Where(item => item.Value.UserId == userId).ToArray())
        {
          refreshTokens[item.Key] = item.Value with { RevokedAt = item.Value.RevokedAt ?? receivedAt };
        }
      }

      return Task.CompletedTask;
    }
  }

  private List<LedgerEntry> Ledger(Guid userId)
  {
    if (!ledgerByUser.TryGetValue(userId, out var ledger))
    {
      ledger = [];
      ledgerByUser[userId] = ledger;
    }

    return ledger;
  }

  private void MergeUser(Guid sourceUserId, Guid targetUserId, DateTimeOffset mergedAt)
  {
    if (ledgerByUser.TryGetValue(sourceUserId, out var sourceLedger))
    {
      Ledger(targetUserId).AddRange(sourceLedger);
      ledgerByUser.Remove(sourceUserId);
    }

    foreach (var item in reservationsByJobId.Where(item => item.Value.UserId == sourceUserId).ToArray())
    {
      reservationsByJobId[item.Key] = item.Value with { UserId = targetUserId };
    }

    foreach (var item in generationOwners.Where(item => item.Value == sourceUserId).ToArray())
    {
      generationOwners[item.Key] = targetUserId;
    }

    foreach (var item in transactionUserIds.Where(item => item.Value == sourceUserId).ToArray())
    {
      transactionUserIds[item.Key] = targetUserId;
    }

    foreach (var item in refreshTokens.Where(item => item.Value.UserId == sourceUserId).ToArray())
    {
      refreshTokens[item.Key] = item.Value with { RevokedAt = item.Value.RevokedAt ?? mergedAt };
    }

    if (users.TryGetValue(sourceUserId, out var source))
    {
      users[sourceUserId] = source with { UpdatedAt = mergedAt, DeletedAt = source.DeletedAt ?? mergedAt };
    }
  }

  private CreditBalance BalanceFor(Guid userId)
  {
    var ledger = Ledger(userId);
    var grants = ledger.Where(item => item.Kind == "grant").Sum(item => item.Credits) -
                 ledger.Where(item => item.Kind == "reversal").Sum(item => item.Credits);
    var captured = ledger.Where(item => item.Kind == "capture").Sum(item => item.Credits);
    var reserved = reservationsByJobId.Values
      .Where(item =>
        item.UserId == userId &&
        item.Status == "reserved" &&
        item.ExpiresAt > DateTimeOffset.UtcNow)
      .Sum(item => item.Credits);
    return new CreditBalance(grants, captured, reserved, grants - captured - reserved);
  }

  private sealed record LedgerEntry(string Kind, int Credits, string Reference);
}
