using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gifster.Backend.Security;

public sealed class AppleAppAttestVerifier : IAppAttestVerifier
{
  private const string ExpectedFormat = "apple-appattest";
  private const string AppleNonceExtensionOid = "1.2.840.113635.100.8.2";
  private const byte AttestedCredentialDataFlag = 0x40;

  private readonly string appIdentifier;
  private readonly X509Certificate2 rootCertificate;

  public AppleAppAttestVerifier(string appIdentifier, X509Certificate2 rootCertificate)
  {
    this.appIdentifier = string.IsNullOrWhiteSpace(appIdentifier)
      ? throw new ArgumentException("App Attest app identifier is required.", nameof(appIdentifier))
      : appIdentifier;
    this.rootCertificate = rootCertificate;
  }

  public AppAttestVerificationResult? Verify(
    AppAttestAttestationRequest request,
    AppAttestChallengeResponse challenge
  )
  {
    try
    {
      var clientDataHash = DecodeBase64(request.ClientDataHash);
      if (!CryptographicOperations.FixedTimeEquals(
            clientDataHash,
            SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Challenge))
          ))
      {
        return null;
      }

      var attestation = ParseAttestationObject(DecodeBase64(request.AttestationObject));
      if (attestation is null ||
          attestation.Format != ExpectedFormat ||
          attestation.CertificateChain.Count == 0)
      {
        return null;
      }

      using var leafCertificate = X509CertificateLoader.LoadCertificate(attestation.CertificateChain[0]);
      if (!ValidateCertificateChain(leafCertificate, attestation.CertificateChain))
      {
        return null;
      }

      var authenticatorData = ParseAuthenticatorData(attestation.AuthenticatorData);
      if (authenticatorData is null ||
          (authenticatorData.Flags & AttestedCredentialDataFlag) == 0 ||
          !IsAllowedAaguid(authenticatorData.Aaguid))
      {
        return null;
      }

