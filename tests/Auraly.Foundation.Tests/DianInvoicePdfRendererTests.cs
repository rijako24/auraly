using System.Text;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;
using Auraly.Pos.Printing;
using Microsoft.Playwright;

namespace Auraly.Foundation.Tests;

public sealed class DianInvoicePdfRendererTests
{
    [Fact]
    public void Email_uses_exactly_the_shared_letter_v3_html_and_signed_values()
    {
        var invoice = CreateInvoice();
        var xml = new DianInvoiceUblBuilder().Build(invoice).Xml;
        var original = xml.ToArray();
        var renderer = new DianInvoicePdfRenderer();
        var receipt = renderer.ReadReceipt(xml);
        Assert.Equal(new HalfLetterDocumentRenderer().Render([receipt], HalfLetterDocumentRenderer.Letter,
            templateVersion: 3, autoPrint: false), renderer.RenderHtml(xml));
        Assert.Equal(original, xml);
        Assert.Equal(invoice.Cufe, receipt.Cufe);
        Assert.Equal(invoice.PayableAmount, receipt.PayableAmount);
        Assert.Equal(invoice.Lines[0].UntaxedAmount + invoice.Lines[0].Taxes.Sum(x => x.Amount), receipt.Lines[0].Total);
        Assert.Equal("900123456-8", receipt.InvoicePrintDetails!.SupplierIdentification);
        Assert.Contains("data-auraly-report-version=\"3\"", renderer.RenderHtml(xml));
        Assert.DoesNotContain("Forma de pago", renderer.RenderHtml(xml));
        Assert.DoesNotContain("Plazo", renderer.RenderHtml(xml));
        Assert.DoesNotContain("Vencimiento", renderer.RenderHtml(xml));
    }

