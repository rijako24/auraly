using System.Text;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianSupportDocumentUblTests
{
    [Fact]
    public void Resident_support_document_matches_the_dian_ubl_contract()
    {
        var support = CreateSupportDocument();
        var built = new DianSupportDocumentUblBuilder().Build(support);
        var document = XDocument.Parse(Encoding.UTF8.GetString(built.Xml));
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal(DianSupportDocument.Profile,
            root.Element(DianUblNamespaces.Cbc + "ProfileID")?.Value);
        Assert.Equal("05", root.Element(DianUblNamespaces.Cbc + "InvoiceTypeCode")?.Value);
        Assert.Equal("10", root.Element(DianUblNamespaces.Cbc + "CustomizationID")?.Value);

        var seller = document.Descendants(
            DianUblNamespaces.Cac + "AccountingSupplierParty").Single();
        Assert.Empty(seller.Descendants(DianUblNamespaces.Cac + "PartyIdentification"));
        Assert.Empty(seller.Descendants(DianUblNamespaces.Cac + "PartyLegalEntity"));
        Assert.Equal("200001", seller.Descendants(
            DianUblNamespaces.Cbc + "PostalZone").Single().Value);
        var sellerId = seller.Descendants(DianUblNamespaces.Cac + "PartyTaxScheme")
            .Elements(DianUblNamespaces.Cbc + "CompanyID").Single();
        Assert.Equal("31", sellerId.Attribute("schemeName")?.Value);
        Assert.Equal("4", sellerId.Attribute("schemeID")?.Value);

        var buyer = document.Descendants(
            DianUblNamespaces.Cac + "AccountingCustomerParty").Single();
        Assert.Empty(buyer.Descendants(DianUblNamespaces.Cac + "PhysicalLocation"));
        Assert.Empty(buyer.Descendants(DianUblNamespaces.Cac + "PartyLegalEntity"));
        var buyerId = buyer.Descendants(DianUblNamespaces.Cac + "PartyTaxScheme")
            .Elements(DianUblNamespaces.Cbc + "CompanyID").Single();
        Assert.Equal("31", buyerId.Attribute("schemeName")?.Value);
        Assert.Equal("6", buyerId.Attribute("schemeID")?.Value);

        var lines = root.Elements(DianUblNamespaces.Cac + "InvoiceLine").ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line =>
        {
            var period = Assert.Single(line.Elements(
                DianUblNamespaces.Cac + "InvoicePeriod"));
            Assert.Equal("1", period.Element(
                DianUblNamespaces.Cbc + "DescriptionCode")?.Value);
            Assert.Equal("Por operación", period.Element(
                DianUblNamespaces.Cbc + "Description")?.Value);
            Assert.Equal(
                line.Element(DianUblNamespaces.Cbc + "InvoicedQuantity")?.Value,
                line.Descendants(DianUblNamespaces.Cbc + "BaseQuantity").Single().Value);
            var product = line.Descendants(DianUblNamespaces.Cac +
                    "StandardItemIdentification")
                .Elements(DianUblNamespaces.Cbc + "ID").Single();
            Assert.Equal("Estándar de adopción del contribuyente",
                product.Attribute("schemeName")?.Value);
        });
        Assert.Equal("106421.445", lines[0].Element(
            DianUblNamespaces.Cbc + "LineExtensionAmount")?.Value);
        Assert.Equal("157251.29", root.Descendants(
            DianUblNamespaces.Cac + "LegalMonetaryTotal")
            .Elements(DianUblNamespaces.Cbc + "PayableAmount").Single().Value);

        var validation = new DianSchemaValidator().Validate(built.Xml);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }

    [Fact]
    public void Same_support_document_produces_identical_xml_and_hash()
    {
        var builder = new DianSupportDocumentUblBuilder();
        var support = CreateSupportDocument();

        var first = builder.Build(support);
        var second = builder.Build(support);

        Assert.Equal(first.Xml, second.Xml);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
    }

    [Fact]
    public void Resident_support_document_requires_a_six_digit_postal_zone()
    {
        var support = CreateSupportDocument() with { SellerPostalZone = "20001" };

        var exception = Assert.Throws<ArgumentException>(() =>
            new DianSupportDocumentUblBuilder().Build(support));

        Assert.Contains("six-digit DIAN postal zone", exception.Message,
            StringComparison.Ordinal);
    }

    private static DianSupportDocument CreateSupportDocument()
    {
        var lines = new[]
        {
            new DianInvoiceLine(1, "P1", "999", "Producto uno", "EA",
                9.86m, 10_793.25m, 0m, 106_421.445m,
                [new DianTax("01", "IVA", 106_421.445m, 0m, 0m)]),
            new DianInvoiceLine(2, "P2", "999", "Producto dos", "EA",
                20.9m, 2_432.05m, 0m, 50_829.845m,
                [new DianTax("01", "IVA", 50_829.845m, 0m, 0m)])
        };
        var taxes = new[] { new DianTax("01", "IVA", 157_251.29m, 0m, 0m) };
        var address = new DianAddress(
            "20001", "Valledupar", "Cesar", "20", "CRA 15 19E-03 BARRIO LA GRANJA");
        return new DianSupportDocument(
            "FVL21", new string('a', 96),
            new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.FromHours(-5)),
            "COP", 1,
            new DianAuthorization("18764000000001", new DateOnly(2026, 9, 1),
                new DateOnly(2028, 9, 1), "FVL2", 1, 10_000),
            new DianSoftware("1065658655", "6", Guid.Empty.ToString(), "12345"),
            new DianParty("7571928", "4", "31", "2", "HECTOR TORRES",
                "HECTOR TORRES", "R-99-PN", "ZZ", "No aplica", address),
            new DianParty("1065658655", "6", "31", "1", "MEGA FRUVER",
                "MEGA FRUVER", "R-99-PN", "01", "IVA", address),
            "10", "200001", lines, taxes,
            new DianPayment("1", "10", new DateOnly(2026, 9, 17), null),
            157_251.29m, 157_251.29m, 157_251.29m, 0m, 157_251.29m,
            "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=" +
            new string('a', 96));
    }
}
