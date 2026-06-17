using System.Net.Http.Headers;

namespace GifForge.Backend.Security;

public sealed class AccountSecurityService
{
  private readonly AccountSecurityOptions options;
  private readonly IAppleIdentityTokenValidator appleIdentityTokenValidator;
  private readonly IStoreKitTransactionVerifier storeKitTransactionVerifier;
  private readonly IAppStoreServerNotificationVerifier appStoreServerNotificationVerifier;
  private readonly IBackendTokenService backendTokenService;
  private readonly IGifForgeAccountStore accountStore;

  public AccountSecurityService(
    AccountSecurityOptions options,
    IAppleIdentityTokenValidator appleIdentityTokenValidator,
    IStoreKitTransactionVerifier storeKitTransactionVerifier,
    IAppStoreServerNotificationVerifier appStoreServerNotificationVerifier,
    IBackendTokenService backendTokenService,
    IGifForgeAccountStore accountStore
  )
  {
    this.options = options;
    this.appleIdentityTokenValidator = appleIdentityTokenValidator;
    this.storeKitTransactionVerifier = storeKitTransactionVerifier;
    this.appStoreServerNotificationVerifier = appStoreServerNotificationVerifier;
    this.backendTokenService = backendTokenService;
    this.accountStore = accountStore;
  }

  public bool AuthRequired => options.AuthRequired;

  public async Task<AuthTokenResponse?> SignInWithAppleAsync(
    AppleAuthRequest request,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(request.IdentityToken))
    {
      return null;
    }

    var identity = await appleIdentityTokenValidator
      .ValidateAsync(request.IdentityToken, request.Nonce, cancellationToken)
      .ConfigureAwait(false);
    if (identity is null)
    {
      return null;
    }

