using Auraly.Application.Fiscal;

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
