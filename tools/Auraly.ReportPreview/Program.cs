using Auraly.Pos.Printing;
using System.Diagnostics;
using System.Net;
using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Fiscal.Ubl;
using Auraly.Pos.Edge.Infrastructure;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var output = ResolveOutput(args, root);
Directory.CreateDirectory(output);

var details = new SalesInvoicePrintDetails(
    "Comercializadora Uno SAS", "900123456", "R-99-PN",
    "Calle 10 # 20-30, Valledupar", "Carrera 4 # 5-06, Valledupar",
    "18760000001", new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31),
    "FE", 1, 10000, "1", "10", new DateOnly(2026, 9, 18),
    "900123456", "Auraly");
var online = BuildOnlineReceipt(details);
var thermalRenderer = new HtmlReceiptPreviewRenderer();
var sheetRenderer = new HalfLetterDocumentRenderer();
var pages = new List<(string FileName, string Label)>();

foreach (var width in new[] { 58, 80 })
foreach (var version in new[] { 2, 3 })
{
    var fileName = $"tirilla-{width}mm-v{version}.html";
    await File.WriteAllTextAsync(
        Path.Combine(output, fileName),
        thermalRenderer.Render(BuildThermalReceipt(width, details), version, autoPrint: false),
        Encoding.UTF8);
    pages.Add((fileName, $"Tirilla {width} mm · v{version}"));
}

var formats = new[]
{
    (HalfLetterDocumentRenderer.HalfLetter, "media-carta", "Media carta"),
    (HalfLetterDocumentRenderer.HalfLegal, "media-oficio", "Media oficio"),
    (HalfLetterDocumentRenderer.Letter, "carta", "Carta")
};
foreach (var (format, slug, label) in formats)
foreach (var version in new[] { 2, 3 })
{
    var fileName = $"{slug}-v{version}.html";
    await File.WriteAllTextAsync(
        Path.Combine(output, fileName),
        sheetRenderer.Render([online], format, version, autoPrint: false),
        Encoding.UTF8);
    pages.Add((fileName, $"{label} · v{version}"));
}

var order = online with
{
    DocumentType = "Order", DocumentNumber = "PED-0000042", FiscalNumber = null,
    Lines = [new("CAFE-01", "Café molido premium", 2m, 11900m, 0m, 0m, 23800m)],
    UntaxedAmount = 23800m, TaxAmount = 0m,
    CustomerPhone = "300 123 4567",
    CustomerAddress = "Calle 10 # 20-30, edificio Los Almendros, apartamento 402.\nEntregar en portería, entrada por la carrera 5.",
    Payments = [], Cufe = null, QrPayload = null, InvoicePrintDetails = null
};
foreach (var width in new[] { 58, 80 })
{
    var file = $"pedido-{width}mm.html";
    await File.WriteAllTextAsync(Path.Combine(output, file),
        new SalesReceiptHtmlRenderer().Render(order, width, autoPrint: false), Encoding.UTF8);
    pages.Add((file, $"Pedido · {width} mm"));
    var now = new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.FromHours(-5));
    var closure = new WorkSessionClosureView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Sede principal", null, null, Guid.NewGuid(), "María González", null, now.AddHours(-8), now,
        85000m, 0m, 3000m, 88000m, 88000m, 88000m, 0m, "Cierre de prueba",
        [new WorkSessionPaymentTotal("Cash", 85000m, 0m, 3000m, 88000m, 88000m, 0m, true, 5000m, 2000m)],
        3, 1, 25000m, 0, [new WorkSessionCreditSale("Cliente con nombre extenso", "FV-1234", 25000m)], 4,
        [new WorkSessionCashMovementDetail(Guid.NewGuid(), "In", "ING-1", "Base adicional", 5000m,
             now.AddHours(-2), "María", Notes: "Cambio para el turno.\nBilletes de baja denominación."),
         new WorkSessionCashMovementDetail(Guid.NewGuid(), "Out", "EGR-1", "Compra de suministros", 2000m,
             now.AddHours(-1), "María", Notes: "Papel para la impresora de caja y elementos de oficina.")],
        InvoiceCharges: [
            new(Guid.NewGuid(), "FV-1233", Guid.NewGuid(), Guid.NewGuid(), "DOM", "Domicilio",
                "Domiciliario de prueba", 5000m, 5000m, 0m, 0m, [new(1, "Cash", 5000m)]),
            new(Guid.NewGuid(), "FV-1234", Guid.NewGuid(), Guid.NewGuid(), "AGOT", "Agotados",
                "Domiciliario de prueba", 6500m, 0m, 6500m, 0m, [])]);
    file = $"cierre-{width}mm-v4.html";
    await File.WriteAllTextAsync(Path.Combine(output, file),
        WorkSessionClosureReceiptRenderer.RenderHtml(closure, paperWidthMillimeters: width), Encoding.UTF8);
    pages.Add((file, $"Cierre v4 · {width} mm"));
}
foreach (var (format, slug, label) in formats)
{
    var file = $"pedido-{slug}.html";
    await File.WriteAllTextAsync(Path.Combine(output, file),
        sheetRenderer.Render([order], format, autoPrint: false), Encoding.UTF8);
    pages.Add((file, $"Pedido · {label}"));
}

