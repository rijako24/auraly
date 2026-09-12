using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Auraly.Fiscal.Core;

namespace Auraly.Fiscal.Ubl;

public sealed record DianUblDocument(byte[] Xml, string Sha256Hex);

public sealed class DianInvoiceUblBuilder
{
    private static readonly XNamespace Inv = DianUblNamespaces.Invoice;
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;
    private static readonly XNamespace Ext = DianUblNamespaces.Ext;
    private static readonly XNamespace Sts = DianUblNamespaces.Sts;
    private static readonly XNamespace Ds = DianUblNamespaces.Ds;
    private static readonly XNamespace Xades = DianUblNamespaces.Xades;
    private static readonly XNamespace Xades141 = DianUblNamespaces.Xades141;
    private static readonly XNamespace Xsi = DianUblNamespaces.Xsi;

    public DianUblDocument Build(DianInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        invoice.Validate();
        var issuedAt = DianFiscalDateTime.InColombia(invoice.IssuedAt);
        var root = new XElement(
            Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XAttribute(XNamespace.Xmlns + "sts", Sts),
            new XAttribute(XNamespace.Xmlns + "ds", Ds),
            new XAttribute(XNamespace.Xmlns + "xades", Xades),
            new XAttribute(XNamespace.Xmlns + "xades141", Xades141),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", $"{Inv} UBL-Invoice-2.1.xsd"),
            Extensions(invoice),
            E(Cbc, "UBLVersionID", "UBL 2.1"),
            E(Cbc, "CustomizationID", invoice.CustomizationId),
            E(Cbc, "ProfileID", invoice.ProfileId),
            E(Cbc, "ProfileExecutionID", invoice.Environment),
            E(Cbc, "ID", invoice.DocumentNumber),
            new XElement(Cbc + "UUID",
                new XAttribute("schemeID", invoice.Environment),
                new XAttribute("schemeName", invoice.UniqueCodeScheme),
                invoice.Cufe),
            E(Cbc, "IssueDate", issuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            E(Cbc, "IssueTime", issuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture)),
            E(Cbc, "DueDate", invoice.Payment.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            E(Cbc, "InvoiceTypeCode", invoice.InvoiceTypeCode),
            new XElement(Cbc + "DocumentCurrencyCode",
                new XAttribute("listAgencyID", "6"),
                new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
                new XAttribute("listID", "ISO 4217 Alpha"),
                invoice.CurrencyCode),
            E(Cbc, "LineCountNumeric", invoice.Lines.Count),
            Party("AccountingSupplierParty", invoice.Supplier,
                invoice.BuyerGenerated ? null : invoice.Authorization.Prefix,
                invoice.BuyerGenerated ? null : invoice.Authorization.Number),
            Party("AccountingCustomerParty", invoice.Customer,
                invoice.BuyerGenerated ? invoice.Authorization.Prefix : null,
                invoice.BuyerGenerated ? invoice.Authorization.Number : null),
            Payment(invoice.Payment),
            DianTaxTotalXml.Header(invoice.Taxes, invoice.CurrencyCode),
            LegalMonetaryTotal(invoice),
            invoice.Lines.Select(line => InvoiceLine(line, invoice.CurrencyCode)));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
        var bytes = Serialize(document);
        return new DianUblDocument(bytes, Hash(bytes));
    }

    private static XElement Extensions(DianInvoice invoice)
    {
        var securityCode = SoftwareSecurityCodeCalculator.Calculate(
            invoice.Software.SoftwareId,
            invoice.Software.SoftwarePin,
            invoice.DocumentNumber);
        return new XElement(Ext + "UBLExtensions",
            new XElement(Ext + "UBLExtension",
                new XElement(Ext + "ExtensionContent",
                    new XElement(Sts + "DianExtensions",
                        new XElement(Sts + "InvoiceControl",
                            E(Sts, "InvoiceAuthorization", invoice.Authorization.Number),
                            new XElement(Sts + "AuthorizationPeriod",
                                E(Cbc, "StartDate", Date(invoice.Authorization.ValidFrom)),
                                E(Cbc, "EndDate", Date(invoice.Authorization.ValidUntil))),
                            new XElement(Sts + "AuthorizedInvoices",
                                E(Sts, "Prefix", invoice.Authorization.Prefix),
                                E(Sts, "From", invoice.Authorization.RangeStart),
                                E(Sts, "To", invoice.Authorization.RangeEnd))),
                        new XElement(Sts + "InvoiceSource",
                            new XElement(Cbc + "IdentificationCode",
                                new XAttribute("listAgencyID", "6"),
                                new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
                                new XAttribute("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1"),
                                "CO")),
                        new XElement(Sts + "SoftwareProvider",
                            ProviderIdentification(Sts + "ProviderID", invoice.Software.ProviderTaxId,
                                invoice.Software.ProviderCheckDigit, "31"),
                            new XElement(Sts + "SoftwareID",
                                AgencyAttributes(), invoice.Software.SoftwareId)),
                        new XElement(Sts + "SoftwareSecurityCode", AgencyAttributes(), securityCode),
                        new XElement(Sts + "AuthorizationProvider",
                            ProviderIdentification(Sts + "AuthorizationProviderID", "800197268", "4", "31")),
                        E(Sts, "QRCode", invoice.QrPayload)))));
    }

    private static XElement Party(string name, DianParty party, string? prefix, string? registrationName) =>
        new(Cac + name,
            E(Cbc, "AdditionalAccountID", party.OrganizationTypeCode),
            new XElement(Cac + "Party",
                IsFinalConsumer(party)
                    ? new XElement(Cac + "PartyIdentification",
                        Identification(Cbc + "ID", party.Identification, party.CheckDigit,
                            party.IdentificationTypeCode))
                    : null,
                new XElement(Cac + "PartyName", E(Cbc, "Name", party.TradeName)),
                new XElement(Cac + "PhysicalLocation", Address(party.Address)),
                new XElement(Cac + "PartyTaxScheme",
                    E(Cbc, "RegistrationName", party.RegistrationName),
                    Identification(Cbc + "CompanyID", party.Identification, party.CheckDigit, party.IdentificationTypeCode),
                    new XElement(Cbc + "TaxLevelCode",
                        new XAttribute("listName", name == "AccountingSupplierParty" ? "04" : "05"),
                        party.TaxResponsibilityCode),
                    Address(party.Address, "RegistrationAddress"),
                    TaxScheme(party.TaxSchemeId, party.TaxSchemeName)),
                new XElement(Cac + "PartyLegalEntity",
                    E(Cbc, "RegistrationName", party.RegistrationName),
                    Identification(Cbc + "CompanyID", party.Identification, party.CheckDigit, party.IdentificationTypeCode),
                    prefix is null ? null : new XElement(Cac + "CorporateRegistrationScheme",
                        E(Cbc, "ID", prefix), E(Cbc, "Name", registrationName ?? string.Empty))),
                string.IsNullOrWhiteSpace(party.Email) && string.IsNullOrWhiteSpace(party.Telephone)
                    ? null
                    : new XElement(Cac + "Contact",
                        string.IsNullOrWhiteSpace(party.Telephone) ? null : E(Cbc, "Telephone", party.Telephone),
                        string.IsNullOrWhiteSpace(party.Email) ? null : E(Cbc, "ElectronicMail", party.Email))));

    private static XElement Address(DianAddress address, string name = "Address") =>
        new(Cac + name,
            E(Cbc, "ID", address.MunicipalityCode),
            E(Cbc, "CityName", address.CityName),
            E(Cbc, "CountrySubentity", address.DepartmentName),
            E(Cbc, "CountrySubentityCode", address.DepartmentCode),
            new XElement(Cac + "AddressLine", E(Cbc, "Line", address.AddressLine)),
            new XElement(Cac + "Country",
                E(Cbc, "IdentificationCode", address.CountryCode),
                new XElement(Cbc + "Name", new XAttribute("languageID", "es"), address.CountryName)));

    private static XElement Payment(DianPayment payment) =>
        new(Cac + "PaymentMeans",
            E(Cbc, "ID", payment.PaymentFormCode),
            E(Cbc, "PaymentMeansCode", payment.PaymentMeansCode),
            E(Cbc, "PaymentDueDate", Date(payment.DueDate)),
            string.IsNullOrWhiteSpace(payment.Reference) ? null : E(Cbc, "PaymentID", payment.Reference));

    private static XElement LegalMonetaryTotal(DianInvoice invoice) =>
        new(Cac + "LegalMonetaryTotal",
            MoneyElement("LineExtensionAmount", invoice.LineExtensionAmount, invoice.CurrencyCode),
            MoneyElement("TaxExclusiveAmount", invoice.TaxExclusiveAmount, invoice.CurrencyCode),
            MoneyElement("TaxInclusiveAmount", invoice.TaxInclusiveAmount, invoice.CurrencyCode),
            MoneyElement("AllowanceTotalAmount", invoice.DiscountAmount, invoice.CurrencyCode),
            invoice.PayableRoundingAmount == 0
                ? null
                : MoneyElement("PayableRoundingAmount", invoice.PayableRoundingAmount,
                    invoice.CurrencyCode),
            MoneyElement("PayableAmount", invoice.PayableAmount, invoice.CurrencyCode));

    private static XElement InvoiceLine(DianInvoiceLine line, string currency) =>
        new(Cac + "InvoiceLine",
            E(Cbc, "ID", line.Number),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", line.UnitCode), Number(line.Quantity)),
            MoneyElement("LineExtensionAmount", line.UntaxedAmount, currency),
            line.DiscountAmount == 0 ? null : new XElement(Cac + "AllowanceCharge",
                E(Cbc, "ID", "1"), E(Cbc, "ChargeIndicator", "false"),
                E(Cbc, "AllowanceChargeReasonCode", "00"),
                E(Cbc, "AllowanceChargeReason", "Descuento"),
                MoneyElement("Amount", line.DiscountAmount, currency),
                MoneyElement("BaseAmount", line.Quantity * line.UnitPrice, currency)),
            DianTaxTotalXml.Line(line.Taxes, currency),
            new XElement(Cac + "Item",
                E(Cbc, "Description", line.Description),
                new XElement(Cac + "StandardItemIdentification",
                    new XElement(Cbc + "ID", new XAttribute("schemeID", line.ProductCodeScheme), line.ProductCode))),
            new XElement(Cac + "Price",
                MoneyElement("PriceAmount", line.UnitPrice, currency),
                new XElement(Cbc + "BaseQuantity", new XAttribute("unitCode", line.UnitCode), "1.000000")));

    private static XElement TaxScheme(string id, string name) =>
        new(Cac + "TaxScheme", E(Cbc, "ID", id), E(Cbc, "Name", name));

    private static XElement Identification(XName name, string value, string checkDigit, string typeCode) =>
        new(name, AgencyAttributes(),
            typeCode == "31" ? new XAttribute("schemeID", checkDigit) : null,
            new XAttribute("schemeName", typeCode), value);
    private static bool IsFinalConsumer(DianParty party) =>
        party.Identification == "222222222222" && party.IdentificationTypeCode == "13";
    private static XElement ProviderIdentification(
        XName name,
        string value,
        string checkDigit,
        string identificationTypeCode) =>
        new(name, AgencyAttributes(), new XAttribute("schemeID", checkDigit),
            new XAttribute("schemeName", identificationTypeCode), value);

    private static object[] AgencyAttributes() =>
    [
        new XAttribute("schemeAgencyID", "195"),
        new XAttribute("schemeAgencyName", "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)")
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
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        }))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
}
