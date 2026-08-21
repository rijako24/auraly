using Auraly.Contracts.Organization;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Host;
using Microsoft.Data.Sqlite;
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
                PosPrinterModes.WindowsRaw, "  Tirilla  ", 58, "  Carta  "));
            var reloaded = new PosPrinterConfigurationStore(
                path, Path.Combine(directory, "receipts")).Load();

            Assert.Equal("Tirilla", saved.ReceiptPrinterName);
            Assert.Equal("Carta", saved.LetterPrinterName);
            Assert.Equal(58, reloaded.ReceiptPaperWidthMillimeters);
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
}
