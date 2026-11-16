using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class MegafruverPaymentCutoverUpgradeTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData("Valid")]
    [InlineData("CustomForm")]
    [InlineData("ChangedAccount")]
    public async Task Reviewed_payment_cutover_preserves_identity_custom_forms_and_idempotency(string scenario)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var business = Guid.NewGuid();
        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql,connection,transaction);
            command.Parameters.AddWithValue("@Business",business);
            command.Parameters.AddWithValue("@Custom",scenario == "CustomForm");
            command.Parameters.AddWithValue("@Actor",fixture.UserId);
            await command.ExecuteNonQueryAsync();
        }
        async Task<byte[]> Fingerprint(string sql)
        {
            await using var command = new SqlCommand(sql,connection,transaction);
            return (byte[])(await command.ExecuteScalarAsync())!;
        }
        var variables = new Dictionary<string,string> { ["DeploymentEnvironment"] = "prod" };
        try
        {
            await Execute("""
                DECLARE @Tenant uniqueidentifier='01a06318-d4bf-7f57-9776-4695b7833f7a',@Now datetimeoffset(7)=SYSUTCDATETIME();
                INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@Tenant,N'Migración revisada QA',N'migration@example.test');
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                  VALUES(@Business,@Tenant,N'Sede de migración QA',N'',N'',N'',N'',N'');
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                VALUES
                  ('bf06746d-1969-4778-8a75-babbe778a824',@Tenant,N'110505',N'Caja general',N'Asset',1,0,1,@Now),
                  ('de383cad-a363-48e5-b190-3dd768362ca8',@Tenant,N'13809501',N'Tarjetas débito por conciliar',N'Asset',1,0,1,@Now),
                  ('b652ffc3-d8f9-47f5-b085-ba0ae2ce5ab5',@Tenant,N'13809502',N'Tarjetas crédito por conciliar',N'Asset',1,0,1,@Now),
                  ('953b5664-986b-40e9-8517-50a180d7540d',@Tenant,N'240810',N'IVA descontable',N'Asset',1,0,1,@Now),
                  ('d3c0a3bc-b8b7-428f-9502-3bbecd1407a8',@Tenant,N'111005',N'Bancos nacionales',N'Asset',1,0,1,@Now);
                EXEC dbo.AccountingDefaultsProvision @Tenant,@Business,@Now;
                DELETE reference.PaymentMethods WHERE TenantId=@Tenant;
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,
                  CreatedAt,FinancialFunction,BalanceSide)
                VALUES('a3bfed17-f9f8-48ab-aeb6-264fd49f9481',@Tenant,N'240811',N'IVA descontable en compras',N'Liability',1,0,1,@Now,N'General',N'Debit');
                INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                VALUES('7a23e29c-cbe3-4c45-89a6-63b73069957d',@Tenant,NULL,N'InputVat',
                  'a3bfed17-f9f8-48ab-aeb6-264fd49f9481','20260101',NULL,@Now);
                UPDATE dbo.AccountingAccountMappings SET MappingId='89b511d4-22af-44f9-ba30-3e3543fce827',EffectiveFrom='20260101'
                  WHERE TenantId=@Tenant AND Category=N'Bank' AND BusinessId IS NULL;
                DECLARE @BankType uniqueidentifier=(SELECT OptionId FROM reference.Options
                  WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking');
                INSERT accounting.BankAccounts(BankAccountId,TenantId,AccountingAccountId,AccountTypeOptionId,
                  BankName,AccountNumber,DisplayName,CurrencyCode,IsPrimary,IsActive,CreatedByUserId,UpdatedByUserId,CreatedAt,UpdatedAt)
                VALUES
                  ('182b6502-5d58-4746-bdea-cc010e7e2296',@Tenant,'d3c0a3bc-b8b7-428f-9502-3bbecd1407a8',@BankType,
                    N'Banco QA A',N'QA-A',N'Principal QA',N'COP',1,1,@Actor,@Actor,@Now,@Now),
                  ('2f2f8b6b-d33c-4dfb-a991-238898d5b3c3',@Tenant,'d3c0a3bc-b8b7-428f-9502-3bbecd1407a8',@BankType,
                    N'Banco QA B',N'QA-B',N'Secundario QA',N'COP',0,1,@Actor,@Actor,@Now,@Now);
                IF @Custom=1
                BEGIN
                  DECLARE @Own uniqueidentifier=NEWID();
                  INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,
                    CreatedAt,FinancialFunction,BalanceSide)
                  VALUES(@Own,@Tenant,N'11059999',N'Caja elegida por cliente',N'Asset',1,0,1,@Now,N'Cash',N'Debit');
                  INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,
                    UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
                  SELECT @Tenant,NEWID(),N'OWN-CASH',N'Mi caja',OptionId,@Own,1,1,1,1,1,1,5,1,@Now,@Now
                    FROM reference.Options WHERE CatalogCode=N'payment-method' AND Code=N'Cash';
                END;
                """);
            const string historicalAccounts = """
                SELECT HASHBYTES('SHA2_256',(SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty
                  FROM dbo.AccountingAccounts WHERE AccountId IN(
                    'bf06746d-1969-4778-8a75-babbe778a824','de383cad-a363-48e5-b190-3dd768362ca8',
                    'b652ffc3-d8f9-47f5-b085-ba0ae2ce5ab5','953b5664-986b-40e9-8517-50a180d7540d')
                  ORDER BY AccountId FOR JSON PATH));
                """;
            var before = await Fingerprint(historicalAccounts);
            await fixture.ApplyMigrationAsync("20261023_BankAuxiliariesAndAcceptedAccounts.sql",connection,transaction);
            if (scenario == "ChangedAccount")
            {
                await Execute("UPDATE dbo.AccountingAccounts SET Name=N'Configuración propia' WHERE AccountId='bf06746d-1969-4778-8a75-babbe778a824';");
                var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                    "20261116_CompleteReviewedMegafruverPaymentCatalog.sql",connection,transaction,sqlcmdVariables:variables));
                Assert.Equal(51983,error.Number);
                return;
            }
            await fixture.ApplyMigrationAsync("20261116_CompleteReviewedMegafruverPaymentCatalog.sql",connection,transaction,sqlcmdVariables:variables);
            Assert.Equal(before,await Fingerprint(historicalAccounts));
            const string migratedRows = """
                SELECT HASHBYTES('SHA2_256',CONCAT(
                  (SELECT * FROM reference.PaymentMethods WHERE TenantId='01a06318-d4bf-7f57-9776-4695b7833f7a' ORDER BY PaymentMethodId FOR JSON PATH),
                  (SELECT * FROM dbo.AccountingAccounts WHERE TenantId='01a06318-d4bf-7f57-9776-4695b7833f7a' ORDER BY AccountId FOR JSON PATH),
                  (SELECT * FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId IN(
                    SELECT BusinessId FROM dbo.Businesses WHERE TenantId='01a06318-d4bf-7f57-9776-4695b7833f7a')
                    ORDER BY NotificationId FOR JSON PATH)));
                """;
            var first = await Fingerprint(migratedRows);
            await fixture.ApplyMigrationAsync("20261116_CompleteReviewedMegafruverPaymentCatalog.sql",connection,transaction,sqlcmdVariables:variables);
            Assert.Equal(first,await Fingerprint(migratedRows));
            await using var verify = new SqlCommand("""
                SELECT MethodCode,AccountCode,BankAccountId,Code,IsReady FROM reference.PaymentMethodDetails
                  WHERE TenantId='01a06318-d4bf-7f57-9776-4695b7833f7a' ORDER BY MethodCode,Code;
                SELECT COUNT(*) FROM dbo.AccountingAccountMappings mapping
                  CROSS APPLY accounting.BankPostingAccount(mapping.TenantId,'182b6502-5d58-4746-bdea-cc010e7e2296') bank
                  WHERE mapping.MappingId='89b511d4-22af-44f9-ba30-3e3543fce827' AND mapping.AccountId=bank.AccountId;
                """,connection,transaction);
            await using var reader = await verify.ExecuteReaderAsync();
            var count = 0; var transfers = new HashSet<Guid>();
            while (await reader.ReadAsync())
            {
                count++; Assert.True(reader.GetBoolean(4));
                if (reader.GetString(0) == "Cash") Assert.Equal(scenario == "CustomForm" ? "OWN-CASH" : "Cash-110505",reader.GetString(3));
                if (reader.GetString(0) == "Transfer") transfers.Add(reader.GetGuid(2));
            }
            Assert.Equal(5,count); Assert.Equal(2,transfers.Count);
            Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal(1,reader.GetInt32(0));
        }
        finally { if (transaction.Connection is not null) await transaction.RollbackAsync(); }
    }
}
