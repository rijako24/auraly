using Auraly.Application.Fiscal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Auraly.Foundation.Tests;

public sealed class FiscalCertificateIdentityPolicyTests
{
    [Fact]
    public void Certificate_identity_must_match_the_legal_profile_nit()
    {
        Assert.False(FiscalCertificateIdentityPolicy.IsAcceptable(
            "1002269668",
            "SERIALNUMBER=49693606, CN=Representante Legal"));
    }

    [Theory]
    [InlineData("1002269668", "SERIALNUMBER=1002269668, CN=Empresa")]
    [InlineData("1.002.269.668", "CN=Empresa, OID.2.5.4.5=1.002.269.668")]
    [InlineData("1002269668", "CN=Empresa+2.5.4.5=\"1002269668\"")]
    public void Matching_certificate_identity_is_accepted_after_normalization(
        string supplierTaxId,
        string certificateSubject)
    {
        Assert.True(FiscalCertificateIdentityPolicy.IsAcceptable(
            supplierTaxId, certificateSubject));
    }

    [Fact]
    public void An_unlisted_self_signed_root_is_not_a_trusted_fiscal_authority()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Unlisted fiscal root", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            true, false, 0, true));
        using var root = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.False(FiscalCertificateTrustPolicy.IsOfficialRoot(root));
    }

    [Fact]
    public void An_unavailable_revocation_service_does_not_look_like_a_revoked_certificate()
    {
        Assert.True(FiscalCertificateTrustPolicy.AreOnlyRevocationAvailabilityFailures([
            X509ChainStatusFlags.RevocationStatusUnknown,
            X509ChainStatusFlags.OfflineRevocation
        ]));
        Assert.False(FiscalCertificateTrustPolicy.AreOnlyRevocationAvailabilityFailures([
            X509ChainStatusFlags.Revoked
        ]));
        Assert.False(FiscalCertificateTrustPolicy.AreOnlyRevocationAvailabilityFailures([
            X509ChainStatusFlags.PartialChain
        ]));
    }

    [Theory]
    [InlineData("", "SERIALNUMBER=49693606, CN=Representante Legal")]
    [InlineData("1002269668", "CN=Firmante sin identificación")]
    public void Issuer_and_signer_must_both_have_an_identification(
        string supplierTaxId,
        string certificateSubject)
    {
        Assert.False(FiscalCertificateIdentityPolicy.IsAcceptable(
            supplierTaxId, certificateSubject));
    }
}
