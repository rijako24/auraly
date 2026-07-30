using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Authorization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosToServerRecoveryTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Physical_sqlite_restarts_uploads_once_and_preserves_conflict()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-server-e2e-{Guid.NewGuid():N}.db");
        try
        {
            var userId = new UserId(fixture.UserId);
            var register = new RegisterContext(
                new TenantId(fixture.TenantId),
                new BusinessId(fixture.BusinessId),
                new WarehouseId(fixture.WarehouseId),
                new RegisterId(fixture.RegisterId),
                WarehouseAllowsNegativeStockSales: true);
            var permissions = new UserPermissionSet(
                register.TenantId,
                userId,
                [CommercePermissionCodes.SalesCreate]);
            var confirmation = new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new FixedPermissionProvider(permissions)));
            var connectionString = $"Data Source={databasePath}";
            var firstProcess = new PosEdgeSaleStore(connectionString, confirmation);
            await firstProcess.InitializeAsync();
            await firstProcess.ProvisionDocumentSeriesAsync(
                new PosEdgeDocumentSeriesProvision(
                    fixture.DocumentSeriesId,
                    register.RegisterId,
                    AuralyDocumentTypes.SalesInvoice,
                    "VTA",
                    "03",
                    8,
                    501,
                    600));
            await firstProcess.ProvisionSeriesAsync(
                new PosEdgeSeriesProvision(
                    fixture.SeriesId,
                    register.RegisterId,
                    ServerSliceFixture.Prefix,
                    ServerSliceFixture.AuthorizationNumber,
                    501,
                    600,
                    new DateOnly(2028, 12, 31),
                    fixture.FiscalAuthorizationId));
            var firstDocument = new DocumentId(Guid.NewGuid());
            var secondDocument = new DocumentId(Guid.NewGuid());
            var firstCommand = CreateCommand(userId, firstDocument, register, fixture);
            await firstProcess.IssueAsync(firstCommand);
            await firstProcess.IssueAsync(CreateCommand(userId, secondDocument, register, fixture) with
            {
                IssuedAt = firstCommand.IssuedAt.AddSeconds(1)
            });

            var reopened = new PosEdgeSaleStore(connectionString, confirmation);
            await reopened.InitializeAsync();
            Assert.Equal(2, (await reopened.GetPendingOutboxAsync()).Count);

            using var httpClient = fixture.CreateClient();
            var realClient = new HttpPosSaleUploadClient(
                httpClient,
                ServerSliceFixture.DeviceSecret);
            var uploader = new PosEdgeOutboxUploader(
                reopened,
                realClient,
                TimeProvider.System);
            Assert.True(await uploader.UploadNextAsync());
            var firstOutbox = await reopened.GetOutboxAsync(firstDocument);
            Assert.NotNull(firstOutbox);
            Assert.Equal(PosOutboxStatus.Uploaded, firstOutbox.Status);

            var duplicateRequest = PosSaleContractSerializer.Deserialize(firstOutbox.Payload);
            var duplicate = await realClient.UploadAsync(
                duplicateRequest,
                duplicateRequest.DocumentId.ToString("D"));
            Assert.Equal(PosSaleUploadDisposition.Uploaded, duplicate.Disposition);
            Assert.Equal(
                PosSaleRemoteStatuses.AlreadyProcessed,
                duplicate.Response!.Status);
            Assert.Equal(duplicateRequest.FiscalSnapshot.Cufe, duplicate.Response.CufeReceived);
            Assert.Equal(duplicateRequest.FiscalSnapshot.Cufe, duplicate.Response.CufeCalculated);
            Assert.Equal(1, await fixture.CountAsync("SalesDocuments", firstDocument.Value));
            Assert.Equal(1, await fixture.CountAsync("InventoryMovements", firstDocument.Value));
            Assert.Equal(1, await fixture.CountAsync("SalesPayments", firstDocument.Value));
            Assert.Equal(1, await fixture.CountAsync("ServerOutboxMessages", firstDocument.Value));

            var tamperingClient = new TamperingClient(realClient);
            var conflictUploader = new PosEdgeOutboxUploader(
                reopened,
                tamperingClient,
                TimeProvider.System);
            Assert.True(await conflictUploader.UploadNextAsync());
            var secondOutbox = await reopened.GetOutboxAsync(secondDocument);
            Assert.NotNull(secondOutbox);
            Assert.Equal(PosOutboxStatus.FiscalIntegrityConflict, secondOutbox.Status);
            Assert.Equal(
                PosSaleRemoteStatuses.FiscalIntegrityConflict,
                secondOutbox.RemoteStatus);
            Assert.Equal(1, await fixture.CountAsync("SalesDocuments", secondDocument.Value));
            Assert.Equal(0, await fixture.CountAsync("InventoryMovements", secondDocument.Value));
            Assert.Equal(0, await fixture.CountAsync("SalesPayments", secondDocument.Value));

            var secondPayload = PosSaleContractSerializer.Deserialize(secondOutbox.Payload);
            Assert.Equal(secondDocument.Value, secondPayload.DocumentId);
            Assert.NotEqual(Guid.Empty, secondOutbox.ServerReceiptId);
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
    public async Task Timeout_and_non_durable_response_keep_the_local_outbox()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-timeout-{Guid.NewGuid():N}.db");
        try
        {
            var store = await CreateSinglePendingStoreAsync(databasePath);
            var timeoutUploader = new PosEdgeOutboxUploader(
                store,
                new FixedUploadClient(
                    new PosSaleUploadAttempt(
                        PosSaleUploadDisposition.RetryableFailure,
                        null,
                        "timeout")),
                TimeProvider.System);
            Assert.True(await timeoutUploader.UploadNextAsync());
            var pending = Assert.Single(await store.GetPendingOutboxAsync());
            Assert.Equal(PosOutboxStatus.RetryScheduled, pending.Status);
            Assert.Equal(1, pending.AttemptCount);

            var reopened = await CreateStoreOnlyAsync(databasePath);
            var persisted = await reopened.GetOutboxAsync(pending.DocumentId);
            Assert.NotNull(persisted);
            Assert.Equal(PosOutboxStatus.RetryScheduled, persisted.Status);
            Assert.NotNull(persisted.NextAttemptAt);
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
    public async Task Non_durable_server_response_never_completes_the_local_outbox()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"auraly-pos-nondurable-{Guid.NewGuid():N}.db");
        try
        {
            var store = await CreateSinglePendingStoreAsync(databasePath);
            var original = Assert.Single(await store.GetPendingOutboxAsync());
            var uploader = new PosEdgeOutboxUploader(
                store,
                new FixedUploadClient(
                    new PosSaleUploadAttempt(
                        PosSaleUploadDisposition.Uploaded,
                        null,
                        null)),
                TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => uploader.UploadNextAsync());

            var local = await store.GetOutboxAsync(original.DocumentId);
            Assert.NotNull(local);
            Assert.Equal(PosOutboxStatus.Uploading, local.Status);
            var reopened = await CreateStoreOnlyAsync(databasePath);
            var persisted = await reopened.GetOutboxAsync(local.DocumentId);
            Assert.NotNull(persisted);
            Assert.Equal(PosOutboxStatus.Uploading, persisted.Status);
            Assert.Null(persisted.ServerReceiptId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfPresent(databasePath);
            DeleteIfPresent($"{databasePath}-wal");
            DeleteIfPresent($"{databasePath}-shm");
        }
    }
    private async Task<PosEdgeSaleStore> CreateSinglePendingStoreAsync(string databasePath)
    {
        var userId = new UserId(Guid.NewGuid());
        var store = await CreateStoreOnlyAsync(databasePath, userId);
        var register = RegisterContext();
        await store.ProvisionDocumentSeriesAsync(
            new PosEdgeDocumentSeriesProvision(
                fixture.DocumentSeriesId,
                register.RegisterId,
                AuralyDocumentTypes.SalesInvoice,
                "VTA",
                "03",
                8,
                701,
                800));
        await store.ProvisionSeriesAsync(
            new PosEdgeSeriesProvision(
                fixture.SeriesId,
                register.RegisterId,
                ServerSliceFixture.Prefix,
                ServerSliceFixture.AuthorizationNumber,
                701,
                800,
                new DateOnly(2028, 12, 31),
                fixture.FiscalAuthorizationId));
        await store.IssueAsync(CreateCommand(userId, new DocumentId(Guid.NewGuid()), register, fixture));
        return store;
    }

    private async Task<PosEdgeSaleStore> CreateStoreOnlyAsync(
        string databasePath,
        UserId? configuredUserId = null)
    {
        var userId = configuredUserId ?? new UserId(Guid.NewGuid());
        var permissions = new UserPermissionSet(
            new TenantId(fixture.TenantId),
            userId,
            [CommercePermissionCodes.SalesCreate]);
        var store = new PosEdgeSaleStore(
            $"Data Source={databasePath}",
            new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new FixedPermissionProvider(permissions))));
        await store.InitializeAsync();
        return store;
    }

    private RegisterContext RegisterContext() =>
        new(
            new TenantId(fixture.TenantId),
            new BusinessId(fixture.BusinessId),
            new WarehouseId(fixture.WarehouseId),
            new RegisterId(fixture.RegisterId),
            true);

    private static PosEdgeIssueCommand CreateCommand(
        UserId userId,
        DocumentId documentId,
        RegisterContext register,
        ServerSliceFixture fixture)
    {
        var product = new PosCatalogProduct(
            new ProductId(fixture.ProductId),
            "P-E2E",
            "Producto E2E",
            ["7701234567890"],
            true,
            false,
            "01",
            19m);
        return new PosEdgeIssueCommand(
            userId,
            documentId,
            register,
            new DateTimeOffset(2026, 7, 27, 14, 35, 12, TimeSpan.FromHours(-5)),
            ServerSliceFixture.SupplierTaxId,
            "222222222",
            new FiscalTechnicalKey(
                ServerSliceFixture.TechnicalKeyValue,
                ServerSliceFixture.TechnicalKeyVersion),
            FiscalEnvironment.Test,
            ServerSliceFixture.QrValidationUrl,
            [new OfflineSaleLine(product, 1m, 10_000m, 0m, 1_900m)],
            fixture.DeviceId,
            [new OfflineSalePayment("Cash", 11_900m)]);
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

    private sealed class TamperingClient(IPosSaleUploadClient inner) : IPosSaleUploadClient
    {
        public Task<PosSaleUploadAttempt> UploadAsync(
            PosSaleUploadRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var line = request.Lines.Single();
            var changed = request with
            {
                Lines = [line with { Quantity = line.Quantity + 1 }]
            };
            return inner.UploadAsync(changed, idempotencyKey, cancellationToken);
        }
    }

    private sealed class FixedUploadClient(PosSaleUploadAttempt result) : IPosSaleUploadClient
    {
        public Task<PosSaleUploadAttempt> UploadAsync(
            PosSaleUploadRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}

