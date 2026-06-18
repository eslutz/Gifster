using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using GifForge.Backend.Security;

namespace GifForge.Backend.Tests;

public sealed class AppleAppAttestVerifierTests
{
  [Fact]
  public void VerifyAcceptsValidAttestationFixture()
  {
    var fixture = AppAttestFixture.Create();
    var verifier = new AppleAppAttestVerifier(
      fixture.AppIdentifier,
      fixture.RootCertificate
    );

    var result = verifier.Verify(fixture.Request, fixture.Challenge);

    Assert.NotNull(result);
    Assert.Equal(fixture.Request.KeyId, result.KeyId);
  }

  [Fact]
  public void VerifyRejectsMismatchedChallengeHash()
  {
    var fixture = AppAttestFixture.Create();
    var verifier = new AppleAppAttestVerifier(
      fixture.AppIdentifier,
      fixture.RootCertificate
    );
    var request = fixture.Request with
    {
      ClientDataHash = Convert.ToBase64String(SHA256.HashData("wrong-challenge"u8.ToArray()))
    };

    var result = verifier.Verify(request, fixture.Challenge);

    Assert.Null(result);
  }

  [Fact]
  public void VerifyAcceptsMessagesExtensionIdentifierWhenConfigured()
  {
    const string containingAppIdentifier = "TEAMID.dev.ericslutz.gifforge";
    const string extensionIdentifier = "TEAMID.dev.ericslutz.gifforge.messagesextension";
    var fixture = AppAttestFixture.Create(extensionIdentifier);
    var verifier = new AppleAppAttestVerifier(
      [containingAppIdentifier, extensionIdentifier],
      fixture.RootCertificate
    );

    var result = verifier.Verify(fixture.Request, fixture.Challenge);

    Assert.NotNull(result);
  }

  [Fact]
  public void VerifyRejectsUnconfiguredAppIdentifier()
  {
    var fixture = AppAttestFixture.Create("TEAMID.dev.ericslutz.gifforge.messagesextension");
    var verifier = new AppleAppAttestVerifier(
      ["TEAMID.dev.ericslutz.gifforge"],
      fixture.RootCertificate
    );

    var result = verifier.Verify(fixture.Request, fixture.Challenge);

    Assert.Null(result);
  }
}