    [Theory]
    [InlineData("UUID")]
    [InlineData("DocumentCurrencyCode")]
    public void Missing_required_fields_fail_closed(string field)
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml));
        doc.Root!.Element(DianUblNamespaces.Cbc + field)!.Remove();
        Assert.Throws<InvalidOperationException>(() => new DianInvoicePdfRenderer().RenderHtml(Encoding.UTF8.GetBytes(doc.ToString())));
    }

    [Fact]
    public void Xml_entities_markup_and_unidentified_software_cannot_change_the_document()
    {
        var renderer = new DianInvoicePdfRenderer();
        Assert.Throws<InvalidOperationException>(() => renderer.RenderHtml(Encoding.UTF8.GetBytes("<!DOCTYPE Invoice SYSTEM 'file:///secret'><Invoice/>")));
        var invoice = CreateInvoice();
        var badProvider = invoice with { Software = invoice.Software with { ProviderTaxId = "999999999" } };
        Assert.Throws<InvalidOperationException>(() => renderer.RenderHtml(new DianInvoiceUblBuilder().Build(badProvider).Xml));
        var safe = invoice with { Customer = invoice.Customer with { RegistrationName = "Peña <script>alert('x')</script>" } };
        var html = renderer.RenderHtml(new DianInvoiceUblBuilder().Build(safe).Xml);
        Assert.Contains("Pe&#241;a &lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
    }

    [Fact]
    public void Credit_discount_and_rounding_are_projected_without_repricing()
    {
        var invoice = CreateInvoice();
        invoice = invoice with {
            Payment = invoice.Payment with { PaymentFormCode = "2", DueDate = new DateOnly(2026, 10, 18) },
            Lines = [invoice.Lines[0] with { UnitPrice = 11000m, DiscountAmount = 1000m }],
            DiscountAmount = 1000m, PayableRoundingAmount = 0.25m, PayableAmount = 11900.25m };
        var receipt = new DianInvoicePdfRenderer().ReadReceipt(new DianInvoiceUblBuilder().Build(invoice).Xml);
        Assert.Empty(receipt.Payments); // UBL form/means do not describe actual payment allocations.
        Assert.Equal(11900.25m, receipt.PayableAmount);
        Assert.Contains("11.900,25", new DianInvoicePdfRenderer().RenderHtml(receipt));
        Assert.Contains("Descuento:", new DianInvoicePdfRenderer().RenderHtml(receipt));
        Assert.Equal(1000m, Assert.Single(receipt.Lines).Discount);
        Assert.Equal(new DateOnly(2026, 10, 18), receipt.InvoicePrintDetails!.PaymentDueDate);
        Assert.Equal(1900m, Assert.Single(receipt.TaxTotals!).Amount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(1000)]
    public async Task Shared_letter_paginates_all_rows_with_QR_CUFE_and_real_PDF(int count)
    {
        var invoice = CreateInvoice();
        var line = invoice.Lines[0];
        invoice = invoice with {
            Lines = Enumerable.Range(1, count).Select(i => line with { Number = i, Description = $"Producto único {i}" }).ToArray(),
            LineExtensionAmount = 10000m * count, TaxExclusiveAmount = 10000m * count,
            TaxInclusiveAmount = 11900m * count, PayableAmount = 11900m * count,
            Taxes = [invoice.Taxes[0] with { TaxableAmount = 10000m * count, Amount = 1900m * count }] };
        var xml = new DianInvoiceUblBuilder().Build(invoice).Xml;
        var renderer = new DianInvoicePdfRenderer();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        await page.SetContentAsync(renderer.RenderHtml(xml));
        await page.WaitForFunctionAsync("document.documentElement.dataset.auralyReportReady === 'true'");
        Assert.Equal(count, await page.Locator("tbody > tr").CountAsync());
        var pages = await page.Locator(".sheet").CountAsync();
        Assert.Equal(pages, await page.Locator(".qr").CountAsync());
        Assert.Equal(pages, await page.Locator(".fiscal").CountAsync());
        Assert.Equal(pages, await page.Locator("thead").CountAsync());
        Assert.Equal(pages, await page.Locator(".page-number").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("Array.from(document.querySelectorAll('.document')).every(d => d.querySelector('.document-content').scrollHeight <= d.clientHeight + 1)"));
        if (count == 1) Assert.Equal(1, pages); else Assert.True(pages > 1);
        Assert.Contains($"Producto único {count}", await page.Locator("tbody > tr").Last.InnerTextAsync());
        var pdf = await renderer.RenderAsync(xml);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.True(pdf.Length > 1000);
    }
    internal static DianInvoice CreateInvoice()
    {
        var address = new DianAddress(
            "20001", "Valledupar", "Cesar", "20", "Calle 10 # 20-30");
        return new DianInvoice(
            "FE42", new string('a', 96),
            new DateTimeOffset(2026, 9, 18, 10, 30, 0, TimeSpan.FromHours(-5)),
            "COP", "01", 1,
            new DianAuthorization("18760000001", new DateOnly(2026, 1, 1),
                new DateOnly(2027, 12, 31), "FE", 1, 10000),
            new DianSoftware("900123456", "8", "aaaaaaaa-1111-4111-8111-111111111111", "12345"),
            new DianParty("900123456", "8", "31", "1",
                "Comercializadora Uno SAS", "Comercializadora Uno", "R-99-PN",
                "01", "IVA", address),
            new DianParty("222222222222", "0", "13", "2",
                "Consumidor final", "Consumidor final", "R-99-PN",
                "ZZ", "No aplica", address),
            [new DianInvoiceLine(1, "P-001", "999", "Producto prueba", "EA",
                1m, 10_000m, 0m, 10_000m,
                [new DianTax("01", "IVA", 10_000m, 1_900m, 19m)])],
            [new DianTax("01", "IVA", 10_000m, 1_900m, 19m)],
            new DianPayment("1", "10", new DateOnly(2026, 9, 18), null),
            10_000m, 10_000m, 11_900m, 0m, 11_900m,
            "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=" +
            new string('a', 96));
    }
}