    var user = await accountStore.UpsertAppleUserAsync(identity, cancellationToken).ConfigureAwait(false);
    return await IssueSessionAsync(user, cancellationToken).ConfigureAwait(false);
  }

  public async Task<AuthTokenResponse?> RefreshAsync(
    RefreshTokenRequest request,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
      return null;
    }

    var tokenHash = backendTokenService.HashToken(request.RefreshToken);
    var refreshToken = SecurityTokenHelpers.NewOpaqueToken();
    var refreshTokenHash = backendTokenService.HashToken(refreshToken);
    var refreshExpiresAt = DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime);
    var claim = await accountStore
      .RotateRefreshTokenAsync(
        tokenHash,
        refreshTokenHash,
        DateTimeOffset.UtcNow,
        refreshExpiresAt,
        cancellationToken
      )
      .ConfigureAwait(false);
    if (claim.Token is null)
    {
      return null;
    }

    if (!claim.Claimed)
    {
      await accountStore
        .RevokeRefreshTokenFamilyAsync(claim.Token.FamilyId, DateTimeOffset.UtcNow, cancellationToken)
        .ConfigureAwait(false);
      return null;
    }

    var user = await accountStore.GetUserAsync(claim.Token.UserId, cancellationToken).ConfigureAwait(false);
    if (user is null)
    {
      return null;
    }

    var access = backendTokenService.IssueAccessToken(user);
    return new AuthTokenResponse(
      user.UserId.ToString("D"),
      user.AppAccountToken.ToString("D"),
      access.Token,
      access.ExpiresAt,
      refreshToken,
      refreshExpiresAt
    );
  }

  public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
      return;
    }

    await accountStore
      .RevokeRefreshTokenAsync(backendTokenService.HashToken(request.RefreshToken), DateTimeOffset.UtcNow, null, cancellationToken)
      .ConfigureAwait(false);
  }

  public AuthenticatedUser? Authenticate(HttpContext context)
  {
    var authorization = context.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorization) ||
        !AuthenticationHeaderValue.TryParse(authorization, out var header) ||
        !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(header.Parameter))
    {
      return null;
    }

    return backendTokenService.ValidateAccessToken(header.Parameter);
  }

  public async Task<MeResponse?> ProfileAsync(AuthenticatedUser user, CancellationToken cancellationToken)
  {
    var stored = await accountStore.GetUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
    return stored is null
      ? null
      : new MeResponse(stored.UserId.ToString("D"), stored.AppAccountToken.ToString("D"));
  }

  public async Task<CreditBalanceResponse?> CreditsAsync(AuthenticatedUser user, CancellationToken cancellationToken)
  {
    if (await accountStore.GetUserAsync(user.UserId, cancellationToken).ConfigureAwait(false) is null)
    {
      return null;
    }

    return ToResponse(await accountStore.GetCreditBalanceAsync(user.UserId, cancellationToken).ConfigureAwait(false));
  }

  public async Task<IapProductsResponse> ProductsAsync(CancellationToken cancellationToken)
  {
    var products = await accountStore.GetProductsAsync(cancellationToken).ConfigureAwait(false);
    return new IapProductsResponse(
      products
        .Where(item => item.Active)
        .Select(item => new IapProductResponse(item.ProductId, item.Credits, item.Active))
        .ToArray()
    );
  }

  public async Task<IapTransactionSubmissionResponse?> ProcessTransactionAsync(
    AuthenticatedUser authenticatedUser,
    IapTransactionSubmissionRequest request,
    CancellationToken cancellationToken
  )
  {
    var user = await accountStore.GetUserAsync(authenticatedUser.UserId, cancellationToken).ConfigureAwait(false);
    if (user is null)
    {
      return null;
    }

    var transaction = await storeKitTransactionVerifier
      .VerifyAsync(request, user.AppAccountToken, cancellationToken)
      .ConfigureAwait(false);
    if (transaction is null)
    {
      return null;
    }

    var result = await accountStore
      .GrantCreditsAsync(user.UserId, transaction, cancellationToken)
      .ConfigureAwait(false);
    return result is null
      ? null
      : new IapTransactionSubmissionResponse(
        result.TransactionId,
        result.ProductId,
        result.GrantedCredits,
        result.AlreadyProcessed,
        result.Balance.AvailableCredits
      );
  }

  public Task<CreditReservation?> ReserveGenerationCreditAsync(
    AuthenticatedUser user,
    string jobId,
    CancellationToken cancellationToken
  ) =>
    accountStore.TryReserveCreditsAsync(
      user.UserId,
      jobId,
      1,
      DateTimeOffset.UtcNow.Add(options.ReservationLifetime),
      cancellationToken
    );

  public Task CaptureGenerationCreditAsync(string jobId, CancellationToken cancellationToken) =>
    accountStore.CaptureReservationAsync(jobId, cancellationToken);

  public Task ReleaseGenerationCreditAsync(string jobId, string reason, CancellationToken cancellationToken) =>
    accountStore.ReleaseReservationAsync(jobId, reason, cancellationToken);

  public Task<bool> UserOwnsGenerationAsync(
    AuthenticatedUser user,
    string jobId,
    CancellationToken cancellationToken
  ) =>
    accountStore.UserOwnsGenerationAsync(user.UserId, jobId, cancellationToken);

  public async Task ProcessAppStoreNotificationAsync(
    AppStoreServerNotificationRequest request,
    CancellationToken cancellationToken
  )
  {
    var transactionId = await appStoreServerNotificationVerifier
      .ReversedTransactionIdAsync(request, cancellationToken)
      .ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(transactionId))
    {
      return;
    }

    await accountStore.ReverseTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false);
  }

  private async Task<AuthTokenResponse> IssueSessionAsync(
    GifForgeUser user,
    CancellationToken cancellationToken,
    Guid? familyId = null,
    string? refreshToken = null,
    string? refreshTokenHash = null
  )
  {
    var access = backendTokenService.IssueAccessToken(user);
    refreshToken ??= SecurityTokenHelpers.NewOpaqueToken();
    refreshTokenHash ??= backendTokenService.HashToken(refreshToken);
    var refreshExpiresAt = DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime);
    await accountStore.SaveRefreshTokenAsync(
      new RefreshTokenRecord(
        refreshTokenHash,
        user.UserId,
        familyId ?? Guid.NewGuid(),
        refreshExpiresAt,
        DateTimeOffset.UtcNow
      ),
      cancellationToken
    ).ConfigureAwait(false);

    return new AuthTokenResponse(
      user.UserId.ToString("D"),
      user.AppAccountToken.ToString("D"),
      access.Token,
      access.ExpiresAt,
      refreshToken,
      refreshExpiresAt
    );
  }

  private static CreditBalanceResponse ToResponse(CreditBalance balance) =>
    new(
      balance.GrantedCredits,
      balance.CapturedDebits,
      balance.ReservedCredits,
      balance.AvailableCredits
    );
}
