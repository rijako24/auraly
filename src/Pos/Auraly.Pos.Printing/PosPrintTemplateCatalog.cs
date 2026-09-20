using Auraly.Contracts.Sales;

namespace Auraly.Pos.Printing;

public readonly record struct PosPrintTemplateVersion(string Code, int Version);

public static class PosPrintTemplateCatalog
{
    public static readonly PosPrintTemplateVersion SalesInvoiceV1 = new("sales-invoice", 1);
    public static readonly PosPrintTemplateVersion SalesInvoiceV2 = new("sales-invoice", 2);
    public static readonly PosPrintTemplateVersion SalesInvoice = new("sales-invoice", 3);
    public static readonly PosPrintTemplateVersion SalesReceiptV1 = new("sales-receipt", 1);
    public static readonly PosPrintTemplateVersion SalesReceipt = new("sales-receipt", 2);
    public static readonly PosPrintTemplateVersion OrderV1 = new("order", 1);
    public static readonly PosPrintTemplateVersion Order = new("order", 2);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV1 = new("work-session-closure", 1);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV2 = new("work-session-closure", 2);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV3 = new("work-session-closure", 3);
    public static readonly PosPrintTemplateVersion WorkSessionClosure = new("work-session-closure", 4);
    public static readonly PosPrintTemplateVersion CashEntryV1 = new("cash-entry", 1);
    public static readonly PosPrintTemplateVersion CashExitV1 = new("cash-exit", 1);
    public static readonly PosPrintTemplateVersion CashEntryV2 = new("cash-entry", 2);
    public static readonly PosPrintTemplateVersion CashExitV2 = new("cash-exit", 2);
    public static readonly PosPrintTemplateVersion CashEntry = new("cash-entry", 3);
    public static readonly PosPrintTemplateVersion CashExit = new("cash-exit", 3);
    public static readonly PosPrintTemplateVersion CreditSaleAcknowledgement =
        new("credit-sale-acknowledgement", 1);

    public static PosPrintTemplateVersion ForSale(string documentType) =>
        PosSaleDocumentTypes.IsFiscal(documentType) ? SalesInvoice : SalesReceipt;

    public static PosPrintTemplateVersion ForDocument(string documentType) =>
        documentType == "Order" ? Order : ForSale(documentType);

    public static PosPrintTemplateVersion ForOrder(int? version) => version switch
    {
        1 => OrderV1,
        2 or null => Order,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };
}
