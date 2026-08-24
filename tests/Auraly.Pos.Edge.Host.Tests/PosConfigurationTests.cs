using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using System.IO.Ports;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosConfigurationTests
{
    [Fact]
    public void Receipt_only_enrollment_does_not_require_or_emit_fiscal_secrets()
    {
        var package = new PosEnrollmentPackage(
            Guid.NewGuid(),
            "device-secret",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Negocio",
            "01",
            "Bodega",
            false,
            Guid.NewGuid(),
            "Cajero",
            ["sales.create"],
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesInvoice", "VTA", "01", 8, 1, 99_999_999),
            null,
            new PosEnrollmentDocumentSeries(
                Guid.NewGuid(), "SalesReceipt", "CVI", "01", 8, 1, 99_999_999),
            null,
            DateTimeOffset.UtcNow);

        var configuration = PosEdgeEnrollmentStore.ToConfiguration(
            package,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db"));

        Assert.Equal(
            package.ReceiptDocumentSeries.SeriesId.ToString("D"),
            configuration["PosEdge:Documents:SalesReceipt:SeriesId"]);
        Assert.DoesNotContain(
            configuration.Keys,
            key => key.StartsWith("PosEdge:Fiscal:", StringComparison.Ordinal));
        Assert.DoesNotContain("PosEdge:SupplierTaxId", configuration.Keys);
    }

    [Fact]
    public void Printer_configuration_is_validated_normalized_and_persisted()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-printer-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new PosPrinterConfigurationStore(
                path, Path.Combine(directory, "receipts"));
            var saved = store.Save(new PosPrinterConfiguration(
                PosPrinterModes.WindowsRaw, "  Tirilla  ", 58, "  Carta  ",
                PosPrinterName: "  Caja POS  ", OrdersPrinterName: "  Pedidos  ",
                OrdersReceiptPaperWidthMillimeters: 80,
                Scale: new PosScaleConfiguration(
                    true, "  COM8  ", 19_200, 7, "Even", "Two", true,
                    "P\\r\\n", 2, 8, true, true, 3_000)));
            var reloaded = new PosPrinterConfigurationStore(
                path, Path.Combine(directory, "receipts")).Load();

            Assert.Equal("Tirilla", saved.ReceiptPrinterName);
            Assert.Equal("Carta", saved.LetterPrinterName);
            Assert.Equal("Caja POS", reloaded.PosPrinterName);
            Assert.Equal("Pedidos", reloaded.OrdersPrinterName);
            Assert.Equal(80, reloaded.OrdersReceiptPaperWidthMillimeters);
            Assert.Equal(58, reloaded.ReceiptPaperWidthMillimeters);
            Assert.True(reloaded.Scale?.Enabled);
            Assert.Equal("COM8", reloaded.Scale?.PortName);
            Assert.Equal(19_200, reloaded.Scale?.BaudRate);
            Assert.Equal("P\\r\\n", reloaded.Scale?.RequestText);
            Assert.True(reloaded.Scale?.DivideBy1000);
            Assert.Equal(PrintTemplateFormats.Receipt, reloaded.PosOutputFormat);
            Assert.Equal(PrintTemplateFormats.HalfLetter, reloaded.OrdersOutputFormat);
            Assert.Equal(4, reloaded.TemplateRoutes?.Count);
            Assert.All(
                reloaded.TemplateRoutes!.Where(route =>
                    route.Format == PrintTemplateFormats.Receipt),
                route => Assert.Equal("Tirilla", route.PrinterName));
            Assert.All(
                reloaded.TemplateRoutes!.Where(route =>
                    route.Format == PrintTemplateFormats.HalfLetter),
                route => Assert.Equal("Carta", route.PrinterName));
            Assert.Equal(
                saved with { TemplateRoutes = null },
                reloaded with { TemplateRoutes = null });
            Assert.Equal<PrintTemplateRoute>(
                saved.TemplateRoutes!, reloaded.TemplateRoutes!);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Scale_configuration_rejects_invalid_serial_parameters()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-scale-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));
            var invalid = PosPrinterConfiguration.Default with
            {
                PosPrinterName = "Caja",
                OrdersPrinterName = "Pedidos",
                Scale = new PosScaleConfiguration(
                    true, "COM1", 9_600, 8, "Invalid", "One")
            };

            Assert.Throws<ArgumentException>(() => store.Save(invalid));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Peripheral_discovery_returns_the_com_ports_reported_by_windows()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));

            Assert.Equal(
                SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase),
                store.SerialPorts());
            Assert.Equal(
                store.InstalledPrinters()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase),
                store.InstalledPrinters());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Sales_orders_and_closure_use_the_printers_assigned_to_their_workflows()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-routing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));
            store.Save(new PosPrinterConfiguration(
                PosPrinterModes.WindowsRaw,
                "Factura POS",
                80,
                "Media carta",
                PosPrinterName: "Factura POS",
                OrdersPrinterName: "Pedidos POS",
                OrdersReceiptPaperWidthMillimeters: 58));
            var raw = new RecordingRawPrintJob();
            var receiptPrinter = new ConfigurablePosReceiptPrinter(
                store,
                new EscPosReceiptRenderer(),
                new HtmlReceiptPreviewRenderer(),
                new NoopPreviewLauncher(),
                new ConfigurableOrderDocumentPrinter(
                    store, new HalfLetterDocumentRenderer()),
                raw);
            var receipt = Receipt();

            await receiptPrinter.PrintAsync(receipt);
            await receiptPrinter.PrintOrdersReceiptAsync(new OnlineSalesReceipt(
                receipt.DocumentId.Value,
                receipt.DocumentType,
                receipt.DocumentNumber,
                receipt.FiscalNumber,
                receipt.IssuedAt,
                receipt.CustomerIdentification,
                receipt.Lines.Select(line => new OnlineSalesReceiptLine(
                    line.ProductCode, line.Description, line.Quantity,
                    line.UnitPrice, line.Discount, line.Tax, line.Total)).ToArray(),
                receipt.Payments.Select(payment => new OnlineSalesPayment(
                    payment.MethodCode, payment.Amount, payment.Reference)).ToArray(),
                receipt.UntaxedAmount,
                receipt.TaxAmount,
                receipt.PayableAmount,
                receipt.Cufe,
                receipt.QrPayload,
                null,
                "Cliente"));
            var now = DateTimeOffset.UtcNow;
            await new PosWorkSessionClosurePrinter(store, raw).PrintAsync(
                new WorkSessionClosureView(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Negocio",
                    Guid.NewGuid(), "Bodega", Guid.NewGuid(), "Cajero", null,
                    now.AddHours(-8), now, 10m, 0, 0, 10m, 10m, 10m, 0, null,
                    [new WorkSessionPaymentTotal("Cash", 10m, 0, 0, 10m, 10m, 0)]),
                CancellationToken.None);

            Assert.Equal(
                new[] { "Factura POS", "Pedidos POS", "Factura POS" },
                raw.PrinterNames);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cash_movement_is_durable_and_idempotent_before_server_sync()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "auraly-cash-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var businessId = Guid.NewGuid();
            var reasonId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var workSessionId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var connectionString = "Data Source=" + path;
            var store = new PosCashMovementStore(connectionString, TimeProvider.System);
            await store.InitializeAsync();
            await store.ReplaceReasonsAsync(
                businessId,
                [new CashMovementReasonView(
                    reasonId, businessId, "BASE", "Base de caja", "Receipt",
                    "CashOverShort", null, null, "110505", "Caja general",
                    true, true, true)]);
            var request = new QueueLocalCashMovementRequest(
                documentId, reasonId, 50_000m, DateTimeOffset.UtcNow,
                "APERTURA-1", "Base inicial", null);

            var first = await store.QueueAsync(
                businessId, workSessionId, userId, request);
            var replay = await store.QueueAsync(
                businessId, workSessionId, userId, request);
            var reopened = new PosCashMovementStore(connectionString, TimeProvider.System);
            var pending = await reopened.ClaimAsync();

            Assert.False(first.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.NotNull(pending);
            Assert.Equal(documentId, pending.Value.DocumentId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static PosReceipt Receipt() => new(
        Guid.NewGuid(),
        new DocumentId(Guid.NewGuid()),
        "CVI01-1",
        null,
        DateTimeOffset.UtcNow,
        "222222222222",
        [new PosReceiptLine("P-1", "Producto", 1, 10m, 0, 0, 10m)],
        [new OfflineSalePayment("Cash", 10m)],
        10m,
        0,
        10m,
        null,
        null,
        80,
        PosSaleDocumentTypes.Receipt);

    private sealed class RecordingRawPrintJob : IWindowsRawPrintJob
    {
        public List<string> PrinterNames { get; } = [];

        public void Print(string printerName, string documentName, byte[] bytes) =>
            PrinterNames.Add(printerName);
    }

    private sealed class NoopPreviewLauncher : IReceiptPreviewLauncher
    {
        public Task OpenAsync(
            string absolutePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
