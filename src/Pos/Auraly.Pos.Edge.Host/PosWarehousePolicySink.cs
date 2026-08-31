using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosWarehousePolicySink(
    PosEdgeRuntimeContext runtime,
    PosEdgeEnrollmentStore enrollmentStore,
    PosSynchronizationEventLog events,
    ILogger<PosWarehousePolicySink> logger) : IPosWarehousePolicySink
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task ApplyAsync(
        bool allowsNegativeStock,
        CancellationToken cancellationToken = default)
    {
        if (runtime.WarehouseAllowsNegativeStock == allowsNegativeStock) return;

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (runtime.WarehouseAllowsNegativeStock == allowsNegativeStock) return;
            var enrollment = enrollmentStore.Load()
                ?? throw new InvalidOperationException(
                    "The enrolled warehouse policy cannot be persisted without its enrollment package.");
            enrollmentStore.Save(enrollment with
            {
                WarehouseAllowsNegativeStock = allowsNegativeStock
            });
            runtime.ApplyWarehousePolicy(allowsNegativeStock);
            events.Record(
                "Success",
                "Bodega",
                "Regla de inventario actualizada",
                allowsNegativeStock
                    ? "La bodega permite vender con existencias negativas."
                    : "La bodega exige existencias disponibles para vender.");
            logger.LogInformation(
                "Warehouse negative-stock policy updated locally to {AllowsNegativeStock}.",
                allowsNegativeStock);
        }
        finally
        {
            gate.Release();
        }
    }
}