const string pdfName = "representacion-grafica-fiscal-v1.pdf";
await File.WriteAllBytesAsync(
    Path.Combine(output, pdfName),
    new DianInvoicePdfRenderer().Render(BuildUblInvoice()));
pages.Add((pdfName, "PDF del correo · v1"));

var buttons = string.Join(Environment.NewLine, pages.Select((page, index) =>
    $"<button{(index == 0 ? " class=\"active\"" : "")} data-file=\"{WebUtility.HtmlEncode(page.FileName)}\">{WebUtility.HtmlEncode(page.Label)}</button>"));
var indexHtml = $$"""
    <!doctype html>
    <html lang="es">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width,initial-scale=1">
      <title>Visor real de reportes Auraly</title>
      <style>
        * { box-sizing: border-box; }
        html, body { height: 100%; margin: 0; font: 14px/1.4 system-ui, sans-serif; background: #111827; color: #f9fafb; }
        body { display: grid; grid-template-rows: auto 1fr; }
        header { padding: 12px 16px; border-bottom: 1px solid #374151; }
        h1 { margin: 0 0 4px; font-size: 17px; }
        p { margin: 0; color: #9ca3af; }
        main { min-height: 0; display: grid; grid-template-columns: 230px 1fr; }
        nav { overflow: auto; padding: 12px; border-right: 1px solid #374151; }
        button { width: 100%; margin: 0 0 7px; padding: 9px 10px; border: 1px solid #4b5563; border-radius: 6px; background: #1f2937; color: inherit; text-align: left; cursor: pointer; }
        button:hover, button.active { border-color: #2dd4bf; background: #134e4a; }
        iframe { width: 100%; height: 100%; border: 0; background: white; }
      </style>
    </head>
    <body>
      <header><h1>Visor de desarrollo · reportes reales</h1><p>El contenido del panel es la salida directa del mismo renderizador usado para imprimir o adjuntar.</p></header>
      <main><nav>{{buttons}}</nav><iframe title="Reporte real" src="{{WebUtility.HtmlEncode(pages[0].FileName)}}"></iframe></main>
      <script>
        const frame = document.querySelector('iframe');
        document.querySelectorAll('button').forEach(button => button.addEventListener('click', () => {
          document.querySelector('button.active')?.classList.remove('active');
          button.classList.add('active');
          frame.src = button.dataset.file;
        }));
      </script>
    </body>
    </html>
    """;
var indexPath = Path.Combine(output, "index.html");
await File.WriteAllTextAsync(indexPath, indexHtml, Encoding.UTF8);
Console.WriteLine(indexPath);

