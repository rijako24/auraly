using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Auraly.Fiscal.Core;

namespace Auraly.Fiscal.Ubl;

public sealed record DianAttachedDocumentMetadata(
    string SupplierTaxId,
    string SupplierLegalName,
    string SupplierTradeName,
    string CustomerName,
    string FiscalNumber,
    string DocumentTypeCode,
    string ProfileExecutionId);

public sealed record DianAttachedDocumentBuildResult(
    byte[] Xml,
    string Sha256Hex,
    DianAttachedDocumentMetadata Metadata);

public sealed class DianAttachedDocumentBuilder
{
    private static readonly XNamespace Attached = DianUblNamespaces.AttachedDocument;
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;
    private static readonly XNamespace Ext = DianUblNamespaces.Ext;
    private static readonly XNamespace Ds = DianUblNamespaces.Ds;
    private static readonly XNamespace Xades = DianUblNamespaces.Xades;
    private static readonly XNamespace Xades141 = DianUblNamespaces.Xades141;
    private static readonly XNamespace Xsi = DianUblNamespaces.Xsi;

    public DianAttachedDocumentBuildResult Build(
        ReadOnlyMemory<byte> signedInvoice,
        ReadOnlyMemory<byte> dianApplicationResponse,
        DateTimeOffset generatedAt)
    {
        if (signedInvoice.IsEmpty)
            throw new ArgumentException("Falta el XML fiscal firmado.", nameof(signedInvoice));
        if (dianApplicationResponse.IsEmpty)
            throw new ArgumentException("Falta la respuesta XML de la DIAN.", nameof(dianApplicationResponse));

        var invoice = Load(signedInvoice, "documento fiscal firmado");
        var response = Load(dianApplicationResponse, "DIAN ApplicationResponse");
        var isCreditNote = invoice.Root?.Name == DianUblNamespaces.CreditNote + "CreditNote";
        if (!isCreditNote && invoice.Root?.Name != DianUblNamespaces.Invoice + "Invoice")
            throw new InvalidOperationException("El envío requiere una factura o nota crédito UBL.");
        if (invoice.Root is null) throw new InvalidOperationException("El documento fiscal no tiene contenido.");
        if (response.Root?.Name.LocalName != "ApplicationResponse")
            throw new InvalidOperationException("La respuesta de la DIAN no es un ApplicationResponse UBL.");

        var fiscalNumber = Required(invoice.Root.Element(Cbc + "ID"), "Invoice/cbc:ID");
        var uniqueCode = Required(invoice.Root.Element(Cbc + "UUID"), "Invoice/cbc:UUID");
        var invoiceIssuedOn = Required(invoice.Root.Element(Cbc + "IssueDate"), "Invoice/cbc:IssueDate");
        var documentTypeCode = Required(invoice.Root.Element(Cbc +
            (isCreditNote ? "CreditNoteTypeCode" : "InvoiceTypeCode")), "document type code");
        var profileExecutionId = Required(invoice.Root.Element(Cbc + "ProfileExecutionID"), "Invoice/cbc:ProfileExecutionID");
        var supplier = RequiredTaxScheme(invoice, "AccountingSupplierParty");
        var customer = RequiredTaxScheme(invoice, "AccountingCustomerParty");
        var supplierTaxId = Required(supplier.Element(Cbc + "CompanyID"), "supplier CompanyID");
        var supplierLegalName = Required(supplier.Element(Cbc + "RegistrationName"), "supplier RegistrationName");
        var supplierTradeName = invoice.Descendants(Cac + "AccountingSupplierParty")
            .Descendants(Cac + "PartyName")
            .Elements(Cbc + "Name")
            .Select(value => value.Value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? supplierLegalName;
        var customerName = Required(customer.Element(Cbc + "RegistrationName"), "customer RegistrationName");

        var responseIssuedOn = RequiredLocal(response, "IssueDate");
        var responseIssuedAt = RequiredLocal(response, "IssueTime");
        var validationCode = response.Descendants()
            .FirstOrDefault(value => value.Name.LocalName == "ResponseCode")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(validationCode))
            throw new InvalidOperationException("La respuesta de la DIAN no tiene código de resultado.");

        var colombiaTime = DianFiscalDateTime.InColombia(generatedAt);
        var root = new XElement(
            Attached + "AttachedDocument",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XAttribute(XNamespace.Xmlns + "ds", Ds),
            new XAttribute(XNamespace.Xmlns + "xades", Xades),
            new XAttribute(XNamespace.Xmlns + "xades141", Xades141),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", $"{Attached} UBL-AttachedDocument-2.1.xsd"),
            E(Cbc, "UBLVersionID", "UBL 2.1"),
            E(Cbc, "CustomizationID", "Documentos adjuntos"),
            E(Cbc, "ProfileID", isCreditNote ? "Nota Crédito Electrónica" : "Factura Electrónica de Venta"),
            E(Cbc, "ProfileExecutionID", profileExecutionId),
            E(Cbc, "ID", fiscalNumber),
            E(Cbc, "IssueDate", colombiaTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            E(Cbc, "IssueTime", colombiaTime.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture)),
            E(Cbc, "DocumentType", isCreditNote ? "Contenedor de Nota Crédito Electrónica" : "Contenedor de Factura Electrónica"),
            E(Cbc, "ParentDocumentID", fiscalNumber),
            new XElement(Cac + "SenderParty", new XElement(supplier)),
            new XElement(Cac + "ReceiverParty", new XElement(customer)),
            XmlAttachment(signedInvoice),
            new XElement(Cac + "ParentDocumentLineReference",
                E(Cbc, "LineID", "1"),
                new XElement(Cac + "DocumentReference",
                    E(Cbc, "ID", fiscalNumber),
                    new XElement(Cbc + "UUID",
                        new XAttribute("schemeName", isCreditNote ? "CUDE-SHA384" : "CUFE-SHA384"), uniqueCode),
                    E(Cbc, "IssueDate", invoiceIssuedOn),
                    E(Cbc, "DocumentType", "ApplicationResponse"),
                    XmlAttachment(dianApplicationResponse),
                    new XElement(Cac + "ResultOfVerification",
                        E(Cbc, "ValidatorID", "Unidad Especial Dirección de Impuestos y Aduanas Nacionales"),
                        E(Cbc, "ValidationResultCode", validationCode),
                        E(Cbc, "ValidationDate", responseIssuedOn),
                        E(Cbc, "ValidationTime", responseIssuedAt)))));

        var bytes = Serialize(new XDocument(new XDeclaration("1.0", "utf-8", "no"), root));
        return new DianAttachedDocumentBuildResult(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new DianAttachedDocumentMetadata(
                supplierTaxId, supplierLegalName, supplierTradeName, customerName,
                fiscalNumber, documentTypeCode, profileExecutionId));
    }

