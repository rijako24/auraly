using System.Text.Json.Serialization;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesPayment(
    string MethodCode,
    decimal Amount,
    string? Reference,
    string? CardFranchiseCode = null,
    string? ApprovalNumber = null,
    Guid? BankAccountId = null,
    string? Notes = null,
    decimal? TenderedAmount = null,
    decimal RoundingAdjustment = 0m)
{
    public decimal CollectedAmount => Amount + RoundingAdjustment;
}

public sealed record OnlineSalesCreditTerms(
    decimal Amount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteOnlineSalesDraftRequest(
    long ExpectedVersion,
    IReadOnlyList<OnlineSalesPayment> Payments,
    OnlineSalesCreditTerms? Credit = null,
    string DocumentType = PosSaleDocumentTypes.Invoice);

public sealed record OnlineSalesReceiptLine(
    string ProductCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    decimal Total,
    string TaxCode = "01",
    decimal TaxRate = 0,
    string UnitCode = "EA");

public sealed record SalesInvoicePrintDetails(
    string SupplierName,
    string SupplierIdentification,
    string SupplierTaxResponsibility,
    string SupplierAddress,
    string CustomerAddress,
    string AuthorizationNumber,
    DateOnly AuthorizationValidFrom,
    DateOnly AuthorizationValidUntil,
    string AuthorizationPrefix,
    long AuthorizationRangeStart,
    long AuthorizationRangeEnd,
    string PaymentFormCode,
    string PaymentMeansCode,
    DateOnly PaymentDueDate,
    string SoftwareProviderIdentification,
    string SoftwareName);

public sealed record CreditSaleAcknowledgement(
    Guid DocumentId,
    string DocumentNumber,
    DateTimeOffset IssuedAt,
    string CustomerName,
    string CustomerIdentification,
    decimal CreditAmount,
    decimal? RemainingCredit,
    string SoldByName,
    string? CompanyName = null,
    string? CompanyLogoSource = null,
    string? BusinessName = null,
    string? WarehouseName = null);

public sealed record CreditSaleAcknowledgementRenderRequest(
    IReadOnlyList<CreditSaleAcknowledgement> Acknowledgements,
    string Format,
    int ReceiptPaperWidthMillimeters = 80);

public sealed record CreditSaleAcknowledgementRenderResponse(
    IReadOnlyList<string> HtmlDocuments);

public sealed record SalesReceiptTaxTotal(string Name, decimal Rate, decimal TaxableAmount, decimal Amount);

public sealed record OnlineSalesReceipt(
    Guid DocumentId,
    string DocumentType,
    string DocumentNumber,
    string? FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    IReadOnlyList<OnlineSalesReceiptLine> Lines,
    IReadOnlyList<OnlineSalesPayment> Payments,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string? Cufe,
    string? QrPayload,
    string? FiscalStatus,
    string CustomerName,
    string? CompanyName = null,
    string? CompanyLogoSource = null,
    decimal WithholdingTotal = 0m,
    decimal NetPayableAmount = 0m,
    IReadOnlyList<WithholdingLineSnapshot>? Withholdings = null,
    CreditSaleAcknowledgement? CreditAcknowledgement = null,
    SalesInvoicePrintDetails? InvoicePrintDetails = null,
    string? CustomerPhone = null,
    string? CustomerAddress = null,
    IReadOnlyList<SalesReceiptTaxTotal>? TaxTotals = null,
    decimal PayableRoundingAmount = 0m);

public sealed record CompleteOnlineSalesDraftResponse(
    OnlineSalesReceipt Receipt,
    OnlineSalesDraft NextDraft,
    bool IsDuplicate);

// A presentation-only request: it reads and writes no business data.
public sealed record SalesReceiptsRenderRequest(
    IReadOnlyList<OnlineSalesReceipt> Receipts,
    string Format,
    int PaperWidthMillimeters = 80,
    string? BusinessName = null,
    bool AutoPrint = true);
