using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Auraly.Fiscal.Core;

namespace Auraly.Fiscal.Ubl;

public sealed class DianSupportDocumentUblBuilder
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

    public DianUblDocument Build(DianSupportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        var issuedAt = DianFiscalDateTime.InColombia(document.IssuedAt);
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
            Extensions(document),
            E(Cbc, "UBLVersionID", "UBL 2.1"),
            E(Cbc, "CustomizationID", document.SellerOriginCode),
            E(Cbc, "ProfileID", DianSupportDocument.Profile),
            E(Cbc, "ProfileExecutionID", document.Environment),
            E(Cbc, "ID", document.DocumentNumber),
            new XElement(Cbc + "UUID",
                new XAttribute("schemeID", document.Environment),
                new XAttribute("schemeName", "CUDS-SHA384"),
                document.Cuds),
            E(Cbc, "IssueDate", Date(DateOnly.FromDateTime(issuedAt.Date))),
            E(Cbc, "IssueTime", issuedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture)),
            E(Cbc, "DueDate", Date(document.Payment.DueDate)),
            E(Cbc, "InvoiceTypeCode", "05"),
            new XElement(Cbc + "DocumentCurrencyCode",
                new XAttribute("listAgencyID", "6"),
                new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
                new XAttribute("listID", "ISO 4217 Alpha"),
                document.CurrencyCode),
            E(Cbc, "LineCountNumeric", document.Lines.Count),
            Seller(document),
            Buyer(document.Buyer),
            Payment(document.Payment),
            DianTaxTotalXml.Header(document.Taxes, document.CurrencyCode),
            LegalMonetaryTotal(document),
            document.Lines.Select(line => InvoiceLine(
                line, document.CurrencyCode, DateOnly.FromDateTime(issuedAt.Date))));

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
        var bytes = Serialize(xml);
        return new DianUblDocument(bytes, Hash(bytes));
    }

    private static XElement Extensions(DianSupportDocument document)
    {
        var securityCode = SoftwareSecurityCodeCalculator.Calculate(
            document.Software.SoftwareId,
            document.Software.SoftwarePin,
            document.DocumentNumber);
        return new XElement(Ext + "UBLExtensions",
            new XElement(Ext + "UBLExtension",
                new XElement(Ext + "ExtensionContent",
                    new XElement(Sts + "DianExtensions",
                        new XElement(Sts + "InvoiceControl",
                            E(Sts, "InvoiceAuthorization", document.Authorization.Number),
                            new XElement(Sts + "AuthorizationPeriod",
                                E(Cbc, "StartDate", Date(document.Authorization.ValidFrom)),
                                E(Cbc, "EndDate", Date(document.Authorization.ValidUntil))),
                            new XElement(Sts + "AuthorizedInvoices",
                                E(Sts, "Prefix", document.Authorization.Prefix),
                                E(Sts, "From", document.Authorization.RangeStart),
                                E(Sts, "To", document.Authorization.RangeEnd))),
                        new XElement(Sts + "InvoiceSource",
                            new XElement(Cbc + "IdentificationCode",
                                new XAttribute("listAgencyID", "6"),
                                new XAttribute("listAgencyName", "United Nations Economic Commission for Europe"),
                                new XAttribute("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1"),
                                "CO")),
                        new XElement(Sts + "SoftwareProvider",
                            Identification(Sts + "ProviderID", document.Software.ProviderTaxId,
                                document.Software.ProviderCheckDigit, "31"),
                            new XElement(Sts + "SoftwareID", AgencyAttributes(),
                                document.Software.SoftwareId)),
                        new XElement(Sts + "SoftwareSecurityCode", AgencyAttributes(), securityCode),
                        new XElement(Sts + "AuthorizationProvider",
                            Identification(Sts + "AuthorizationProviderID", "800197268", "4", "31")),
                        E(Sts, "QRCode", document.QrPayload)))));
    }

    private static XElement Seller(DianSupportDocument document)
    {
        var seller = document.Seller;
        var identificationType = document.SellerOriginCode == "10"
            ? "31"
            : seller.IdentificationTypeCode;
        return new XElement(Cac + "AccountingSupplierParty",
            E(Cbc, "AdditionalAccountID", seller.OrganizationTypeCode),
            new XElement(Cac + "Party",
                new XElement(Cac + "PhysicalLocation",
                    Address(seller.Address, document.SellerPostalZone,
                        document.SellerOriginCode == "10")),
                new XElement(Cac + "PartyTaxScheme",
                    E(Cbc, "RegistrationName", seller.RegistrationName),
                    Identification(Cbc + "CompanyID", seller.Identification,
                        seller.CheckDigit, identificationType),
                    new XElement(Cbc + "TaxLevelCode",
                        new XAttribute("listName", "04"), seller.TaxResponsibilityCode),
                    TaxScheme(seller.TaxSchemeId, seller.TaxSchemeName))));
    }

    private static XElement Buyer(DianParty buyer) =>
        new(Cac + "AccountingCustomerParty",
            E(Cbc, "AdditionalAccountID", buyer.OrganizationTypeCode),
            new XElement(Cac + "Party",
                new XElement(Cac + "PartyTaxScheme",
                    E(Cbc, "RegistrationName", buyer.RegistrationName),
                    Identification(Cbc + "CompanyID", buyer.Identification,
                        buyer.CheckDigit, "31"),
                    new XElement(Cbc + "TaxLevelCode",
                        new XAttribute("listName", "05"),
                        buyer.TaxResponsibilityCode),
                    TaxScheme(buyer.TaxSchemeId, buyer.TaxSchemeName))));

    private static XElement Address(DianAddress address, string postalZone, bool resident) =>
        new(Cac + "Address",
            resident ? E(Cbc, "ID", address.MunicipalityCode) : null,
            E(Cbc, "CityName", address.CityName),
            resident ? E(Cbc, "PostalZone", postalZone) : null,
            resident ? E(Cbc, "CountrySubentity", address.DepartmentName) : null,
            resident ? E(Cbc, "CountrySubentityCode", address.DepartmentCode) : null,
            resident
                ? new XElement(Cac + "AddressLine", E(Cbc, "Line", address.AddressLine))
                : null,
            new XElement(Cac + "Country",
                E(Cbc, "IdentificationCode", address.CountryCode),
                new XElement(Cbc + "Name", new XAttribute("languageID", "es"),
                    address.CountryName)));

    private static XElement Payment(DianPayment payment) =>
        new(Cac + "PaymentMeans",
            E(Cbc, "ID", payment.PaymentFormCode),
            E(Cbc, "PaymentMeansCode", payment.PaymentMeansCode),
            E(Cbc, "PaymentDueDate", Date(payment.DueDate)),
            string.IsNullOrWhiteSpace(payment.Reference)
                ? null
                : E(Cbc, "PaymentID", payment.Reference));

    private static XElement LegalMonetaryTotal(DianSupportDocument document) =>
        new(Cac + "LegalMonetaryTotal",
            MoneyElement("LineExtensionAmount", document.LineExtensionAmount,
                document.CurrencyCode),
            MoneyElement("TaxExclusiveAmount", document.TaxExclusiveAmount,
                document.CurrencyCode),
            MoneyElement("TaxInclusiveAmount", document.TaxInclusiveAmount,
                document.CurrencyCode),
            MoneyElement("PayableAmount", document.PayableAmount, document.CurrencyCode));

    private static XElement InvoiceLine(
        DianInvoiceLine line,
        string currency,
        DateOnly acquisitionDate) =>
        new(Cac + "InvoiceLine",
            E(Cbc, "ID", line.Number),
            new XElement(Cbc + "InvoicedQuantity",
                new XAttribute("unitCode", line.UnitCode), Number(line.Quantity)),
            MoneyElement("LineExtensionAmount", line.UntaxedAmount, currency),
            new XElement(Cac + "InvoicePeriod",
                E(Cbc, "StartDate", Date(acquisitionDate)),
                E(Cbc, "DescriptionCode", "1"),
                E(Cbc, "Description", "Por operación")),
            line.DiscountAmount == 0
                ? null
                : new XElement(Cac + "AllowanceCharge",
                    E(Cbc, "ID", "1"),
                    E(Cbc, "ChargeIndicator", "false"),
                    E(Cbc, "AllowanceChargeReason", "Descuento"),
                    E(Cbc, "MultiplierFactorNumeric", Percentage(
                        line.DiscountAmount, line.Quantity * line.UnitPrice)),
                    MoneyElement("Amount", line.DiscountAmount, currency),
                    MoneyElement("BaseAmount", line.Quantity * line.UnitPrice, currency)),
            DianTaxTotalXml.Line(line.Taxes, currency),
            new XElement(Cac + "Item",
                E(Cbc, "Description", line.Description),
                new XElement(Cac + "StandardItemIdentification",
                    ProductIdentification(line.ProductCode, line.ProductCodeScheme))),
            new XElement(Cac + "Price",
                MoneyElement("PriceAmount", line.UnitPrice, currency),
                new XElement(Cbc + "BaseQuantity",
                    new XAttribute("unitCode", line.UnitCode), Number(line.Quantity))));

    private static XElement ProductIdentification(string code, string schemeId)
    {
        var schemeName = schemeId switch
        {
            "001" => "UNSPSC",
            "010" => "GTIN",
            "999" => "Estándar de adopción del contribuyente",
            _ => throw new ArgumentException(
                $"Product code scheme '{schemeId}' is not valid for a support document.")
        };
        return new XElement(Cbc + "ID",
            new XAttribute("schemeID", schemeId),
            new XAttribute("schemeName", schemeName),
            schemeId == "001" ? new XAttribute("schemeAgencyID", "10") : null,
            schemeId == "010" ? new XAttribute("schemeAgencyID", "9") : null,
            code);
    }

    private static XElement Identification(
        XName name,
        string value,
        string checkDigit,
        string typeCode) =>
        new(name, AgencyAttributes(),
            typeCode == "31" ? new XAttribute("schemeID", checkDigit) : null,
            new XAttribute("schemeName", typeCode), value);

    private static XElement TaxScheme(string id, string name) =>
        new(Cac + "TaxScheme", E(Cbc, "ID", id), E(Cbc, "Name", name));

    private static object[] AgencyAttributes() =>
    [
        new XAttribute("schemeAgencyID", "195"),
        new XAttribute("schemeAgencyName",
            "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)")
    ];

    private static XElement MoneyElement(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency),
            DianUblAmountFormatter.Money(value));

    private static XElement E(XNamespace ns, string name, object value) =>
        new(ns + name, value);

    private static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);

    private static string Percentage(decimal amount, decimal baseAmount) =>
        decimal.Round(amount / baseAmount * 100m, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);

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

    private static string Hash(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))
            .ToLowerInvariant();
}
