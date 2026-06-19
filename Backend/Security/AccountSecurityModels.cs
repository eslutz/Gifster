using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace GifForge.Backend.Security;

public sealed record AppleAuthRequest(string IdentityToken, string? Nonce);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthTokenResponse(
  string UserId,
  string AppAccountToken,
  string AccessToken,
  DateTimeOffset AccessTokenExpiresAt,
  string RefreshToken,
  DateTimeOffset RefreshTokenExpiresAt
);

public sealed record MeResponse(
  string UserId,
  string AppAccountToken,
  string AccountKind,
  string? RecoveryProvider
);

public sealed record CreditBalanceResponse(
  int GrantedCredits,
  int CapturedDebits,
  int ReservedCredits,
  int AvailableCredits
);

public sealed record IapProductResponse(string ProductId, int Credits, bool Active);

public sealed record IapProductsResponse(IReadOnlyList<IapProductResponse> Products);

public sealed record IapTransactionSubmissionRequest(string ProductId, string SignedTransaction);

public sealed record IapTransactionSubmissionResponse(
  string TransactionId,
  string ProductId,
  int GrantedCredits,
  bool AlreadyProcessed,
  int AvailableCredits
);

public sealed record AppStoreServerNotificationRequest(string SignedPayload);

public sealed record AppStoreServerNotificationResponse(string Status);

public sealed record SignInWithAppleNotificationRequest(string Payload);

public sealed record SignInWithAppleNotificationResponse(string Status);

public sealed record AppleIdentity(string Subject, string? Email);

public sealed record SignInWithAppleNotification(string EventType, string Subject, string? Email);

public sealed record GifForgeUser(
  Guid UserId,
  string? AppleSubject,
  Guid AppAccountToken,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? DeletedAt = null
);

public sealed record AuthenticatedUser(Guid UserId, string? AppleSubject);

public sealed record AuthSession(
  GifForgeUser User,
  string AccessToken,
  DateTimeOffset AccessTokenExpiresAt,
  string RefreshToken,
  DateTimeOffset RefreshTokenExpiresAt
);

public sealed record RefreshTokenRecord(
  string TokenHash,
  Guid UserId,
  Guid FamilyId,
  DateTimeOffset ExpiresAt,
  DateTimeOffset CreatedAt,
  DateTimeOffset? RevokedAt = null,
  string? ReplacedByTokenHash = null
);

public sealed record RefreshTokenClaimResult(bool Claimed, RefreshTokenRecord? Token);

public sealed record CreditBalance(
  int GrantedCredits,
  int CapturedDebits,
  int ReservedCredits,
  int AvailableCredits
);

public sealed record CreditReservation(
  Guid ReservationId,
  Guid UserId,
  string JobId,
  int Credits,
  string Status,
  DateTimeOffset ExpiresAt,
  DateTimeOffset CreatedAt,
  DateTimeOffset? CapturedAt = null,
  DateTimeOffset? ReleasedAt = null
);

public sealed record StoreKitProduct(string ProductId, int Credits, bool Active);

public sealed record VerifiedStoreKitTransaction(
  string TransactionId,
  string OriginalTransactionId,
  string ProductId,
  Guid AppAccountToken,
  string Environment,
  string SignedPayloadHash
);

public sealed record StoreKitGrantResult(
  string TransactionId,
  string ProductId,
  int GrantedCredits,
  bool AlreadyProcessed,
  CreditBalance Balance
);

public interface IAppleIdentityTokenValidator
{
  Task<AppleIdentity?> ValidateAsync(string identityToken, string? nonce, CancellationToken cancellationToken);
}

public interface IStoreKitTransactionVerifier
{
  Task<VerifiedStoreKitTransaction?> VerifyAsync(
    IapTransactionSubmissionRequest request,
    Guid expectedAppAccountToken,
    CancellationToken cancellationToken
  );
}

public interface IAppStoreServerNotificationVerifier
{
  Task<string?> ReversedTransactionIdAsync(
    AppStoreServerNotificationRequest request,
    CancellationToken cancellationToken
  );
}

public interface ISignInWithAppleNotificationVerifier
{
  Task<SignInWithAppleNotification?> VerifyAsync(
    SignInWithAppleNotificationRequest request,
    CancellationToken cancellationToken
  );
}

