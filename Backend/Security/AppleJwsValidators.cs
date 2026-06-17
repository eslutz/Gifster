using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace GifForge.Backend.Security;

public sealed class AppleIdentityTokenValidator : IAppleIdentityTokenValidator
{
  private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

  private readonly AccountSecurityOptions options;
  private readonly HttpClient httpClient;
  private readonly SemaphoreSlim keyCacheSync = new(1, 1);
  private IReadOnlyList<AppleJwk> cachedKeys = [];
  private DateTimeOffset cachedKeysUntil = DateTimeOffset.MinValue;

  public AppleIdentityTokenValidator(AccountSecurityOptions options, HttpClient httpClient)
  {
    this.options = options;
    this.httpClient = httpClient;
  }

  public async Task<AppleIdentity?> ValidateAsync(
    string identityToken,
    string? nonce,
    CancellationToken cancellationToken
  )
  {
    if (options.AppleIdentityAudiences.Count == 0)
    {
      return null;
    }

    var parts = identityToken.Split('.');
    if (parts.Length != 3)
    {
      return null;
    }

    using var header = ParseBase64UrlJson(parts[0]);
    if (header is null ||
        !string.Equals(StringClaim(header.RootElement, "alg"), "RS256", StringComparison.Ordinal) ||
        StringClaim(header.RootElement, "kid") is not { Length: > 0 } keyId)
    {
      return null;
    }

    var keys = await KeysAsync(cancellationToken).ConfigureAwait(false);
    var key = keys.FirstOrDefault(item => string.Equals(item.KeyId, keyId, StringComparison.Ordinal));
    if (key is null || !VerifyRs256(parts, key))
    {
      return null;
    }

    using var payload = ParseBase64UrlJson(parts[1]);
    if (payload is null)
    {
      return null;
    }

    var root = payload.RootElement;
    var now = DateTimeOffset.UtcNow;
    var issuer = StringClaim(root, "iss");
    var subject = StringClaim(root, "sub");
    var email = StringClaim(root, "email");
    var expiresAt = UnixTimeClaim(root, "exp");
    var issuedAt = UnixTimeClaim(root, "iat");

    if (!string.Equals(issuer, options.AppleIdentityIssuer, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(subject) ||
        expiresAt is null ||
        expiresAt.Value <= now.Subtract(ClockSkew) ||
        (issuedAt is not null && issuedAt.Value > now.Add(ClockSkew)) ||
        !AudienceMatches(root))
    {
      return null;
    }

    if (!NonceMatches(root, nonce))
    {
      return null;
    }

    return new AppleIdentity(subject, email);
  }

  private async Task<IReadOnlyList<AppleJwk>> KeysAsync(CancellationToken cancellationToken)
  {
    if (cachedKeysUntil > DateTimeOffset.UtcNow && cachedKeys.Count > 0)
    {
      return cachedKeys;
    }

    await keyCacheSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (cachedKeysUntil > DateTimeOffset.UtcNow && cachedKeys.Count > 0)
      {
        return cachedKeys;
      }

      using var response = await httpClient.GetAsync(options.AppleIdentityJwksUrl, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        return [];
      }

      await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
      using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
      if (!document.RootElement.TryGetProperty("keys", out var keysElement) ||
          keysElement.ValueKind != JsonValueKind.Array)
      {
        return [];
      }

      var keys = new List<AppleJwk>();
      foreach (var keyElement in keysElement.EnumerateArray())
      {
        var keyId = StringClaim(keyElement, "kid");
        var modulus = StringClaim(keyElement, "n");
        var exponent = StringClaim(keyElement, "e");
        var keyType = StringClaim(keyElement, "kty");
        if (!string.IsNullOrWhiteSpace(keyId) &&
            !string.IsNullOrWhiteSpace(modulus) &&
            !string.IsNullOrWhiteSpace(exponent) &&
            string.Equals(keyType, "RSA", StringComparison.Ordinal))
        {
          keys.Add(new AppleJwk(keyId, modulus, exponent));
        }
      }

      cachedKeys = keys;
      cachedKeysUntil = DateTimeOffset.UtcNow.AddHours(12);
      return cachedKeys;
    }
    catch (Exception error) when (error is HttpRequestException or JsonException or FormatException)
    {
      return [];
    }
    finally
    {
      keyCacheSync.Release();
    }
  }

