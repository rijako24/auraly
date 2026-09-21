using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Auraly.Contracts.Sales;
using Auraly.Pos.Printing;
using Microsoft.Playwright;

namespace Auraly.Fiscal.Ubl;

// Delivery adapts the signed UBL to the shared sales-invoice template. It owns no layout.
public sealed class DianInvoicePdfRenderer
{
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;
    private static readonly XNamespace Sts = DianUblNamespaces.Sts;

    public string RenderHtml(ReadOnlyMemory<byte> signedInvoice) =>
        RenderHtml(ReadReceipt(signedInvoice));

    public string RenderHtml(OnlineSalesReceipt receipt) =>
        new HalfLetterDocumentRenderer().Render([receipt], HalfLetterDocumentRenderer.Letter,
            templateVersion: 3, autoPrint: false);

    public Task<byte[]> RenderAsync(ReadOnlyMemory<byte> signedInvoice, CancellationToken cancellationToken = default) =>
        RenderAsync(ReadReceipt(signedInvoice), cancellationToken);

    public async Task<byte[]> RenderAsync(OnlineSalesReceipt receipt, CancellationToken cancellationToken = default)
    {
        var html = RenderHtml(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 });
        // A fresh context per document prevents cookies, storage and content crossing tenants.
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        await context.RouteAsync("**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        // Use print media before running the template's own pagination.
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.Load });
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count != 0) throw new InvalidOperationException("The shared invoice template could not paginate.");
        await page.WaitForFunctionAsync("document.documentElement.dataset.auralyReportReady === 'true'");
        var pdf = await page.PdfAsync(new() { PreferCSSPageSize = true, PrintBackground = true, DisplayHeaderFooter = false })
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return pdf;
    }

    // Fiscal projection only: payment allocations must come from the recorded sale, not PaymentMeans.
    public OnlineSalesReceipt ReadReceipt(ReadOnlyMemory<byte> signedInvoice)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(signedInvoice.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception) { throw new InvalidOperationException("The signed invoice is invalid XML.", exception); }
        var root = document.Root;
        if (root?.Name != DianUblNamespaces.Invoice + "Invoice")
            throw new InvalidOperationException("The PDF source is not a UBL Invoice document.");
        var supplier = Party(root, "AccountingSupplierParty");
        var customer = Party(root, "AccountingCustomerParty");
        var number = Required(root.Element(Cbc + "ID"), "Invoice/ID");
        var currency = Required(root.Element(Cbc + "DocumentCurrencyCode"), "DocumentCurrencyCode");
        if (currency != "COP") throw new InvalidOperationException("The shared invoice template requires COP.");
        var payment = root.Element(Cac + "PaymentMeans") ?? throw new InvalidOperationException("Missing PaymentMeans.");
        var form = Required(payment.Element(Cbc + "ID"), "PaymentMeans/ID");
        if (form is not ("1" or "2")) throw new InvalidOperationException("Unsupported payment form.");
        var means = Required(payment.Element(Cbc + "PaymentMeansCode"), "PaymentMeansCode");
        var provider = Required(document.Descendants(Sts + "ProviderID").SingleOrDefault(), "ProviderID");
        if (provider != Required(supplier.Element(Cbc + "CompanyID"), "supplier ID"))
            throw new InvalidOperationException("The software provider is not identified by the signed invoice.");
        var totals = root.Element(Cac + "LegalMonetaryTotal") ?? throw new InvalidOperationException("Missing LegalMonetaryTotal.");
        var payable = Amount(totals.Element(Cbc + "PayableAmount"));
        var lines = root.Elements(Cac + "InvoiceLine").Select(line =>
        {
            var quantity = line.Element(Cbc + "InvoicedQuantity");
            var taxes = line.Elements(Cac + "TaxTotal").ToArray();
            var tax = taxes.Sum(value => Amount(value.Element(Cbc + "TaxAmount")));
            var category = taxes.Elements(Cac + "TaxSubtotal").Elements(Cac + "TaxCategory").FirstOrDefault();
            return new OnlineSalesReceiptLine(
                Required(line.Element(Cac + "Item")?.Element(Cac + "StandardItemIdentification")?.Element(Cbc + "ID"), "ProductCode"),
                Required(line.Element(Cac + "Item")?.Element(Cbc + "Description"), "Description"),
                Amount(quantity), Amount(line.Element(Cac + "Price")?.Element(Cbc + "PriceAmount")),
                line.Elements(Cac + "AllowanceCharge").Where(value => value.Element(Cbc + "ChargeIndicator")?.Value == "false")
                    .Sum(value => Amount(value.Element(Cbc + "Amount"))),
                tax, Amount(line.Element(Cbc + "LineExtensionAmount")) + tax,
                category?.Element(Cac + "TaxScheme")?.Element(Cbc + "ID")?.Value ?? "ZZ",
                category?.Element(Cbc + "Percent") is { } percent ? Amount(percent) : 0,
                quantity?.Attribute("unitCode")?.Value ?? throw new InvalidOperationException("Missing unitCode."));
        }).ToArray();
        if (lines.Length == 0) throw new InvalidOperationException("The invoice has no lines.");
        var taxSummary = root.Elements(Cac + "TaxTotal").Elements(Cac + "TaxSubtotal").Select(tax =>
            new SalesReceiptTaxTotal(
                Required(tax.Element(Cac + "TaxCategory")?.Element(Cac + "TaxScheme")?.Element(Cbc + "Name"), "TaxName"),
                Amount(tax.Element(Cac + "TaxCategory")?.Element(Cbc + "Percent")),
                Amount(tax.Element(Cbc + "TaxableAmount")), Amount(tax.Element(Cbc + "TaxAmount")))).ToArray();
        var details = new SalesInvoicePrintDetails(
            Required(supplier.Element(Cbc + "RegistrationName"), "SupplierName"), Identification(supplier),
            Required(supplier.Element(Cbc + "TaxLevelCode"), "TaxLevelCode"), Address(supplier), Address(customer),
            Required(document.Descendants(Sts + "InvoiceAuthorization").SingleOrDefault(), "InvoiceAuthorization"),
            Date(document.Descendants(Sts + "AuthorizationPeriod").Elements(Cbc + "StartDate").SingleOrDefault()),
            Date(document.Descendants(Sts + "AuthorizationPeriod").Elements(Cbc + "EndDate").SingleOrDefault()),
            Required(document.Descendants(Sts + "Prefix").SingleOrDefault(), "Prefix"),
            long.Parse(Required(document.Descendants(Sts + "From").SingleOrDefault(), "From"), CultureInfo.InvariantCulture),
            long.Parse(Required(document.Descendants(Sts + "To").SingleOrDefault(), "To"), CultureInfo.InvariantCulture),
            form, means, Date(payment.Element(Cbc + "PaymentDueDate")), provider, "Auraly");
        return new OnlineSalesReceipt(
            Guid.Empty, PosSaleDocumentTypes.Invoice, number, number,
            DateTimeOffset.Parse(Required(root.Element(Cbc + "IssueDate"), "IssueDate") + "T" +
                Required(root.Element(Cbc + "IssueTime"), "IssueTime"), CultureInfo.InvariantCulture),
            Identification(customer), lines, [],
            Amount(totals.Element(Cbc + "TaxExclusiveAmount")),
            root.Elements(Cac + "TaxTotal").Sum(tax => Amount(tax.Element(Cbc + "TaxAmount"))), payable,
            Required(root.Element(Cbc + "UUID"), "UUID"),
            Required(document.Descendants(Sts + "QRCode").SingleOrDefault(), "QRCode"), null,
            Required(customer.Element(Cbc + "RegistrationName"), "CustomerName"), details.SupplierName,
            InvoicePrintDetails: details, TaxTotals: taxSummary);
    }

    private static XElement Party(XElement root, string name) => root.Element(Cac + name)?.Element(Cac + "Party")?.Element(Cac + "PartyTaxScheme")
        ?? throw new InvalidOperationException($"Missing {name}/PartyTaxScheme.");
    private static string Identification(XElement party)
    {
        var id = party.Element(Cbc + "CompanyID");
        return Required(id, "CompanyID") + (id?.Attribute("schemeName")?.Value == "31" && id.Attribute("schemeID") is { } digit ? $"-{digit.Value}" : string.Empty);
    }
    private static string Address(XElement party) => Required(party.Element(Cac + "RegistrationAddress")?.Element(Cac + "AddressLine")?.Element(Cbc + "Line"), "AddressLine");
    private static string Required(XElement? element, string name) => !string.IsNullOrWhiteSpace(element?.Value)
        ? element.Value.Trim() : throw new InvalidOperationException($"The signed invoice has no {name}.");
    private static decimal Amount(XElement? element) => decimal.Parse(Required(element, "amount"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static DateOnly Date(XElement? element) => DateOnly.ParseExact(Required(element, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
