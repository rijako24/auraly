using Auraly.Platform.Application.Identity.Services;
using Auraly.Contracts.Tenants;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class TenantProvisioningRequestValidatorTests
{
    [Theory]
    [InlineData("America/Bogota", true)]
    [InlineData("SA Pacific Standard Time", true)]
    [InlineData("America/New_York", true)]
    [InlineData("", false)]
    [InlineData("Unknown/Zone", false)]
    public void Primary_business_requires_a_valid_time_zone(string timeZone, bool valid)
    {
        var request = new ProvisionTenantRequest(Guid.NewGuid(), "Empresa", "Empresa", "NaturalPerson",
            "CC", "1020304050", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Dirección", "3001234567", "empresa@example.test", "", "Principal", "Dirección",
            "3001234567", "sede@example.test", timeZone, "LatestReceiptCost", "admin@example.test", 1, 0);
        var exception = Record.Exception(() => TenantProvisioningRequestValidator.Validate(request));
        if (valid) Assert.Null(exception);
        else Assert.IsAssignableFrom<ArgumentException>(exception);
    }

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
