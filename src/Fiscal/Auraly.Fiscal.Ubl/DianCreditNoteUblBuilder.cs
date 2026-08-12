using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

public sealed class DianCreditNoteUblBuilder
{
    private static readonly XNamespace Note = DianUblNamespaces.CreditNote;
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;
    private static readonly XNamespace Ext = DianUblNamespaces.Ext;
    private static readonly XNamespace Sts = DianUblNamespaces.Sts;
    private static readonly XNamespace Ds = DianUblNamespaces.Ds;
    private static readonly XNamespace Xades = DianUblNamespaces.Xades;
    private static readonly XNamespace Xades141 = DianUblNamespaces.Xades141;
    private static readonly XNamespace Xsi = DianUblNamespaces.Xsi;

    public DianUblDocument Build(DianCreditNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.Validate();
        var root = new XElement(Note + "CreditNote",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XAttribute(XNamespace.Xmlns + "sts", Sts),
            new XAttribute(XNamespace.Xmlns + "ds", Ds),
            new XAttribute(XNamespace.Xmlns + "xades", Xades),
            new XAttribute(XNamespace.Xmlns + "xades141", Xades141),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", $"{Note} UBL-CreditNote-2.1.xsd"),
            Extensions(note),
            E(Cbc, "UBLVersionID", "UBL 2.1"),
            E(Cbc, "CustomizationID", note.OperationCode),
            E(Cbc, "ProfileID", "DIAN 2.1: Nota Cr\u00e9dito de Factura Electr\u00f3nica de Venta"),
            E(Cbc, "ProfileExecutionID", note.Environment),
            E(Cbc, "ID", note.DocumentNumber),
            new XElement(Cbc + "UUID",
                new XAttribute("schemeID", note.Environment),
                new XAttribute("schemeName", "CUDE-SHA384"), note.Cude),
            E(Cbc, "IssueDate", note.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            E(Cbc, "IssueTime", note.IssuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture)),
            E(Cbc, "CreditNoteTypeCode", DianCreditNoteCodes.DocumentType),
            Currency(note.CurrencyCode),
            E(Cbc, "LineCountNumeric", note.Lines.Count),
            new XElement(Cac + "DiscrepancyResponse",
                E(Cbc, "ReferenceID", note.OriginalInvoice.DocumentNumber),
                E(Cbc, "ResponseCode", note.CorrectionCode),
                E(Cbc, "Description", note.CorrectionDescription)),
            new XElement(Cac + "BillingReference",
                new XElement(Cac + "InvoiceDocumentReference",
                    E(Cbc, "ID", note.OriginalInvoice.DocumentNumber),
                    new XElement(Cbc + "UUID",
                        new XAttribute("schemeName", "CUFE-SHA384"), note.OriginalInvoice.Cufe),
                    E(Cbc, "IssueDate", Date(note.OriginalInvoice.IssuedOn)))),
            Party("AccountingSupplierParty", note.Supplier),
            Party("AccountingCustomerParty", note.Customer),
            note.Taxes.Select(tax => TaxTotal(tax, note.CurrencyCode)),
            RequestedMonetaryTotal(note),
            note.Lines.Select(line => CreditLine(line, note.CurrencyCode)));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
        var bytes = Serialize(document);
        return new DianUblDocument(bytes, Hash(bytes));
    }

    private static XElement Extensions(DianCreditNote note)
    {
        var security = SoftwareSecurityCodeCalculator.Calculate(
            note.Software.SoftwareId, note.Software.SoftwarePin, note.DocumentNumber);
        return new XElement(Ext + "UBLExtensions",
            new XElement(Ext + "UBLExtension",
                new XElement(Ext + "ExtensionContent",
                    new XElement(Sts + "DianExtensions",
                        new XElement(Sts + "InvoiceSource",
                            new XElement(Cbc + "IdentificationCode",
                                new XAttribute("listAgencyID", "6"),
                                new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
                                new XAttribute("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1"), "CO")),
                        new XElement(Sts + "SoftwareProvider",
                            ProviderId(Sts + "ProviderID", note.Software.ProviderTaxId,
                                note.Supplier.IdentificationTypeCode, note.Software.ProviderCheckDigit),
                            new XElement(Sts + "SoftwareID", Agency(), note.Software.SoftwareId)),
                        new XElement(Sts + "SoftwareSecurityCode", Agency(), security),
                        new XElement(Sts + "AuthorizationProvider",
                            ProviderId(Sts + "AuthorizationProviderID", "800197268", "31", "4")),
                        E(Sts, "QRCode", note.QrPayload)))));
    }

