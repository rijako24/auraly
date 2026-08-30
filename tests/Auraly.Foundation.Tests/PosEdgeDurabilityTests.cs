using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Authorization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosEdgeDurabilityTests
{
    [Fact]
    public async Task Fiscal_preview_is_unavailable_when_enrolled_device_has_no_assigned_resolution()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"auraly-pos-no-fiscal-block-{Guid.NewGuid():N}.db");
        try
        {
            var tenantId = new TenantId(Guid.NewGuid());
            var userId = new UserId(Guid.NewGuid());
            var confirmation = new ConfirmOfflineSaleService(new PermissionAuthorizer(
                new FixedPermissionProvider(new UserPermissionSet(tenantId, userId, []))));
            var store = new PosEdgeSaleStore($"Data Source={databasePath}", confirmation);
            await store.InitializeAsync();

            var preview = await store.PreviewNextFiscalNumberAsync(
                new DeviceId(Guid.NewGuid()),
                new DateTimeOffset(2026, 8, 29, 9, 0, 0, TimeSpan.FromHours(-5)));

            Assert.False(preview.IsAvailable);
            Assert.Equal(Guid.Empty, preview.SeriesId);
            Assert.Empty(preview.FullNumber);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }

    [Fact]
    public async Task Exclusive_resolution_stops_at_authorized_end_and_preserves_cursor_after_restart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"auraly-pos-blocks-{Guid.NewGuid():N}.db");
        try
        {
            var tenantId = new TenantId(Guid.NewGuid());
            var userId = new UserId(Guid.NewGuid());
            var deviceId = new DeviceId(Guid.NewGuid());
            var context = new SalesExecutionContext(
                tenantId, new BusinessId(Guid.NewGuid()), new WarehouseId(Guid.NewGuid()),
                userId, deviceId, new WorkSessionId(Guid.NewGuid()), true);
            var confirmation = new ConfirmOfflineSaleService(new PermissionAuthorizer(
                new FixedPermissionProvider(new UserPermissionSet(
                    tenantId, userId, [CommercePermissionCodes.SalesCreate]))));
            var connectionString = $"Data Source={databasePath}";
            var store = new PosEdgeSaleStore(connectionString, confirmation);
            await store.InitializeAsync();
            await store.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                Guid.NewGuid(), deviceId, AuralyDocumentTypes.SalesInvoice,
                "VTA", "77", 8, 1, 100));
            var authorizationId = Guid.NewGuid();
            var firstSeriesId = Guid.NewGuid();
            var firstProvision = new PosEdgeSeriesProvision(
                firstSeriesId, deviceId, "FV", "AUTH", 1, 2,
                new DateOnly(2027, 12, 31), authorizationId, new DateOnly(2026, 1, 1),
                1, 2);
            await store.ProvisionSeriesAsync(firstProvision);

            var first = await store.IssueAsync(CreateCommand(
                userId, new DocumentId(Guid.NewGuid()), context,
                new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(-5))));
            var previewBeforeLast = await store.PreviewNextFiscalNumberAsync(
                deviceId,
                new DateTimeOffset(2026, 8, 28, 10, 0, 30, TimeSpan.FromHours(-5)));
            var second = await store.IssueAsync(CreateCommand(
                userId, new DocumentId(Guid.NewGuid()), context,
                new DateTimeOffset(2026, 8, 28, 10, 1, 0, TimeSpan.FromHours(-5))));

            var previewAfterExhaustion = await store.PreviewNextFiscalNumberAsync(
                deviceId,
                new DateTimeOffset(2026, 8, 28, 10, 1, 30, TimeSpan.FromHours(-5)));
            var restarted = new PosEdgeSaleStore(connectionString, confirmation);
            await restarted.InitializeAsync();
            await restarted.ProvisionSeriesAsync(firstProvision);
            var exhausted = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                restarted.IssueAsync(CreateCommand(
                userId, new DocumentId(Guid.NewGuid()), context,
                new DateTimeOffset(2026, 8, 28, 10, 2, 0, TimeSpan.FromHours(-5)))));

            Assert.Equal("FV1", first.FiscalNumber);
            Assert.True(previewBeforeLast.IsAvailable);
            Assert.Equal("FV2", previewBeforeLast.FullNumber);
            Assert.Equal("FV2", second.FiscalNumber);
            Assert.False(previewAfterExhaustion.IsAvailable);
            Assert.Contains("agotó su numeración", exhausted.Message, StringComparison.Ordinal);
            var cursor = await restarted.GetFiscalCursorStateAsync(deviceId);
            Assert.NotNull(cursor);
            Assert.Equal(3, cursor.NextConsecutive);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }

    [Fact]
    public async Task Restart_preserves_sales_outbox_cufe_and_idempotency()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-edge-{Guid.NewGuid():N}.db");
        try
        {
            var tenantId = new TenantId(Guid.NewGuid());
            var businessId = new BusinessId(Guid.NewGuid());
            var warehouseId = new WarehouseId(Guid.NewGuid());
            var deviceId = new DeviceId(Guid.NewGuid());
            var userId = new UserId(Guid.NewGuid());
            var context = new SalesExecutionContext(
                tenantId,
                businessId,
                warehouseId,
                userId,
                deviceId,
                new WorkSessionId(Guid.NewGuid()),
                WarehouseAllowsNegativeStockSales: true);
            var permissionSet = new UserPermissionSet(
                tenantId,
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var confirmation = new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new FixedPermissionProvider(permissionSet)));
            var connectionString = $"Data Source={databasePath}";
            var firstProcess = new PosEdgeSaleStore(connectionString, confirmation);
            await firstProcess.InitializeAsync();
            await firstProcess.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                Guid.NewGuid(),
                deviceId,
                AuralyDocumentTypes.SalesInvoice,
                "VTA",
                "03",
                8,
                1,
                99_999_999));
            await firstProcess.ProvisionSeriesAsync(new PosEdgeSeriesProvision(
                Guid.NewGuid(),
                deviceId,
                "FV01",
                "18760000001",
                1,
                100,
                new DateOnly(2027, 7, 27)));
            var firstDocumentId = new DocumentId(Guid.NewGuid());
            var firstCommand = CreateCommand(
                userId,
                firstDocumentId,
                context,
                new DateTimeOffset(2026, 7, 27, 14, 35, 12, TimeSpan.FromHours(-5)));

            var first = await firstProcess.IssueAsync(firstCommand);
            var second = await firstProcess.IssueAsync(CreateCommand(
                userId,
                new DocumentId(Guid.NewGuid()),
                context,
                new DateTimeOffset(2026, 7, 27, 14, 36, 12, TimeSpan.FromHours(-5))));

            var reopenedProcess = new PosEdgeSaleStore(connectionString, confirmation);
            await reopenedProcess.InitializeAsync();
            var duplicate = await reopenedProcess.IssueAsync(firstCommand);
            var pending = await reopenedProcess.GetPendingOutboxAsync();

            Assert.Equal("VTA03-00000001", first.DocumentNumber);
            Assert.Equal("VTA03-00000002", second.DocumentNumber);
            Assert.Equal("FV011", first.FiscalNumber);
            Assert.Equal("FV012", second.FiscalNumber);
            Assert.True(duplicate.WasAlreadyIssued);
            Assert.Equal(first.DocumentNumber, duplicate.DocumentNumber);
            Assert.Equal(first.FiscalNumber, duplicate.FiscalNumber);
            Assert.Equal(first.Cufe, duplicate.Cufe);
            Assert.Equal(first.OutboxMessageId, duplicate.OutboxMessageId);
            Assert.Equal(2, pending.Count);

            await reopenedProcess.MarkUploadedAsync(
                first.OutboxMessageId,
                DateTimeOffset.UtcNow);
            await reopenedProcess.MarkUploadedAsync(
                first.OutboxMessageId,
                DateTimeOffset.UtcNow);

            var afterUpload = await reopenedProcess.GetPendingOutboxAsync();
            Assert.Single(afterUpload);
            Assert.Equal(second.DocumentId, afterUpload.Single().DocumentId);

            var cursor = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 42 });
            await reopenedProcess.ApplyFiscalStatusPageAsync(new PosFiscalStatusPage(
                [new PosFiscalStatusChange(
                    first.DocumentId.Value,
                    first.FiscalNumber!,
                    first.Cufe!,
                    FiscalDocumentStatusCodes.DianAccepted,
                    "00",
                    "Accepted",
                    DateTimeOffset.UtcNow)],
                cursor,
                false));
            var afterFiscalRestart = new PosEdgeSaleStore(connectionString, confirmation);
            await afterFiscalRestart.InitializeAsync();
            var fiscal = await afterFiscalRestart.GetFiscalStatusAsync(first.DocumentId);
            Assert.NotNull(fiscal);
            Assert.Equal(FiscalDocumentStatusCodes.DianAccepted, fiscal.Status);
            Assert.Equal(first.Cufe, fiscal.Cufe);
            Assert.Equal(cursor, await afterFiscalRestart.GetFiscalStatusCursorAsync());
            Assert.Single(await afterFiscalRestart.GetPendingOutboxAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }

    [Fact]
    public async Task Commercial_receipt_is_durable_without_fiscal_artifacts()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), $"auraly-pos-receipt-{Guid.NewGuid():N}.db");
        try
        {
            var tenantId = new TenantId(Guid.NewGuid());
            var businessId = new BusinessId(Guid.NewGuid());
            var warehouseId = new WarehouseId(Guid.NewGuid());
            var deviceId = new DeviceId(Guid.NewGuid());
            var userId = new UserId(Guid.NewGuid());
            var execution = new SalesExecutionContext(
                tenantId, businessId, warehouseId, userId, deviceId,
                new WorkSessionId(Guid.NewGuid()), true);
            var confirmation = new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new FixedPermissionProvider(
                    new UserPermissionSet(
                        tenantId, userId, [CommercePermissionCodes.SalesCreate]))));
            var connectionString = $"Data Source={databasePath}";
            var store = new PosEdgeSaleStore(connectionString, confirmation);
            await store.InitializeAsync();
            await store.ProvisionDocumentSeriesAsync(new PosEdgeDocumentSeriesProvision(
                Guid.NewGuid(), deviceId, AuralyDocumentTypes.SalesReceipt,
                "CVI", "03", 8, 1, 99_999_999));

            var documentId = new DocumentId(Guid.NewGuid());
            var product = new PosCatalogProduct(
                new ProductId(Guid.NewGuid()), "P001", "Producto", ["7701234567890"],
                true, false, "01", 19m);
            var command = new PosEdgeIssueCommand(
                userId,
                documentId,
                execution,
                new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.FromHours(-5)),
                "9001234567",
                "222222222",
                new FiscalTechnicalKey("unused-for-receipt", "v1"),
                FiscalEnvironment.Test,
                "https://example.test/qr",
                [new OfflineSaleLine(product, 1m, 10_000m, 0m, 1_900m)],
                DocumentType: PosSaleDocumentTypes.Receipt);

            var issued = await store.IssueAsync(command);
            var second = await store.IssueAsync(command with
            {
                DocumentId = new DocumentId(Guid.NewGuid()),
                IssuedAt = command.IssuedAt.AddMinutes(1)
            });
            var reopened = new PosEdgeSaleStore(connectionString, confirmation);
            await reopened.InitializeAsync();
            var replay = await reopened.IssueAsync(command);
            var pending = await reopened.GetPendingOutboxAsync();

            Assert.Equal("CVI03-00000001", issued.DocumentNumber);
            Assert.Equal("CVI03-00000002", second.DocumentNumber);
            Assert.Null(issued.FiscalNumber);
            Assert.Null(second.FiscalNumber);
            Assert.Null(issued.Cufe);
            Assert.Null(issued.QrPayload);
            Assert.Equal(PosSaleDocumentTypes.Receipt, issued.Upload.CommercialSnapshot.DocumentType);
            Assert.Null(issued.Upload.FiscalSnapshot);
            Assert.Null(issued.Upload.UblSnapshot);
            Assert.True(replay.WasAlreadyIssued);
            Assert.Equal(issued.DocumentNumber, replay.DocumentNumber);
            Assert.Null(replay.Cufe);
            Assert.Equal(2, pending.Count);
            Assert.All(pending, item =>
                Assert.Equal("sales.receipt.confirmed", item.Type));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }

    private static PosEdgeIssueCommand CreateCommand(
        UserId userId,
        DocumentId documentId,
        SalesExecutionContext context,
        DateTimeOffset issuedAt)
    {
        var product = new PosCatalogProduct(
            new ProductId(Guid.NewGuid()),
            "P001",
            "Producto",
            ["7701234567890"],
            true,
            false,
            "01",
            19m);
        return new PosEdgeIssueCommand(
            userId,
            documentId,
            context,
            issuedAt,
            "9001234567",
            "222222222",
            new FiscalTechnicalKey("CLAVE-TECNICA", "v1"),
            FiscalEnvironment.Test,
            "https://catalogo-vpfe.dian.gov.co/document/searchqr",
            [new OfflineSaleLine(product, 1m, 10_000m, 0m, 1_900m)]);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class FixedPermissionProvider(UserPermissionSet permissionSet)
        : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId) => permissionSet;
    }
}
