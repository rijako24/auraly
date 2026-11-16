namespace Auraly.BuildingBlocks.Domain.Payments;

/// <summary>Captured form identity and display evidence; its account is resolved again at posting.</summary>
public readonly record struct PaymentMethodEvidence(
    Guid? Id, int? Version, string? Code, string? Name, Guid? AccountId)
{
    // Financing is an obligation, not another monetary tender. Its optional
    // form is validated independently; absent evidence uses the A/R mapping.
    public static bool IsValidSale(IEnumerable<PaymentMethodEvidence> payments, PaymentMethodEvidence? financing)
    {
        var money = payments.ToArray();
        // Restaurant partial collections may use the same form more than once;
        // each tender keeps its own approval/reference evidence.
        return HasValidShape(money) && (financing is not { } credit ||
            IsValidSet([credit]) && (credit.Id is null || !money.Any(payment => payment.Id == credit.Id)));
    }

    public static bool IsValidSet(IEnumerable<PaymentMethodEvidence> evidence)
    {
        var values = evidence.ToArray();
        return HasValidShape(values) && (values.All(value => value.Id is null) ||
            values.Select(value => value.Id).Distinct().Count() == values.Length);
    }

    private static bool HasValidShape(IReadOnlyCollection<PaymentMethodEvidence> values)
    {
        if (values.All(value => value.Id is null))
            return values.All(value => value.Version is null && value.Code is null &&
                value.Name is null && value.AccountId is null);

        return values.All(value => value.Id is { } id && id != Guid.Empty && value.Version is > 0 &&
                !string.IsNullOrWhiteSpace(value.Code) && value.Code.Length <= 64 &&
                !string.IsNullOrWhiteSpace(value.Name) && value.Name.Length <= 160 &&
                value.AccountId is { } account && account != Guid.Empty);
    }
}
