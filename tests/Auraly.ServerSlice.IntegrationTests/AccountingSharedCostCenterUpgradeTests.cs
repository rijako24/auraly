using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingSharedCostCenterUpgradeCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Shared cost-center deployment upgrade";
}

[Collection(AccountingSharedCostCenterUpgradeCollection.Name)]
public sealed class AccountingSharedCostCenterUpgradeTests(ServerSliceFixture fixture)
{
    // Reconstruct the deployed center keys and filtered index, while keeping
    // the real IDs, foreign references and posted journal untouched.
    private const string PreviousShapeSql = """
        UPDATE dbo.AccountingCostCenters SET LegacyBusinessId=@Business
          WHERE TenantId=@Tenant AND LegacyBusinessId IS NULL;
        -- Synthetic bootstrap tenants have global centers with no old origin.
        -- Construct their unrelated legacy scope only inside this disposable DB;
        -- the migration itself never selects an original site this way.
        INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
          SELECT NEWID(),center.TenantId,N'Sede del fixture histórico',N'',N'',N'',N'',N''
          FROM dbo.AccountingCostCenters center WHERE center.LegacyBusinessId IS NULL
            AND NOT EXISTS(SELECT 1 FROM dbo.Businesses business WHERE business.TenantId=center.TenantId)
          GROUP BY center.TenantId;
        UPDATE center SET LegacyBusinessId=business.BusinessId FROM dbo.AccountingCostCenters center
          CROSS APPLY(SELECT TOP(1) BusinessId FROM dbo.Businesses business
            WHERE business.TenantId=center.TenantId ORDER BY BusinessId) business WHERE center.LegacyBusinessId IS NULL;
        ALTER TABLE dbo.AccountingPostingJobs DROP CONSTRAINT FK_AccountingPostingJobs_ResolvedCostCenter;
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT FK_AccountingCostCenterAssignments_Center;
        ALTER TABLE payroll.Employments DROP CONSTRAINT FK_PayrollEmployments_Center;
        ALTER TABLE payroll.Novelties DROP CONSTRAINT FK_PayrollNovelties_CostCenter;
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT FK_AccountingCostCenters_Parent;
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT FK_AccountingCostCenters_Tenant;
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT FK_AccountingCostCenters_LegacyBusiness;
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT UQ_AccountingCostCenters_Tenant_Center;
        ALTER TABLE dbo.AccountingCostCenters DROP CONSTRAINT UQ_AccountingCostCenters_Tenant_Code;
        DROP INDEX UX_AccountingCostCenters_Tenant_Default ON dbo.AccountingCostCenters;
        ALTER TABLE dbo.AccountingCostCenters DROP COLUMN TenantId;
        EXEC sys.sp_rename N'dbo.AccountingCostCenters.LegacyBusinessId',N'BusinessId',N'COLUMN';
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT CK_AccountingCostCenterAssignments_NoWarehouse;
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT FK_AccountingCostCenterAssignments_LegacyWarehouse;
        ALTER TABLE dbo.AccountingCostCenterAssignments DROP CONSTRAINT UQ_AccountingCostCenterAssignments_Rule;
        EXEC sys.sp_rename N'dbo.AccountingCostCenterAssignments.LegacyWarehouseId',N'WarehouseId',N'COLUMN';
        EXEC sys.sp_executesql N'
          ALTER TABLE dbo.AccountingCostCenters ALTER COLUMN BusinessId uniqueidentifier NOT NULL;
          ALTER TABLE dbo.AccountingCostCenters ADD CONSTRAINT UQ_AccountingCostCenters_Business_Center UNIQUE(BusinessId,CostCenterId);
          ALTER TABLE dbo.AccountingCostCenters ADD CONSTRAINT UQ_AccountingCostCenters_Business_Code UNIQUE(BusinessId,Code);
          ALTER TABLE dbo.AccountingCostCenters ADD CONSTRAINT FK_AccountingCostCenters_Businesses FOREIGN KEY(BusinessId) REFERENCES dbo.Businesses(BusinessId);
          ALTER TABLE dbo.AccountingCostCenters ADD CONSTRAINT FK_AccountingCostCenters_Parent FOREIGN KEY(BusinessId,ParentCostCenterId) REFERENCES dbo.AccountingCostCenters(BusinessId,CostCenterId);
          CREATE UNIQUE INDEX UX_AccountingCostCenters_Business_Default ON dbo.AccountingCostCenters(BusinessId) WHERE IsDefault=1 AND IsActive=1;
          ALTER TABLE dbo.AccountingPostingJobs ADD CONSTRAINT FK_AccountingPostingJobs_ResolvedCostCenter FOREIGN KEY(BusinessId,ResolvedCostCenterId) REFERENCES dbo.AccountingCostCenters(BusinessId,CostCenterId);
          ALTER TABLE dbo.AccountingCostCenterAssignments ADD CONSTRAINT FK_AccountingCostCenterAssignments_Center FOREIGN KEY(BusinessId,CostCenterId) REFERENCES dbo.AccountingCostCenters(BusinessId,CostCenterId);
          ALTER TABLE dbo.AccountingCostCenterAssignments ADD CONSTRAINT FK_AccountingCostCenterAssignments_Warehouse FOREIGN KEY(BusinessId,WarehouseId) REFERENCES dbo.Warehouses(BusinessId,WarehouseId);';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.AccountingCostCenterAssignments ADD CONSTRAINT UQ_AccountingCostCenterAssignments_Rule UNIQUE(BusinessId,OperationKind,WarehouseId);';
        DELETE dbo.__RefactorLog WHERE OperationKey IN
          ('3f404f29-480b-43bd-ab93-dfb7c2ad75fa','22833c5d-9257-4960-8306-b459aa73ab06');
        """;

