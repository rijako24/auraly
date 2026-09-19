using System.Text;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianInvoicePdfRendererTests
{
    [Fact]
    public void Pdf_is_deterministic_and_contains_the_DIAN_representation_fields()
    {
        var address = new DianAddress(
            "20001", "Valledupar", "Cesar", "20", "Calle 10 # 20-30");
        var invoice = new DianInvoice(
            "FE42", new string('a', 96),
            new DateTimeOffset(2026, 9, 18, 10, 30, 0, TimeSpan.FromHours(-5)),
            "COP", "01", 1,
            new DianAuthorization("18760000001", new DateOnly(2026, 1, 1),
                new DateOnly(2027, 12, 31), "FE", 1, 10000),
            new DianSoftware("900123456", "8", Guid.NewGuid().ToString(), "12345"),
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
        var xml = new DianInvoiceUblBuilder().Build(invoice).Xml;
        var renderer = new DianInvoicePdfRenderer();

        var first = renderer.Render(xml);
        var second = renderer.Render(xml);
        var text = Encoding.ASCII.GetString(first);

        Assert.Equal(first, second);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("FACTURA ELECTRONICA DE VENTA", text);
        Assert.Contains("Comercializadora Uno SAS", text);
        Assert.Contains("Consumidor final", text);
        Assert.Contains("Resolucion DIAN: 18760000001", text);
        Assert.Contains("Rango autorizado: 1 a 10000", text);
        Assert.Contains("P-001 / Producto prueba", text);
        Assert.Contains("IVA 19", text);
        Assert.Contains("CUFE", text);
        Assert.Contains("Software: Auraly", text);
    }
}
