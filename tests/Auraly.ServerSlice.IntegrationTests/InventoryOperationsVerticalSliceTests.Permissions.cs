using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class InventoryOperationsVerticalSliceTests
{
    [Theory]
    [InlineData("inventory.counts.confirm", "StockCount")]
    [InlineData("inventory.physical-counts.capture", "StockCount")]
    [InlineData("inventory.physical-counts.manage", "StockCount")]
    [InlineData("inventory.adjustments.confirm", "InventoryAdjustment")]
    [InlineData("inventory.transfers.dispatch", "WarehouseTransfer")]
    [InlineData("inventory.transfers.receive", "WarehouseTransfer")]
    [InlineData("inventory.transfers.resolve-difference", "WarehouseTransfer")]
    [InlineData("inventory.conversions.confirm", "ProductConversion")]
    [InlineData("inventory.production.confirm", "Production")]
    [InlineData("inventory.damages.confirm", "Damage")]
    [InlineData("purchasing.goods-receipts.read", "GoodsReceipt")]
    public void Operation_visibility_uses_only_its_existing_capability(string permission, string type)
    {
        Assert.Empty(InventoryOperationAccess.ReadableDocumentTypes(new HashSet<string> { InventoryPermissionCodes.Read }));
        Assert.Equal(type, Assert.Single(InventoryOperationAccess.ReadableDocumentTypes(
            new HashSet<string> { InventoryPermissionCodes.Read, permission })));
    }

    [Fact]
    public async Task Inventory_reader_keeps_stock_and_ledger_but_cannot_read_operations_or_count_drafts()
    {
        using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Read);
        using var balances = await client.GetAsync("/api/commerce/v1/inventory/balances?page=1&pageSize=1");
        balances.EnsureSuccessStatusCode();
        using var movements = await client.GetAsync("/api/commerce/v1/inventory/movements?page=1&pageSize=1");
        movements.EnsureSuccessStatusCode();
        var operations = await client.GetFromJsonAsync<InventoryOperationPage>("/api/commerce/v1/inventory/operations?page=1&pageSize=20");
        Assert.Empty(operations!.Items);
        Assert.Equal(0, operations.TotalCount);
        foreach (var path in new[] {
            "/api/commerce/v1/inventory/physical-counts?page=1&pageSize=20",
            "/api/commerce/v1/inventory/physical-count-drafts?page=1&pageSize=20",
            $"/api/commerce/v1/inventory/conversion-products?warehouseId={fixture.WarehouseId:D}&page=1&pageSize=20",
            $"/api/commerce/v1/inventory/production-products?warehouseId={fixture.WarehouseId:D}&page=1&pageSize=20",
            $"/api/commerce/v1/inventory/operations/{Guid.NewGuid():D}",
            "/api/commerce/v1/inventory/operations?documentType=ProductConversion&page=1&pageSize=20" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("permiso", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Operation_permissions_filter_count_and_pages_before_loading_details_in_one_command()
    {
        var marker = $"QA-PERM-{Guid.NewGuid():N}";
        var documents = Enumerable.Range(0, 2000).Select(index => new {
            Id = Guid.NewGuid(), Type = index < 1000 ? "InventoryAdjustment" : "ProductConversion",
            At = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddSeconds(index)
        }).ToArray();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using (var seed = new SqlCommand("""
            INSERT dbo.InventoryOperations(InventoryOperationId,BusinessId,DocumentType,WarehouseId,
                OccurredAt,ReasonCode,ReasonDescription,Status,CreatedAt)
            SELECT Id,@Business,Type,@Warehouse,[At],N'INITIAL_BALANCE',@Marker,N'Draft',[At]
            FROM OPENJSON(@Documents) WITH(Id uniqueidentifier '$.Id',Type nvarchar(64) '$.Type',[At] datetimeoffset '$.At');
            INSERT dbo.InventoryOperationLines(InventoryOperationId,LineNumber,Direction,ProductId,
                ProductCodeSnapshot,DescriptionSnapshot,Quantity)
            SELECT operation.InventoryOperationId,number.LineNumber,
                CASE WHEN operation.DocumentType=N'InventoryAdjustment' THEN N'ADJUSTMENT' ELSE N'INPUT' END,
                @Product,N'QA-PERM',@Marker,1
            FROM dbo.InventoryOperations operation
            CROSS JOIN (VALUES(1),(2),(3),(4),(5)) number(LineNumber)
            WHERE operation.BusinessId=@Business AND operation.ReasonDescription=@Marker;
            """, connection))
        {
            seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
            seed.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@Product", fixture.ProductId);
            seed.Parameters.AddWithValue("@Marker", marker);
            seed.Parameters.AddWithValue("@Documents", JsonSerializer.Serialize(documents));
            await seed.ExecuteNonQueryAsync();
        }
        try
        {
            using var client = fixture.CreateAdminClient(InventoryPermissionCodes.Read, InventoryPermissionCodes.Adjust);
            var allIds = new HashSet<Guid>();
            var measurements = new List<double>();
            for (var sample = 0; sample < 10; sample++)
            foreach (var page in new[] { 1, 2 })
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText == "dbo.InventoryOperationsSearch");
                var watch = Stopwatch.StartNew();
                var result = await client.GetFromJsonAsync<InventoryOperationPage>(
                    $"/api/commerce/v1/inventory/operations?search={marker}&page={page}&pageSize=50");
                watch.Stop();
                Assert.Equal(1, counter.Count);
                Assert.Equal(1000, result!.TotalCount);
                Assert.Equal(20, result.TotalPages);
                Assert.Equal(50, result.Items.Count);
                Assert.All(result.Items, item => { Assert.Equal("InventoryAdjustment", item.DocumentType); Assert.Equal(5, item.LineCount); });
                if (sample == 0) foreach (var item in result.Items) Assert.True(allIds.Add(item.DocumentId));
                else measurements.Add(watch.Elapsed.TotalMilliseconds);
            }
            var p95 = measurements.Order().ElementAt((int)Math.Ceiling(measurements.Count * .95) - 1);
            Assert.True(p95 < 1000, $"Inventory operation history p95 {p95:F2} ms exceeds 1000 ms.");
            var visibleId = documents.First(item => item.Type == "InventoryAdjustment").Id;
            var hiddenId = documents.First(item => item.Type == "ProductConversion").Id;
            var detail = await client.GetFromJsonAsync<InventoryOperationDetail>($"/api/commerce/v1/inventory/operations/{visibleId:D}");
            Assert.Equal(5, detail!.Lines.Count);
            using var hidden = await client.GetAsync($"/api/commerce/v1/inventory/operations/{hiddenId:D}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE line FROM dbo.InventoryOperationLines line JOIN dbo.InventoryOperations operation
                    ON operation.InventoryOperationId=line.InventoryOperationId
                    WHERE operation.BusinessId=@Business AND operation.ReasonDescription=@Marker;
                DELETE dbo.InventoryOperations WHERE BusinessId=@Business AND ReasonDescription=@Marker;
                """, connection);
            cleanup.Parameters.AddWithValue("@Business", fixture.BusinessId);
            cleanup.Parameters.AddWithValue("@Marker", marker);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Repeating_inventory_permission_seed_preserves_a_custom_role_with_revoked_operation_access()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var roleId = Guid.NewGuid();
        try
        {
            await using var seed = new SqlCommand("""
                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,IsActive,CreatedAt)
                VALUES(@Role,@Tenant,@Name,@Name,1,SYSUTCDATETIME());
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@Role,PermissionId,SYSUTCDATETIME() FROM dbo.Permissions WHERE Resource=N'inventory.read';
                """, connection, transaction);
            seed.Parameters.AddWithValue("@Role", roleId);
            seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            seed.Parameters.AddWithValue("@Name", $"QA-READER-{roleId:N}");
            await seed.ExecuteNonQueryAsync();
            var script = await File.ReadAllTextAsync(Path.Combine(ServerSliceFixture.FindRepositoryRoot(),
                "database", "Auraly.Database", "Scripts", "Seeds", "SeedInventoryPermissions.sql"));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var apply = new SqlCommand(script, connection, transaction);
                await apply.ExecuteNonQueryAsync();
            }
            await using var read = new SqlCommand("""
                SELECT permission.Resource FROM dbo.RolePermissions assignment
                JOIN dbo.Permissions permission ON permission.PermissionId=assignment.PermissionId WHERE assignment.RoleId=@Role;
                """, connection, transaction);
            read.Parameters.AddWithValue("@Role", roleId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("inventory.read", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }
        finally { if (transaction.Connection is not null) await transaction.RollbackAsync(); }
    }
}
