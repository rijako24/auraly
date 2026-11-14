namespace Auraly.BuildingBlocks.Domain.Payments;

/// <summary>Captured form identity and display evidence; its account is resolved again at posting.</summary>
public readonly record struct PaymentMethodEvidence(
    Guid? Id, int? Version, string? Code, string? Name, Guid? AccountId)
{
    public static bool IsValidSet(IEnumerable<PaymentMethodEvidence> evidence)
    {
        var values = evidence.ToArray();
        if (values.All(value => value.Id is null))
            return values.All(value => value.Version is null && value.Code is null &&
                value.Name is null && value.AccountId is null);

        return values.All(value => value.Id is { } id && id != Guid.Empty && value.Version is > 0 &&
                !string.IsNullOrWhiteSpace(value.Code) && value.Code.Length <= 64 &&
                !string.IsNullOrWhiteSpace(value.Name) && value.Name.Length <= 160 &&
                value.AccountId is { } account && account != Guid.Empty) &&
            values.Select(value => value.Id).Distinct().Count() == values.Length;
    }
}
