using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianCreditNoteUblTests
{
    [Fact]
    public void Builder_is_deterministic_and_references_original_invoice()
    {
        var note = CreateNote();
        var first = new DianCreditNoteUblBuilder().Build(note);
        var second = new DianCreditNoteUblBuilder().Build(note);
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(first.Xml));

        Assert.Equal(first.Xml, second.Xml);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
        Assert.Equal("CreditNote", xml.Root!.Name.LocalName);
        Assert.Equal(note.OriginalInvoice.DocumentNumber,
            xml.Descendants(DianUblNamespaces.Cac + "InvoiceDocumentReference")
                .Elements(DianUblNamespaces.Cbc + "ID").Single().Value);
        Assert.Equal(DianCreditNoteCodes.PartialReturn,
            xml.Descendants(DianUblNamespaces.Cbc + "ResponseCode").Single().Value);
        Assert.Equal("2", xml.Descendants(DianUblNamespaces.Cbc + "ProfileExecutionID").Single().Value);
        Assert.Equal(DianCreditNoteCodes.DocumentType,
            xml.Descendants(DianUblNamespaces.Cbc + "CreditNoteTypeCode").Single().Value);
        var originalCufe = xml.Descendants(DianUblNamespaces.Cac + "InvoiceDocumentReference")
            .Elements(DianUblNamespaces.Cbc + "UUID").Single();
        Assert.Equal("2", originalCufe.Attribute("schemeID")?.Value);
        Assert.Equal("CUFE-SHA384", originalCufe.Attribute("schemeName")?.Value);
        var provider = xml.Descendants(DianUblNamespaces.Sts + "ProviderID").Single();
        Assert.Equal(note.Software.ProviderCheckDigit, provider.Attribute("schemeID")?.Value);
        Assert.Equal("31", provider.Attribute("schemeName")?.Value);
        var authorizationProvider = xml
            .Descendants(DianUblNamespaces.Sts + "AuthorizationProviderID").Single();
        Assert.Equal("4", authorizationProvider.Attribute("schemeID")?.Value);
        Assert.Equal("31", authorizationProvider.Attribute("schemeName")?.Value);
        var customerIdentification = xml
            .Descendants(DianUblNamespaces.Cac + "AccountingCustomerParty").Single()
            .Descendants(DianUblNamespaces.Cac + "PartyTaxScheme")
            .Elements(DianUblNamespaces.Cbc + "CompanyID").Single();
        Assert.Equal("13", customerIdentification.Attribute("schemeName")?.Value);
        Assert.Null(customerIdentification.Attribute("schemeID"));
    }

    [Fact]
    public void Builder_passes_the_official_credit_note_xsd()
    {
        var built = new DianCreditNoteUblBuilder().Build(CreateNote());
        var result = new DianSchemaValidator().Validate(built.Xml);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Support_document_adjustment_uses_type_95_and_references_the_original_cuds()
    {
        var originalCuds = new string('c', 96);
        var note = CreateNote() with
        {
            DocumentNumber = "NADS1",
            Cude = new string('d', 96),
            DocumentTypeCode = "95",
            CustomizationId = "10",
            ProfileId = "DIAN 2.1: Nota de ajuste al documento soporte en adquisiciones efectuadas a sujetos no obligados a expedir factura o documento equivalente",
            UniqueCodeScheme = "CUDS-SHA384",
            OriginalUniqueCodeScheme = "CUDS-SHA384",
            BuyerGenerated = true,
            CorrectionCode = "1",
            Lines =
            [
                new DianCreditNoteLine(1, "770123", "999", "Producto", "EA", 1m,
                    6000m, 1000m, 5000m,
                    [new DianTax("01", "IVA", 5000m, 950m, 19m)])
            ],
            DiscountAmount = 1000m,
            OriginalInvoice = new DianInvoiceReference(
                "DS1", originalCuds, new DateOnly(2026, 7, 31))
        };

        var built = new DianCreditNoteUblBuilder().Build(note);
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(built.Xml));
        var originalReference = xml
            .Descendants(DianUblNamespaces.Cac + "InvoiceDocumentReference").Single();

        Assert.Equal("95", xml.Descendants(
            DianUblNamespaces.Cbc + "CreditNoteTypeCode").Single().Value);
        Assert.Equal("10", xml.Descendants(
            DianUblNamespaces.Cbc + "CustomizationID").Single().Value);
        Assert.Equal(originalCuds, originalReference.Element(
            DianUblNamespaces.Cbc + "UUID")?.Value);
        Assert.Equal("CUDS-SHA384", originalReference.Element(
            DianUblNamespaces.Cbc + "UUID")?.Attribute("schemeName")?.Value);
        var validation = new DianSchemaValidator().Validate(built.Xml);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }

    private static DianCreditNote CreateNote()
    {
        var address = new DianAddress("11001", "Bogota", "Bogota D.C.", "11", "Calle 1");
        var supplier = new DianParty("900373076", "4", "31", "1", "Auraly SAS", "Auraly",
            "R-99-PN", "01", "IVA", address, "fiscal@auraly.co", "6010000000");
        var customer = new DianParty("8355990", "0", "13", "2", "Cliente", "Cliente",
            "R-99-PN", "ZZ", "No aplica", address);
        var tax = new DianTax("01", "IVA", 5000m, 950m, 19m);
        return new DianCreditNote(
            "NC1", new string('a', 96),
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
            "COP", DianCreditNoteCodes.ReferencesInvoiceOperation,
            DianCreditNoteCodes.PartialReturn, "Devolucion parcial de bienes", 2,
            new DianSoftware("900373076", "4", "software-id", "12301"),
            supplier, customer,
            new DianInvoiceReference("SETP1", new string('b', 96), new DateOnly(2026, 7, 31)),
            [new DianCreditNoteLine(1, "770123", "999", "Producto", "EA", 1m,
                5000m, 0m, 5000m, [tax])],
            [tax], 5000m, 5000m, 5950m, 0m, 5950m,
            "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96));
    }
}
