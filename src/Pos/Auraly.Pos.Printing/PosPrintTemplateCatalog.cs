using Auraly.Contracts.Sales;

namespace Auraly.Pos.Printing;

public readonly record struct PosPrintTemplateVersion(string Code, int Version);

public static class PosPrintTemplateCatalog
{
    public static readonly PosPrintTemplateVersion SalesReturn = new("sales-return", 1);

    public static PosPrintTemplateVersion ForReturn(int version) => version == 1
        ? SalesReturn : throw new ArgumentOutOfRangeException(nameof(version));
    public static readonly PosPrintTemplateVersion SalesInvoiceV1 = new("sales-invoice", 1);
    public static readonly PosPrintTemplateVersion SalesInvoiceV2 = new("sales-invoice", 2);
    public static readonly PosPrintTemplateVersion SalesInvoiceV3 = new("sales-invoice", 3);
    public static readonly PosPrintTemplateVersion SalesInvoice = new("sales-invoice", 4);
    public static readonly PosPrintTemplateVersion SalesReceiptV1 = new("sales-receipt", 1);
    public static readonly PosPrintTemplateVersion SalesReceiptV2 = new("sales-receipt", 2);
    public static readonly PosPrintTemplateVersion SalesReceiptV3 = new("sales-receipt", 3);
    public static readonly PosPrintTemplateVersion SalesReceiptV4 = new("sales-receipt", 4);
    public static readonly PosPrintTemplateVersion SalesReceipt = new("sales-receipt", 5);
    public static readonly PosPrintTemplateVersion OrderV1 = new("order", 1);
    public static readonly PosPrintTemplateVersion Order = new("order", 2);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV1 = new("work-session-closure", 1);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV2 = new("work-session-closure", 2);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV3 = new("work-session-closure", 3);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV4 = new("work-session-closure", 4);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV5 = new("work-session-closure", 5);
    public static readonly PosPrintTemplateVersion WorkSessionClosureV6 = new("work-session-closure", 6);
    public static readonly PosPrintTemplateVersion WorkSessionClosure = new("work-session-closure", 7);
    public static readonly PosPrintTemplateVersion CashEntryV1 = new("cash-entry", 1);
    public static readonly PosPrintTemplateVersion CashExitV1 = new("cash-exit", 1);
    public static readonly PosPrintTemplateVersion CashEntryV2 = new("cash-entry", 2);
    public static readonly PosPrintTemplateVersion CashExitV2 = new("cash-exit", 2);
    public static readonly PosPrintTemplateVersion CashEntry = new("cash-entry", 3);
    public static readonly PosPrintTemplateVersion CashExit = new("cash-exit", 3);
    public static readonly PosPrintTemplateVersion CashEntrySheet = new("cash-entry", 4);
    public static readonly PosPrintTemplateVersion CashExitSheet = new("cash-exit", 4);
    public static readonly PosPrintTemplateVersion CreditSaleAcknowledgement =
        new("credit-sale-acknowledgement", 1);
    public static readonly PosPrintTemplateVersion ReceivablePayment =
        new("receivable-payment", 1);
    public static readonly PosPrintTemplateVersion PayablePayment =
        new("payable-payment", 1);
    public static readonly PosPrintTemplateVersion ReceivablePaymentSheet =
        new("receivable-payment", 2);
    public static readonly PosPrintTemplateVersion PayablePaymentSheet =
        new("payable-payment", 2);

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

    public static PosPrintTemplateVersion ForReceipt(int? version) => version switch
    {
        1 => SalesReceiptV1,
        2 => SalesReceiptV2,
        3 => SalesReceiptV3,
        4 => SalesReceiptV4,
        5 or null => SalesReceipt,
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };
}
