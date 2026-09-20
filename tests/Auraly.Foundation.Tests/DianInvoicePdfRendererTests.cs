using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianInvoicePdfRendererTests
{
    [Fact]
    public void Published_v1_stays_available_and_deterministic()
    {
        var xml = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;
        var renderer = new DianInvoicePdfRenderer();
        var first = renderer.Render(xml, 1);
        var second = renderer.Render(xml, 1);
        var text = Encoding.ASCII.GetString(first);
        Assert.Equal(first, second);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("FACTURA ELECTRONICA DE VENTA", text);
        Assert.Contains("P-001 / Producto prueba", text);
        Assert.Contains("Resolucion DIAN: 18760000001", text);
    }

    [Fact]
    public void Default_template_is_letter_v2_and_keeps_signed_values_and_accents()
    {
        var invoice = CreateInvoice() with { Supplier = CreateInvoice().Supplier with {
            RegistrationName = "Comercializadora Peña SAS", Email = "factura@example.test", Telephone = "3001234567" } };
        var xml = new DianInvoiceUblBuilder().Build(invoice).Xml;
        var before = xml.ToArray();
        var renderer = new DianInvoicePdfRenderer();
        var pdf = renderer.Render(xml);
        var text = Encoding.ASCII.GetString(pdf);
        Assert.Equal(pdf, renderer.Render(xml));
        Assert.Equal(before, xml);
        Assert.Contains("/MediaBox [0 0 612 792]", text);
        Assert.Contains("/Subject (dian-invoice-letter/2)", text);
        Assert.Contains("Pe\\361a", text);
        Assert.Contains("900123456-8", text);
        Assert.Contains("factura@example.test", text);
        Assert.Contains("3001234567", text);
        Assert.Contains("11.900,00 COP", text);
        Assert.Contains("IVA 19%", text);
        Assert.Contains("Software: Auraly", text);
        Assert.Contains("2026-09-18  10:30:00-05:00", text);
        Assert.Contains("Rango autorizado: 1 a 10000", text);
        Assert.Contains("Consumidor final", text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(100)]
    public void Pagination_keeps_every_item_and_repeats_CUFE_QR_and_table_headers(int count)
    {
        var original = CreateInvoice();
        var lines = Enumerable.Range(1, count).Select(i => original.Lines[0] with {
            Number = i, ProductCode = $"SKU-{i:000}", Description = $"Producto número {i:000} con descripción extensa para entrega" }).ToArray();
        var invoice = original with { Lines = lines, LineExtensionAmount = 10000m * count,
            TaxExclusiveAmount = 10000m * count, TaxInclusiveAmount = 11900m * count, PayableAmount = 11900m * count,
            Taxes = [new DianTax("01", "IVA", 10000m * count, 1900m * count, 19m)] };
        var pdf = Encoding.ASCII.GetString(new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
        var pages = Regex.Matches(pdf, @"/Type /Page /Parent").Count;
        Assert.Equal(pages, Regex.Matches(pdf, "/QR Do").Count);
        Assert.Equal(pages, Regex.Matches(pdf, "CUFE /").Count);
        Assert.Equal(pages, Regex.Matches(pdf, "/MediaBox \\[0 0 612 792\\]").Count);
        Assert.Single(Regex.Matches(pdf, "/Subtype /Form"));
        foreach (var line in lines) Assert.Contains(line.ProductCode, pdf);
        foreach (Match stream in Regex.Matches(pdf, @"stream\n(.*?)\nendstream", RegexOptions.Singleline))
            if (stream.Value.Contains("SKU-", StringComparison.Ordinal))
                Assert.Contains("V. unitario", stream.Value);
        if (count == 100) Assert.True(pages > 2);
        Assert.Contains("TOTAL A PAGAR", pdf);
    }

    [Fact]
    public void Discounts_credit_rounding_and_multiple_tax_totals_are_not_lost_or_recalculated()
    {
        var invoice = CreateInvoice() with {
            Payment = new DianPayment("2", "42", new DateOnly(2026, 10, 18), null),
            Lines = [new DianInvoiceLine(1, "DESC", "999", "Producto con descuento", "EA", 2m, 6000m, 2000m, 10000m,
                [new DianTax("01", "IVA", 10000m, 1900m, 19m), new DianTax("04", "INC", 10000m, 800m, 8m)])],
            Taxes = [new DianTax("01", "IVA", 10000m, 1900m, 19m), new DianTax("04", "INC", 10000m, 800m, 8m)],
            DiscountAmount = 2000m, TaxInclusiveAmount = 12700m, PayableAmount = 12700.5m, PayableRoundingAmount = 0.5m };
        var pdf = Encoding.ASCII.GetString(new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
        Assert.Contains("2.000,00", pdf);
        Assert.Contains("2.700,00", pdf);
        Assert.Contains("12.700,50 COP", pdf);
        Assert.Contains("INC 8%", pdf);
        Assert.Contains("Plazo: 30", pdf);
        Assert.Contains("Ajuste al peso", pdf);
    }

    [Fact]
    public void Very_long_description_spans_pages_without_losing_its_tail()
    {
        var invoice = CreateInvoice();
        invoice = invoice with { Lines = [invoice.Lines[0] with {
            Description = string.Concat(Enumerable.Repeat("Descripción muy extensa de producto. ", 250)) + "FINAL-DESCRIPCION" }] };
        var pdf = Encoding.ASCII.GetString(new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
        Assert.Contains("FINAL-DESCRIPCION", pdf);
        Assert.True(Regex.Matches(pdf, "/Type /Page /Parent").Count > 2);
        Assert.Contains("TOTAL A PAGAR", pdf);
    }

    [Fact]
    public void Six_decimal_prices_and_pdf_delimiters_remain_exact()
    {
        var invoice = CreateInvoice();
        invoice = invoice with { Lines = [invoice.Lines[0] with { UnitPrice = 10000.123456m,
            Description = "Café (especial) \\ premium" }] };
        var pdf = Encoding.ASCII.GetString(new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
        Assert.Contains("10.000,123456", pdf);
        Assert.Contains("Caf\\351 \\(especial\\) \\\\ premium", pdf);
    }

    [Fact]
    public void A_short_invoice_with_contacts_keeps_its_summary_on_one_letter_page()
    {
        var original = CreateInvoice();
        var invoice = original with {
            Supplier = original.Supplier with { Email = "facturacion@example.test", Telephone = "3001234567" },
            Customer = original.Customer with { Email = "compras@example.test", Telephone = "3101234567" },
            Lines = Enumerable.Range(1, 3).Select(i => original.Lines[0] with { Number = i,
                Description = "Café especial de origen, selección premium, presentación 250 g" }).ToArray(),
            LineExtensionAmount = 30000m, TaxExclusiveAmount = 30000m, TaxInclusiveAmount = 35700m, PayableAmount = 35700m,
            Taxes = [new DianTax("01", "IVA", 30000m, 5700m, 19m)] };
        var pdf = Encoding.ASCII.GetString(new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
        Assert.Single(Regex.Matches(pdf, "/Type /Page /Parent"));
        Assert.Contains("Software: Auraly", pdf);
        Assert.Contains("TOTAL A PAGAR", pdf);
    }

    [Fact]
    public void Missing_QR_and_unidentified_provider_fail_without_inventing_legal_data()
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml));
        document.Descendants(DianUblNamespaces.Sts + "QRCode").Single().Remove();
        Assert.Throws<InvalidOperationException>(() => new DianInvoicePdfRenderer().Render(Encoding.UTF8.GetBytes(document.ToString())));
        var invoice = CreateInvoice() with { Software = CreateInvoice().Software with { ProviderTaxId = "999999999" } };
        Assert.Throws<InvalidOperationException>(() => new DianInvoicePdfRenderer().Render(new DianInvoiceUblBuilder().Build(invoice).Xml));
    }

    [Theory]
    [InlineData("UUID")]
    [InlineData("DocumentCurrencyCode")]
    public void Missing_fiscal_data_fails_closed(string field)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml));
        document.Root!.Element(DianUblNamespaces.Cbc + field)!.Remove();
        Assert.Throws<InvalidOperationException>(() => new DianInvoicePdfRenderer().Render(Encoding.UTF8.GetBytes(document.ToString())));
    }

    [Fact]
    public void Invalid_xml_and_unknown_versions_are_rejected()
    {
        var renderer = new DianInvoicePdfRenderer();
        Assert.Throws<InvalidOperationException>(() => renderer.Render(Encoding.UTF8.GetBytes("<!DOCTYPE Invoice SYSTEM 'file:///secret'><Invoice/>")));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml, 99));
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