internal sealed record AppAttestFixture(
  string AppIdentifier,
  X509Certificate2 RootCertificate,
  AppAttestChallengeResponse Challenge,
  AppAttestAttestationRequest Request
)
{
  public static AppAttestFixture Create(string appIdentifier = "TEAMID.dev.ericslutz.GifForge")
  {
    var challenge = new AppAttestChallengeResponse(
      Guid.NewGuid().ToString("D"),
      "test-challenge",
      DateTimeOffset.UtcNow.AddMinutes(5)
    );
    var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Challenge));

    using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var rootRequest = new CertificateRequest(
      "CN=GifForge Test App Attest Root",
      rootKey,
      HashAlgorithmName.SHA256
    );
    rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
      X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
      true
    ));
    using var root = rootRequest.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddDays(-1),
      DateTimeOffset.UtcNow.AddDays(30)
    );
    var rootCertificate = X509CertificateLoader.LoadCertificate(root.Export(X509ContentType.Cert));

    using var attestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var credentialId = RandomNumberGenerator.GetBytes(32);
    var keyId = Base64Url(credentialId);
    var publicKey = attestKey.ExportParameters(false);
    var authData = AuthenticatorData(appIdentifier, credentialId, CoseKey(publicKey));
    var nonce = SHA256.HashData(authData.Concat(clientDataHash).ToArray());
    using var leaf = CreateLeafCertificate(root, rootKey, attestKey, nonce);
    var attestationObject = AttestationObject(authData, leaf.RawData, rootCertificate.RawData);

    return new AppAttestFixture(
      appIdentifier,
      rootCertificate,
      challenge,
      new AppAttestAttestationRequest(
        keyId,
        challenge.ChallengeId,
        Convert.ToBase64String(attestationObject),
        Convert.ToBase64String(clientDataHash)
      )
    );
  }

  private static X509Certificate2 CreateLeafCertificate(
    X509Certificate2 issuer,
    ECDsa issuerKey,
    ECDsa subjectKey,
    byte[] nonce
  )
  {
    var request = new CertificateRequest(
      "CN=GifForge Test App Attest Leaf",
      subjectKey,
      HashAlgorithmName.SHA256
    );
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509Extension(
      "1.2.840.113635.100.8.2",
      AppleNonceExtension(nonce),
      false
    ));

    return request.Create(
      issuer,
      DateTimeOffset.UtcNow.AddMinutes(-5),
      DateTimeOffset.UtcNow.AddDays(7),
      RandomNumberGenerator.GetBytes(16)
    );
  }

  private static byte[] AuthenticatorData(string appIdentifier, byte[] credentialId, byte[] coseKey)
  {
    var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(appIdentifier));
    var data = new byte[32 + 1 + 4 + 16 + 2 + credentialId.Length + coseKey.Length];
    var offset = 0;
    rpIdHash.CopyTo(data, offset);
    offset += 32;
    data[offset++] = 0x41;
    offset += 4;
    Encoding.ASCII.GetBytes("appattestdevelop").CopyTo(data, offset);
    offset += 16;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), (ushort)credentialId.Length);
    offset += 2;
    credentialId.CopyTo(data, offset);
    offset += credentialId.Length;
    coseKey.CopyTo(data, offset);
    return data;
  }

  private static byte[] CoseKey(ECParameters publicKey)
  {
    var writer = new TestCborWriter();
    writer.WriteMapStart(5);
    writer.WriteInt(1);
    writer.WriteInt(2);
    writer.WriteInt(3);
    writer.WriteInt(-7);
    writer.WriteInt(-1);
    writer.WriteInt(1);
    writer.WriteInt(-2);
    writer.WriteBytes(publicKey.Q.X ?? []);
    writer.WriteInt(-3);
    writer.WriteBytes(publicKey.Q.Y ?? []);
    return writer.ToArray();
  }

  private static byte[] AttestationObject(byte[] authData, byte[] leafCertificate, byte[] rootCertificate)
  {
    var writer = new TestCborWriter();
    writer.WriteMapStart(3);
    writer.WriteText("fmt");
    writer.WriteText("apple-appattest");
    writer.WriteText("authData");
    writer.WriteBytes(authData);
    writer.WriteText("attStmt");
    writer.WriteMapStart(2);
    writer.WriteText("x5c");
    writer.WriteArrayStart(2);
    writer.WriteBytes(leafCertificate);
    writer.WriteBytes(rootCertificate);
    writer.WriteText("receipt");
    writer.WriteBytes([]);
    return writer.ToArray();
  }

  private static byte[] AppleNonceExtension(byte[] nonce)
  {
    var octet = new byte[2 + nonce.Length];
    octet[0] = 0x04;
    octet[1] = (byte)nonce.Length;
    nonce.CopyTo(octet.AsSpan(2));

    var wrapped = new byte[2 + octet.Length];
    wrapped[0] = 0x04;
    wrapped[1] = (byte)octet.Length;
    octet.CopyTo(wrapped.AsSpan(2));

    var sequence = new byte[2 + wrapped.Length];
    sequence[0] = 0x30;
    sequence[1] = (byte)wrapped.Length;
    wrapped.CopyTo(sequence.AsSpan(2));
    return sequence;
  }

  private static string Base64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
}

internal sealed class TestCborWriter
{
  private readonly List<byte> bytes = [];

  public void WriteMapStart(int count) => WriteHeader(5, (ulong)count);
  public void WriteArrayStart(int count) => WriteHeader(4, (ulong)count);
  public void WriteText(string value)
  {
    var data = Encoding.UTF8.GetBytes(value);
    WriteHeader(3, (ulong)data.Length);
    bytes.AddRange(data);
  }

  public void WriteBytes(byte[] value)
  {
    WriteHeader(2, (ulong)value.Length);
    bytes.AddRange(value);
  }

  public void WriteInt(long value)
  {
    if (value >= 0)
    {
      WriteHeader(0, (ulong)value);
    }
    else
    {
      WriteHeader(1, (ulong)(-value - 1));
    }
  }

  public byte[] ToArray() => [.. bytes];

  private void WriteHeader(int majorType, ulong value)
  {
    if (value < 24)
    {
      bytes.Add((byte)((majorType << 5) | (byte)value));
    }
    else if (value <= byte.MaxValue)
    {
      bytes.Add((byte)((majorType << 5) | 24));
      bytes.Add((byte)value);
    }
    else if (value <= ushort.MaxValue)
    {
      bytes.Add((byte)((majorType << 5) | 25));
      Span<byte> buffer = stackalloc byte[2];
      BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
      bytes.AddRange(buffer.ToArray());
    }
    else
    {
      bytes.Add((byte)((majorType << 5) | 26));
      Span<byte> buffer = stackalloc byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)value);
      bytes.AddRange(buffer.ToArray());
    }
  }
}
