namespace Auraly.BuildingBlocks.Domain.Documents;

public static class AuralyDocumentTypes
{
    public const string SalesInvoice = "SalesInvoice";
    public const string SalesReceipt = "SalesReceipt";
    public const string SalesOrder = "SalesOrder";
    public const string SalesReturn = "SalesReturn";
    public const string SalesDebitNote = "SalesDebitNote";
    public const string GoodsReceipt = "GoodsReceipt";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string Purchase = "Purchase";
    public const string PurchaseReturn = "PurchaseReturn";
    public const string WarehouseTransfer = "WarehouseTransfer";
    public const string StockCount = "StockCount";
    public const string InventoryEntry = "InventoryEntry";
    public const string InventoryExit = "InventoryExit";
    public const string InventoryAdjustment = "InventoryAdjustment";
    public const string Damage = "Damage";
    public const string ProductConversion = "ProductConversion";
    public const string CashCount = "CashCount";
    public const string CustomsLoad = "CustomsLoad";
    public const string CashReceipt = "CashReceipt";
    public const string CashDisbursement = "CashDisbursement";
    public const string ReceivablePayment = "ReceivablePayment";
    public const string PayablePayment = "PayablePayment";
    public const string Expense = "Expense";

    public static string DefaultPrefix(string documentType) => documentType switch
    {
        SalesInvoice => "VTA",
        SalesReceipt => "CVI",
        SalesOrder => "PED",
        SalesReturn => "DVT",
        SalesDebitNote => "NDB",
        GoodsReceipt => "EMC",
        PurchaseOrder => "OCP",
        Purchase => "CMP",
        PurchaseReturn => "DCP",
        WarehouseTransfer => "TRB",
        StockCount => "CTI",
        InventoryEntry => "EIN",
        InventoryExit => "SIN",
        InventoryAdjustment => "AJI",
        Damage => "AVE",
        ProductConversion => "CNV",
        CashCount => "ARQ",
        CustomsLoad => "ADU",
        CashReceipt => "ING",
        CashDisbursement => "EGR",
        ReceivablePayment => "RCC",
        PayablePayment => "PGP",
        Expense => "GTO",
        _ => throw new ArgumentOutOfRangeException(
            nameof(documentType),
            documentType,
            "The Auraly document type does not have a canonical prefix.")
    };
}

public sealed record AuralyDocumentNumberAssignment(
    Guid SeriesId,
    string DocumentType,
    string Prefix,
    string SeriesCode,
    long Consecutive,
    int Padding,
    string FullNumber)
{
    public const int CanonicalPadding = 8;
    public const long MaximumConsecutive = 99_999_999;

    public static AuralyDocumentNumberAssignment Create(
        Guid seriesId,
        string documentType,
        string prefix,
        string seriesCode,
        long consecutive,
        int padding)
    {
        if (seriesId == Guid.Empty) throw new ArgumentException("A document series ID is required.", nameof(seriesId));
        if (string.IsNullOrWhiteSpace(documentType)) throw new ArgumentException("A document type is required.", nameof(documentType));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A document prefix is required.", nameof(prefix));
        if (string.IsNullOrWhiteSpace(seriesCode)) throw new ArgumentException("A series code is required.", nameof(seriesCode));
        if (consecutive is <= 0 or > MaximumConsecutive)
            throw new ArgumentOutOfRangeException(nameof(consecutive));
        if (padding != CanonicalPadding)
            throw new ArgumentOutOfRangeException(
                nameof(padding),
                $"Auraly operational numbers require {CanonicalPadding} consecutive digits.");

        var normalizedPrefix = prefix.Trim().ToUpperInvariant();
        var normalizedSeries = seriesCode.Trim().ToUpperInvariant();
        var canonicalPrefix = AuralyDocumentTypes.DefaultPrefix(documentType);
        if (!string.Equals(normalizedPrefix, canonicalPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Document type '{documentType}' requires Auraly prefix '{canonicalPrefix}'.",
                nameof(prefix));
        }

        var number = consecutive.ToString($"D{padding}", System.Globalization.CultureInfo.InvariantCulture);
        return new AuralyDocumentNumberAssignment(
            seriesId,
            documentType.Trim(),
            normalizedPrefix,
            normalizedSeries,
            consecutive,
            padding,
            $"{normalizedPrefix}{normalizedSeries}-{number}");
    }
}
