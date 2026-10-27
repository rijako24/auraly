using Auraly.Commerce.Accounting.Domain;

namespace Auraly.Foundation.Tests;

public sealed class PucAuxiliaryDefinitionTests
{
    [Theory]
    [InlineData("Asset", "Debit", true, false, null, true)]
    [InlineData("Liability", "Debit", true, false, null, false)]
    [InlineData("Asset", "Credit", true, false, null, false)]
    [InlineData("Asset", "Debit", false, false, null, false)]
    [InlineData("Asset", "Debit", true, true, null, false)]
    [InlineData("Asset", "Debit", true, false, "Cash", false)]
    public void Unallocated_purchase_cost_is_an_explicit_asset_without_treasury_dimensions(
        string type, string side, bool posting, bool bank, string? payment, bool valid)
    {
        var error = PucAuxiliaryDefinition.Validate("UnallocatedPurchaseCost", type, side, posting, false, bank, payment);
        if (valid) Assert.Null(error);
        else Assert.NotNull(error);
    }
}
