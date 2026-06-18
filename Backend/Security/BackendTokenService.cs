using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GifForge.Backend.Security;

public sealed class BackendTokenService : IBackendTokenService
{
  private readonly AccountSecurityOptions options;
  private readonly byte[] signingKey;

  public BackendTokenService(AccountSecurityOptions options)
  {
    this.options = options;
    signingKey = Encoding.UTF8.GetBytes(options.SigningKey);
  }

  public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(GifForgeUser user)
  {
    var expiresAt = DateTimeOffset.UtcNow.Add(options.AccessTokenLifetime);
    var header = SecurityTokenHelpers.Base64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
    var payloadJson =
      $$"""{"iss":"{{EscapeJson(options.Issuer)}}","aud":"{{EscapeJson(options.Audience)}}","sub":"{{user.UserId:D}}","appleSub":"{{EscapeJson(user.AppleSubject)}}","exp":{{expiresAt.ToUnixTimeSeconds()}}}""";
    var payload = SecurityTokenHelpers.Base64Url(Encoding.UTF8.GetBytes(payloadJson));
    var signature = Signature($"{header}.{payload}");
    return ($"{header}.{payload}.{signature}", expiresAt);
  }

  public AuthenticatedUser? ValidateAccessToken(string token)
  {
    var parts = token.Split('.');
    if (parts.Length != 3)
    {
      return null;
    }

    var signed = $"{parts[0]}.{parts[1]}";
    var expected = Signature(signed);
    if (!FixedTimeEquals(expected, parts[2]))
    {
      return null;
    }

    JsonDocument payload;
    try
    {
      payload = JsonDocument.Parse(
        Encoding.UTF8.GetString(SecurityTokenHelpers.DecodeBase64Url(parts[1]))
      );
    }
    catch (Exception error) when (error is JsonException or FormatException)
    {
      return null;
    }

    using (payload)
    {
      var root = payload.RootElement;
      var issuer = root.TryGetProperty("iss", out var issuerProperty) ? issuerProperty.GetString() : null;
      var audience = root.TryGetProperty("aud", out var audienceProperty) ? audienceProperty.GetString() : null;
      var subject = root.TryGetProperty("sub", out var subjectProperty) ? subjectProperty.GetString() : null;
      var appleSubject = root.TryGetProperty("appleSub", out var appleSubjectProperty)
        ? appleSubjectProperty.GetString()
        : null;
      var expiresAt = root.TryGetProperty("exp", out var expiresAtProperty)
        ? expiresAtProperty.GetInt64()
        : 0;

      if (issuer != options.Issuer ||
          audience != options.Audience ||
          expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
          !Guid.TryParse(subject, out var userId) ||
          string.IsNullOrWhiteSpace(appleSubject))
      {
        return null;
      }

      return new AuthenticatedUser(userId, appleSubject);
    }

  }

  private static string EscapeJson(string value)
  {
    var builder = new StringBuilder(value.Length);
    foreach (var character in value)
    {
      switch (character)
      {
        case '\\':
          builder.Append(@"\\");
          break;
        case '"':
          builder.Append("\\\"");
          break;
        case '\b':
          builder.Append(@"\b");
          break;
        case '\f':
          builder.Append(@"\f");
          break;
        case '\n':
          builder.Append(@"\n");
          break;
        case '\r':
          builder.Append(@"\r");
          break;
        case '\t':
          builder.Append(@"\t");
          break;
        default:
          if (char.IsControl(character))
          {
            builder.Append("\\u");
            builder.Append(((int)character).ToString("x4"));
          }
          else
          {
            builder.Append(character);
          }
          break;
      }
    }

    return builder.ToString();
  }

  public string HashToken(string token) =>
    SecurityTokenHelpers.Sha256Base64Url(token);

  private string Signature(string signed)
  {
    using var hmac = new HMACSHA256(signingKey);
    return SecurityTokenHelpers.Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed)));
  }

  private static bool FixedTimeEquals(string left, string right)
  {
    try
    {
      var leftBytes = SecurityTokenHelpers.DecodeBase64Url(left);
      var rightBytes = SecurityTokenHelpers.DecodeBase64Url(right);
      return leftBytes.Length == rightBytes.Length &&
             CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
    catch (FormatException)
    {
      return false;
    }
  }
}