if (args.Contains("--open", StringComparer.OrdinalIgnoreCase))
    Process.Start(new ProcessStartInfo { FileName = indexPath, UseShellExecute = true });

static string FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            return directory.FullName;
    throw new InvalidOperationException("No se encontró la raíz del repositorio.");
}

static string ResolveOutput(string[] arguments, string root)
{
    var index = Array.FindIndex(arguments, value => value.Equals("--output", StringComparison.OrdinalIgnoreCase));
    if (index < 0) return Path.Combine(root, "artifacts", "report-preview");
    if (index == arguments.Length - 1) throw new ArgumentException("--output requiere una ruta.");
    return Path.GetFullPath(arguments[index + 1], root);
}

static PosReceipt BuildThermalReceipt(int width, SalesInvoicePrintDetails details) =>
    new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        new DocumentId(Guid.Parse("20000000-0000-0000-0000-000000000002")),
        "VTA01-00000042", "FE42",
        new DateTimeOffset(2026, 9, 18, 10, 30, 0, TimeSpan.FromHours(-5)),
        "222222222",
        [new PosReceiptLine("P-001", "Café molido premium", 2m, 10_000m, 0m, 3_800m, 23_800m, "01", 19m, "EA")],
        [new OfflineSalePayment("Cash", 23_800m, TenderedAmount: 25_000m)],
        20_000m, 3_800m, 23_800m, new string('a', 96),
        "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96),
        width, PosSaleDocumentTypes.Invoice,
        CompanyName: "Comercializadora Uno SAS", CustomerName: "Cliente de prueba SAS",
        InvoicePrintDetails: details);

static OnlineSalesReceipt BuildOnlineReceipt(SalesInvoicePrintDetails details) =>
    new(
        Guid.Parse("20000000-0000-0000-0000-000000000002"), PosSaleDocumentTypes.Invoice,
        "VTA01-00000042", "FE42",
        new DateTimeOffset(2026, 9, 18, 10, 30, 0, TimeSpan.FromHours(-5)),
        "222222222",
        [new OnlineSalesReceiptLine("P-001", "Café molido premium", 2m, 10_000m, 0m, 3_800m, 23_800m, "01", 19m, "EA")],
        [new OnlineSalesPayment("Cash", 23_800m, null, TenderedAmount: 25_000m)],
        20_000m, 3_800m, 23_800m, new string('a', 96),
        "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96),
        "Accepted", "Cliente de prueba SAS", "Comercializadora Uno SAS",
        InvoicePrintDetails: details);

static byte[] BuildUblInvoice()
{
    var address = new DianAddress("20001", "Valledupar", "Cesar", "20", "Calle 10 # 20-30");
    var invoice = new DianInvoice(
        "FE42", new string('a', 96),
        new DateTimeOffset(2026, 9, 18, 10, 30, 0, TimeSpan.FromHours(-5)),
        "COP", "01", 1,
        new DianAuthorization("18760000001", new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), "FE", 1, 10000),
        new DianSoftware("900123456", "8", "11111111-1111-1111-1111-111111111111", "12345"),
        new DianParty("900123456", "8", "31", "1", "Comercializadora Uno SAS", "Comercializadora Uno", "R-99-PN", "01", "IVA", address),
        new DianParty("222222222222", "0", "13", "2", "Cliente de prueba SAS", "Cliente de prueba", "R-99-PN", "ZZ", "No aplica", address),
        [new DianInvoiceLine(1, "P-001", "999", "Café molido premium", "EA", 2m, 10_000m, 0m, 20_000m, [new DianTax("01", "IVA", 20_000m, 3_800m, 19m)])],
        [new DianTax("01", "IVA", 20_000m, 3_800m, 19m)],
        new DianPayment("1", "10", new DateOnly(2026, 9, 18), null),
        20_000m, 20_000m, 23_800m, 0m, 23_800m,
        "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96));
    return new DianInvoiceUblBuilder().Build(invoice).Xml;
}
