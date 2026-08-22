using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlBusinessDefaultsProvisioner(
    ApplicationDbContext db,
    IAuralyIdGenerator ids) : IBusinessDefaultsProvisioner
{
    public async Task ProvisionWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        string inventoryCostBasis,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var currentTransaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Business defaults must be provisioned in the business transaction.");
        var transaction = (SqlTransaction)currentTransaction.GetDbTransaction();
        await using var command = new SqlCommand("""
            IF NOT EXISTS (
                SELECT 1 FROM dbo.Businesses
                WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
                THROW 51041,'La sede no pertenece al tenant autenticado o no está activa.',1;

            IF EXISTS (SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId)
                THROW 51042,'La sede ya tiene bodegas configuradas.',1;

            INSERT dbo.Warehouses
              (WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
            VALUES
              (@SalesWarehouseId,@BusinessId,N'VEN',N'Bodega de venta',0,@CostBasis,0,1,1,1,1,@Now),
              (@OrdersWarehouseId,@BusinessId,N'PED',N'Bodega de pedidos',0,@CostBasis,1,0,0,0,1,@Now),
              (@DamagedWarehouseId,@BusinessId,N'AVE',N'Bodega de averías',0,@CostBasis,1,0,0,0,1,@Now);

            INSERT dbo.BusinessReasons(
                ReasonId,BusinessId,ReasonType,Code,Name,Direction,
                CounterpartAccountingCategory,DefaultCostCenterId,RequiresReference,
                IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
            SELECT NEWID(),@BusinessId,t.ReasonType,t.Code,t.Name,t.Direction,
                   t.CounterpartAccountingCategory,NULL,t.RequiresReference,
                   1,1,t.DisplayOrder,@Now,@Now
            FROM dbo.AccountingConfigurationProfiles p
            INNER JOIN dbo.ReasonTemplates t ON t.ProfileCode=p.ProfileCode
            WHERE p.IsDefault=1 AND p.IsActive=1 AND t.IsActive=1;

            INSERT dbo.ProductUnits(
                ProductUnitId,BusinessId,Code,Name,Symbol,
                AllowsFractionalQuantity,DecimalPlaces,IsActive,CreatedAt)
            VALUES
              (NEWID(),@BusinessId,N'EA',N'Unidad',N'und',0,0,1,@Now),
              (NEWID(),@BusinessId,N'KG',N'Kilogramo',N'kg',1,3,1,@Now),
              (NEWID(),@BusinessId,N'M',N'Metro',N'm',1,3,1,@Now),
              (NEWID(),@BusinessId,N'L',N'Litro',N'L',1,3,1,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SalesWarehouseId", ids.NewId());
        command.Parameters.AddWithValue("@OrdersWarehouseId", ids.NewId());
        command.Parameters.AddWithValue("@DamagedWarehouseId", ids.NewId());
        command.Parameters.AddWithValue("@CostBasis", inventoryCostBasis);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var accounting = new SqlCommand("dbo.AccountingDefaultsProvision", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        accounting.Parameters.AddWithValue("@TenantId", tenantId);
        accounting.Parameters.AddWithValue("@BusinessId", businessId);
        accounting.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await accounting.ExecuteNonQueryAsync(cancellationToken);
    }
}
