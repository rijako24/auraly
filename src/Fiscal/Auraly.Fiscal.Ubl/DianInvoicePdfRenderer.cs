using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using QRCoder;

namespace Auraly.Fiscal.Ubl;

public sealed partial class DianInvoicePdfRenderer
{
    // Fiscal delivery templates are independent of POS paper/printer profiles.
    // Persisted FiscalArtifacts remain the authority for historical deliveries.
    public const string TemplateCode = "dian-invoice-letter";
    public const int CurrentTemplateVersion = 2;
    private const int LinesPerPage = 43;
    private static readonly XNamespace Cac = DianUblNamespaces.Cac;
    private static readonly XNamespace Cbc = DianUblNamespaces.Cbc;
    private static readonly XNamespace Sts = DianUblNamespaces.Sts;

    public byte[] Render(ReadOnlyMemory<byte> signedInvoice, int templateVersion = CurrentTemplateVersion)
    {
        var invoice = Load(signedInvoice);
        if (invoice.Root?.Name != DianUblNamespaces.Invoice + "Invoice")
            throw new InvalidOperationException("The PDF source is not a UBL Invoice document.");

        if (templateVersion == 2) return RenderLetterV2(invoice);
        if (templateVersion != 1)
            throw new ArgumentOutOfRangeException(nameof(templateVersion), "The fiscal PDF template version is not available.");

        var lines = BuildLines(invoice);
        var qrPayload = Required(invoice.Descendants(Sts + "QRCode").SingleOrDefault(),
            "DianExtensions/QRCode");
        var pages = lines.Chunk(LinesPerPage).Select(chunk => chunk.ToArray()).ToArray();
        return BuildPdf(pages, qrPayload);
    }