      var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(appIdentifier));
      if (!CryptographicOperations.FixedTimeEquals(authenticatorData.RpIdHash, expectedRpIdHash))
      {
        return null;
      }

      var keyId = DecodeBase64UrlOrStandard(request.KeyId);
      if (!CryptographicOperations.FixedTimeEquals(authenticatorData.CredentialId, keyId))
      {
        return null;
      }

      var expectedNonce = SHA256.HashData(authenticatorData.RawData.Concat(clientDataHash).ToArray());
      var nonceExtension = leafCertificate.Extensions[AppleNonceExtensionOid];
      if (nonceExtension is null || !ContainsBytes(nonceExtension.RawData, expectedNonce))
      {
        return null;
      }

      var cosePublicKey = ParseCosePublicKey(authenticatorData.CredentialPublicKey);
      if (cosePublicKey is null || !CertificatePublicKeyMatches(leafCertificate, cosePublicKey))
      {
        return null;
      }

      return new AppAttestVerificationResult(request.KeyId);
    }
    catch (Exception error) when (
      error is CryptographicException or FormatException or InvalidOperationException or ArgumentException
    )
    {
      return null;
    }
  }

  private bool ValidateCertificateChain(X509Certificate2 leafCertificate, IReadOnlyList<byte[]> certificateChain)
  {
    using var chain = new X509Chain();
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);

    for (var index = 1; index < certificateChain.Count; index += 1)
    {
      if (certificateChain[index].AsSpan().SequenceEqual(rootCertificate.RawData))
      {
        continue;
      }

      using var certificate = X509CertificateLoader.LoadCertificate(certificateChain[index]);
      chain.ChainPolicy.ExtraStore.Add(certificate);
    }

    return chain.Build(leafCertificate);
  }

  private static bool CertificatePublicKeyMatches(X509Certificate2 certificate, CosePublicKey publicKey)
  {
    using var ecdsa = certificate.GetECDsaPublicKey();
    if (ecdsa is null)
    {
      return false;
    }

    var certKey = ecdsa.ExportParameters(false);
    return certKey.Q.X is not null &&
           certKey.Q.Y is not null &&
           CryptographicOperations.FixedTimeEquals(certKey.Q.X, publicKey.X) &&
           CryptographicOperations.FixedTimeEquals(certKey.Q.Y, publicKey.Y);
  }

  private static ParsedAttestationObject? ParseAttestationObject(byte[] data)
  {
    var reader = new MinimalCborReader(data);
    var mapCount = reader.ReadMapStart();
    string? format = null;
    byte[]? authData = null;
    List<byte[]>? certificates = null;

    for (var index = 0; index < mapCount; index += 1)
    {
      var key = reader.ReadTextString();
      switch (key)
      {
        case "fmt":
          format = reader.ReadTextString();
          break;
        case "authData":
          authData = reader.ReadByteString();
          break;
        case "attStmt":
          certificates = ParseAttestationStatement(reader);
          break;
        default:
          reader.SkipValue();
          break;
      }
    }

    return format is null || authData is null || certificates is null
      ? null
      : new ParsedAttestationObject(format, authData, certificates);
  }

  private static List<byte[]> ParseAttestationStatement(MinimalCborReader reader)
  {
    var certificates = new List<byte[]>();
    var mapCount = reader.ReadMapStart();
    for (var index = 0; index < mapCount; index += 1)
    {
      var key = reader.ReadTextString();
      if (key == "x5c")
      {
        var count = reader.ReadArrayStart();
        for (var certificateIndex = 0; certificateIndex < count; certificateIndex += 1)
        {
          certificates.Add(reader.ReadByteString());
        }
      }
      else
      {
        reader.SkipValue();
      }
    }

    return certificates;
  }

  private static AuthenticatorData? ParseAuthenticatorData(byte[] data)
  {
    const int fixedLength = 32 + 1 + 4 + 16 + 2;
    if (data.Length < fixedLength)
    {
      return null;
    }

    var offset = 0;
    var rpIdHash = data.AsSpan(offset, 32).ToArray();
    offset += 32;
    var flags = data[offset++];
    offset += 4;
    var aaguid = data.AsSpan(offset, 16).ToArray();
    offset += 16;
    var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    offset += 2;

    if (data.Length < offset + credentialIdLength)
    {
      return null;
    }

    var credentialId = data.AsSpan(offset, credentialIdLength).ToArray();
    offset += credentialIdLength;
    var credentialPublicKey = data.AsSpan(offset).ToArray();

    return new AuthenticatorData(
      data,
      rpIdHash,
      flags,
      aaguid,
      credentialId,
      credentialPublicKey
    );
  }

  private static CosePublicKey? ParseCosePublicKey(byte[] data)
  {
    var reader = new MinimalCborReader(data);
    var mapCount = reader.ReadMapStart();
    int? keyType = null;
    int? curve = null;
    byte[]? x = null;
    byte[]? y = null;

    for (var index = 0; index < mapCount; index += 1)
    {
      var key = reader.ReadInt32();
      switch (key)
      {
        case 1:
          keyType = reader.ReadInt32();
          break;
        case -1:
          curve = reader.ReadInt32();
          break;
        case -2:
          x = reader.ReadByteString();
          break;
        case -3:
          y = reader.ReadByteString();
          break;
        default:
          reader.SkipValue();
          break;
      }
    }

    return keyType == 2 && curve == 1 && x is not null && y is not null
      ? new CosePublicKey(x, y)
      : null;
  }

  private static bool IsAllowedAaguid(byte[] aaguid)
  {
    var ascii = Encoding.ASCII.GetString(aaguid).TrimEnd('\0');
    return ascii is "appattest" or "appattestdevelop";
  }

  private static byte[] DecodeBase64(string value) =>
    Convert.FromBase64String(value);

  private static byte[] DecodeBase64UrlOrStandard(string value)
  {
    var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
    var padding = normalized.Length % 4;
    if (padding > 0)
    {
      normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
    }

    return Convert.FromBase64String(normalized);
  }

  private static bool ContainsBytes(byte[] haystack, byte[] needle)
  {
    if (needle.Length == 0 || haystack.Length < needle.Length)
    {
      return false;
    }

    for (var index = 0; index <= haystack.Length - needle.Length; index += 1)
    {
      if (CryptographicOperations.FixedTimeEquals(haystack.AsSpan(index, needle.Length), needle))
      {
        return true;
      }
    }

    return false;
  }

  private sealed record ParsedAttestationObject(
    string Format,
    byte[] AuthenticatorData,
    IReadOnlyList<byte[]> CertificateChain
  );

  private sealed record AuthenticatorData(
    byte[] RawData,
    byte[] RpIdHash,
    byte Flags,
    byte[] Aaguid,
    byte[] CredentialId,
    byte[] CredentialPublicKey
  );

  private sealed record CosePublicKey(byte[] X, byte[] Y);
}

