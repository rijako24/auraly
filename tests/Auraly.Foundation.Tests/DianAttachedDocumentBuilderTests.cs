using System.Text;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianAttachedDocumentBuilderTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 9, 12, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Builds_schema_valid_container_with_exact_invoice_and_dian_response()
    {
        var invoice = Encoding.UTF8.GetBytes(InvoiceXml);
        var response = Encoding.UTF8.GetBytes(ApplicationResponseXml);

        var result = new DianAttachedDocumentBuilder().Build(invoice, response, GeneratedAt);

        var validation = new DianSchemaValidator().Validate(result.Xml);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        var document = XDocument.Parse(Encoding.UTF8.GetString(result.Xml));
        XNamespace cac = DianUblNamespaces.Cac;
        XNamespace cbc = DianUblNamespaces.Cbc;
        var descriptions = document.Descendants(cbc + "Description").ToArray();
        Assert.Equal(2, descriptions.Length);
        Assert.Equal(InvoiceXml, descriptions[0].Value);
        Assert.Equal(ApplicationResponseXml, descriptions[1].Value);
        Assert.Single(document.Descendants(cac + "ParentDocumentLineReference"));
        Assert.Equal("00", document.Descendants(cbc + "ValidationResultCode").Single().Value);
        Assert.Equal("2026-09-12", document.Root!.Element(cbc + "IssueDate")!.Value);
        Assert.Equal("11:30:00-05:00", document.Root.Element(cbc + "IssueTime")!.Value);
    }

    [Fact]
    public void Reads_immutable_subject_metadata_from_the_signed_invoice()
    {
        var metadata = new DianAttachedDocumentBuilder()
            .ReadMetadata(Encoding.UTF8.GetBytes(InvoiceXml));

        Assert.Equal("900123456", metadata.SupplierTaxId);
        Assert.Equal("Auraly SAS", metadata.SupplierLegalName);
        Assert.Equal("Auraly", metadata.SupplierTradeName);
        Assert.Equal("Cliente Prueba", metadata.CustomerName);
        Assert.Equal("SETP990000099", metadata.FiscalNumber);
        Assert.Equal("01", metadata.DocumentTypeCode);
        Assert.Equal("2", metadata.ProfileExecutionId);
    }

    [Fact]
    public void Rejects_a_response_without_the_dian_validation_code()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DianAttachedDocumentBuilder().Build(
                Encoding.UTF8.GetBytes(InvoiceXml),
                Encoding.UTF8.GetBytes("<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"><cbc:IssueDate>2026-09-12</cbc:IssueDate><cbc:IssueTime>11:29:59-05:00</cbc:IssueTime></ApplicationResponse>"),
                GeneratedAt));

        Assert.Contains("ResponseCode", exception.Message, StringComparison.Ordinal);
    }

    private const string InvoiceXml =
        "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"><cbc:ProfileExecutionID>2</cbc:ProfileExecutionID><cbc:ID>SETP990000099</cbc:ID><cbc:UUID schemeName=\"CUFE-SHA384\">abc123</cbc:UUID><cbc:IssueDate>2026-09-12</cbc:IssueDate><cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode><cac:AccountingSupplierParty><cac:Party><cac:PartyName><cbc:Name>Auraly</cbc:Name></cac:PartyName><cac:PartyTaxScheme><cbc:RegistrationName>Auraly SAS</cbc:RegistrationName><cbc:CompanyID schemeAgencyID=\"195\" schemeID=\"8\" schemeName=\"31\">900123456</cbc:CompanyID><cbc:TaxLevelCode listName=\"04\">R-99-PN</cbc:TaxLevelCode><cac:TaxScheme><cbc:ID>01</cbc:ID><cbc:Name>IVA</cbc:Name></cac:TaxScheme></cac:PartyTaxScheme></cac:Party></cac:AccountingSupplierParty><cac:AccountingCustomerParty><cac:Party><cac:PartyTaxScheme><cbc:RegistrationName>Cliente Prueba</cbc:RegistrationName><cbc:CompanyID schemeAgencyID=\"195\" schemeID=\"7\" schemeName=\"31\">901234567</cbc:CompanyID><cbc:TaxLevelCode listName=\"05\">R-99-PN</cbc:TaxLevelCode><cac:TaxScheme><cbc:ID>ZZ</cbc:ID><cbc:Name>No aplica</cbc:Name></cac:TaxScheme></cac:PartyTaxScheme></cac:Party></cac:AccountingCustomerParty></Invoice>";

    private const string ApplicationResponseXml =
        "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"><cbc:IssueDate>2026-09-12</cbc:IssueDate><cbc:IssueTime>11:29:59-05:00</cbc:IssueTime><cac:DocumentResponse><cac:Response><cbc:ResponseCode>00</cbc:ResponseCode></cac:Response></cac:DocumentResponse></ApplicationResponse>";
}