    private static IReadOnlyList<string> BuildLines(XDocument invoice)
    {
        var root = invoice.Root
            ?? throw new InvalidOperationException("The fiscal XML has no document element.");
        var supplier = TaxScheme(invoice, "AccountingSupplierParty");
        var customer = TaxScheme(invoice, "AccountingCustomerParty");
        var authorization = Required(invoice.Descendants(Sts + "InvoiceAuthorization").SingleOrDefault(), "InvoiceAuthorization");
        var prefix = Required(invoice.Descendants(Sts + "Prefix").SingleOrDefault(), "AuthorizedInvoices/Prefix");
        var rangeStart = Required(invoice.Descendants(Sts + "From").SingleOrDefault(), "AuthorizedInvoices/From");
        var rangeEnd = Required(invoice.Descendants(Sts + "To").SingleOrDefault(), "AuthorizedInvoices/To");
        var validFrom = Required(invoice.Descendants(Sts + "AuthorizationPeriod").Elements(Cbc + "StartDate").SingleOrDefault(), "AuthorizationPeriod/StartDate");
        var validUntil = Required(invoice.Descendants(Sts + "AuthorizationPeriod").Elements(Cbc + "EndDate").SingleOrDefault(), "AuthorizationPeriod/EndDate");
        var payment = invoice.Descendants(Cac + "PaymentMeans").SingleOrDefault()
            ?? throw new InvalidOperationException("The fiscal XML has no PaymentMeans.");
        var providerId = Required(invoice.Descendants(Sts + "ProviderID").SingleOrDefault(), "SoftwareProvider/ProviderID");
        var currency = Required(root.Element(Cbc + "DocumentCurrencyCode"), "DocumentCurrencyCode");
        var result = new List<string>
        {
            "FACTURA ELECTRONICA DE VENTA - REPRESENTACION GRAFICA",
            "Documento validado por la DIAN",
            $"Numero: {Required(root.Element(Cbc + "ID"), "Invoice/ID")}",
            $"Fecha y hora de generacion: {Required(root.Element(Cbc + "IssueDate"), "IssueDate")} {Required(root.Element(Cbc + "IssueTime"), "IssueTime")}",
            $"Vendedor: {Required(supplier.Element(Cbc + "RegistrationName"), "supplier name")}",
            $"NIT vendedor: {Required(supplier.Element(Cbc + "CompanyID"), "supplier ID")}  Responsabilidad fiscal: {Required(supplier.Element(Cbc + "TaxLevelCode"), "supplier responsibility")}",
            $"Direccion vendedor: {Address(invoice, "AccountingSupplierParty")}",
            $"Cliente: {Required(customer.Element(Cbc + "RegistrationName"), "customer name")}",
            $"Identificacion cliente: {Required(customer.Element(Cbc + "CompanyID"), "customer ID")}",
            $"Direccion cliente: {Address(invoice, "AccountingCustomerParty")}",
            $"Resolucion DIAN: {authorization}  Prefijo: {prefix}",
            $"Rango autorizado: {rangeStart} a {rangeEnd}  Vigencia: {validFrom} a {validUntil}",
            $"Forma de pago: {PaymentForm(Required(payment.Element(Cbc + "ID"), "PaymentMeans/ID"))}  Medio: {PaymentMeans(Required(payment.Element(Cbc + "PaymentMeansCode"), "PaymentMeansCode"))}",
            $"Vencimiento: {Required(payment.Element(Cbc + "PaymentDueDate"), "PaymentDueDate")}  Moneda: {currency}",
            $"Lineas: {Required(root.Element(Cbc + "LineCountNumeric"), "LineCountNumeric")}",
            "No. Codigo / descripcion | Cantidad Unidad | Valor unitario | Valor linea"
        };

        foreach (var line in root.Elements(Cac + "InvoiceLine"))
        {
            var quantity = line.Element(Cbc + "InvoicedQuantity");
            var code = line.Descendants(Cac + "StandardItemIdentification").Elements(Cbc + "ID").FirstOrDefault()?.Value.Trim() ?? string.Empty;
            var description = Required(line.Descendants(Cac + "Item").Elements(Cbc + "Description").SingleOrDefault(), "InvoiceLine/Description");
            var price = Required(line.Descendants(Cac + "Price").Elements(Cbc + "PriceAmount").SingleOrDefault(), "InvoiceLine/PriceAmount");
            result.Add($"{Required(line.Element(Cbc + "ID"), "InvoiceLine/ID")}. {code} / {description} | {Required(quantity, "InvoicedQuantity")} {quantity!.Attribute("unitCode")?.Value ?? "EA"} | {price} | {Required(line.Element(Cbc + "LineExtensionAmount"), "LineExtensionAmount")}");
        }

        result.Add("IMPUESTOS DISCRIMINADOS");
        foreach (var tax in root.Elements(Cac + "TaxTotal").Elements(Cac + "TaxSubtotal"))
        {
            var category = tax.Element(Cac + "TaxCategory")
                ?? throw new InvalidOperationException("The fiscal XML has no TaxSubtotal/TaxCategory.");
            var scheme = category.Element(Cac + "TaxScheme")
                ?? throw new InvalidOperationException("The fiscal XML has no TaxSubtotal/TaxScheme.");
            result.Add($"{Required(scheme.Element(Cbc + "Name"), "TaxScheme/Name")} {Required(category.Element(Cbc + "Percent"), "TaxSubtotal/Percent")}% - Base {Required(tax.Element(Cbc + "TaxableAmount"), "TaxableAmount")} - Impuesto {Required(tax.Element(Cbc + "TaxAmount"), "TaxAmount")}");
        }

        var totals = root.Element(Cac + "LegalMonetaryTotal")
            ?? throw new InvalidOperationException("The fiscal XML has no LegalMonetaryTotal.");
        result.Add($"Subtotal: {Required(totals.Element(Cbc + "TaxExclusiveAmount"), "TaxExclusiveAmount")} {currency}");
        result.Add($"Total impuestos: {Required(root.Elements(Cac + "TaxTotal").First().Element(Cbc + "TaxAmount"), "TaxTotal/TaxAmount")} {currency}");
        result.Add($"TOTAL: {Required(totals.Element(Cbc + "PayableAmount"), "PayableAmount")} {currency}");
        result.Add($"CUFE: {Required(root.Element(Cbc + "UUID"), "Invoice/UUID")}");
        result.Add($"Software: Auraly  Fabricante/proveedor: {Required(supplier.Element(Cbc + "RegistrationName"), "supplier name")}  NIT: {providerId}");
        result.Add("Consulte el documento en la DIAN mediante el codigo QR.");
        return result.SelectMany(value => Wrap(value, 105)).ToArray();
    }