    public DianAttachedDocumentMetadata ReadMetadata(ReadOnlyMemory<byte> signedInvoice)
    {
        var invoice = Load(signedInvoice, "documento fiscal firmado");
        var isCreditNote = invoice.Root?.Name == DianUblNamespaces.CreditNote + "CreditNote";
        if (!isCreditNote && invoice.Root?.Name != DianUblNamespaces.Invoice + "Invoice")
            throw new InvalidOperationException("El envío requiere una factura o nota crédito UBL.");
        var supplier = RequiredTaxScheme(invoice, "AccountingSupplierParty");
        var customer = RequiredTaxScheme(invoice, "AccountingCustomerParty");
        var legalName = Required(supplier.Element(Cbc + "RegistrationName"), "supplier RegistrationName");
        var tradeName = invoice.Descendants(Cac + "AccountingSupplierParty")
            .Descendants(Cac + "PartyName")
            .Elements(Cbc + "Name")
            .Select(value => value.Value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? legalName;
        return new DianAttachedDocumentMetadata(
            Required(supplier.Element(Cbc + "CompanyID"), "supplier CompanyID"),
            legalName,
            tradeName,
            Required(customer.Element(Cbc + "RegistrationName"), "customer RegistrationName"),
            Required(invoice.Root?.Element(Cbc + "ID"), "Invoice/cbc:ID"),
            Required(invoice.Root?.Element(Cbc +
                (isCreditNote ? "CreditNoteTypeCode" : "InvoiceTypeCode")), "document type code"),
            Required(invoice.Root?.Element(Cbc + "ProfileExecutionID"), "Invoice/cbc:ProfileExecutionID"));
    }

    private static XElement XmlAttachment(ReadOnlyMemory<byte> xml) =>
        new(Cac + "Attachment",
            new XElement(Cac + "ExternalReference",
                E(Cbc, "MimeCode", "text/xml"),
                E(Cbc, "EncodingCode", "UTF-8"),
                new XElement(Cbc + "Description", new XCData(Utf8(xml)))));

    private static XDocument Load(ReadOnlyMemory<byte> xml, string label)
    {
        try
        {
            using var stream = new MemoryStream(xml.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException($"El {label} no es un XML válido.", exception);
        }
    }

    private static XElement RequiredTaxScheme(XDocument invoice, string partyName) =>
        invoice.Descendants(Cac + partyName)
            .Elements(Cac + "Party")
            .Elements(Cac + "PartyTaxScheme")
            .SingleOrDefault()
        ?? throw new InvalidOperationException($"El documento fiscal no tiene {partyName}/PartyTaxScheme.");

    private static string RequiredLocal(XDocument document, string localName) =>
        document.Root?.Elements().FirstOrDefault(value => value.Name.LocalName == localName)
            is { } element
            ? Required(element, $"ApplicationResponse/{localName}")
            : throw new InvalidOperationException($"La respuesta de la DIAN no tiene {localName}.");

    private static string Required(XElement? element, string field)
    {
        var value = element?.Value.Trim();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"El XML fiscal no tiene {field}.");
    }

    private static string Utf8(ReadOnlyMemory<byte> value)
    {
        var text = new UTF8Encoding(false, true).GetString(value.Span);
        if (text.Contains("]]>", StringComparison.Ordinal))
            throw new InvalidOperationException("El XML fiscal adjunto no puede representarse como CDATA.");
        return text;
    }

    private static XElement E(XNamespace ns, string name, object value) => new(ns + name, value);

    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        }))
            document.Save(writer);
        return stream.ToArray();
    }
}
