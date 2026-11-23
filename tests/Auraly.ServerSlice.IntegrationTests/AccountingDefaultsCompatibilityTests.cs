using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retiring_the_tenant_payment_switch_is_idempotent_and_preserves_tenant_identity(bool historicalSwitch)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var cursors = new SqlCommand("""
                SELECT business.BusinessId,COALESCE(MAX(notice.AvailableThroughCursor),0) CursorValue,
                  COUNT(notice.NotificationId) NoticeCount INTO #PreviousConfigurationCursors
                FROM dbo.Businesses business LEFT JOIN dbo.PosSynchronizationOutboxMessages notice
                  ON notice.BusinessId=business.BusinessId AND notice.Stream=N'Configuration'
                GROUP BY business.BusinessId;
                """,connection,transaction)) await cursors.ExecuteNonQueryAsync();
            byte[] original;
            await using (var snapshot = new SqlCommand(
                "SELECT HASHBYTES('SHA2_256',(SELECT * FROM dbo.Tenants ORDER BY TenantId FOR JSON PATH));", connection, transaction))
                original = (byte[])(await snapshot.ExecuteScalarAsync())!;
            await using (var before = new SqlCommand("""
                IF @Historical=1 EXEC(N'ALTER TABLE dbo.Tenants ADD PaymentMethodContractVersion tinyint NOT NULL
                  CONSTRAINT DF_Tenants_PaymentMethodContractVersion DEFAULT (1),
                  CONSTRAINT CK_Tenants_PaymentMethodContractVersion CHECK (PaymentMethodContractVersion IN(1,2));');
                """, connection, transaction))
            {
                before.Parameters.AddWithValue("@Historical", historicalSwitch);
                await before.ExecuteNonQueryAsync();
            }
            for (var attempt = 0; attempt < 2; attempt++)
                await fixture.ApplyMigrationAsync("20261116_RetireTenantPaymentContractSwitch.sql", connection, transaction);
            await using var verify = new SqlCommand("""
                SELECT COL_LENGTH(N'dbo.Tenants',N'PaymentMethodContractVersion'),
                  HASHBYTES('SHA2_256',(SELECT * FROM dbo.Tenants ORDER BY TenantId FOR JSON PATH)),
                  (SELECT COUNT(*) FROM #PreviousConfigurationCursors previous OUTER APPLY(
                    SELECT COALESCE(MAX(notice.AvailableThroughCursor),0) CursorValue,COUNT(*) NoticeCount
                    FROM dbo.PosSynchronizationOutboxMessages notice
                    WHERE notice.BusinessId=previous.BusinessId AND notice.Stream=N'Configuration') currentNotices
                   WHERE currentNotices.CursorValue<>previous.CursorValue+@Added
                     OR currentNotices.NoticeCount<>previous.NoticeCount+@Added);
                """, connection, transaction);
            verify.Parameters.AddWithValue("@Added",historicalSwitch?1:0);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(0));
            Assert.Equal(original, (byte[])reader[1]);
            Assert.Equal(0,reader.GetInt32(2));
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tenant_payment_defaults_are_five_shared_forms_and_reprovisioning_preserves_configuration(bool historicalCash)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var tenant = Guid.NewGuid(); var business = Guid.NewGuid(); var secondBusiness = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid(); var foreignBusiness = Guid.NewGuid();
        await using (var buffers=new SqlCommand("""
            SELECT TOP(0) AccountId,CONVERT(binary(8),RowVersion) RowVersion INTO #OriginalCash FROM dbo.AccountingAccounts;
            SELECT TOP(0) PaymentMethodId,Name,UseForSales,IsActive,Version,CONVERT(binary(8),RowVersion) RowVersion INTO #RetainedForms FROM reference.PaymentMethods;
            SELECT TOP(0) PaymentMethodId,CONVERT(binary(8),RowVersion) RowVersion INTO #BeforeCreditUpgrade FROM reference.PaymentMethods;
            """,connection,transaction)) await buffers.ExecuteNonQueryAsync();
        try
        {
            await using (var setup = new SqlCommand("""
                INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@Tenant,N'Formas iniciales',CONCAT(@Tenant,N'@example.test')),
                  (@Foreign,N'Otro tenant',CONCAT(@Foreign,N'@example.test'));
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                  VALUES(@Business,@Tenant,N'Primera',N'',N'',N'',N'',N''),(@Second,@Tenant,N'Segunda',N'',N'',N'',N'',N''),
                    (@ForeignBusiness,@Foreign,N'Sede ajena',N'',N'',N'',N'',N'');
                IF @Historical=1 INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,
                  RequiresParty,IsActive,CreatedAt,ConfigurationLockedAt)
                SELECT NEWID(),@Tenant,AccountCode,AccountName,AccountType,AllowsPosting,RequiresParty,1,@Now,@Now
                  FROM accounting.DefaultAccountDefinitions(N'AURALY_CO') WHERE Category=N'Cash';
                INSERT #OriginalCash SELECT AccountId,RowVersion FROM dbo.AccountingAccounts WHERE TenantId=@Tenant;
                """, connection, transaction))
            {
                setup.Parameters.AddWithValue("@Tenant",tenant); setup.Parameters.AddWithValue("@Foreign",foreignTenant);
                setup.Parameters.AddWithValue("@ForeignBusiness",foreignBusiness);
                setup.Parameters.AddWithValue("@Business",business); setup.Parameters.AddWithValue("@Second",secondBusiness);
                setup.Parameters.AddWithValue("@Historical",historicalCash); setup.Parameters.AddWithValue("@Now",DateTimeOffset.UtcNow);
                await setup.ExecuteNonQueryAsync();
            }
            async Task Provision(Guid site)
            {
                await using var provision = new SqlCommand("EXEC dbo.AccountingDefaultsProvision @Tenant,@Business,@Now;",connection,transaction);
                provision.Parameters.AddWithValue("@Tenant",tenant); provision.Parameters.AddWithValue("@Business",site);
                provision.Parameters.AddWithValue("@Now",DateTimeOffset.UtcNow);
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await provision.ExecuteNonQueryAsync(); watch.Stop();
                output.WriteLine("Tenant defaults: {0} commands, {1:F1} ms",counter.Count,watch.Elapsed.TotalMilliseconds);
                Assert.Equal(1,counter.Count); Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2),watch.Elapsed.ToString());
            }
            await Provision(business);
            await using (var initial = new SqlCommand("""
                SELECT MethodCode,IsActive,IsReady,UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers
                  FROM reference.PaymentMethodDetails WHERE TenantId=@Tenant ORDER BY SortOrder;
                """,connection,transaction))
            {
                initial.Parameters.AddWithValue("@Tenant",tenant);
                await using var reader = await initial.ExecuteReaderAsync();
                var types = new List<string>();
                while(await reader.ReadAsync())
                {
                    var type=reader.GetString(0); types.Add(type);
                    var ready=type!="Transfer" && !(historicalCash && type=="Cash");
                    Assert.Equal(ready,reader.GetBoolean(1)); Assert.Equal(ready,reader.GetBoolean(2));
                    Assert.True(reader.GetBoolean(3));
                    Assert.Equal(type!="Credit",reader.GetBoolean(4));
                    Assert.Equal(type!="Credit",reader.GetBoolean(7));
                    Assert.Equal(type is "Cash" or "Transfer",reader.GetBoolean(5));
                    Assert.Equal(type is "Cash" or "Transfer",reader.GetBoolean(6));
                }
                Assert.Equal(new[]{"Cash","DebitCard","CreditCard","Transfer","Credit"},types);
            }
            // Simulate an installed tenant whose four monetary forms already
            // exist. The same provisioner must add only the missing financing.
            await using (var beforeUpgrade = new SqlCommand("""
                DELETE reference.PaymentMethods WHERE TenantId=@Tenant AND Code=N'Credit';
                -- A customer may already use that code for a different type.
                UPDATE reference.PaymentMethods SET Code=N'Credit' WHERE TenantId=@Tenant AND Code=N'Cash';
                INSERT #BeforeCreditUpgrade SELECT PaymentMethodId,RowVersion
                  FROM reference.PaymentMethods WHERE TenantId=@Tenant;
                """,connection,transaction))
            { beforeUpgrade.Parameters.AddWithValue("@Tenant",tenant); await beforeUpgrade.ExecuteNonQueryAsync(); }
            await Provision(business);
            await using (var upgrade = new SqlCommand("""
                SELECT COUNT(*) FROM #BeforeCreditUpgrade original
                  JOIN reference.PaymentMethods currentForm ON currentForm.TenantId=@Tenant
                    AND currentForm.PaymentMethodId=original.PaymentMethodId
                    AND currentForm.RowVersion=original.RowVersion;
                """,connection,transaction))
            { upgrade.Parameters.AddWithValue("@Tenant",tenant); Assert.Equal(4,(int)(await upgrade.ExecuteScalarAsync())!); }
            await using (var personalize = new SqlCommand("""
                UPDATE form SET Name=CASE type.Code WHEN N'Cash' THEN N'Caja personalizada' ELSE N'Financiación personalizada' END,
                  Code=CASE type.Code WHEN N'Credit' THEN N'CREDITO-PROPIO' ELSE form.Code END,
                  UseForSales=0,IsActive=0,Version=Version+1
                FROM reference.PaymentMethods form JOIN reference.Options type ON type.OptionId=form.PaymentTypeId
                  WHERE form.TenantId=@Tenant AND type.Code IN(N'Cash',N'Credit');
                INSERT #RetainedForms SELECT PaymentMethodId,Name,UseForSales,IsActive,Version,RowVersion
                  FROM reference.PaymentMethods WHERE TenantId=@Tenant;
                """,connection,transaction))
            { personalize.Parameters.AddWithValue("@Tenant",tenant); await personalize.ExecuteNonQueryAsync(); }
            await Provision(secondBusiness); await Provision(business);
            await using var verify = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM reference.PaymentMethods WHERE TenantId=@Tenant),
                  (SELECT COUNT(*) FROM reference.PaymentMethods WHERE TenantId=@Foreign),
                  (SELECT COUNT(*) FROM #RetainedForms original JOIN reference.PaymentMethods currentForm
                    ON currentForm.PaymentMethodId=original.PaymentMethodId AND currentForm.TenantId=@Tenant
                    AND currentForm.RowVersion=original.RowVersion),
                  (SELECT COUNT(*) FROM #OriginalCash original JOIN dbo.AccountingAccounts account ON account.AccountId=original.AccountId
                    AND account.RowVersion=original.RowVersion AND account.FinancialFunction IS NULL),
                  (SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
                    WHERE BusinessId IN(@Business,@Second) AND Stream=N'Configuration'),
                  (SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages notice
                    JOIN dbo.Businesses business ON business.BusinessId=notice.BusinessId
                    WHERE business.TenantId=@Foreign AND notice.Stream=N'Configuration');
                """,connection,transaction);
            verify.Parameters.AddWithValue("@Tenant",tenant); verify.Parameters.AddWithValue("@Foreign",foreignTenant);
            verify.Parameters.AddWithValue("@Business",business); verify.Parameters.AddWithValue("@Second",secondBusiness);
            await using var result = await verify.ExecuteReaderAsync(); Assert.True(await result.ReadAsync());
            Assert.Equal(5,result.GetInt32(0)); Assert.Equal(0,result.GetInt32(1)); Assert.Equal(5,result.GetInt32(2));
            Assert.Equal(historicalCash?1:0,result.GetInt32(3));
            Assert.Equal(4,result.GetInt32(4)); Assert.Equal(0,result.GetInt32(5));
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Fact]
    public async Task Reprovisioning_preserves_the_users_existing_mapping_across_years()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var accountId = Guid.NewGuid();
            var mappingId = Guid.NewGuid();
            await using var command = new SqlCommand("""
                INSERT dbo.AccountingAccounts(AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
                VALUES(@AccountId,@TenantId,@Code,N'Cuenta elegida por el contador',N'Expense',1,0,1,SYSDATETIMEOFFSET());
                DELETE dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId IS NULL AND Category=N'OperatingExpense';
                INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                VALUES(@MappingId,@TenantId,NULL,N'OperatingExpense',@AccountId,'20000101',NULL,SYSDATETIMEOFFSET());
                EXEC dbo.AccountingDefaultsProvision @TenantId,@BusinessId,'2026-10-23T12:00:00-05:00';
                EXEC dbo.AccountingDefaultsProvision @TenantId,@BusinessId,'2027-10-23T12:00:00-05:00';
                SELECT MappingId,AccountId,EffectiveFrom,EffectiveTo FROM dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND BusinessId IS NULL AND Category=N'OperatingExpense'
                ORDER BY EffectiveFrom;
                """, connection, transaction);
            command.Parameters.AddWithValue("@AccountId", accountId); command.Parameters.AddWithValue("@MappingId", mappingId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId); command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@Code", "5199" + BitConverter.ToUInt64(accountId.ToByteArray()).ToString("D20"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(mappingId, reader.GetGuid(0)); Assert.Equal(accountId, reader.GetGuid(1));
            Assert.Equal(new DateTime(2000, 1, 1), reader.GetDateTime(2)); Assert.True(reader.IsDBNull(3));
            Assert.False(await reader.ReadAsync());
        }
        finally { await transaction.RollbackAsync(); }
    }
}
