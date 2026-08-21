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

            DECLARE @Reasons TABLE(
                OperationType nvarchar(64),Code nvarchar(40),Name nvarchar(120),DisplayOrder int);
            INSERT @Reasons VALUES
              (N'StockCount',N'PHYSICAL_COUNT',N'Conteo físico programado',10),
              (N'StockCount',N'INVENTORY_VERIFICATION',N'Verificación de existencias',20),
              (N'InventoryAdjustment',N'MANUAL_ADJUSTMENT',N'Corrección de saldo',10),
              (N'InventoryAdjustment',N'INITIAL_BALANCE',N'Saldo inicial',20),
              (N'InventoryAdjustment',N'FOUND_SURPLUS',N'Sobrante identificado',30),
              (N'InventoryAdjustment',N'FOUND_SHORTAGE',N'Faltante identificado',40),
              (N'WarehouseTransfer',N'WAREHOUSE_TRANSFER',N'Reabastecimiento entre bodegas',10),
              (N'WarehouseTransfer',N'STOCK_REDISTRIBUTION',N'Redistribución de existencias',20),
              (N'ProductConversion',N'PRESENTATION_CHANGE',N'Cambio de presentación',10),
              (N'Damage',N'DAMAGE',N'Producto averiado',10),
              (N'Damage',N'EXPIRED',N'Producto vencido',20),
              (N'Damage',N'NOT_SALEABLE',N'Producto no vendible',30);

            INSERT dbo.InventoryReasons(
                InventoryReasonId,BusinessId,OperationType,Code,Name,
                IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
            SELECT NEWID(),@BusinessId,r.OperationType,r.Code,r.Name,
                   1,1,r.DisplayOrder,@Now,@Now
            FROM @Reasons r;

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
    }
}
