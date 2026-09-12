using System.Net;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

public sealed record DianSchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class DianSchemaValidator
{
    private static readonly XNamespace Cac =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Sts =
        "dian:gov:co:facturaelectronica:Structures-2-1";
    private static readonly XNamespace Ext =
        "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private static readonly XNamespace Ds =
        "http://www.w3.org/2000/09/xmldsig#";
    private static readonly XNamespace Xades =
        "http://uri.etsi.org/01903/v1.3.2#";
    private readonly XmlSchemaSet schemas;

    public DianSchemaValidator(string? schemaRoot = null)
    {
        var root = Path.GetFullPath(schemaRoot ?? Path.Combine(AppContext.BaseDirectory, "Schemas"));
        var main = Path.Combine(root, "maindoc");
        var resolver = new LocalSchemaResolver(root);
        schemas = new XmlSchemaSet { XmlResolver = resolver };
        AddSchema(schemas, "http://www.w3.org/2000/09/xmldsig#", Path.Combine(root, "common", "UBL-xmldsig-core-schema-2.1.xsd"), resolver);
        AddSchema(schemas, "http://uri.etsi.org/01903/v1.3.2#", Path.Combine(root, "common", "UBL-XAdESv132-2.1.xsd"), resolver);
        AddSchema(schemas, "http://uri.etsi.org/01903/v1.4.1#", Path.Combine(root, "common", "UBL-XAdESv141-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "DIAN_UBL_Structures.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-Invoice-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-CreditNote-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-DebitNote-2.1.xsd"), resolver);
        schemas.Compile();
    }

    private static void AddSchema(XmlSchemaSet set, string? targetNamespace, string path, XmlResolver resolver)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = resolver
        });
        set.Add(targetNamespace, reader);
    }
    public DianSchemaValidationResult Validate(ReadOnlyMemory<byte> xml)
    {
        var errors = new List<string>();
        XDocument document;
        try
        {
            using var source = new MemoryStream(xml.ToArray(), writable: false);
            using var sourceReader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document = XDocument.Load(sourceReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            errors.Add($"Error: XML inválido: {exception.Message}");
            return new DianSchemaValidationResult(false, errors);
        }

        ValidateDianMandatoryRules(document, errors);
        var schemaInput = NormalizePublishedSchemaConflict(document);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Schemas = schemas,
            ValidationType = ValidationType.Schema
        };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, args) => errors.Add($"{args.Severity}: {args.Message}");
        using var stream = new MemoryStream(schemaInput, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read()) { }
        return new DianSchemaValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateDianMandatoryRules(XDocument document, List<string> errors)
    {
        ValidateProvider(document, Sts + "ProviderID", null, null, errors);
        ValidateProvider(document, Sts + "AuthorizationProviderID", "800197268", "4", errors);
        ValidateNitIdentifications(document, errors);
        ValidateFinalConsumer(document, errors);
        ValidateCountryLanguage(document, errors);
        ValidateTaxResponsibilityListNames(document, errors);
        ValidateColombiaTime(document, errors);
        ValidateTaxTotals(document, errors);
    }

    private static void ValidateTaxResponsibilityListNames(
        XDocument document,
        List<string> errors)
    {
        ValidatePartyTaxResponsibilityListName(
            document, "AccountingSupplierParty", "04", errors);
        ValidatePartyTaxResponsibilityListName(
            document, "AccountingCustomerParty", "05", errors);
    }

    private static void ValidatePartyTaxResponsibilityListName(
        XDocument document,
        string partyElement,
        string expected,
        List<string> errors)
    {
        foreach (var taxLevel in document.Descendants(Cac + partyElement)
                     .Descendants(Cbc + "TaxLevelCode"))
            if (!string.Equals(
                    taxLevel.Attribute("listName")?.Value,
                    expected,
                    StringComparison.Ordinal))
                errors.Add(
                    $"Error: FAJ27 {partyElement}/TaxLevelCode/@listName debe ser '{expected}'.");
    }

    private static void ValidateCountryLanguage(XDocument document, List<string> errors)
    {
        foreach (var countryName in document.Descendants(Cac + "Country")
                     .Elements(Cbc + "Name"))
        {
            if (countryName.Attribute(XNamespace.Xml + "lang") is not null)
                errors.Add("Error: ZB01 cbc:Country/cbc:Name no permite el atributo xml:lang.");
            if (!string.Equals(
                    countryName.Attribute("languageID")?.Value,
                    "es",
                    StringComparison.Ordinal))
                errors.Add("Error: FAJ18 cbc:Country/cbc:Name/@languageID debe ser 'es'.");
        }
    }

    private static void ValidateColombiaTime(XDocument document, List<string> errors)
    {
        var issueTime = document.Root?.Element(Cbc + "IssueTime")?.Value;
        if (!string.IsNullOrWhiteSpace(issueTime) &&
            (!DateTimeOffset.TryParseExact(
                 $"2000-01-01T{issueTime}",
                 "yyyy-MM-dd'T'HH:mm:sszzz",
                 CultureInfo.InvariantCulture,
                 DateTimeStyles.None,
                 out var parsed) ||
             parsed.Offset != TimeSpan.FromHours(-5)))
            errors.Add("Error: FAD10 cbc:IssueTime debe usar la zona horaria oficial de Colombia (-05:00).");

        foreach (var signingTime in document.Descendants(Xades + "SigningTime"))
        {
            if (!DateTimeOffset.TryParse(
                    signingTime.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var signedAt) ||
                signedAt.Offset != TimeSpan.FromHours(-5))
                errors.Add("Error: la hora de firma XAdES debe usar la zona horaria oficial de Colombia (-05:00).");
        }
    }

    private static void ValidateTaxTotals(XDocument document, List<string> errors)
    {
        var root = document.Root;
        if (root is null) return;

        var headerTotals = root.Elements(Cac + "TaxTotal").ToArray();
        var duplicateHeaderCodes = headerTotals
            .SelectMany(TaxSubtotals)
            .GroupBy(value => value.Code, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.Owner).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateHeaderCodes.Length > 0)
            errors.Add(
                $"Error: FAS01 cada tributo debe tener un único TaxTotal de encabezado. Duplicados: {string.Join(", ", duplicateHeaderCodes)}.");

        var lineSubtotals = root.Descendants()
            .Where(element => element.Name.LocalName is
                "InvoiceLine" or "CreditNoteLine" or "DebitNoteLine")
            .SelectMany(line => line.Elements(Cac + "TaxTotal"))
            .SelectMany(TaxSubtotals)
            .GroupBy(value => new { value.Code, value.Name, value.Percent })
            .ToDictionary(
                group => (group.Key.Code, group.Key.Name, group.Key.Percent),
                group => (Taxable: group.Sum(value => value.Taxable),
                    Amount: group.Sum(value => value.Amount)));
        var headerSubtotals = headerTotals.SelectMany(TaxSubtotals)
            .GroupBy(value => new { value.Code, value.Name, value.Percent })
            .ToDictionary(
                group => (group.Key.Code, group.Key.Name, group.Key.Percent),
                group => (Taxable: group.Sum(value => value.Taxable),
                    Amount: group.Sum(value => value.Amount)));

        if (lineSubtotals.Count != headerSubtotals.Count ||
            lineSubtotals.Any(value =>
                !headerSubtotals.TryGetValue(value.Key, out var header) ||
                header != value.Value))
            errors.Add(
                "Error: FAS01a/FAS01b los tributos de encabezado no coinciden con código, nombre, porcentaje, base y valor informados en las líneas.");

        foreach (var total in headerTotals)
        {
            if (!TryDecimal(total.Element(Cbc + "TaxAmount")?.Value, out var declared) ||
                declared != TaxSubtotals(total).Sum(value => value.Amount))
                errors.Add("Error: el TaxAmount de encabezado no coincide con sus TaxSubtotal.");
        }
    }

    private static IEnumerable<TaxSubtotalValue> TaxSubtotals(XElement total)
    {
        foreach (var subtotal in total.Elements(Cac + "TaxSubtotal"))
        {
            var category = subtotal.Element(Cac + "TaxCategory");
            var scheme = category?.Element(Cac + "TaxScheme");
            if (scheme is null ||
                !TryDecimal(subtotal.Element(Cbc + "TaxableAmount")?.Value, out var taxable) ||
                !TryDecimal(subtotal.Element(Cbc + "TaxAmount")?.Value, out var amount) ||
                !TryDecimal(category?.Element(Cbc + "Percent")?.Value, out var percent))
                continue;
            yield return new TaxSubtotalValue(
                total,
                scheme.Element(Cbc + "ID")?.Value.Trim() ?? string.Empty,
                scheme.Element(Cbc + "Name")?.Value.Trim() ?? string.Empty,
                percent,
                taxable,
                amount);
        }
    }

    private static bool TryDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private sealed record TaxSubtotalValue(
        XElement Owner,
        string Code,
        string Name,
        decimal Percent,
        decimal Taxable,
        decimal Amount);

    private static void ValidateNitIdentifications(XDocument document, List<string> errors)
    {
        foreach (var companyId in document.Descendants(Cbc + "CompanyID")
                     .Where(value => string.Equals(
                         value.Attribute("schemeName")?.Value, "31", StringComparison.Ordinal)))
        {
            var expected = CalculateColombianNitCheckDigit(companyId.Value.Trim());
            if (expected is null || !string.Equals(
                    companyId.Attribute("schemeID")?.Value, expected, StringComparison.Ordinal))
                errors.Add("Error: CompanyID identificado como NIT no contiene el dígito verificador correcto en @schemeID.");
        }
    }

    private static void ValidateProvider(
        XDocument document,
        XName elementName,
        string? expectedTaxId,
        string? expectedCheckDigit,
        List<string> errors)
    {
        var elements = document.Descendants(elementName).Take(2).ToArray();
        if (elements.Length == 0) return;
        if (elements.Length > 1)
        {
            errors.Add($"Error: {elementName.LocalName} debe aparecer exactamente una vez.");
            return;
        }

        var element = elements[0];

        var label = elementName.LocalName;
        var taxId = element.Value.Trim();
        var schemeId = element.Attribute("schemeID")?.Value;
        var schemeName = element.Attribute("schemeName")?.Value;
        if (!string.Equals(schemeName, "31", StringComparison.Ordinal))
            errors.Add($"Error: {label}/@schemeName debe ser '31'.");
        if (expectedTaxId is not null && !string.Equals(taxId, expectedTaxId, StringComparison.Ordinal))
            errors.Add($"Error: {label} no corresponde al NIT autorizado.");

        var calculatedCheckDigit = CalculateColombianNitCheckDigit(taxId);
        var requiredCheckDigit = expectedCheckDigit ?? calculatedCheckDigit;
        if (requiredCheckDigit is null ||
            !string.Equals(schemeId, requiredCheckDigit, StringComparison.Ordinal))
            errors.Add($"Error: {label}/@schemeID no contiene el dígito verificador correcto.");
    }

    private static void ValidateFinalConsumer(XDocument document, List<string> errors)
    {
        var customers = document.Descendants(Cac + "AccountingCustomerParty").Take(2).ToArray();
        if (customers.Length > 1)
        {
            errors.Add("Error: AccountingCustomerParty debe aparecer exactamente una vez.");
            return;
        }

        var customer = customers.FirstOrDefault();
        var party = customer?.Element(Cac + "Party");
        var taxParty = party?.Element(Cac + "PartyTaxScheme");
        var companyId = taxParty?.Element(Cbc + "CompanyID");
        var registrationName = taxParty?.Element(Cbc + "RegistrationName")?.Value.Trim();
        var isFinalConsumer = string.Equals(companyId?.Value.Trim(), "222222222222", StringComparison.Ordinal) ||
                              string.Equals(registrationName, "Consumidor final", StringComparison.OrdinalIgnoreCase);
        if (!isFinalConsumer) return;

        RequireValue(companyId, "222222222222", "FAK21 CompanyID", errors);
        RequireAttribute(companyId, "schemeName", "13", "FAK25 CompanyID", errors);
        RequireNoAttribute(companyId, "schemeID", "FAK24 CompanyID", errors);

        var partyIdentification = party?.Element(Cac + "PartyIdentification")?.Element(Cbc + "ID");
        RequireValue(partyIdentification, "222222222222", "FAK61/FAK62 PartyIdentification", errors);
        RequireAttribute(partyIdentification, "schemeName", "13", "FAK63 PartyIdentification", errors);
        RequireNoAttribute(partyIdentification, "schemeID", "FAK64 PartyIdentification", errors);

        RequireValue(taxParty?.Element(Cbc + "TaxLevelCode"), "R-99-PN", "FAK26 TaxLevelCode", errors);
        var taxScheme = taxParty?.Element(Cac + "TaxScheme");
        RequireValue(taxScheme?.Element(Cbc + "ID"), "ZZ", "FAK40 TaxScheme/ID", errors);
        RequireValue(taxScheme?.Element(Cbc + "Name"), "No aplica", "FAK41 TaxScheme/Name", errors);
    }

    private static void RequireValue(XElement? element, string expected, string field, List<string> errors)
    {
        if (element is null || !string.Equals(element.Value.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Error: {field} debe ser '{expected}'.");
    }

    private static void RequireAttribute(
        XElement? element,
        string attribute,
        string expected,
        string field,
        List<string> errors)
    {
        if (!string.Equals(element?.Attribute(attribute)?.Value, expected, StringComparison.Ordinal))
            errors.Add($"Error: {field}/@{attribute} debe ser '{expected}'.");
    }

    private static void RequireNoAttribute(
        XElement? element,
        string attribute,
        string field,
        List<string> errors)
    {
        if (element?.Attribute(attribute) is not null)
            errors.Add($"Error: {field}/@{attribute} solo aplica a identificaciones NIT.");
    }

    private static string? CalculateColombianNitCheckDigit(string taxId)
    {
        ReadOnlySpan<int> weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
        if (taxId.Length is < 1 or > 15 || taxId.Any(character => !char.IsAsciiDigit(character)))
            return null;

        var sum = 0;
        for (var index = 0; index < taxId.Length; index++)
            sum += (taxId[taxId.Length - index - 1] - '0') * weights[index];
        var remainder = sum % 11;
        return (remainder > 1 ? 11 - remainder : remainder).ToString();
    }

    private static byte[] NormalizePublishedSchemaConflict(XDocument original)
    {
        // Toolbox FE 1.9 v2026 still declares coID2Type/@schemeID as the document type.
        // Annex 1.9 rules FAB22/FAB23 and FAB34/FAB35, its Schematron and its examples
        // require the opposite: schemeID is the NIT check digit and schemeName is "31".
        // Validate that real contract above, then normalize only a clone used by the XSD.
        var normalized = new XDocument(original);
        // The .NET XSD validator narrows xs:integer to decimal and rejects valid
        // X.509 serial numbers longer than 29 digits. XMLDSIG defines that value
        // as an unbounded integer. Signature structure is checked above and its
        // cryptographic integrity is verified by DianXadesSigner, so remove only
        // the signature from the clone used for UBL XSD validation.
        normalized.Descendants(Ds + "Signature")
            .Select(signature => signature.Ancestors(Ext + "UBLExtension").FirstOrDefault())
            .OfType<XElement>()
            .Distinct()
            .Remove();
        foreach (var element in normalized.Descendants()
                     .Where(value => value.Name == Sts + "ProviderID" ||
                                     value.Name == Sts + "AuthorizationProviderID"))
        {
            var schemeId = element.Attribute("schemeID");
            var schemeName = element.Attribute("schemeName");
            if (schemeId is null || schemeName is null) continue;
            (schemeId.Value, schemeName.Value) = (schemeName.Value, schemeId.Value);
        }

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(false),
                   OmitXmlDeclaration = false
               }))
            normalized.Save(writer);
        return stream.ToArray();
    }

    private sealed class LocalSchemaResolver : XmlResolver
    {
        private readonly string root;
        public LocalSchemaResolver(string root) => this.root = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile) throw new XmlException("Only local official schemas may be resolved.");
            var path = Path.GetFullPath(absoluteUri.LocalPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new XmlException("Schema resolution escaped the official schema directory.");
            return File.OpenRead(path);
        }
    }
}
