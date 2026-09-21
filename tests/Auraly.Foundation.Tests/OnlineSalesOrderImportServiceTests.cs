using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class OnlineSalesOrderImportServiceTests
{
    [Fact]
    public async Task Import_preserves_authoritative_order_amounts_without_recalculating_them()
    {
        var store = new CapturingStore();
        var service = new OnlineSalesOrderImportService(store);
        var user = new OnlineSalesUserIdentity(
            Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { CommercePermissionCodes.SalesCreate });
        var request = new ImportOnlineSalesOrderRequest(
            Guid.NewGuid(), "PED-PRUEBA", null, "COP",
            [new OnlineSalesOrderImportLine(
                Guid.NewGuid(), "YUCA", "Yuca seleccionada", "KGM",
                0.96m, 4549.71m, 0m, 4358.62m, "Order", 3000m, "01", 0m)],
            1);

        await service.ImportAsync(user, Guid.NewGuid(), request, "order-import");

        Assert.Same(request, store.Request);
    }

    private sealed class CapturingStore : IOnlineSalesOrderImportStore
    {
        public ImportOnlineSalesOrderRequest? Request { get; private set; }

        public Task<OnlineSalesDraft> ImportOrderAsync(
            OnlineSalesUserIdentity user,
            Guid draftId,
            ImportOnlineSalesOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult<OnlineSalesDraft>(null!);
        }
    }
}
