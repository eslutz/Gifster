using System.Collections.Concurrent;

namespace GifForge.Backend.Security;

public sealed class MemoryGifForgeAccountStore : IGifForgeAccountStore
{
  private readonly Lock sync = new();
  private readonly Dictionary<string, Guid> userIdsByAppleSubject = new(StringComparer.Ordinal);
  private readonly Dictionary<Guid, GifForgeUser> users = [];
  private readonly Dictionary<string, RefreshTokenRecord> refreshTokens = [];
  private readonly Dictionary<string, VerifiedStoreKitTransaction> transactions = new(StringComparer.Ordinal);
  private readonly Dictionary<Guid, List<LedgerEntry>> ledgerByUser = [];
  private readonly Dictionary<string, CreditReservation> reservationsByJobId = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Guid> generationOwners = new(StringComparer.Ordinal);

  private static readonly StoreKitProduct[] Products =
  [
    new("dev.ericslutz.gifforge.credits.10", 10, true),
    new("dev.ericslutz.gifforge.credits.25", 25, true),
    new("dev.ericslutz.gifforge.credits.60", 60, true)
  ];

  public Task<GifForgeUser> UpsertAppleUserAsync(AppleIdentity identity, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      if (userIdsByAppleSubject.TryGetValue(identity.Subject, out var existingId) &&
          users.TryGetValue(existingId, out var existing))
      {
        var updated = existing with { UpdatedAt = DateTimeOffset.UtcNow };
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

  public Task<GifForgeUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
  {
    lock (sync)
    {
      users.TryGetValue(userId, out var user);
      return Task.FromResult(user);
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
      if (!users.ContainsKey(userId) || BalanceFor(userId).AvailableCredits < credits)
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
      return Task.FromResult(generationOwners.TryGetValue(jobId, out var ownerId) && ownerId == userId);
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

      var user = users.Values.FirstOrDefault(item => item.AppAccountToken == transaction.AppAccountToken);
      var product = Products.FirstOrDefault(item => item.ProductId == transaction.ProductId);
      if (user is not null && product is not null)
      {
        Ledger(user.UserId).Add(new LedgerEntry("reversal", product.Credits, transactionId));
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
