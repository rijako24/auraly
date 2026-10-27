namespace Auraly.Commerce.Taxation.Domain;

public static class AcquisitionTaxAmounts
{
    // Shared precision of acquisition bases; callers resolve the profile and
    // validate the document. This preserves the expense calculation unchanged.
    public static decimal InputVat(decimal taxExclusiveAmount, decimal rate) =>
        decimal.Round(taxExclusiveAmount * rate / 100m, 4, MidpointRounding.AwayFromZero);
}