    private static XElement Party(string element, DianParty party) =>
        new(Cac + element,
            E(Cbc, "AdditionalAccountID", party.OrganizationTypeCode),
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyName", E(Cbc, "Name", party.TradeName)),
                new XElement(Cac + "PhysicalLocation", Address(party.Address)),
                new XElement(Cac + "PartyTaxScheme",
                    E(Cbc, "RegistrationName", party.RegistrationName),
                    Identification(Cbc + "CompanyID", party.Identification,
                        party.CheckDigit, party.IdentificationTypeCode),
                    new XElement(Cbc + "TaxLevelCode", new XAttribute("listName", "48"),
                        party.TaxResponsibilityCode),
                    Address(party.Address, "RegistrationAddress"),
                    TaxScheme(party.TaxSchemeId, party.TaxSchemeName)),
                new XElement(Cac + "PartyLegalEntity",
                    E(Cbc, "RegistrationName", party.RegistrationName),
                    Identification(Cbc + "CompanyID", party.Identification,
                        party.CheckDigit, party.IdentificationTypeCode)),
                string.IsNullOrWhiteSpace(party.Email) && string.IsNullOrWhiteSpace(party.Telephone)
                    ? null
                    : new XElement(Cac + "Contact",
                        string.IsNullOrWhiteSpace(party.Telephone) ? null : E(Cbc, "Telephone", party.Telephone),
                        string.IsNullOrWhiteSpace(party.Email) ? null : E(Cbc, "ElectronicMail", party.Email))));

    private static XElement Address(DianAddress value, string name = "Address") =>
        new(Cac + name,
            E(Cbc, "ID", value.MunicipalityCode), E(Cbc, "CityName", value.CityName),
            E(Cbc, "CountrySubentity", value.DepartmentName),
            E(Cbc, "CountrySubentityCode", value.DepartmentCode),
            new XElement(Cac + "AddressLine", E(Cbc, "Line", value.AddressLine)),
            new XElement(Cac + "Country",
                E(Cbc, "IdentificationCode", value.CountryCode),
                new XElement(Cbc + "Name", new XAttribute(XNamespace.Xml + "lang", "es"), value.CountryName)));

    private static XElement TaxTotal(DianTax tax, string currency) =>
        new(Cac + "TaxTotal",
            MoneyElement("TaxAmount", tax.Amount, currency),
            new XElement(Cac + "TaxSubtotal",
                MoneyElement("TaxableAmount", tax.TaxableAmount, currency),
                MoneyElement("TaxAmount", tax.Amount, currency),
                new XElement(Cac + "TaxCategory", E(Cbc, "Percent", Number(tax.Percent)),
                    TaxScheme(tax.Code, tax.Name))));

    private static XElement RequestedMonetaryTotal(DianCreditNote note) =>
        new(Cac + "LegalMonetaryTotal",
            MoneyElement("LineExtensionAmount", note.LineExtensionAmount, note.CurrencyCode),
            MoneyElement("TaxExclusiveAmount", note.TaxExclusiveAmount, note.CurrencyCode),
            MoneyElement("TaxInclusiveAmount", note.TaxInclusiveAmount, note.CurrencyCode),
            MoneyElement("AllowanceTotalAmount", note.DiscountAmount, note.CurrencyCode),
            MoneyElement("PayableAmount", note.PayableAmount, note.CurrencyCode));

    private static XElement CreditLine(DianCreditNoteLine line, string currency) =>
        new(Cac + "CreditNoteLine",
            E(Cbc, "ID", line.Number),
            new XElement(Cbc + "CreditedQuantity", new XAttribute("unitCode", line.UnitCode), Number(line.Quantity)),
            MoneyElement("LineExtensionAmount", line.UntaxedAmount, currency),
            line.DiscountAmount == 0 ? null : new XElement(Cac + "AllowanceCharge",
                E(Cbc, "ID", "1"), E(Cbc, "ChargeIndicator", "false"),
                E(Cbc, "AllowanceChargeReasonCode", "00"), E(Cbc, "AllowanceChargeReason", "Descuento"),
                MoneyElement("Amount", line.DiscountAmount, currency),
                MoneyElement("BaseAmount", line.Quantity * line.UnitPrice, currency)),
            line.Taxes.Select(tax => TaxTotal(tax, currency)),
            new XElement(Cac + "Item", E(Cbc, "Description", line.Description),
                new XElement(Cac + "StandardItemIdentification",
                    new XElement(Cbc + "ID", new XAttribute("schemeID", line.ProductCodeScheme), line.ProductCode))),
            new XElement(Cac + "Price", MoneyElement("PriceAmount", line.UnitPrice, currency),
                new XElement(Cbc + "BaseQuantity", new XAttribute("unitCode", line.UnitCode), "1.000000")));

    private static XElement Currency(string code) => new(Cbc + "DocumentCurrencyCode",
        new XAttribute("listAgencyID", "6"),
        new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
        new XAttribute("listID", "ISO 4217 Alpha"), code);
    private static XElement TaxScheme(string id, string name) =>
        new(Cac + "TaxScheme", E(Cbc, "ID", id), E(Cbc, "Name", name));
    private static XElement Identification(XName name, string value, string check, string type) =>
        new(name, Agency(), new XAttribute("schemeID", check), new XAttribute("schemeName", type), value);
    private static XElement ProviderId(XName name, string value, string type, string check) =>
        new(name, Agency(), new XAttribute("schemeID", type), new XAttribute("schemeName", check), value);
    private static object[] Agency() =>
    [
        new XAttribute("schemeAgencyID", "195"),
        new XAttribute("schemeAgencyName", "CO, DIAN (Direcci\u00f3n de Impuestos y Aduanas Nacionales)")
    ];
    private static XElement MoneyElement(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency), Money(value));
    private static XElement E(XNamespace ns, string name, object value) => new(ns + name, value);
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Number(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = false,
            OmitXmlDeclaration = false, NewLineHandling = NewLineHandling.None
        })) document.Save(writer);
        return stream.ToArray();
    }
    private static string Hash(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
}