internal sealed class MinimalCborReader
{
  private readonly byte[] data;
  private int offset;

  public MinimalCborReader(byte[] data)
  {
    this.data = data;
  }

  public int ReadMapStart() => checked((int)ReadLengthForMajorType(5));
  public int ReadArrayStart() => checked((int)ReadLengthForMajorType(4));

  public string ReadTextString()
  {
    var length = checked((int)ReadLengthForMajorType(3));
    EnsureAvailable(length);
    var value = Encoding.UTF8.GetString(data, offset, length);
    offset += length;
    return value;
  }

  public byte[] ReadByteString()
  {
    var length = checked((int)ReadLengthForMajorType(2));
    EnsureAvailable(length);
    var value = data.AsSpan(offset, length).ToArray();
    offset += length;
    return value;
  }

  public int ReadInt32() => checked((int)ReadInteger());

  public void SkipValue()
  {
    var (majorType, length) = ReadHeader();
    switch (majorType)
    {
      case 0:
      case 1:
      case 7:
        return;
      case 2:
      case 3:
        EnsureAvailable(checked((int)length));
        offset += checked((int)length);
        return;
      case 4:
        for (var index = 0; index < checked((int)length); index += 1)
        {
          SkipValue();
        }
        return;
      case 5:
        for (var index = 0; index < checked((int)length); index += 1)
        {
          SkipValue();
          SkipValue();
        }
        return;
      default:
        throw new InvalidOperationException("Unsupported CBOR value.");
    }
  }

  private long ReadInteger()
  {
    var (majorType, value) = ReadHeader();
    return majorType switch
    {
      0 => checked((long)value),
      1 => checked(-1 - (long)value),
      _ => throw new InvalidOperationException("Expected CBOR integer.")
    };
  }

  private ulong ReadLengthForMajorType(int expectedMajorType)
  {
    var (majorType, length) = ReadHeader();
    if (majorType != expectedMajorType)
    {
      throw new InvalidOperationException("Unexpected CBOR major type.");
    }

    return length;
  }

  private (int MajorType, ulong Length) ReadHeader()
  {
    EnsureAvailable(1);
    var header = data[offset++];
    var majorType = header >> 5;
    var additional = header & 0x1F;

    return (majorType, additional switch
    {
      < 24 => (ulong)additional,
      24 => ReadUInt8(),
      25 => ReadUInt16(),
      26 => ReadUInt32(),
      27 => ReadUInt64(),
      _ => throw new InvalidOperationException("Indefinite-length CBOR is not supported.")
    });
  }

  private byte ReadUInt8()
  {
    EnsureAvailable(1);
    return data[offset++];
  }

  private ushort ReadUInt16()
  {
    EnsureAvailable(2);
    var value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    offset += 2;
    return value;
  }

  private uint ReadUInt32()
  {
    EnsureAvailable(4);
    var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    offset += 4;
    return value;
  }

  private ulong ReadUInt64()
  {
    EnsureAvailable(8);
    var value = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8));
    offset += 8;
    return value;
  }

  private void EnsureAvailable(int length)
  {
    if (length < 0 || data.Length - offset < length)
    {
      throw new InvalidOperationException("CBOR data ended unexpectedly.");
    }
  }
}
