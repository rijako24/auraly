using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using System.IO.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosConfigurationTests
{
    [Fact]
    public async Task Warehouse_policy_push_is_applied_in_memory_and_persisted_for_offline_restart()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-warehouse-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var enrollment = EnrollmentPackage(allowsNegativeStock: false);
            var store = new PosEdgeEnrollmentStore(
                Path.Combine(directory, "enrollment.protected"),
                Path.Combine(directory, "keys"));
            store.Save(enrollment);
            var runtime = new PosEdgeRuntimeContext(
                new BusinessId(enrollment.BusinessId),
                new WarehouseId(enrollment.WarehouseId),
                new DeviceId(enrollment.DeviceId),
                warehouseAllowsNegativeStock: false);
            var events = new PosSynchronizationEventLog(TimeProvider.System);
            var sink = new PosWarehousePolicySink(
                runtime, store, events, NullLogger<PosWarehousePolicySink>.Instance);

            await sink.ApplyAsync(true);

            Assert.True(runtime.WarehouseAllowsNegativeStock);
            Assert.True(store.Load()!.WarehouseAllowsNegativeStock);
            var policyEvent = Assert.Single(events.Read());
            Assert.Equal("Bodega", policyEvent.Category);
            Assert.Contains("permite", policyEvent.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Remote_unenrollment_removes_only_the_protected_enrollment_identity()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-unenrollment-" + Guid.NewGuid().ToString("N"));
        try
        {
            var databasePath = Path.Combine(directory, "auraly-pos.db");
            Directory.CreateDirectory(directory);
            File.WriteAllText(databasePath, "audit-data");
            var store = new PosEdgeEnrollmentStore(
                Path.Combine(directory, "enrollment.protected"),
                Path.Combine(directory, "keys"));
            store.Save(EnrollmentPackage(allowsNegativeStock: false));

            store.Clear();

            Assert.Null(store.Load());
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Enrollment_preview_is_used_until_the_cashier_configures_a_printer()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-printer-default-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"),
                PosPrinterConfiguration.Default with
                {
                    ReceiptMode = PosPrinterModes.BrowserPreview
                });

            Assert.Equal(PosPrinterModes.BrowserPreview, store.Load().ReceiptMode);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Microsoft Print to PDF")]
    [InlineData("Adobe PDF")]
    [InlineData("Microsoft XPS Document Writer")]
    public void Virtual_document_printers_require_rendered_output(string printerName)
    {
        Assert.True(WindowsPrinterOutput.RequiresRenderedDocument(printerName));
        Assert.False(WindowsPrinterOutput.RequiresRenderedDocument("EPSON TM-T20III Receipt"));
    }

    [Fact]
    public async Task Virtual_document_printer_receives_rendered_receipt_without_browser_dialog()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-pdf-printer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));
            store.Save(new PosPrinterConfiguration(
                PosPrinterModes.WindowsRaw,
                "Microsoft Print to PDF",
                80,
                "Microsoft Print to PDF",
                PosPrinterName: "Microsoft Print to PDF",
                OrdersPrinterName: "Microsoft Print to PDF"));
            var raw = new RecordingRawPrintJob();
            var rendered = new RecordingRenderedPrintJob();
            var documents = new ConfigurableOrderDocumentPrinter(
                store, new HalfLetterDocumentRenderer(), rendered);
            var printer = new ConfigurablePosReceiptPrinter(
                store,
                new EscPosReceiptRenderer(),
                new HtmlReceiptPreviewRenderer(),
                new NoopPreviewLauncher(),
                raw,
                rendered,
                documents);

            var receipt = Receipt();
            await printer.PrintAsync(receipt);
            Assert.Empty(raw.PrinterNames);
            Assert.Single(rendered.PrinterNames);
            Assert.Equal("Microsoft Print to PDF", rendered.PrinterNames[0]);
            Assert.Contains("<!doctype html>", rendered.Documents[0],
                StringComparison.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            await new PosWorkSessionClosurePrinter(store, raw, rendered).PrintAsync(
                new WorkSessionClosureView(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Negocio",
                    Guid.NewGuid(), "Bodega", Guid.NewGuid(), "Cajero", null,
                    now.AddHours(-8), now, 10m, 0, 0, 10m, 10m, 10m, 0, null,
                    [new WorkSessionPaymentTotal("Cash", 10m, 0, 0, 10m, 10m, 0)]),
                CancellationToken.None);

            Assert.Equal(2, rendered.PrinterNames.Count);
            Assert.Contains("ARQUEO DE CAJA", rendered.Documents[1],
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

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
            DateTimeOffset.UtcNow,
            "Comercializadora Uno",
            "data:image/png;base64,AA==");

        var configuration = PosEdgeEnrollmentStore.ToConfiguration(
            package,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db"));

        Assert.Equal(
            package.ReceiptDocumentSeries.SeriesId.ToString("D"),
            configuration["PosEdge:Documents:SalesReceipt:SeriesId"]);
        Assert.Equal("Comercializadora Uno", configuration["PosEdge:CompanyName"]);
        Assert.Equal("data:image/png;base64,AA==",
            configuration["PosEdge:CompanyLogoSource"]);
        Assert.DoesNotContain(
            configuration.Keys,
            key => key.StartsWith("PosEdge:Fiscal:", StringComparison.Ordinal));
        Assert.DoesNotContain("PosEdge:SupplierTaxId", configuration.Keys);
    }

    [Fact]
    public async Task Configured_sheet_format_is_sent_to_the_local_printer_without_browser_dialog()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-sheet-printer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));
            store.Save(new PosPrinterConfiguration(
                PosPrinterModes.WindowsRaw,
                "Microsoft XPS Document Writer",
                80,
                "Microsoft XPS Document Writer",
                OrderPrinterModes.WindowsPrint,
                PosOutputFormat: PrintTemplateFormats.HalfLetter,
                PosPrinterName: "Microsoft XPS Document Writer"));
            var rendered = new RecordingRenderedPrintJob();
            var documents = new ConfigurableOrderDocumentPrinter(
                store, new HalfLetterDocumentRenderer(), rendered);
            var printer = new ConfigurablePosReceiptPrinter(
                store,
                new EscPosReceiptRenderer(),
                new HtmlReceiptPreviewRenderer(),
                new NoopPreviewLauncher(),
                new RecordingRawPrintJob(),
                rendered,
                documents);

            var receipt = Receipt();
            await printer.PrintAsync(receipt);

            Assert.Single(rendered.PrinterNames);
            Assert.Equal("Microsoft XPS Document Writer", rendered.PrinterNames[0]);
            Assert.Contains(receipt.DocumentNumber, rendered.Documents[0],
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static PosEnrollmentPackage EnrollmentPackage(bool allowsNegativeStock) =>
        new(
            Guid.NewGuid(),
            "device-secret",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Negocio",
            "01",
            "Bodega",
            allowsNegativeStock,
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
            Assert.Equal(8, reloaded.TemplateRoutes?.Count);
            Assert.All(
                reloaded.TemplateRoutes!.Where(route =>
                    route.Format == PrintTemplateFormats.Receipt),
                route => Assert.Equal("Tirilla", route.PrinterName));
            Assert.All(
                reloaded.TemplateRoutes!.Where(route =>
                    route.Format == PrintTemplateFormats.HalfLetter),
                route => Assert.Equal("Carta", route.PrinterName));
            Assert.All(
                reloaded.TemplateRoutes!.Where(route =>
                    route.Format is PrintTemplateFormats.HalfLegal or
                        PrintTemplateFormats.Letter),
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

    [Theory]
    [InlineData(PrintTemplateFormats.HalfLetter)]
    [InlineData(PrintTemplateFormats.HalfLegal)]
    [InlineData(PrintTemplateFormats.Letter)]
    public void Sheet_output_formats_are_valid_and_keep_the_document_printer(
        string format)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "auraly-sheet-format-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PosPrinterConfigurationStore(
                Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "receipts"));

            var saved = store.Save(PosPrinterConfiguration.Default with
            {
                ReceiptPrinterName = "Tirilla",
                LetterPrinterName = "Documentos",
                PosPrinterName = "Documentos",
                OrdersPrinterName = "Documentos",
                PosOutputFormat = format,
                OrdersOutputFormat = format
            });

            Assert.Equal(format, saved.PosOutputFormat);
            Assert.Equal(format, saved.OrdersOutputFormat);
            Assert.Equal("Documentos", saved.PrinterFor("SalesInvoice", format));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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
            var rendered = new RecordingRenderedPrintJob();
            var documents = new ConfigurableOrderDocumentPrinter(
                store, new HalfLetterDocumentRenderer(), rendered);
            var receiptPrinter = new ConfigurablePosReceiptPrinter(
                store,
                new EscPosReceiptRenderer(),
                new HtmlReceiptPreviewRenderer(),
                new NoopPreviewLauncher(),
                raw,
                rendered,
                documents);
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
            await new PosWorkSessionClosurePrinter(store, raw, rendered).PrintAsync(
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
            await using var database = new SqliteConnection(connectionString);
            await database.OpenAsync();
            await using var schema = database.CreateCommand();
            schema.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name IN(
                  'PosCashMovementOutbox','PosWorkSessionClosureOutbox');
                """;
            Assert.Equal(0L, (long)(await schema.ExecuteScalarAsync())!);
            await using var unified = database.CreateCommand();
            unified.CommandText = """
                SELECT COUNT(*) FROM Outbox
                WHERE Type='cash.movement.confirmed' AND DocumentId=$document;
                """;
            unified.Parameters.AddWithValue("$document", documentId.ToString("D"));
            Assert.Equal(1L, (long)(await unified.ExecuteScalarAsync())!);
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

    private sealed class RecordingRenderedPrintJob : IWindowsRenderedPrintJob
    {
        public List<string> PrinterNames { get; } = [];
        public List<string> Documents { get; } = [];

        public Task PrintAsync(
            string printerName,
            string documentName,
            string html,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            PrinterNames.Add(printerName);
            Documents.Add(html);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopPreviewLauncher : IReceiptPreviewLauncher
    {
        public Task OpenAsync(
            string absolutePath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