  private bool AudienceMatches(JsonElement root)
  {
    if (!root.TryGetProperty("aud", out var audience))
    {
      return false;
    }

    if (audience.ValueKind == JsonValueKind.String)
    {
      var value = audience.GetString();
      return options.AppleIdentityAudiences.Any(item => string.Equals(item, value, StringComparison.Ordinal));
    }

    if (audience.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in audience.EnumerateArray())
      {
        var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        if (options.AppleIdentityAudiences.Any(expected => string.Equals(expected, value, StringComparison.Ordinal)))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static bool NonceMatches(JsonElement root, string? nonce)
  {
    if (string.IsNullOrWhiteSpace(nonce))
    {
      return true;
    }

    var claim = StringClaim(root, "nonce");
    if (string.IsNullOrWhiteSpace(claim))
    {
      return false;
    }

    return string.Equals(claim, nonce, StringComparison.Ordinal) ||
           string.Equals(claim, SecurityTokenHelpers.Sha256HexLowercase(nonce), StringComparison.Ordinal);
  }

  private static bool VerifyRs256(string[] parts, AppleJwk key)
  {
    try
    {
      using var rsa = RSA.Create();
      rsa.ImportParameters(new RSAParameters
      {
        Modulus = SecurityTokenHelpers.DecodeBase64Url(key.Modulus),
        Exponent = SecurityTokenHelpers.DecodeBase64Url(key.Exponent)
      });

      return rsa.VerifyData(
        Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
        SecurityTokenHelpers.DecodeBase64Url(parts[2]),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1
      );
    }
    catch (CryptographicException)
    {
      return false;
    }
  }

  private static JsonDocument? ParseBase64UrlJson(string value)
  {
    try
    {
      return JsonDocument.Parse(SecurityTokenHelpers.DecodeBase64Url(value));
    }
    catch (Exception error) when (error is JsonException or FormatException)
    {
      return null;
    }
  }

  private static string? StringClaim(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var property))
    {
      return null;
    }

    return property.ValueKind switch
    {
      JsonValueKind.String => property.GetString(),
      JsonValueKind.Number => property.GetRawText(),
      _ => null
    };
  }

  private static DateTimeOffset? UnixTimeClaim(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.Number ||
        !property.TryGetInt64(out var unixTime))
    {
      return null;
    }

    return DateTimeOffset.FromUnixTimeSeconds(unixTime);
  }

  private sealed record AppleJwk(string KeyId, string Modulus, string Exponent);
}

public sealed class StoreKitJwsTransactionVerifier : IStoreKitTransactionVerifier
{
  private readonly AccountSecurityOptions options;
  private readonly AppleCertificateJwsVerifier jwsVerifier;

  public StoreKitJwsTransactionVerifier(AccountSecurityOptions options)
  {
    this.options = options;
    jwsVerifier = new AppleCertificateJwsVerifier(options.AppStoreJwsRootCertificatePem);
  }

  public Task<VerifiedStoreKitTransaction?> VerifyAsync(
    IapTransactionSubmissionRequest request,
    Guid expectedAppAccountToken,
    CancellationToken cancellationToken
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    using var payload = jwsVerifier.Verify(request.SignedTransaction);
    if (payload is null)
    {
      return Task.FromResult<VerifiedStoreKitTransaction?>(null);
    }

    var transaction = VerifiedTransactionFromPayload(
      payload.RootElement,
      request.ProductId,
      expectedAppAccountToken,
      options.AppStoreBundleId,
      SecurityTokenHelpers.Sha256Base64Url(request.SignedTransaction)
    );
    return Task.FromResult(transaction);
  }

  internal static VerifiedStoreKitTransaction? VerifiedTransactionFromPayload(
    JsonElement root,
    string expectedProductId,
    Guid expectedAppAccountToken,
    string expectedBundleId,
    string signedPayloadHash
  )
  {
    var transactionId = StringClaim(root, "transactionId");
    var originalTransactionId = StringClaim(root, "originalTransactionId") ?? transactionId;
    var productId = StringClaim(root, "productId");
    var bundleId = StringClaim(root, "bundleId");
    var environment = StringClaim(root, "environment") ?? "unknown";
    var appAccountTokenValue = StringClaim(root, "appAccountToken");
    var type = StringClaim(root, "type");

    if (string.IsNullOrWhiteSpace(transactionId) ||
        string.IsNullOrWhiteSpace(originalTransactionId) ||
        !string.Equals(productId, expectedProductId, StringComparison.Ordinal) ||
        (!string.IsNullOrWhiteSpace(expectedBundleId) &&
          !string.Equals(bundleId, expectedBundleId, StringComparison.Ordinal)) ||
        !Guid.TryParse(appAccountTokenValue, out var appAccountToken) ||
        appAccountToken != expectedAppAccountToken ||
        (type is not null && !string.Equals(type, "Consumable", StringComparison.Ordinal)) ||
        HasNonNullProperty(root, "revocationDate"))
    {
      return null;
    }

    return new VerifiedStoreKitTransaction(
      transactionId,
      originalTransactionId,
      expectedProductId,
      appAccountToken,
      environment,
      signedPayloadHash
    );
  }

  private static bool HasNonNullProperty(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null;

  private static string? StringClaim(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var property))
    {
      return null;
    }

    return property.ValueKind switch
    {
      JsonValueKind.String => property.GetString(),
      JsonValueKind.Number => property.GetRawText(),
      _ => null
    };
  }
}

public sealed class AppStoreServerNotificationJwsVerifier : IAppStoreServerNotificationVerifier
{
  private readonly AppleCertificateJwsVerifier jwsVerifier;

