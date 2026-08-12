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
            var reopened = new PosEdgeSaleStore(connectionString, confirmation);
            await reopened.InitializeAsync();
            var replay = await reopened.IssueAsync(command);
            var pending = await reopened.GetPendingOutboxAsync();

            Assert.Equal("CVI03-00000001", issued.DocumentNumber);
            Assert.Null(issued.FiscalNumber);
            Assert.Null(issued.Cufe);
            Assert.Null(issued.QrPayload);
            Assert.Equal(PosSaleDocumentTypes.Receipt, issued.Upload.CommercialSnapshot.DocumentType);
            Assert.Null(issued.Upload.FiscalSnapshot);
            Assert.Null(issued.Upload.UblSnapshot);
            Assert.True(replay.WasAlreadyIssued);
            Assert.Equal(issued.DocumentNumber, replay.DocumentNumber);
            Assert.Null(replay.Cufe);
            Assert.Single(pending);
            Assert.Equal("sales.receipt.confirmed", pending.Single().Type);
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
