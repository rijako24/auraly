using Auraly.Contracts.Sales;

namespace Auraly.Pos.Edge.Infrastructure;

public readonly record struct PosPrintTemplateVersion(string Code, int Version);

public static class PosPrintTemplateCatalog
{
    public static readonly PosPrintTemplateVersion SalesInvoice = new("sales-invoice", 1);
    public static readonly PosPrintTemplateVersion SalesReceipt = new("sales-receipt", 1);
    public static readonly PosPrintTemplateVersion WorkSessionClosure = new("work-session-closure", 1);
    public static readonly PosPrintTemplateVersion CashEntry = new("cash-entry", 1);
    public static readonly PosPrintTemplateVersion CashExit = new("cash-exit", 1);

    public static PosPrintTemplateVersion ForSale(string documentType) =>
        PosSaleDocumentTypes.IsFiscal(documentType) ? SalesInvoice : SalesReceipt;
}