    private static byte[] BuildPdf(IReadOnlyList<string[]> pages, string qrPayload)
    {
        var objects = new List<byte[]>();
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageObjectIds = Enumerable.Range(0, pages.Count).Select(index => 4 + index * 2).ToArray();
        objects.Add(Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pageObjectIds.Select(id => $"{id} 0 R"))}] >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = pageObjectIds[index];
            var contentId = pageId + 1;
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));
            var content = PageContent(pages[index], index + 1, pages.Count, qrPayload);
            objects.Add(Concat(Ascii($"<< /Length {content.Length} >>\nstream\n"), content, Ascii("\nendstream")));
        }

        using var output = new MemoryStream();
        output.Write(Ascii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            output.Write(Ascii($"{index + 1} 0 obj\n"));
            output.Write(objects[index]);
            output.Write(Ascii("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1))
            output.Write(Ascii($"{offset:0000000000} 00000 n \n"));
        output.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
        return output.ToArray();
    }

    private static byte[] PageContent(IReadOnlyList<string> lines, int page, int pageCount, string qrPayload)
    {
        var content = new StringBuilder("0.06 0.47 0.43 rg\n48 748 516 2 re f\n0 g\n");
        var y = 765;
        foreach (var line in lines)
        {
            var size = y == 765 ? 13 : 8;
            content.Append("BT /F1 ").Append(size).Append(" Tf 48 ").Append(y)
                .Append(" Td (").Append(PdfText(line)).Append(") Tj ET\n");
            y -= y == 765 ? 18 : 12;
        }
        content.Append("BT /F1 8 Tf 48 28 Td (Pagina ").Append(page).Append(" de ").Append(pageCount).Append(") Tj ET\n");
        DrawQr(content, qrPayload, 438, 40, 118);
        return Ascii(content.ToString());
    }

    private static void DrawQr(StringBuilder content, string payload, double x, double y, double size)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var modules = data.ModuleMatrix.Count;
        var unit = size / modules;
        content.Append("0 g\n");
        for (var row = 0; row < modules; row++)
            for (var column = 0; column < modules; column++)
                if (data.ModuleMatrix[row][column])
                    content.Append((x + column * unit).ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                        .Append((y + (modules - row - 1) * unit).ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(unit.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(unit.ToString("0.###", CultureInfo.InvariantCulture)).Append(" re f\n");
    }

    private static XDocument Load(ReadOnlyMemory<byte> xml)
    {
        try
        {
            using var stream = new MemoryStream(xml.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("The signed invoice is invalid XML.", exception);
        }
    }

    private static XElement TaxScheme(XDocument invoice, string name) =>
        invoice.Descendants(Cac + name).Elements(Cac + "Party").Elements(Cac + "PartyTaxScheme").SingleOrDefault()
        ?? throw new InvalidOperationException($"The fiscal XML has no {name}/PartyTaxScheme.");

    private static string Address(XDocument invoice, string partyName) =>
        invoice.Descendants(Cac + partyName).Elements(Cac + "Party")
            .Descendants(Cac + "RegistrationAddress").Descendants(Cac + "AddressLine")
            .Elements(Cbc + "Line").SingleOrDefault()?.Value.Trim() ?? string.Empty;

    private static string Required(XElement? element, string field) =>
        !string.IsNullOrWhiteSpace(element?.Value) ? element.Value.Trim() : throw new InvalidOperationException($"The fiscal XML has no {field}.");

    private static string PaymentForm(string code) => code == "2" ? "Credito" : "Contado";
    private static string PaymentMeans(string code) => code switch { "10" => "Efectivo", "42" => "Transferencia", "48" => "Tarjeta credito", "49" => "Tarjeta debito", _ => code };
    private static IEnumerable<string> Wrap(string value, int width)
    {
        for (var offset = 0; offset < value.Length; offset += width)
            yield return value.Substring(offset, Math.Min(width, value.Length - offset));
        if (value.Length == 0) yield return string.Empty;
    }

    private static string PdfText(string value) => Normalize(value).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());
    }
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static byte[] Concat(params byte[][] values)
    {
        var result = new byte[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values) { Buffer.BlockCopy(value, 0, result, offset, value.Length); offset += value.Length; }
        return result;
    }
}
