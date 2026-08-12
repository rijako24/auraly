using System.Text;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianInvoiceUblTests
{
    [Fact]
    public void Same_invoice_produces_identical_xml_and_hash()
    {
        var builder = new DianInvoiceUblBuilder();
        var invoice = CreateInvoice();

        var first = builder.Build(invoice);
        var second = builder.Build(invoice);

        Assert.Equal(first.Xml, second.Xml);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
        Assert.Equal(64, first.Sha256Hex.Length);
    }

    [Fact]
    public void Invoice_uses_required_ubl_namespaces_and_verified_cufe()
    {
        var invoice = CreateInvoice();
        var built = new DianInvoiceUblBuilder().Build(invoice);
        var document = XDocument.Parse(Encoding.UTF8.GetString(built.Xml));
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal(DianUblNamespaces.Invoice + "Invoice", root.Name);
        Assert.Equal("UBL 2.1", root.Element(DianUblNamespaces.Cbc + "UBLVersionID")?.Value);
        Assert.Equal(invoice.Cufe, root.Element(DianUblNamespaces.Cbc + "UUID")?.Value);
        Assert.Equal("2", root.Element(DianUblNamespaces.Cbc + "ProfileExecutionID")?.Value);
        Assert.Equal("SETP990000001", root.Element(DianUblNamespaces.Cbc + "ID")?.Value);
    }

    [Fact]
    public void Generated_invoice_passes_official_ubl_xsd()
    {
        var built = new DianInvoiceUblBuilder().Build(CreateInvoice());
        var result = new DianSchemaValidator().Validate(built.Xml);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Software_security_code_uses_official_sha384_composition()
    {
        var result = SoftwareSecurityCodeCalculator.Calculate(
            "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0",
            "20191",
            "SETP990000001");

        Assert.Equal(96, result.Length);
        Assert.Equal(result, SoftwareSecurityCodeCalculator.Calculate(
            "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0", "20191", "SETP990000001"));
        Assert.NotEqual(result, SoftwareSecurityCodeCalculator.Calculate(
            "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0", "20191", "SETP990000002"));
    }

    private static DianInvoice CreateInvoice()
    {
        var address = new DianAddress("11001", "Bogotá", "Bogotá D.C.", "11", "Carrera 8 # 6C-38");
        var supplier = new DianParty(
            "900123456", "7", "31", "1", "Auraly Comercio SAS", "Auraly",
            "O-48", "01", "IVA", address, "facturacion@auraly.test", "6015550000");
        var customer = new DianParty(
            "222222222", "0", "13", "2", "Consumidor final", "Consumidor final",
            "R-99-PN", "ZZ", "No aplica", address);
        var tax = new DianTax("01", "IVA", 10_000m, 1_900m, 19m);
        var line = new DianInvoiceLine(
            1, "7701234567890", "010", "Producto de prueba", "EA",
            2m, 5_000m, 0m, 10_000m, [tax]);
        return new DianInvoice(
            "SETP990000001",
            new string('a', 96),
            new DateTimeOffset(2026, 7, 28, 10, 15, 30, TimeSpan.FromHours(-5)),
            "COP",
            "01",
            2,
            new DianAuthorization("18760000001", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "SETP", 990000000, 995000000),
            new DianSoftware("900123456", "7", "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0", "20191"),
            supplier,
            customer,
            [line],
            [tax],
            new DianPayment("1", "10", new DateOnly(2026, 7, 28), null),
            10_000m,
            10_000m,
            11_900m,
            0m,
            11_900m,
            "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96));
    }
}