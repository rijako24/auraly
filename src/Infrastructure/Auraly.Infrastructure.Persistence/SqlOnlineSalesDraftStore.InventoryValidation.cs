using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesInventoryValidation> ValidateInventoryAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var validation = await ValidateInventoryAsync(
            connection, transaction, state, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return validation;
    }

    private static async Task<OnlineSalesInventoryValidation> ValidateInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        if (state.WarehouseAllowsNegativeStock)
            return new OnlineSalesInventoryValidation(true, true, []);
        var demand = await ReadDraftInventoryDemandsAsync(connection,transaction,state,draftId,null,null,0m,cancellationToken);
        var issues = InventoryDemandResolver.AllocateWholeLines(demand.Lines,demand.Available)
            .Where(allocation => !allocation.CanReserve)
            .GroupBy(allocation => allocation.Line.LineId)
            .Select(group =>
            {
                var line = group.First().Line;
                var detail = demand.Details[line.LineId];
                return new OnlineSalesInventoryIssue(line.LineId,line.ProductId,detail.Code,detail.Description,
                    line.Quantity,Math.Max(0m,decimal.Round(group.Min(allocation =>
                        InventoryDemandResolver.InProductUnits(allocation.AvailableInventoryQuantity,
                            allocation.Line.InventoryFactor)),6)));
            }).ToArray();
        return new OnlineSalesInventoryValidation(issues.Length == 0,true,issues);
    }

    private sealed record DraftInventoryDemands(
        IReadOnlyList<InventoryDemandLine> Lines,
        IReadOnlyDictionary<Guid,(string Code,string Description)> Details,
        IReadOnlyDictionary<Guid,decimal> Available);

    private static async Task<DraftInventoryDemands> ReadDraftInventoryDemandsAsync(
        SqlConnection connection,SqlTransaction transaction,DraftState state,Guid draftId,
        Guid? selectedProductId,Guid? excludedLineId,decimal selectedQuantity,CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @TenantId uniqueidentifier=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
            WITH candidates AS
            (
                SELECT line.SalesDraftLineId LineId,line.ProductId,line.ProductCode,line.Description,line.Quantity,line.Position
                FROM dbo.SalesDraftLines line WITH(UPDLOCK,HOLDLOCK)
                WHERE line.SalesDraftId=@DraftId AND (@ExcludedLineId IS NULL OR line.SalesDraftLineId<>@ExcludedLineId)
                UNION ALL
                SELECT CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000'),product.ProductId,
                    COALESCE(product.ProductCode,product.Sku,N''),product.Name,@SelectedQuantity,2147483647
                FROM dbo.Products product WHERE product.TenantId=@TenantId AND product.ProductId=@SelectedProductId
            )
            SELECT candidate.LineId,candidate.ProductId,candidate.ProductCode,candidate.Description,candidate.Quantity,
                   consumption.InventoryFactor,consumption.InventoryProductId,consumption.ManageStock,
                   COALESCE(balance.QuantityOnHand,0)
            FROM candidates candidate
            OUTER APPLY inventory.CurrentProductConsumption(@TenantId,candidate.ProductId) consumption
            LEFT JOIN dbo.InventoryBalances balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
             AND balance.ProductId=consumption.InventoryProductId
            -- An order already reserved its managed sale products in PED. Recipe components were not reserved.
            WHERE @SourceOrderId IS NULL OR consumption.IsComponent=1 OR consumption.InventoryProductId IS NULL
            ORDER BY candidate.Position,candidate.LineId,consumption.ComponentLineNumber;
            """,connection,transaction);
        command.Parameters.AddRange([
            P("@BusinessId",state.BusinessId),P("@WarehouseId",state.WarehouseId),P("@DraftId",draftId),
            P("@SelectedProductId",selectedProductId),P("@ExcludedLineId",excludedLineId),
            P("@SelectedQuantity",selectedQuantity),P("@SourceOrderId",state.SourceOrderId)
        ]);
        var lines = new List<InventoryDemandLine>();
        var details = new Dictionary<Guid,(string,string)>();
        var available = new Dictionary<Guid,decimal>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(6))
                throw new OnlineSalesDraftValidationException("La composición del producto no está disponible para validar inventario.");
            var lineId = reader.GetGuid(0);var target = reader.GetGuid(6);
            lines.Add(new(lineId,reader.GetGuid(1),target,reader.GetDecimal(5),reader.GetDecimal(4),reader.GetBoolean(7)));
            details[lineId]=(reader.GetString(2),reader.GetString(3));
            available.TryAdd(target,reader.GetDecimal(8));
        }
        return new(lines,details,available);
    }
}
