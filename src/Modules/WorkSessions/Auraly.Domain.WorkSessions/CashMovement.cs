namespace Auraly.Domain.WorkSessions;

public enum CashMovementDirection
{
    In,
    Out
}

public sealed record CashMovementReasonDefinition(
    Guid ReasonId,
    Guid BusinessId,
    string Code,
    string Name,
    CashMovementDirection Direction,
    string? CounterpartAccountingCategory,
    Guid? DefaultCostCenterId,
    bool RequiresReference,
    bool IsActive)
{
    public static CashMovementReasonDefinition Create(
        Guid reasonId,
        Guid businessId,
        string code,
        string name,
        CashMovementDirection direction,
        string? counterpartAccountingCategory,
        Guid? defaultCostCenterId,
        bool requiresReference,
        bool isActive)
    {
        if (reasonId == Guid.Empty || businessId == Guid.Empty)
            throw new CashMovementRuleException(
                "The cash movement reason requires valid identifiers.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
            throw new CashMovementRuleException(
                "The reason code is required and limited to 32 characters.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new CashMovementRuleException(
                "The reason name is required and limited to 120 characters.");
        if (counterpartAccountingCategory?.Trim().Length > 64)
            throw new CashMovementRuleException(
                "The counterpart accounting category is limited to 64 characters.");
        if (!CashCounterpartCategories.IsSupported(counterpartAccountingCategory))
            throw new CashMovementRuleException(
                "The counterpart accounting category is not supported.");
        if (defaultCostCenterId == Guid.Empty)
            throw new CashMovementRuleException(
                "The default cost center must be null or a valid identifier.");

        return new CashMovementReasonDefinition(
            reasonId,
            businessId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            direction,
            string.IsNullOrWhiteSpace(counterpartAccountingCategory)
                ? null
                : counterpartAccountingCategory.Trim(),
            defaultCostCenterId,
            requiresReference,
            isActive);
    }
}

public sealed record CashMovement(
    Guid DocumentId,
    Guid BusinessId,
    Guid WorkSessionId,
    CashMovementReasonDefinition Reason,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes,
    Guid? CostCenterId)
{
    public decimal SignedAmount =>
        Reason.Direction == CashMovementDirection.In ? Amount : -Amount;

    public static CashMovement Create(
        Guid documentId,
        Guid businessId,
        Guid workSessionId,
        CashMovementReasonDefinition reason,
        decimal amount,
        DateTimeOffset occurredAt,
        string? reference,
        string? notes,
        Guid? costCenterId)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (documentId == Guid.Empty || businessId == Guid.Empty ||
            workSessionId == Guid.Empty)
            throw new CashMovementRuleException(
                "The cash movement requires valid identifiers.");
        if (reason.BusinessId != businessId || !reason.IsActive)
            throw new CashMovementRuleException(
                "The selected cash movement reason is not active for this business.");
        if (amount <= 0)
            throw new CashMovementRuleException(
                "The cash movement amount must be greater than zero.");
        if (occurredAt == default)
            throw new CashMovementRuleException(
                "The cash movement date is required.");
        var normalizedReference = Normalize(reference, 160, "reference");
        var normalizedNotes = Normalize(notes, 500, "notes");
        if (reason.RequiresReference && normalizedReference is null)
            throw new CashMovementRuleException(
                "The selected cash movement reason requires a reference.");
        if (costCenterId == Guid.Empty)
            throw new CashMovementRuleException(
                "The cost center must be null or a valid identifier.");

        return new CashMovement(
            documentId,
            businessId,
            workSessionId,
            reason,
            decimal.Round(amount, 4),
            occurredAt,
            normalizedReference,
            normalizedNotes,
            costCenterId ?? reason.DefaultCostCenterId);
    }

    private static string? Normalize(string? value, int maximum, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new CashMovementRuleException(
                $"The cash movement {label} exceeds {maximum} characters.");
        return normalized;
    }
}

public static class CashCounterpartCategories
{
    public const string OtherIncome = "OtherIncome";
    public const string OwnerContributions = "OwnerContributions";
    public const string OperatingExpense = "OperatingExpense";
    public const string OtherExpense = "OtherExpense";
    public const string Bank = "Bank";

    private static readonly IReadOnlySet<string> Supported = new HashSet<string>(
    [
        OtherIncome,
        OwnerContributions,
        OperatingExpense,
        OtherExpense,
        Bank
    ], StringComparer.Ordinal);

    public static bool IsSupported(string? category) =>
        category is not null && Supported.Contains(category.Trim());
}
public sealed class CashMovementRuleException(string message) : Exception(message);
