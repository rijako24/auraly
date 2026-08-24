using Auraly.Application.Fiscal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Auraly.Foundation.Tests;

public sealed class FiscalCertificateIdentityPolicyTests
{
    [Fact]
    public void Legal_representative_certificate_can_sign_for_a_company_with_a_different_nit()
    {
        Assert.True(FiscalCertificateIdentityPolicy.IsAcceptable(
            "1002269668",
            "SERIALNUMBER=49693606, CN=Representante Legal"));
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