public interface IBackendTokenService
{
  (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(GifForgeUser user);
  AuthenticatedUser? ValidateAccessToken(string token);
  string HashToken(string token);
}

public interface IGifForgeAccountStore
{
  Task<GifForgeUser> CreateAnonymousUserAsync(CancellationToken cancellationToken);
  Task<GifForgeUser> UpsertAppleUserAsync(AppleIdentity identity, CancellationToken cancellationToken);
  Task<GifForgeUser?> LinkAppleUserAsync(Guid currentUserId, AppleIdentity identity, DateTimeOffset linkedAt, CancellationToken cancellationToken);
  Task<GifForgeUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
  Task SaveRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken);
  Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);
  Task<RefreshTokenClaimResult> RotateRefreshTokenAsync(
    string tokenHash,
    string replacementTokenHash,
    DateTimeOffset rotatedAt,
    DateTimeOffset replacementExpiresAt,
    CancellationToken cancellationToken
  );
  Task RevokeRefreshTokenAsync(string tokenHash, DateTimeOffset revokedAt, string? replacedByTokenHash, CancellationToken cancellationToken);
  Task RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken);
  Task<IReadOnlyList<StoreKitProduct>> GetProductsAsync(CancellationToken cancellationToken);
  Task<StoreKitGrantResult?> GrantCreditsAsync(
    Guid userId,
    VerifiedStoreKitTransaction transaction,
    CancellationToken cancellationToken
  );
  Task<CreditBalance> GetCreditBalanceAsync(Guid userId, CancellationToken cancellationToken);
  Task<CreditReservation?> TryReserveCreditsAsync(
    Guid userId,
    string jobId,
    int credits,
    DateTimeOffset expiresAt,
    CancellationToken cancellationToken
  );
  Task CaptureReservationAsync(string jobId, CancellationToken cancellationToken);
  Task ReleaseReservationAsync(string jobId, string reason, CancellationToken cancellationToken);
  Task<bool> UserOwnsGenerationAsync(Guid userId, string jobId, CancellationToken cancellationToken);
  Task ReverseTransactionAsync(string transactionId, CancellationToken cancellationToken);
  Task ApplySignInWithAppleNotificationAsync(
    SignInWithAppleNotification notification,
    DateTimeOffset receivedAt,
    CancellationToken cancellationToken
  );
}

public sealed class AccountSecurityOptions
{
  public bool AuthRequired { get; init; }
  public bool AuthDemoBypassEnabled { get; init; }
  public bool IapDemoBypassEnabled { get; init; }
  public string Issuer { get; init; } = "gifforge-backend";
  public string Audience { get; init; } = "gifforge-ios";
  public string SigningKey { get; init; } = "dev-only-gifforge-auth-signing-key-change-before-production";
  public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
  public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
  public TimeSpan ReservationLifetime { get; init; } = TimeSpan.FromHours(1);
  public string AppleIdentityIssuer { get; init; } = "https://appleid.apple.com";
  public string AppleIdentityJwksUrl { get; init; } = "https://appleid.apple.com/auth/keys";
  public IReadOnlyList<string> AppleIdentityAudiences { get; init; } = [];
  public string AppStoreBundleId { get; init; } = "dev.ericslutz.gifforge";
  public string? AppStoreJwsRootCertificatePem { get; init; }
  public int AuthRateLimitMax { get; init; } = 30;
  public int AppAttestRateLimitMax { get; init; } = 60;
  public int PurchaseRateLimitMax { get; init; } = 30;
  public int GenerationRateLimitMax { get; init; } = 20;
  public int GenerationStatusRateLimitMax { get; init; } = 120;
  public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(1);

