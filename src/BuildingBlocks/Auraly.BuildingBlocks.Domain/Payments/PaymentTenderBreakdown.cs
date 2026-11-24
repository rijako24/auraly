using System.Text.Json.Serialization;
using System.Collections.Frozen;

namespace Auraly.BuildingBlocks.Domain.Payments;

// Supported portfolio protocols, not the editable catalog or its labels. Native
// capture and account-based capture must enforce the same financial capabilities.
public static class PortfolioPaymentMethods
{
    public const string Cash = "Cash";
    public const string Transfer = "Transfer";
    public const string DebitCard = "DebitCard";
    public const string CreditCard = "CreditCard";
    public static IReadOnlySet<string> Supported { get; } =
        new[] { Cash, Transfer, DebitCard, CreditCard }.ToFrozenSet(StringComparer.Ordinal);
    public static bool IsSupported(string? value) => value is not null && Supported.Contains(value);
}

public sealed record PaymentTender(
    string MethodCode,
    decimal Amount,
    decimal? TenderedAmount = null,
    Guid? BankAccountId = null,
    string? Reference = null,
    string? Notes = null,
    string? CardFranchiseCode = null,
    string? ApprovalNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaymentMethodId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PaymentMethodVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AccountId = null);

public sealed record PaymentTenderBreakdown(
    IReadOnlyList<PaymentTender> Tenders,
    decimal TotalAmount)
{
    public const int MaximumReferenceLength = 120;
    public const int MaximumNotesLength = 500;
    public const int MaximumCardFranchiseLength = 40;
    public const int MaximumApprovalLength = 80;

    public static void EnsureUnambiguousInvoiceAttribution(int invoiceCount, int paymentMethodCount)
    {
        if (invoiceCount > 1 && paymentMethodCount > 1)
            throw new ArgumentException("Para varias facturas se usa una sola forma de pago. Conserva una factura o retira las formas adicionales.");
    }

    public static PaymentTenderBreakdown Create(
        IEnumerable<PaymentTender> tenders,
        decimal expectedAmount,
        int allocationCount,
        IReadOnlySet<string> supportedMethods,
        bool requireEffectiveCash = false)
    {
        ArgumentNullException.ThrowIfNull(tenders);
        ArgumentNullException.ThrowIfNull(supportedMethods);
        var values = tenders.Select(Normalize).ToArray();
        var configured = values.Any(value => value.PaymentMethodId.HasValue);
        var expected = Round(expectedAmount);
        if (values.Any(value => value.Reference?.Length > MaximumReferenceLength || value.Notes?.Length > MaximumNotesLength ||
            value.CardFranchiseCode?.Length > MaximumCardFranchiseLength || value.ApprovalNumber?.Length > MaximumApprovalLength))
            throw new ArgumentException("Payment evidence exceeds its supported field length.", nameof(tenders));

        if (values.Length is < 1 or > 10)
            throw new ArgumentException("A payment must use between one and ten payment methods.", nameof(tenders));
        if (allocationCount < 1)
            throw new ArgumentOutOfRangeException(nameof(allocationCount));
        // The versioned legacy contract remains readable during the coordinated
        // cutover. Configured forms identify a tender independently of its type.
        if (!configured && allocationCount > 1 && values.Length != 1)
            throw new ArgumentException("A payment applied to multiple invoices must use one payment method.", nameof(tenders));
        if (values.Any(value => string.IsNullOrWhiteSpace(value.MethodCode) ||
                                !supportedMethods.Contains(value.MethodCode)))
            throw new ArgumentException("Every payment method must be supported.", nameof(tenders));
        if (configured && values.Any(value => value.PaymentMethodId is null ||
                value.PaymentMethodId == Guid.Empty || value.PaymentMethodVersion is null or < 1))
            throw new ArgumentException("Every configured tender requires its form identity and version.", nameof(tenders));
        if (configured
            ? values.Select(value => value.PaymentMethodId).Distinct().Count() != values.Length
            : values.Select(value => value.MethodCode).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("A payment method can only appear once.", nameof(tenders));
        if (values.Any(value => value.Amount <= 0))
            throw new ArgumentException("Every payment amount must be greater than zero.", nameof(tenders));
        if (values.Any(value => value.MethodCode != "Cash" && value.TenderedAmount is not null))
            throw new ArgumentException("Only cash can contain a tendered amount.", nameof(tenders));
        if (values.Any(value => value.MethodCode == "Cash" &&
                                value.TenderedAmount is { } tendered && tendered < value.Amount))
            throw new ArgumentException("Cash tendered cannot be less than cash applied.", nameof(tenders));
        // Administrative capture also applies this rule to unconfigured legacy
        // accounts. Other legacy callers retain their versioned cash contract.
        if ((configured || requireEffectiveCash) && values.Any(value => value.MethodCode == "Cash" &&
                value.TenderedAmount is { } tendered && tendered != value.Amount))
            throw new ArgumentException("El movimiento administrativo registra el dinero efectivo, sin vueltos. Clasifica la diferencia mediante una partida explícita.", nameof(tenders));
        if (values.Any(value => value.MethodCode == "Transfer" && value.Reference is null))
            throw new ArgumentException("La transferencia requiere la referencia del soporte bancario.");
        if (values.Any(value => value.MethodCode is "DebitCard" or "CreditCard" &&
                                (value.CardFranchiseCode is null || value.ApprovalNumber is null)))
            throw new ArgumentException("El pago con tarjeta requiere franquicia y número de aprobación.");
        if (values.Any(value => value.MethodCode != "Transfer" && value.BankAccountId is not null))
            throw new ArgumentException("Only a bank transfer can select a bank account.", nameof(tenders));

        var total = Round(values.Sum(value => value.Amount));
        if (total != expected)
            throw new ArgumentException("Payment methods must equal the amount applied to invoices.", nameof(tenders));
        return new(values, total);
    }

    private static PaymentTender Normalize(PaymentTender value) => value with
    {
        MethodCode = value.MethodCode?.Trim() ?? string.Empty,
        Amount = Round(value.Amount),
        TenderedAmount = value.TenderedAmount is null ? null : Round(value.TenderedAmount.Value),
        Reference = Text(value.Reference),
        Notes = Text(value.Notes),
        CardFranchiseCode = Text(value.CardFranchiseCode),
        ApprovalNumber = Text(value.ApprovalNumber)
    };

    private static decimal Round(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