    [Theory]
    [InlineData("DuplicateCode")]
    [InlineData("MultipleDefaults")]
    [InlineData("Warehouse")]
    public async Task Unreviewed_configuration_blocks_the_cut_without_changing_the_catalog(string conflict)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var shape = new SqlCommand(PreviousShapeSql, connection, transaction);
            shape.Parameters.AddWithValue("@Business", fixture.BusinessId); shape.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            await shape.ExecuteNonQueryAsync();
            await using var setup = new SqlCommand("""
                DECLARE @Center uniqueidentifier=(SELECT TOP(1) CostCenterId FROM dbo.AccountingCostCenters WHERE BusinessId=@Business AND IsDefault=1),
                  @Code nvarchar(32)=(SELECT TOP(1) Code FROM dbo.AccountingCostCenters WHERE BusinessId=@Business AND IsDefault=1);
                IF @Conflict=N'Warehouse'
                  INSERT dbo.AccountingCostCenterAssignments(AssignmentId,TenantId,BusinessId,CostCenterId,OperationKind,WarehouseId,IsActive,CreatedAt,UpdatedAt)
                    VALUES(NEWID(),@Tenant,@Business,@Center,N'Inventory',@Warehouse,1,SYSUTCDATETIME(),SYSUTCDATETIME());
                ELSE
                BEGIN
                  DECLARE @OtherBusiness uniqueidentifier=NEWID();
                  INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                    VALUES(@OtherBusiness,@Tenant,N'Conflicto sin revisar',N'',N'',N'',N'',N'');
                  INSERT dbo.AccountingCostCenters(CostCenterId,BusinessId,Code,Name,IsDefault,IsActive,CreatedAt)
                    VALUES(NEWID(),@OtherBusiness,CASE WHEN @Conflict=N'DuplicateCode' THEN @Code ELSE N'OTHER-DEFAULT' END,
                      N'Configuración propia sin revisar',1,1,SYSUTCDATETIME());
                END;
                """, connection, transaction);
            setup.Parameters.AddWithValue("@Business", fixture.BusinessId); setup.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            setup.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId); setup.Parameters.AddWithValue("@Conflict", conflict);
            await setup.ExecuteNonQueryAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                "20261113_ShareAccountingCostCenters.sql", connection, transaction,
                sqlcmdVariables: new Dictionary<string, string> { ["DeploymentEnvironment"] = "dev" }));
            Assert.Equal(51769, error.Number);
        }
        finally { if (transaction.Connection is not null) await transaction.RollbackAsync(); }
        // XACT_ABORT and the outer test transaction restore the previous schema
        // as well as all setup data; no deployment or application fallback runs.
        await using var unchanged = new SqlCommand("SELECT COL_LENGTH(N'dbo.AccountingCostCenters',N'BusinessId'),COL_LENGTH(N'dbo.AccountingCostCenters',N'TenantId');", connection);
        await using var reader = await unchanged.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0)); Assert.Equal((short)16, reader.GetInt16(1));
    }

    [Fact]
    public async Task Used_center_and_reviewed_dev_catalog_upgrade_preserve_journals_and_repeated_cut_has_no_new_effects()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var center = Assert.Single((await client.GetFromJsonAsync<AccountingCostCenterPage>(
            "/api/commerce/v1/accounting/cost-centers?isDefault=true"))!.Items);
        var account = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!
            .First(row => row.IsActive && row.AllowsPosting && !row.RequiresParty && row.Semantics?.FinancialFunction is null or "General");
        var voucher = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "AJUSTE", "Centro histórico utilizado",
            [new(account.AccountId, fixture.SupplierPartyId, center.CostCenterId, "Débito histórico", 123.4567m, 0),
             new(account.AccountId, null, center.CostCenterId, "Crédito histórico", 0, 123.4567m)]);
        using (var accepted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", voucher)) accepted.EnsureSuccessStatusCode();
        using (var rename = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/cost-centers/{center.CostCenterId}",
            new UpdateCostCenterRequest("BEFORE-CUT", "Maestro posterior al asiento", null, true, true, center.RowVersion))) rename.EnsureSuccessStatusCode();
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        // Legacy journals may lack a party display snapshot even though their
        // center snapshot is complete. Reapplying the old metadata migration
        // must never replace that frozen center with the current master label.
        await using (var legacyParty = new SqlCommand("""
            UPDATE dbo.Parties SET DisplayName=NULL WHERE PartyId=@Party;
            UPDATE line SET PartyNameSnapshot=NULL FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
            WHERE entry.SourceDocumentId=@Document AND line.PartyId=@Party;
            """, connection))
        {
            legacyParty.Parameters.AddWithValue("@Party", fixture.SupplierPartyId);
            legacyParty.Parameters.AddWithValue("@Document", voucher.VoucherId);
            await legacyParty.ExecuteNonQueryAsync();
        }
        await using var historical = new SqlCommand("""
            SELECT (SELECT * FROM dbo.AccountingEntries WHERE SourceDocumentId=@Document FOR JSON PATH),
              (SELECT line.* FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
               WHERE entry.SourceDocumentId=@Document ORDER BY line.LineNumber FOR JSON PATH),
              (SELECT * FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Document FOR JSON PATH),
              (SELECT * FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Document FOR JSON PATH);
            """, connection);
        historical.Parameters.AddWithValue("@Document", voucher.VoucherId);
        async Task<string[]> ReadHistoryAsync()
        {
            await using var reader = await historical.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            return Enumerable.Range(0, 4).Select(reader.GetString).ToArray();
        }
        var before = await ReadHistoryAsync(); Assert.Contains(center.CostCenterId.ToString(), before[1], StringComparison.OrdinalIgnoreCase);
        async Task AssertHistoryUnchangedAsync()
        {
            var after = await ReadHistoryAsync();
            // DACPAC may reorder physical columns without changing any value.
            for (var index = 0; index < before.Length; index++)
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before[index]), JsonNode.Parse(after[index])),
                    $"Historical snapshot {index} changed. Expected {before[index]}; actual {after[index]}.");
        }

        await using (var old = new SqlCommand(PreviousShapeSql, connection))
        {
            old.Parameters.AddWithValue("@Business", fixture.BusinessId); old.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            await old.ExecuteNonQueryAsync();
        }
        await using (var dev = new SqlCommand("""
            DECLARE @Tenant uniqueidentifier='a0a10000-0000-0000-0000-000000000000',@Account uniqueidentifier;
            IF NOT EXISTS(SELECT 1 FROM dbo.Tenants WHERE TenantId=@Tenant)
              INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@Tenant,N'DEV revisado',N'cost-center-upgrade@example.test');
            DECLARE @Centers TABLE(Id uniqueidentifier,BusinessId uniqueidentifier);
            INSERT @Centers VALUES
              ('098314c8-6dd6-48c4-bd5f-fa7a735020dc','a0a10000-0000-0000-0000-000000000001'),
              ('5233cf30-5f28-40ac-8181-1fc50f215976','b24a3ab4-a226-4dab-bc0c-4d634e34c3fb'),
              ('6c83cbbb-e811-4a6b-accf-19cb73ab88a0','587de932-8c0f-4c99-9f5c-da651a2f3fd4'),
              ('bb3a84fd-178a-4dd0-9e4b-4663d5d60e13','2b8de478-3bce-4f28-b66d-3e53e5dc2c36'),
              ('44bfed0f-9e7e-45ba-989f-a31ea8776fe2','969d7ff9-8464-4337-b927-8f520978af33');
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
              SELECT BusinessId,@Tenant,CONVERT(nvarchar(36),BusinessId),N'',N'',N'',N'',N'' FROM @Centers planned
              WHERE NOT EXISTS(SELECT 1 FROM dbo.Businesses existing WHERE existing.BusinessId=planned.BusinessId);
            -- The empty bootstrap tenant may already have a general center.
            -- Keep that identity and all its references as a separate fixture row.
            UPDATE dbo.AccountingCostCenters SET IsDefault=0,Code=N'FIXTURE-BASELINE'
              WHERE BusinessId='a0a10000-0000-0000-0000-000000000001' AND IsDefault=1
                AND CostCenterId<>'098314c8-6dd6-48c4-bd5f-fa7a735020dc';
            INSERT dbo.AccountingCostCenters(CostCenterId,BusinessId,Code,Name,IsDefault,IsActive,CreatedAt)
              SELECT Id,BusinessId,N'PRINCIPAL',N'Operación principal',1,1,SYSUTCDATETIME() FROM @Centers;
            SELECT TOP(1) @Account=AccountId FROM dbo.AccountingAccounts WHERE TenantId=@Tenant
              AND AccountType=N'Expense' AND AllowsPosting=1 AND IsActive=1 ORDER BY Code;
            IF @Account IS NULL
            BEGIN
              SET @Account=NEWID();
              INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                VALUES(@Account,@Tenant,N'519595',N'Cuenta para referencias históricas',N'Expense',1,0,1,SYSUTCDATETIME());
            END;
            INSERT dbo.ExpenseConcepts(ExpenseConceptId,BusinessId,Code,Name,ExpenseAccountId,DefaultCostCenterId,IsActive,CreatedAt,UpdatedAt)
              SELECT NEWID(),BusinessId,N'USED',N'Referencia del centro',@Account,Id,1,SYSUTCDATETIME(),SYSUTCDATETIME() FROM @Centers;
            """, connection)) await dev.ExecuteNonQueryAsync();
        async Task CutAsync() => await fixture.ApplyMigrationAsync("20261113_ShareAccountingCostCenters.sql", connection,
            sqlcmdVariables: new Dictionary<string, string> { ["DeploymentEnvironment"] = "dev" });
        await CutAsync();
        await using var state = new SqlCommand("""
            SELECT (SELECT CostCenterId,TenantId,BusinessId,Code,Name,IsDefault,IsActive,RowVersion FROM dbo.AccountingCostCenters ORDER BY CostCenterId FOR JSON PATH),
              (SELECT * FROM dbo.AccountingCostCenterAssignments ORDER BY AssignmentId FOR JSON PATH);
            """, connection);
        async Task<string[]> ReadCutAsync()
        {
            await using var reader = await state.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            return [reader.GetString(0), reader.GetString(1)];
        }
        var firstCut = await ReadCutAsync();
        await CutAsync(); Assert.Equal(firstCut, await ReadCutAsync());
        await fixture.ReapplySchemaAsync();
        await AssertHistoryUnchangedAsync();
        await CutAsync(); // New schema: the versioned pre-cut safely does nothing.
        await AssertHistoryUnchangedAsync();
        await using var verify = new SqlCommand("""
            SELECT COUNT(*),COUNT(DISTINCT Code),SUM(CONVERT(int,IsDefault)) FROM dbo.AccountingCostCenters
              WHERE TenantId='a0a10000-0000-0000-0000-000000000000' AND Code IN
                (N'PRINCIPAL',N'PRINCIPAL-BAR',N'PRINCIPAL-RESTBAR',N'PRINCIPAL-TIENDA',N'PRINCIPAL-TIENDA-DEV');
            SELECT COUNT(*) FROM dbo.ExpenseConcepts concept JOIN dbo.AccountingCostCenters center ON center.CostCenterId=concept.DefaultCostCenterId
              WHERE center.TenantId='a0a10000-0000-0000-0000-000000000000' AND concept.Code=N'USED';
            SELECT COUNT(*) FROM dbo.AccountingCostCenterAssignments WHERE TenantId='a0a10000-0000-0000-0000-000000000000' AND OperationKind=N'All';
            """, connection);
        await using (var result = await verify.ExecuteReaderAsync())
        {
            Assert.True(await result.ReadAsync()); Assert.Equal(5, result.GetInt32(0)); Assert.Equal(5, result.GetInt32(1)); Assert.Equal(1, result.GetInt32(2));
            await result.NextResultAsync(); Assert.True(await result.ReadAsync()); Assert.Equal(5, result.GetInt32(0));
            await result.NextResultAsync(); Assert.True(await result.ReadAsync()); Assert.Equal(5, result.GetInt32(0));
        }
        using var replay = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", voucher);
        replay.EnsureSuccessStatusCode();
        await AssertHistoryUnchangedAsync();
    }
}
