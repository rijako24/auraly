using Auraly.Platform.Application.Identity.Services;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class TenantProvisioningRequestValidatorTests
{
    [Theory]
    [InlineData("CC", "1020304050")]
    [InlineData("CE", "1234567")]
    [InlineData("PPT", "987654321")]
    [InlineData("PA", "AB123456")]
    [InlineData("DE", "X9YZ123")]
    public void ValidateIdentification_AcceptsSupportedNaturalPersonDocuments(
        string identificationTypeCode, string identification)
    {
        var exception = Record.Exception(() => TenantProvisioningRequestValidator.ValidateIdentification(
            identificationTypeCode, identification, null));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("CC", "10.203.040")]
    [InlineData("CE", "ABC123")]
    [InlineData("PPT", "123-456")]
    [InlineData("PA", "AB 123")]
    public void ValidateIdentification_RejectsCharactersNotAllowedByDocumentType(
        string identificationTypeCode, string identification)
    {
        Assert.Throws<ArgumentException>(() => TenantProvisioningRequestValidator.ValidateIdentification(
            identificationTypeCode, identification, null));
    }

    [Fact]
    public void ValidateIdentification_AcceptsNitWithMatchingSeparateVerificationDigit()
    {
        const string nit = "900123456";
        var verificationDigit = TenantProvisioningRequestValidator.CalculateNitVerificationDigit(nit).ToString();

        var exception = Record.Exception(() => TenantProvisioningRequestValidator.ValidateIdentification(
            "NIT", nit, verificationDigit));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateIdentification_RejectsNitWithIncorrectVerificationDigit()
    {
        const string nit = "900123456";
        var expected = TenantProvisioningRequestValidator.CalculateNitVerificationDigit(nit);
        var incorrect = ((expected + 1) % 10).ToString();

        Assert.Throws<ArgumentException>(() => TenantProvisioningRequestValidator.ValidateIdentification(
            "NIT", nit, incorrect));
    }
}