public sealed class DemoAppleIdentityTokenValidator : IAppleIdentityTokenValidator
{
  public Task<AppleIdentity?> ValidateAsync(string identityToken, string? nonce, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(identityToken) ||
        !identityToken.StartsWith("demo.", StringComparison.Ordinal))
    {
      return Task.FromResult<AppleIdentity?>(null);
    }

    return Task.FromResult<AppleIdentity?>(new AppleIdentity(identityToken, null));
  }
}

public sealed class UnavailableAppleIdentityTokenValidator : IAppleIdentityTokenValidator
{
  public Task<AppleIdentity?> ValidateAsync(string identityToken, string? nonce, CancellationToken cancellationToken) =>
    Task.FromResult<AppleIdentity?>(null);
}

public sealed class DemoStoreKitTransactionVerifier : IStoreKitTransactionVerifier
{
  public Task<VerifiedStoreKitTransaction?> VerifyAsync(
    IapTransactionSubmissionRequest request,
    Guid expectedAppAccountToken,
    CancellationToken cancellationToken
  )
  {
    var parts = request.SignedTransaction.Split(':');
    if (parts.Length != 4 ||
        parts[0] != "demo" ||
        parts[2] != request.ProductId ||
        !Guid.TryParse(parts[3], out var appAccountToken) ||
        appAccountToken != expectedAppAccountToken)
    {
      return Task.FromResult<VerifiedStoreKitTransaction?>(null);
    }

    return Task.FromResult<VerifiedStoreKitTransaction?>(new VerifiedStoreKitTransaction(
      parts[1],
      parts[1],
      request.ProductId,
      appAccountToken,
      "Sandbox",
      SecurityTokenHelpers.Sha256Base64Url(request.SignedTransaction)
    ));
  }
}

public sealed class UnavailableStoreKitTransactionVerifier : IStoreKitTransactionVerifier
{
  public Task<VerifiedStoreKitTransaction?> VerifyAsync(
    IapTransactionSubmissionRequest request,
    Guid expectedAppAccountToken,
    CancellationToken cancellationToken
  ) =>
    Task.FromResult<VerifiedStoreKitTransaction?>(null);
}

public sealed class DemoAppStoreServerNotificationVerifier : IAppStoreServerNotificationVerifier
{
  public Task<string?> ReversedTransactionIdAsync(
    AppStoreServerNotificationRequest request,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(request.SignedPayload) ||
        !request.SignedPayload.StartsWith("demo:refund:", StringComparison.Ordinal))
    {
      return Task.FromResult<string?>(null);
    }

    return Task.FromResult<string?>(request.SignedPayload["demo:refund:".Length..]);
  }
}

public sealed class UnavailableAppStoreServerNotificationVerifier : IAppStoreServerNotificationVerifier
{
  public Task<string?> ReversedTransactionIdAsync(
    AppStoreServerNotificationRequest request,
    CancellationToken cancellationToken
  ) =>
    Task.FromResult<string?>(null);
}

public sealed class DemoSignInWithAppleNotificationVerifier : ISignInWithAppleNotificationVerifier
{
  public Task<SignInWithAppleNotification?> VerifyAsync(
    SignInWithAppleNotificationRequest request,
    CancellationToken cancellationToken
  )
  {
    if (string.IsNullOrWhiteSpace(request.Payload) ||
        !request.Payload.StartsWith("demo:", StringComparison.Ordinal))
    {
      return Task.FromResult<SignInWithAppleNotification?>(null);
    }

    var parts = request.Payload.Split(':', 4);
    if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
    {
      return Task.FromResult<SignInWithAppleNotification?>(null);
    }

    var eventType = parts[1].ToLowerInvariant();
    if (eventType is not ("email-enabled" or "email-disabled" or "consent-revoked" or "account-delete" or "account-deleted"))
    {
      return Task.FromResult<SignInWithAppleNotification?>(null);
    }

    var email = parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null;
    return Task.FromResult<SignInWithAppleNotification?>(new SignInWithAppleNotification(eventType, parts[2], email));
  }
}
