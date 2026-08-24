using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianDebitNoteUblTests
{
    [Fact]
    public void Builder_is_deterministic_references_invoice_and_passes_official_xsd()
    {
        var address = new DianAddress("11001", "Bogota", "Bogota D.C.", "11", "Calle 1");
        var supplier = new DianParty("900373076", "1", "31", "1", "Auraly SAS", "Auraly",
            "R-99-PN", "01", "IVA", address, "fiscal@auraly.co", "6010000000");
        var customer = new DianParty("8355990", "0", "13", "2", "Cliente", "Cliente",
            "R-99-PN", "ZZ", "No aplica", address);
        var tax = new DianTax("01", "IVA", 5000m, 950m, 19m);
        var note = new DianDebitNote(
            "NDB00-00000001", new string('a', 96),
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.FromHours(-5)),
            "COP", DianDebitNoteCodes.ReferencesInvoiceOperation, "3", "Cambio del valor", 2,
            new DianSoftware("900373076", "1", "software-id", "12301"), supplier, customer,
            new DianInvoiceReference("SETP1", new string('b', 96), new DateOnly(2026, 8, 22)),
            [new DianDebitNoteLine(1, "Ajuste de precio", "EA", 1m, 5000m, 5000m, [tax])],
            [tax], 5000m, 5950m,
            "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96));
        var builder = new DianDebitNoteUblBuilder();
        var first = builder.Build(note);
        var second = builder.Build(note);
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(first.Xml));

        Assert.Equal(first.Xml, second.Xml);
        Assert.Equal("DebitNote", xml.Root!.Name.LocalName);
        Assert.Equal("30", xml.Descendants(DianUblNamespaces.Cbc + "CustomizationID").Single().Value);
        Assert.Equal("3", xml.Descendants(DianUblNamespaces.Cbc + "ResponseCode").Single().Value);
        Assert.Equal("SETP1", xml.Descendants(DianUblNamespaces.Cac + "InvoiceDocumentReference")
            .Elements(DianUblNamespaces.Cbc + "ID").Single().Value);
        var validation = new DianSchemaValidator().Validate(first.Xml);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }
}
