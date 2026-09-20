using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

public sealed partial class DianInvoicePdfRenderer
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    private static byte[] RenderLetterV2(XDocument invoice)
    {
        var root = invoice.Root!;
        var number = Required(root.Element(Cbc + "ID"), "Invoice/ID");
        var cufe = Required(root.Element(Cbc + "UUID"), "Invoice/UUID");
        var qr = Required(invoice.Descendants(Sts + "QRCode").SingleOrDefault(), "DianExtensions/QRCode");
        var currency = Required(root.Element(Cbc + "DocumentCurrencyCode"), "DocumentCurrencyCode");
        var issueDate = Required(root.Element(Cbc + "IssueDate"), "IssueDate");
        var issueTime = Required(root.Element(Cbc + "IssueTime"), "IssueTime");
        var layout = new LetterLayout(number, cufe);

        layout.Section("EMISOR", "ADQUIRENTE", 270);
        layout.Row([PartyDetails(root, "AccountingSupplierParty"), PartyDetails(root, "AccountingCustomerParty")],
            [270, 270], 9, shaded: true);
        layout.Space(10);
        layout.Paragraph($"Generación: {issueDate}  {issueTime}    |    Moneda: {currency}");
        var authorization = invoice.Descendants(Sts + "InvoiceControl").SingleOrDefault()
            ?? throw new InvalidOperationException("The fiscal XML has no InvoiceControl.");
        var period = authorization.Element(Sts + "AuthorizationPeriod");
        var range = authorization.Element(Sts + "AuthorizedInvoices");
        layout.Paragraph($"Resolución DIAN: {Required(authorization.Element(Sts + "InvoiceAuthorization"), "InvoiceAuthorization")}  |  Prefijo: {range?.Element(Sts + "Prefix")?.Value.Trim()}");
        layout.Paragraph($"Rango autorizado: {Required(range?.Element(Sts + "From"), "AuthorizedInvoices/From")} a {Required(range?.Element(Sts + "To"), "AuthorizedInvoices/To")}  |  Vigencia: {Required(period?.Element(Cbc + "StartDate"), "AuthorizationPeriod/StartDate")} a {Required(period?.Element(Cbc + "EndDate"), "AuthorizationPeriod/EndDate")}");
        foreach (var payment in root.Elements(Cac + "PaymentMeans"))
        {
            var form = Required(payment.Element(Cbc + "ID"), "PaymentMeans/ID");
            if (form is not ("1" or "2"))
                throw new InvalidOperationException("The fiscal XML has an unsupported payment form.");
            var due = Required(payment.Element(Cbc + "PaymentDueDate"), "PaymentDueDate");
            var term = form == "2"
                ? $"  |  Plazo: {DateOnly.ParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(issueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber} días"
                : string.Empty;
            layout.Paragraph($"Pago: {PaymentForm(form)}  |  Medio: {PaymentMeans(Required(payment.Element(Cbc + "PaymentMeansCode"), "PaymentMeansCode"))}  |  Vencimiento: {due}{term}");
        }
        if (!root.Elements(Cac + "PaymentMeans").Any())
            throw new InvalidOperationException("The fiscal XML has no PaymentMeans.");

        layout.Space(10);
        layout.Section($"DETALLE DE LA VENTA  /  {Required(root.Element(Cbc + "LineCountNumeric"), "LineCountNumeric")} ítems");
        string[] headers = ["Ítem", "Descripción / código", "Cantidad", "Unidad", "V. unitario", "Descuento", "Base neta"];
        double[] widths = [27, 183, 52, 36, 82, 78, 82];
        layout.TableHeader(headers, widths);
        foreach (var line in root.Elements(Cac + "InvoiceLine"))
        {
            var item = line.Element(Cac + "Item");
            var quantity = line.Element(Cbc + "InvoicedQuantity");
            var description = Required(item?.Element(Cbc + "Description"), "InvoiceLine/Description");
            var code = Required(item?.Element(Cac + "StandardItemIdentification")?.Element(Cbc + "ID"), "InvoiceLine/ItemCode");
            var taxes = line.Elements(Cac + "TaxTotal").Elements(Cac + "TaxSubtotal").Select(TaxDescription);
            var allowances = line.Elements(Cac + "AllowanceCharge").ToArray();
            var discounts = allowances.Where(value => value.Element(Cbc + "ChargeIndicator")?.Value == "false")
                .Sum(value => Decimal(value.Element(Cbc + "Amount"), "AllowanceCharge/Amount"));
            var surcharges = allowances.Where(value => value.Element(Cbc + "ChargeIndicator")?.Value == "true")
                .Select(value => $"Cargo: {Money(value.Element(Cbc + "Amount"), "AllowanceCharge/Amount")}");
            var detail = string.Join('\n', new[] { description,
                string.Join("  |  ", new[] { $"Código: {code}" }.Concat(taxes)) }.Concat(surcharges));
            layout.Row([
                Required(line.Element(Cbc + "ID"), "InvoiceLine/ID"), detail,
                Decimal(quantity, "InvoicedQuantity").ToString("0.######", ColombianCulture),
                RequiredAttribute(quantity, "unitCode"),
                Money(line.Element(Cac + "Price")?.Element(Cbc + "PriceAmount"), "PriceAmount"),
                discounts.ToString("#,##0.00####", ColombianCulture),
                Money(line.Element(Cbc + "LineExtensionAmount"), "LineExtensionAmount")], widths, 8,
                tableHeaders: headers, rightAlignedFrom: 2);
        }
        var taxDetails = new List<string>();
        foreach (var tax in root.Elements().Where(value => value.Name == Cac + "TaxTotal" || value.Name == Cac + "WithholdingTaxTotal").Elements(Cac + "TaxSubtotal"))
        {
            taxDetails.Add((tax.Parent!.Name == Cac + "WithholdingTaxTotal" ? "Retención: " : string.Empty) + TaxDescription(tax));
            taxDetails.Add($"Base: {Money(tax.Element(Cbc + "TaxableAmount"), "TaxableAmount")}  |  Valor: {Money(tax.Element(Cbc + "TaxAmount"), "TaxAmount")}");
        }
        var totals = root.Element(Cac + "LegalMonetaryTotal")
            ?? throw new InvalidOperationException("The fiscal XML has no LegalMonetaryTotal.");
        var summary = new List<(string Label, string Value)> {
            ("Valor neto de líneas", Money(totals.Element(Cbc + "LineExtensionAmount"), "LineExtensionAmount")),
            ("Base antes de impuestos", Money(totals.Element(Cbc + "TaxExclusiveAmount"), "TaxExclusiveAmount")) };
        foreach (var field in new[] { ("AllowanceTotalAmount", "Descuentos globales"), ("ChargeTotalAmount", "Cargos globales") })
            if (totals.Element(Cbc + field.Item1) is { } value) summary.Add((field.Item2, Money(value, field.Item1)));
        summary.Add(("Total impuestos", root.Elements(Cac + "TaxTotal")
            .Sum(tax => Decimal(tax.Element(Cbc + "TaxAmount"), "TaxTotal/TaxAmount")).ToString("#,##0.00####", ColombianCulture)));
        summary.Add(("Total con impuestos", Money(totals.Element(Cbc + "TaxInclusiveAmount"), "TaxInclusiveAmount")));
        foreach (var field in new[] { ("PrepaidAmount", "Anticipos"), ("PayableRoundingAmount", "Ajuste al peso") })
            if (totals.Element(Cbc + field.Item1) is { } value) summary.Add((field.Item2, Money(value, field.Item1)));
        var supplier = TaxScheme(invoice, "AccountingSupplierParty");
        var provider = invoice.Descendants(Sts + "ProviderID").SingleOrDefault();
        var providerId = Required(provider, "SoftwareProvider/ProviderID");
        // The canonical fiscal builder registers the issuer as software provider.
        // Do not silently attribute an unrelated provider's NIT to that issuer.
        if (providerId != Required(supplier.Element(Cbc + "CompanyID"), "supplier ID"))
            throw new InvalidOperationException("The fiscal XML does not identify the software provider's legal name.");
        layout.ClosingBlock(
            string.Join('\n', taxDetails), summary,
            currency, $"{Money(totals.Element(Cbc + "PayableAmount"), "PayableAmount")} {currency}",
            root.Elements(Cbc + "Note").Select(note => $"Observación: {note.Value.Trim()}").ToArray(),
            [$"Software: Auraly  |  Fabricante/proveedor: {Required(supplier.Element(Cbc + "RegistrationName"), "supplier name")}  |  NIT: {Identification(provider!)}",
                $"Identificador del software: {Required(invoice.Descendants(Sts + "SoftwareID").SingleOrDefault(), "SoftwareProvider/SoftwareID")}"]);
        return layout.Build(qr);
    }

    private static string PartyDetails(XElement root, string partyName)
    {
        var party = root.Element(Cac + partyName)?.Element(Cac + "Party")
            ?? throw new InvalidOperationException($"The fiscal XML has no {partyName}.");
        var tax = party.Element(Cac + "PartyTaxScheme")
            ?? throw new InvalidOperationException($"The fiscal XML has no {partyName}/PartyTaxScheme.");
        var address = tax.Element(Cac + "RegistrationAddress");
        var id = tax.Element(Cbc + "CompanyID")
            ?? throw new InvalidOperationException($"The fiscal XML has no {partyName}/CompanyID.");
        var values = new List<string> { Required(tax.Element(Cbc + "RegistrationName"), "RegistrationName"),
            $"{(id.Attribute("schemeName")?.Value == "31" ? "NIT" : "Identificación")}: {Identification(id)}",
            $"Responsabilidad fiscal: {Required(tax.Element(Cbc + "TaxLevelCode"), "TaxLevelCode")}",
            Required(address?.Element(Cac + "AddressLine")?.Element(Cbc + "Line"), "RegistrationAddress/AddressLine"),
            string.Join(", ", new[] { address?.Element(Cbc + "CityName")?.Value,
                address?.Element(Cbc + "CountrySubentity")?.Value, address?.Element(Cac + "Country")?.Element(Cbc + "Name")?.Value }
                .Where(value => !string.IsNullOrWhiteSpace(value))) };
        var contact = party.Element(Cac + "Contact");
        if (contact?.Element(Cbc + "Telephone") is { } phone) values.Add($"Teléfono: {phone.Value.Trim()}");
        if (contact?.Element(Cbc + "ElectronicMail") is { } email) values.Add(email.Value.Trim());
        return string.Join('\n', values);
    }

    private static string Identification(XElement id) => Required(id, "CompanyID") +
        (id.Attribute("schemeName")?.Value == "31" && id.Attribute("schemeID") is { } digit ? $"-{digit.Value}" : string.Empty);
    private static string RequiredAttribute(XElement? value, string name) =>
        !string.IsNullOrWhiteSpace(value?.Attribute(name)?.Value) ? value.Attribute(name)!.Value
            : throw new InvalidOperationException($"The fiscal XML has no {name}.");
    private static decimal Decimal(XElement? value, string field) =>
        decimal.TryParse(Required(value, field), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) ? amount
            : throw new InvalidOperationException($"The fiscal XML has an invalid {field}.");
    private static string Money(XElement? value, string field) => Decimal(value, field).ToString("#,##0.00####", ColombianCulture);
    private static string TaxDescription(XElement tax)
    {
        var category = tax.Element(Cac + "TaxCategory");
        var name = Required(category?.Element(Cac + "TaxScheme")?.Element(Cbc + "Name"), "TaxScheme/Name");
        return category?.Element(Cbc + "Percent") is { } percent
            ? $"{name} {Decimal(percent, "Percent").ToString("0.######", ColombianCulture)}%"
            : name;
    }

    // A bounded, in-memory letter layout. No master-data, fonts or network reads.
    // The QR is one reusable vector object, not regenerated for every page.
    private sealed class LetterLayout(string number, string cufe)
    {
        private const double Left = 36, Width = 540, Bottom = 164;
        private readonly List<StringBuilder> pages = [];
        private StringBuilder page = new();
        private double y;
        private static readonly int[] HelveticaWidths = [278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584];

        private void NewPage()
        {
            page = new StringBuilder();
            pages.Add(page);
            Text("FACTURA ELECTRÓNICA DE VENTA", Left, 752, 17, bold: true, teal: true);
            Text("Representación gráfica del documento electrónico", Left, 734, 9);
            Rect(Left, 692, Width, 28, "0.06 0.35 0.33");
            Text(number, Left + 10, 701, 13, bold: true, white: true);
            Text("Documento validado por la DIAN", 360, 702, 9, white: true);
            y = 677;
        }

        private void Ensure(double height)
        {
            if (pages.Count == 0 || y - height < Bottom) NewPage();
        }

        public void Space(double height) { Ensure(height); y -= height; }
        public void Section(string title, string? second = null, double offset = 0)
        {
            Ensure(54); // Keep section labels with at least a header and a row.
            Text(title, Left, y - 10, 9, bold: true, teal: true);
            if (second is not null) Text(second, Left + offset, y - 10, 9, bold: true, teal: true);
            y -= 20;
        }

        public void Paragraph(string text, double size = 8.5) => Row([text], [Width], size, verticalPadding: 2);
        public void TableHeader(string[] headers, double[] widths)
        {
            Ensure(122); // Header plus an ordinary wrapped item stay together.
            Rect(Left, y - 22, Width, 22, "0.91 0.95 0.94");
            var x = Left;
            for (var i = 0; i < headers.Length; i++)
            {
                Text(headers[i], x + 5, y - 14, 7.5, bold: true);
                x += widths[i];
            }
            y -= 22;
        }

        public void Row(string[] cells, double[] widths, double size, bool shaded = false,
            string[]? tableHeaders = null, int rightAlignedFrom = int.MaxValue, double verticalPadding = 8)
        {
            var lines = cells.Select((cell, i) => WrapMeasured(cell, widths[i] - 12, size).ToArray()).ToArray();
            var count = lines.Max(value => value.Length);
            var offset = 0;
            var leading = size + 3;
            var fullHeight = count * leading + verticalPadding;
            if (tableHeaders is not null && fullHeight <= 100 && y - fullHeight < Bottom)
            {
                NewPage();
                TableHeader(tableHeaders, widths);
            }
            while (offset < count)
            {
                if (pages.Count == 0 || y - leading - verticalPadding < Bottom)
                {
                    NewPage();
                    if (tableHeaders is not null) TableHeader(tableHeaders, widths);
                }
                var take = Math.Min(count - offset, (int)((y - Bottom - verticalPadding) / leading));
                var height = take * leading + verticalPadding;
                if (shaded) Rect(Left, y - height, Width, height, "0.97 0.98 0.98");
                var x = Left;
                for (var column = 0; column < cells.Length; column++)
                {
                    for (var row = 0; row < take && offset + row < lines[column].Length; row++)
                    {
                        var line = lines[column][offset + row];
                        var textX = column >= rightAlignedFrom ? x + widths[column] - 6 - Measure(line, size) : x + 6;
                        Text(line, textX, y - size - verticalPadding / 2 - row * leading, size);
                    }
                    x += widths[column];
                }
                y -= height;
                if (tableHeaders is not null) Rect(Left, y, Width, 0.4, "0.82 0.87 0.87");
                offset += take;
            }
        }

        public void ClosingBlock(string taxes, IReadOnlyList<(string Label, string Value)> summary,
            string currency, string total, string[] notes, string[] software)
        {
            double[] widths = [270, 165, 105];
            var labels = new List<string>();
            var values = new List<string>();
            foreach (var entry in summary)
            {
                var labelLines = WrapMeasured(entry.Label, widths[1] - 12, 8.5).ToArray();
                var valueLines = WrapMeasured(entry.Value, widths[2] - 12, 8.5).ToArray();
                var lines = Math.Max(labelLines.Length, valueLines.Length);
                labels.AddRange(labelLines.Concat(Enumerable.Repeat(string.Empty, lines - labelLines.Length)));
                values.AddRange(valueLines.Concat(Enumerable.Repeat(string.Empty, lines - valueLines.Length)));
            }
            string[] cells = [taxes, string.Join('\n', labels), string.Join('\n', values)];
            var height = 12 + 20 + cells.Select((cell, i) => WrapMeasured(cell, widths[i] - 12, 8.5).Count()).Max() * 11.5 + 8 + 36 + 10
                + notes.Sum(note => WrapMeasured(note, Width - 12, 8.5).Count() * 11.5 + 2)
                + software.Sum(line => WrapMeasured(line, Width - 12, 7.5).Count() * 10.5 + 2);
            // Keep totals and their legal notes together when they fit on a page.
            // Unusually long notes still flow normally instead of being truncated.
            if (height <= 677 - Bottom) Ensure(height);
            Space(12);
            Section("IMPUESTOS DISCRIMINADOS", $"RESUMEN / {currency}", 270);
            Row(cells, widths, 8.5, shaded: true, rightAlignedFrom: 2);
            Total(total);
            foreach (var note in notes) Paragraph(note);
            Space(10);
            foreach (var line in software) Paragraph(line, 7.5);
        }

        private void Total(string value)
        {
            Ensure(36);
            Rect(Left, y - 32, Width, 32, "0.06 0.35 0.33");
            Text("TOTAL A PAGAR", Left + 10, y - 21, 12, bold: true, white: true);
            Text(value, Left + Width - 12 - Measure(value, 13), y - 21, 13, white: true);
            y -= 36;
        }

        public byte[] Build(string qr)
        {
            for (var i = 0; i < pages.Count; i++)
            {
                page = pages[i];
                Rect(Left, 153, Width, 0.8, "0.06 0.35 0.33");
                Text("CUFE / CÓDIGO ÚNICO DE FACTURA ELECTRÓNICA", Left, 137, 8, bold: true, teal: true);
                var cursor = 122d;
                foreach (var line in Wrap(cufe, 48))
                {
                    Text(line, Left, cursor, 8);
                    cursor -= 11;
                }
                Text("Consulte y verifique esta factura en la DIAN", Left, 77, 8.5);
                Text("escaneando el código QR de esta página.", Left, 64, 8.5);
                page.Append("q 1 0 0 1 478 43 cm /QR Do Q\n");
                Text($"Página {i + 1} de {pages.Count}", Left, 27, 8);
                Text(number, 478, 27, 8);
            }
            return WriteLetterPdf(pages, qr);
        }

        private void Rect(double x, double bottom, double width, double height, string color) =>
            page.Append(FormattableString.Invariant($"{color} rg {x:0.###} {bottom:0.###} {width:0.###} {height:0.###} re f\n"));
        private void Text(string text, double x, double baseline, double size, bool bold = false, bool white = false, bool teal = false)
        {
            page.Append(white ? "1 g\n" : teal ? "0.06 0.35 0.33 rg\n" : "0.12 0.17 0.20 rg\n");
            page.Append(FormattableString.Invariant($"BT /{(bold ? "F2" : "F1")} {size:0.###} Tf {x:0.###} {baseline:0.###} Td ({WinAnsiLiteral(text)}) Tj ET\n"));
        }

        private static IEnumerable<string> WrapMeasured(string text, double width, double size)
        {
            foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
            {
                if (paragraph.Length == 0) { yield return string.Empty; continue; }
                var start = 0;
                while (start < paragraph.Length)
                {
                    var end = start;
                    var measured = 0d;
                    var space = -1;
                    while (end < paragraph.Length && measured + GlyphWidth(paragraph[end]) * size / 1000 <= width)
                    {
                        measured += GlyphWidth(paragraph[end]) * size / 1000;
                        if (paragraph[end] == ' ') space = end;
                        end++;
                    }
                    if (end == start) throw new InvalidOperationException("The fiscal PDF text column is too narrow.");
                    if (end < paragraph.Length && space > start) end = space;
                    yield return paragraph[start..end].TrimEnd();
                    start = end;
                    while (start < paragraph.Length && paragraph[start] == ' ') start++;
                }
            }
        }

        private static double Measure(string text, double size) => text.Sum(GlyphWidth) * size / 1000;
        private static int GlyphWidth(char value)
        {
            if (value is >= ' ' and <= '~') return HelveticaWidths[value - ' '];
            var normalized = value.ToString().Normalize(NormalizationForm.FormD)[0];
            return normalized is >= ' ' and <= '~' ? HelveticaWidths[normalized - ' '] : 1000;
        }
        private static string WinAnsiLiteral(string value)
        {
            var result = new StringBuilder();
            foreach (var character in value)
            {
                var code = character switch { '€' => 128, '‘' => 145, '’' => 146, '“' => 147, '”' => 148, '–' => 150, '—' => 151,
                    >= ' ' and <= '~' or >= '\u00a0' and <= '\u00ff' => (int)character,
                    _ => throw new InvalidOperationException("The fiscal PDF contains a character unsupported by its WinAnsi font.") };
                if (code is 40 or 41 or 92) result.Append('\\').Append(character);
                else if (code > 126) result.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
                else result.Append(character);
            }
            return result.ToString();
        }
    }

    private static byte[] WriteLetterPdf(IReadOnlyList<StringBuilder> pages, string qr)
    {
        var pageIds = Enumerable.Range(0, pages.Count).Select(index => 7 + index * 2).ToArray();
        var qrCommands = new StringBuilder();
        DrawQr(qrCommands, qr, 0, 0, 98);
        var objects = new List<byte[]> {
            Ascii("<< /Type /Catalog /Pages 2 0 R /Lang (es-CO) >>"),
            Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
            PdfStream(Ascii(qrCommands.ToString()), "/Type /XObject /Subtype /Form /BBox [0 0 98 98] /Resources << >>"),
            Ascii($"<< /Title (Factura electronica de venta) /Creator (Auraly) /Subject ({TemplateCode}/2) >>") };
        for (var i = 0; i < pages.Count; i++)
        {
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /QR 5 0 R >> >> /Contents {pageIds[i] + 1} 0 R >>"));
            objects.Add(PdfStream(Ascii(pages[i].ToString())));
        }
        using var output = new MemoryStream();
        output.Write(Ascii("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            output.Write(Ascii($"{i + 1} 0 obj\n"));
            output.Write(objects[i]);
            output.Write(Ascii("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) output.Write(Ascii(FormattableString.Invariant($"{offset:0000000000} 00000 n \n")));
        output.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF"));
        return output.ToArray();
    }

    private static byte[] PdfStream(byte[] content, string attributes = "") =>
        Concat(Ascii($"<< {attributes} /Length {content.Length} >>\nstream\n"), content, Ascii("\nendstream"));
}
