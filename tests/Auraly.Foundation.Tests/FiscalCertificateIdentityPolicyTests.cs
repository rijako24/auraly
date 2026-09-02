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

    [Theory]
    [InlineData("032D6DCFE71F2C57ECADA9A99F2F6CE9825A6550")]
    [InlineData("71 EB A8 7B 1D 60 D4 95 F5 BA 91 C4 8B 3B 5C 2A 3D FE B4 86")]
    [InlineData("3977884DA7B83A006AED158D506AAC861BCA1A4F")]
    [InlineData("1139A49E8484AAF2D90D985EC4741A65DD5D94E2")]
    [InlineData("6DC08450A95CD32662C0910F8C2DCE230D7466AD")]
    [InlineData("5463283B6793FF55277CEDE39098E80422F912F7")]
    [InlineData("EBB08B91DF02D0B9A813CBE10E112CC11A50611C")]
    [InlineData("4BA80D75903497F45D32EFEFD25F184B362F1DD0")]
    [InlineData("A08ED8F6DFC49FFD2884E25A576F4EAC980B2481")]
    [InlineData("F68347D8A59B9312389BCB010BEB7E6C3E067FE5")]
    public void Official_fiscal_roots_are_explicitly_allowlisted(string thumbprint)
    {
        Assert.True(FiscalCertificateTrustPolicy.IsAllowedRootThumbprint(thumbprint));
        Assert.False(FiscalCertificateTrustPolicy.IsAllowedRootThumbprint(
            "0000000000000000000000000000000000000000"));
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