  public AppStoreServerNotificationJwsVerifier(AccountSecurityOptions options)
  {
    jwsVerifier = new AppleCertificateJwsVerifier(options.AppStoreJwsRootCertificatePem);
  }

  public Task<string?> ReversedTransactionIdAsync(
    AppStoreServerNotificationRequest request,
    CancellationToken cancellationToken
  )
  {
    cancellationToken.ThrowIfCancellationRequested();
    using var notification = jwsVerifier.Verify(request.SignedPayload);
    if (notification is null)
    {
      return Task.FromResult<string?>(null);
    }

    var notificationType = StringClaim(notification.RootElement, "notificationType");
    if (notificationType is not ("REFUND" or "REVOKE" or "DID_REVOKE"))
    {
      return Task.FromResult<string?>(null);
    }

    if (!notification.RootElement.TryGetProperty("data", out var data) ||
        data.ValueKind != JsonValueKind.Object)
    {
      return Task.FromResult<string?>(null);
    }

    var signedTransaction = StringClaim(data, "signedTransactionInfo");
    if (!string.IsNullOrWhiteSpace(signedTransaction))
    {
      using var transaction = jwsVerifier.Verify(signedTransaction);
      return Task.FromResult(transaction is null
        ? null
        : StringClaim(transaction.RootElement, "transactionId"));
    }

    return Task.FromResult(StringClaim(data, "transactionId"));
  }

  private static string? StringClaim(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var property))
    {
      return null;
    }

    return property.ValueKind switch
    {
      JsonValueKind.String => property.GetString(),
      JsonValueKind.Number => property.GetRawText(),
      _ => null
    };
  }
}

internal sealed class AppleCertificateJwsVerifier
{
  private readonly X509Certificate2? customRootCertificate;

  public AppleCertificateJwsVerifier(string? customRootCertificatePem)
  {
    if (!string.IsNullOrWhiteSpace(customRootCertificatePem))
    {
      try
      {
        customRootCertificate = X509Certificate2.CreateFromPem(customRootCertificatePem);
      }
      catch (CryptographicException)
      {
        customRootCertificate = null;
      }
    }
  }

  public JsonDocument? Verify(string signedPayload)
  {
    var parts = signedPayload.Split('.');
    if (parts.Length != 3)
    {
      return null;
    }

    using var header = ParseBase64UrlJson(parts[0]);
    if (header is null ||
        !string.Equals(StringClaim(header.RootElement, "alg"), "ES256", StringComparison.Ordinal) ||
        !header.RootElement.TryGetProperty("x5c", out var certificateChain) ||
        certificateChain.ValueKind != JsonValueKind.Array)
    {
      return null;
    }

    var certificates = Certificates(certificateChain);
    if (certificates.Count == 0)
    {
      DisposeCertificates(certificates);
      return null;
    }

    try
    {
      var leaf = certificates[0];
      if (!ValidateChain(certificates) || !VerifyEs256(parts, leaf))
      {
        return null;
      }

      return ParseBase64UrlJson(parts[1]);
    }
    finally
    {
      DisposeCertificates(certificates);
    }
  }

  private bool ValidateChain(IReadOnlyList<X509Certificate2> certificates)
  {
    using var chain = new X509Chain();
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

    foreach (var certificate in certificates.Skip(1))
    {
      chain.ChainPolicy.ExtraStore.Add(certificate);
    }

    if (customRootCertificate is not null)
    {
      chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
      chain.ChainPolicy.CustomTrustStore.Add(customRootCertificate);
    }

    return chain.Build(certificates[0]);
  }

  private static bool VerifyEs256(string[] parts, X509Certificate2 leafCertificate)
  {
    try
    {
      using var ecdsa = leafCertificate.GetECDsaPublicKey();
      return ecdsa is not null && ecdsa.VerifyData(
        Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
        SecurityTokenHelpers.DecodeBase64Url(parts[2]),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation
      );
    }
    catch (CryptographicException)
    {
      return false;
    }
  }

  private static List<X509Certificate2> Certificates(JsonElement certificateChain)
  {
    var certificates = new List<X509Certificate2>();
    try
    {
      foreach (var certificateElement in certificateChain.EnumerateArray())
      {
        if (certificateElement.ValueKind != JsonValueKind.String ||
            certificateElement.GetString() is not { Length: > 0 } certificateValue)
        {
          return [];
        }

        certificates.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificateValue)));
      }

      return certificates;
    }
    catch (Exception error) when (error is CryptographicException or FormatException)
    {
      DisposeCertificates(certificates);
      return [];
    }
  }

  private static JsonDocument? ParseBase64UrlJson(string value)
  {
    try
    {
      return JsonDocument.Parse(SecurityTokenHelpers.DecodeBase64Url(value));
    }
    catch (Exception error) when (error is JsonException or FormatException)
    {
      return null;
    }
  }

  private static string? StringClaim(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var property))
    {
      return null;
    }

    return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
  }

  private static void DisposeCertificates(IEnumerable<X509Certificate2> certificates)
  {
    foreach (var certificate in certificates)
    {
      certificate.Dispose();
    }
  }
}