  public static AccountSecurityOptions FromConfiguration(IConfiguration configuration) =>
    new()
    {
      AuthRequired = string.Equals(configuration["GIFFORGE_AUTH_REQUIRED"], "true", StringComparison.OrdinalIgnoreCase),
      AuthDemoBypassEnabled = string.Equals(configuration["GIFFORGE_AUTH_DEMO_BYPASS"], "true", StringComparison.OrdinalIgnoreCase),
      IapDemoBypassEnabled = string.Equals(configuration["GIFFORGE_IAP_DEMO_BYPASS"], "true", StringComparison.OrdinalIgnoreCase),
      Issuer = configuration["GIFFORGE_AUTH_TOKEN_ISSUER"] ?? "gifforge-backend",
      Audience = configuration["GIFFORGE_AUTH_TOKEN_AUDIENCE"] ?? "gifforge-ios",
      SigningKey = string.IsNullOrWhiteSpace(configuration["GIFFORGE_AUTH_SIGNING_KEY"])
        ? "dev-only-gifforge-auth-signing-key-change-before-production"
        : configuration["GIFFORGE_AUTH_SIGNING_KEY"]!,
      AccessTokenLifetime = Minutes(configuration["GIFFORGE_AUTH_ACCESS_TOKEN_MINUTES"], 15),
      RefreshTokenLifetime = Days(configuration["GIFFORGE_AUTH_REFRESH_TOKEN_DAYS"], 30),
      ReservationLifetime = Minutes(configuration["GIFFORGE_CREDIT_RESERVATION_MINUTES"], 60),
      AppleIdentityIssuer = configuration["GIFFORGE_APPLE_ID_TOKEN_ISSUER"] ?? "https://appleid.apple.com",
      AppleIdentityJwksUrl = configuration["GIFFORGE_APPLE_ID_TOKEN_JWKS_URL"] ?? "https://appleid.apple.com/auth/keys",
      AppleIdentityAudiences = SplitCsv(
        configuration["GIFFORGE_APPLE_ID_TOKEN_AUDIENCES"] ??
        configuration["GIFFORGE_APPLE_CLIENT_ID"] ??
        configuration["GIFFORGE_APPLE_BUNDLE_ID"] ??
        "dev.ericslutz.gifforge"
      ),
      AppStoreBundleId = configuration["GIFFORGE_APP_STORE_BUNDLE_ID"] ?? "dev.ericslutz.gifforge",
      AppStoreJwsRootCertificatePem = configuration["GIFFORGE_APP_STORE_JWS_ROOT_CERTIFICATE_PEM"],
      AuthRateLimitMax = PositiveInt(configuration["GIFFORGE_RATE_LIMIT_AUTH_MAX"], 30),
      AppAttestRateLimitMax = PositiveInt(configuration["GIFFORGE_RATE_LIMIT_APP_ATTEST_MAX"], 60),
      PurchaseRateLimitMax = PositiveInt(configuration["GIFFORGE_RATE_LIMIT_PURCHASE_MAX"], 30),
      GenerationRateLimitMax = PositiveInt(configuration["GIFFORGE_RATE_LIMIT_GENERATION_MAX"], 20),
      GenerationStatusRateLimitMax = PositiveInt(configuration["GIFFORGE_RATE_LIMIT_GENERATION_STATUS_MAX"], 120),
      RateLimitWindow = Seconds(configuration["GIFFORGE_RATE_LIMIT_WINDOW_SECONDS"], 60)
    };

  private static TimeSpan Minutes(string? value, int fallback) =>
    int.TryParse(value, out var minutes) && minutes > 0
      ? TimeSpan.FromMinutes(minutes)
      : TimeSpan.FromMinutes(fallback);

  private static TimeSpan Days(string? value, int fallback) =>
    int.TryParse(value, out var days) && days > 0
      ? TimeSpan.FromDays(days)
      : TimeSpan.FromDays(fallback);

  private static TimeSpan Seconds(string? value, int fallback) =>
    int.TryParse(value, out var seconds) && seconds > 0
      ? TimeSpan.FromSeconds(seconds)
      : TimeSpan.FromSeconds(fallback);

  private static int PositiveInt(string? value, int fallback) =>
    int.TryParse(value, out var parsed) && parsed > 0
      ? parsed
      : fallback;

  private static string[] SplitCsv(string value) =>
    value
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Where(item => !string.IsNullOrWhiteSpace(item))
      .Distinct(StringComparer.Ordinal)
      .ToArray();
}

public static class SecurityTokenHelpers
{
  public static string NewOpaqueToken(int byteCount = 48) =>
    Base64Url(RandomNumberGenerator.GetBytes(byteCount));

  public static string Sha256Base64Url(string value) =>
    Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

  public static string Sha256HexLowercase(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

  public static string Base64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');

  public static byte[] DecodeBase64Url(string value)
  {
    var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
    var padding = normalized.Length % 4;
    if (padding > 0)
    {
      normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
    }

    return Convert.FromBase64String(normalized);
  }
}
