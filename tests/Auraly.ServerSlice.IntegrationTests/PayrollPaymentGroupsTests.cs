using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Catalog;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name,DisableParallelization=true)]
public sealed class PayrollPaymentGroupsCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name="Payroll payment groups";
}

[Collection(PayrollPaymentGroupsCollection.Name)]
public sealed class PayrollPaymentGroupsTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(500,6,true)]
    [InlineData(2,7,true)]
    [InlineData(2,8,false)]
    [InlineData(100,9,false)]
    [InlineData(500,10,false)]
    public async Task Payment_keeps_frozen_transfer_and_cash_groups_separate_and_replay_does_not_consume_the_other_group(int count,int month,bool configured)
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Pay,
            AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,AccountingPermissionCodes.Activate,AccountingPermissionCodes.Retry,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using(var defaults=await client.PutAsync("/api/commerce/v1/accounting/defaults",null)) defaults.EnsureSuccessStatusCode();
        using(var active=await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared"))) active.EnsureSuccessStatusCode();
        var options=(await client.GetFromJsonAsync<PayrollWorkspaceOptions>("/api/commerce/v1/payroll/options"))!;
        var transfer=options.Catalogs[PayrollCatalogCodes.PaymentMethod].Single(option=>option.Code=="Transfer");
        var cash=options.Catalogs[PayrollCatalogCodes.PaymentMethod].Single(option=>option.Code=="Cash");
        var runId=Guid.NewGuid();var start=new DateOnly(2026,month,1);var end=start.AddMonths(1).AddDays(-1);
        var people=Enumerable.Range(0,count).Select(index=>new {
            Id=Guid.NewGuid(),EmploymentId=Guid.NewGuid(),RunEmployeeId=Guid.NewGuid(),Name=$"Grupo {index:D3}",
            FiscalMethod=index==count-1?cash.DianCode:transfer.DianCode,
            // The live contract deliberately differs from the frozen accrual.
            LiveMethod=index==count-1?transfer.OptionId:cash.OptionId,
            Amount=100m
        }).ToArray();
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync();
            await using var seed=new SqlCommand("""
                SELECT * INTO #People FROM OPENJSON(@People) WITH(Id uniqueidentifier,EmploymentId uniqueidentifier,
                  RunEmployeeId uniqueidentifier,Name nvarchar(100),FiscalMethod nvarchar(64),
                  LiveMethod uniqueidentifier,Amount decimal(19,4));
                INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                  Identification,NormalizedIdentification,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                SELECT person.Id,@Tenant,N'NaturalPerson',country.CountryId,N'CC',CONVERT(nvarchar(36),person.Id),
                  CONVERT(nvarchar(36),person.Id),person.Name,N'Grupo',person.Name,N'Complete',1,@User,SYSUTCDATETIME()
                FROM #People person CROSS JOIN dbo.Countries country WHERE country.Code=N'CO';
                INSERT payroll.RuleSets(RuleSetId,TenantId,CountryCode,Code,Name,EffectiveFrom,SourceReference,Status,CreatedAt)
                VALUES(@Rules,@Tenant,N'CO',CONVERT(nvarchar(36),@Run),N'Fixture de pagos',@Start,N'Prueba de aceptación de pagos',N'Approved',SYSUTCDATETIME());
                INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,PeriodStart,PeriodEnd,
                  PaymentDate,Status,CalculationVersion,TotalEarnings,TotalDeductions,TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
                VALUES(@Run,@Tenant,@Business,@Rules,@Frequency,N'Regular',@Start,@End,@End,N'Approved',1,@Total,0,0,0,@Total,@User,SYSUTCDATETIME());
                INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,ContractTypeOptionId,SalaryTypeOptionId,
                  PayFrequencyOptionId,RiskClassOptionId,WorkerTypeOptionId,PaymentMethodOptionId,ContractNumber,StartDate,MonthlySalary,
                  BankOptionId,BankAccountTypeOptionId,BankAccountNumber,IsActive,CreatedBy,CreatedAt)
                SELECT EmploymentId,@Tenant,Id,@Business,@Contract,@Salary,@Frequency,@Risk,@Worker,LiveMethod,
                  CONVERT(nvarchar(36),EmploymentId),@Start,3000,@Bank,@AccountType,N'123456789',1,@User,SYSUTCDATETIME() FROM #People;
                INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,EmployeeSnapshotJson,
                  RuleSnapshotJson,WorkedDays,Earnings,Deductions,EmployerContributions,Provisions,NetPayable,CalculationHash)
                SELECT RunEmployeeId,@Tenant,@Run,EmploymentId,Id,
                  (SELECT 1 SnapshotVersion,Name EmployeeName,FiscalMethod PaymentMethodCode,
                    N'Banco de destino' Bank,N'Savings' BankAccountType,N'123456789' BankAccountNumber FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                  N'{}',30,Amount,0,0,0,Amount,HASHBYTES('SHA2_256',CONVERT(nvarchar(36),RunEmployeeId)) FROM #People;
                """,connection,transaction);
            Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(value=>value.Code==code).OptionId;
            seed.Parameters.AddWithValue("@People",JsonSerializer.Serialize(people));seed.Parameters.AddWithValue("@Run",runId);
            seed.Parameters.AddWithValue("@Rules",Guid.NewGuid());seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            seed.Parameters.AddWithValue("@Business",fixture.BusinessId);seed.Parameters.AddWithValue("@User",fixture.UserId);
            seed.Parameters.AddWithValue("@Start",start.ToDateTime(TimeOnly.MinValue));seed.Parameters.AddWithValue("@End",end.ToDateTime(TimeOnly.MinValue));
            seed.Parameters.AddWithValue("@Total",count*100m);seed.Parameters.AddWithValue("@Frequency",Option(PayrollCatalogCodes.PayFrequency,"Monthly"));
            seed.Parameters.AddWithValue("@Contract",Option(PayrollCatalogCodes.ContractType,"Indefinite"));seed.Parameters.AddWithValue("@Salary",Option(PayrollCatalogCodes.SalaryType,"Ordinary"));
            seed.Parameters.AddWithValue("@Risk",Option(PayrollCatalogCodes.RiskClass,"I"));seed.Parameters.AddWithValue("@Worker",Option(PayrollCatalogCodes.WorkerType,"01"));
            seed.Parameters.AddWithValue("@Bank",Option(PayrollCatalogCodes.Bank,"Bancolombia"));seed.Parameters.AddWithValue("@AccountType",Option(PayrollCatalogCodes.BankAccountType,"Savings"));
            await seed.ExecuteNonQueryAsync();
            var lines=people.Select(person=>new PayrollAccountingLine(PayrollAccountingCategories.NetPayable,0,person.Amount,person.Id,"Neto causado"))
                .Append(new("SalaryExpense",count*100m,0,null,"Devengado causado")).ToArray();
            var payload=new PayrollAccountingPayload(fixture.TenantId,fixture.BusinessId,runId,"Regular",start,end,end,"Fixture de causación",lines);
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection,transaction,
                [new(fixture.TenantId,fixture.BusinessId,runId,PayrollAccountingDocumentTypes.Accrual,PayrollContractSerializer.Serialize(payload),new(2026,month,end.Day,17,0,0,TimeSpan.Zero),Guid.NewGuid())],
                DateTimeOffset.UtcNow,AccountingJobRequirement.AccountingEntryOnly,CancellationToken.None);
            await transaction.CommitAsync();
        }
        using var scope=fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountingProcessingCoordinator>().RequestPostingAsync(fixture.BusinessId,runId,PayrollAccountingDocumentTypes.Accrual);
        var user=new PayrollUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{PayrollPermissionCodes.Read,PayrollPermissionCodes.Pay});
        var store=scope.ServiceProvider.GetRequiredService<IPayrollStore>();
        var preview=await store.GetRunAsync(user,runId,CancellationToken.None);
        Assert.Equal(count,preview.PayableEmployees.Count);
        var request=new CreatePayrollPaymentBatchRequest(Guid.NewGuid(),runId,transfer.OptionId,end,"TRANSFER-GROUP");
        SavePaymentMethodRequest? configuration=null; PaymentMethodView? form=null;
        AccountingAccountView? currentBank=null; Guid? originalPayable=null; Guid cashTypeId=Guid.Empty;
        if(configured)
        {
            var before=await CreateBankAuxiliaryAsync(client);
            currentBank=await CreateBankAuxiliaryAsync(client);
            var types=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
            cashTypeId=types.Single(type=>type.Code=="Cash").Id;
            configuration=new(Guid.NewGuid(),$"PAYROLL-{Guid.NewGuid():N}","Banco anterior nómina",
                types.Single(type=>type.Code=="Transfer").Id,before.AccountId,false,false,false,true,true,10);
            using var saved=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}",configuration);
            Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
            form=(await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
            request=request with {PaymentMethodId=form.PaymentMethodId};
            var offered=(await client.GetFromJsonAsync<PaymentMethodPage>("/api/commerce/v1/payment-methods/?usage=Payroll&page=1&pageSize=100"))!;
            Assert.Contains(offered.Items,item=>item.PaymentMethodId==form.PaymentMethodId);
            await Assert.ThrowsAsync<PayrollConflictException>(()=>store.CreatePaymentBatchAsync(user,
                request with {PaymentMethodId=Guid.NewGuid()},CancellationToken.None));
            await Assert.ThrowsAsync<PayrollConflictException>(()=>store.CreatePaymentBatchAsync(user,
                request with {PaymentMethodOptionId=cash.OptionId},CancellationToken.None));
            var accrual=(await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{runId}"))!;
            originalPayable=accrual.Lines.Single(line=>line.PartyId==people[0].Id&&line.Credit>0).AccountId;
        }
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var readSamples=new List<double>();
        for(var sample=0;sample<20;sample++)
        {
            using var commands=new InventoryBalanceProcessingTests.CommandCounter(database);
            var readTimer=Stopwatch.StartNew();
            Assert.Equal(count,(await store.GetRunAsync(user,runId,CancellationToken.None)).PayableEmployees.Count);
            readSamples.Add(readTimer.Elapsed.TotalMilliseconds);Assert.Equal(1,commands.Count);
        }
        if(count==2)
        {
            await AssertDisabledAccountingRejectsAsync(store,user,request,runId);
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await using var corrupt=new SqlCommand("UPDATE payroll.RunEmployees SET EmployeeSnapshotJson=JSON_MODIFY(EmployeeSnapshotJson,'$.BankAccountNumber',@Number) WHERE PayrollRunEmployeeId=@Id AND TenantId=@Tenant",connection);
            corrupt.Parameters.AddWithValue("@Id",people[0].RunEmployeeId);corrupt.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            var number=corrupt.Parameters.Add("@Number",System.Data.SqlDbType.NVarChar,64);number.Value=DBNull.Value;
            Assert.Equal(1,await corrupt.ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PayrollValidationException>(()=>store.CreatePaymentBatchAsync(user,request,CancellationToken.None));
            number.Value="123456789";Assert.Equal(1,await corrupt.ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<PayrollConflictException>(()=>store.CreatePaymentBatchAsync(user with {BusinessId=Guid.NewGuid()},request,CancellationToken.None));
            await Assert.ThrowsAsync<PayrollNotFoundException>(()=>store.GetRunAsync(user with {TenantId=Guid.NewGuid()},runId,CancellationToken.None));
        }
        var commandTimes=new List<double>();
        var timer=Stopwatch.StartNew();PayrollPaymentBatchView accepted;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(database,afterExecute:(_,elapsed)=>commandTimes.Add(elapsed)))
        {
            accepted=await store.CreatePaymentBatchAsync(user,request,CancellationToken.None);
            Assert.Equal(2,commands.Count);
        }
        var acceptanceMs=timer.Elapsed.TotalMilliseconds;
        Assert.False(accepted.IsPaid);Assert.Equal("Pending",accepted.AccountingStatus);
        Assert.Equal(count-1,accepted.EmployeeCount);Assert.Equal((count-1)*100m,accepted.TotalAmount);
        Assert.Equal(cash.OptionId,Assert.Single(accepted.RemainingEmployees).PaymentMethodOptionId);
        using var originalReads=new InventoryBalanceProcessingTests.CommandCounter(database,
            commandFilter:command=>command.CommandText.Contains("DECLARE @Original TABLE(PartyId",StringComparison.Ordinal));
        using var formResolutions=new InventoryBalanceProcessingTests.CommandCounter(database,
            commandFilter:command=>command.CommandText.Contains("DECLARE @Resolved TABLE(Line int PRIMARY KEY",StringComparison.Ordinal));
        if(configured)
        {
            Assert.Null(accepted.AccountId);
            using(var semanticEdit=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form!.PaymentMethodId}",
                configuration! with {PaymentTypeId=cashTypeId,IsActive=false,RowVersion=form.RowVersion}))
                Assert.Equal(HttpStatusCode.Conflict,semanticEdit.StatusCode);
            var changedPayable=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2597","Liability","Payable","Credit");
            using(var changed=await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",new SetAccountMappingRequest(
                fixture.TenantId,fixture.BusinessId,PayrollAccountingCategories.NetPayable,changedPayable.AccountId,new(2026,1,1),null)))
                Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
            using var disabled=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form!.PaymentMethodId}",
                configuration! with {AccountId=currentBank!.AccountId,Name="Banco vigente nómina",IsActive=false,RowVersion=form.RowVersion});
            Assert.True(disabled.IsSuccessStatusCode,await disabled.Content.ReadAsStringAsync());
            form=(await disabled.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
            await scope.ServiceProvider.GetRequiredService<PayrollService>().CreatePaymentBatchAsync(user,request);
            await using(var connection=new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var pending=new SqlCommand("""
                    SELECT job.Status,batch.AccountId,(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
                    FROM payroll.PaymentBatches batch JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=batch.PaymentBatchId
                    WHERE batch.PaymentBatchId=@Id;
                    """,connection);
                pending.Parameters.AddWithValue("@Id",request.PaymentBatchId);
                await using var reader=await pending.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
                Assert.Equal("AccountingPendingConfiguration",reader.GetString(0));Assert.True(reader.IsDBNull(1));Assert.Equal(0,reader.GetInt32(2));
            }
            await AssertPaymentOutcomeAsync(client,request.PaymentBatchId,people[0].Id,start,end,false,"AccountingPendingConfiguration");
            using var enabled=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form.PaymentMethodId}",
                configuration! with {AccountId=currentBank.AccountId,Name="Banco vigente nómina",RowVersion=form.RowVersion});
            Assert.True(enabled.IsSuccessStatusCode,await enabled.Content.ReadAsStringAsync());
            form=(await enabled.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
            if (count == 2)
            {
                // There is no account-retirement HTTP command. Simulate this
                // configuration state only in the isolated fixture and restore
                // it exactly; payment must not use an unavailable accrual.
                await using var originalConnection = new SqlConnection(fixture.ConnectionString);
                await originalConnection.OpenAsync();
                Assert.StartsWith("AuralyServerSlice_", originalConnection.Database);
                await using var availability = new SqlCommand("""
                    UPDATE dbo.AccountingAccounts SET IsActive=@Active
                    OUTPUT deleted.IsActive WHERE AccountId=@Id AND TenantId=@Tenant;
                    """, originalConnection);
                availability.Parameters.AddWithValue("@Id", originalPayable!.Value);
                availability.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                var activeParameter = availability.Parameters.Add("@Active", System.Data.SqlDbType.Bit);
                activeParameter.Value = false;
                var wasActive = Assert.IsType<bool>(await availability.ExecuteScalarAsync());
                Assert.True(wasActive);
                try
                {
                    using var unavailable = await client.PostAsync($"/api/commerce/v1/accounting/postings/{request.PaymentBatchId}/retry", null);
                    Assert.True(unavailable.IsSuccessStatusCode, await unavailable.Content.ReadAsStringAsync());
                    await AssertPaymentOutcomeAsync(client, request.PaymentBatchId, people[0].Id, start, end,
                        false, "AccountingPendingConfiguration");
                    await using var missing = new SqlCommand("""
                        SELECT job.LastErrorCode,batch.AccountId,
                          (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
                        FROM dbo.AccountingPostingJobs job JOIN payroll.PaymentBatches batch
                          ON batch.PaymentBatchId=job.SourceDocumentId
                        WHERE job.SourceDocumentId=@Id AND job.SourceDocumentType=N'PayrollPayment';
                        """, originalConnection);
                    missing.Parameters.AddWithValue("@Id", request.PaymentBatchId);
                    await using var reader = await missing.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
                    Assert.Equal("PayrollOriginalPayableMissing", reader.GetString(0));
                    Assert.True(reader.IsDBNull(1)); Assert.Equal(0, reader.GetInt32(2));
                }
                finally
                {
                    activeParameter.Value = wasActive;
                    Assert.False(Assert.IsType<bool>(await availability.ExecuteScalarAsync()));
                }
            }
            using var retried=await client.PostAsync($"/api/commerce/v1/accounting/postings/{request.PaymentBatchId}/retry",null);
            Assert.True(retried.IsSuccessStatusCode,await retried.Content.ReadAsStringAsync());
        }
        else
        {
            if(count==2)
            {
                // Simulate a pre-upgrade queued job's derived cache, while its
                // immutable source and the original accrual remain unchanged.
                await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
                await using var oldCache=new SqlCommand("""
                    UPDATE dbo.AccountingPostingJobs SET ResolvedAccountMappingsJson=(
                      SELECT @Category Category,configured.AccountId
                      FROM accounting.ConfiguredPostingAccountIdentity(@Tenant,@Business,@Category,@Date) configured FOR JSON PATH)
                    WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PayrollPayment' AND TenantId=@Tenant AND BusinessId=@Business;
                    """,connection);
                oldCache.Parameters.AddWithValue("@Tenant",fixture.TenantId);oldCache.Parameters.AddWithValue("@Business",fixture.BusinessId);
                oldCache.Parameters.AddWithValue("@Category",transfer.MetadataCode!);oldCache.Parameters.AddWithValue("@Date",end.ToDateTime(TimeOnly.MinValue));
                oldCache.Parameters.AddWithValue("@Id",request.PaymentBatchId);Assert.Equal(1,await oldCache.ExecuteNonQueryAsync());
                currentBank=await CreateBankAuxiliaryAsync(client);
                using var changed=await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",new SetAccountMappingRequest(
                    fixture.TenantId,fixture.BusinessId,transfer.MetadataCode!,currentBank.AccountId,new(2026,1,1),null));
                Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
            }
            await scope.ServiceProvider.GetRequiredService<PayrollService>().CreatePaymentBatchAsync(user,request);
        }
        var replay=await scope.ServiceProvider.GetRequiredService<PayrollService>().CreatePaymentBatchAsync(user,request);
        Assert.Equal(accepted.PaymentBatchId,replay.PaymentBatchId);Assert.Single(replay.RemainingEmployees);
        Assert.True(replay.IsPaid);Assert.Equal("Posted",replay.AccountingStatus);
        await AssertPaymentOutcomeAsync(client,request.PaymentBatchId,people[0].Id,start,end,true,"Posted");
        var transferEntry=(await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{request.PaymentBatchId}"))!;
        Assert.Equal(accepted.TotalAmount,transferEntry.DebitTotal);Assert.Equal(accepted.TotalAmount,transferEntry.CreditTotal);
        Assert.Equal(count-1,transferEntry.Lines.Count(line=>line.Debit>0));
        Assert.DoesNotContain(transferEntry.Lines,line=>line.PartyId==people[^1].Id);
        Assert.Equal(configured && count == 2 ? 2 : 1,originalReads.Count);
        Assert.Equal(configured ? count == 2 ? 3 : 2 : 0,formResolutions.Count);
        if(currentBank is not null)Assert.Equal(currentBank.AccountId,transferEntry.Lines.Single(line=>line.Credit>0).AccountId);
        if(configured)
        {
            Assert.Equal(currentBank!.AccountId,transferEntry.Lines.Single(line=>line.Credit>0).AccountId);
            Assert.All(transferEntry.Lines.Where(line=>line.Debit>0),line=>Assert.Equal(originalPayable,line.AccountId));
            using(var changed=await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{form!.PaymentMethodId}",
                configuration! with {IsActive=false,RowVersion=form.RowVersion}))
                Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
            var completedReplay=await scope.ServiceProvider.GetRequiredService<PayrollService>().CreatePaymentBatchAsync(user,request);
            Assert.Equal(currentBank.AccountId,completedReplay.AccountId);
            Assert.Equal("Banco vigente nómina",completedReplay.PaymentMethodName);
            await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
            await using var evidenceQuery=new SqlCommand("""
                SELECT batch.PaymentMethodVersion,batch.AccountId,JSON_VALUE(source.PayloadJson,'$.paymentMethodId'),
                  JSON_VALUE(source.PayloadJson,'$.paymentMethodTypeCode'),JSON_VALUE(source.PayloadJson,'$.accountId'),
                  job.ResolvedAccountMappingsJson
                FROM payroll.PaymentBatches batch JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=batch.PaymentBatchId
                JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
                WHERE batch.PaymentBatchId=@Id;
                """,connection);
            evidenceQuery.Parameters.AddWithValue("@Id",request.PaymentBatchId);
            await using var reader=await evidenceQuery.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
            Assert.Equal(3,reader.GetInt32(0));Assert.Equal(currentBank.AccountId,reader.GetGuid(1));
            Assert.Equal(form.PaymentMethodId.ToString(),reader.GetString(2),ignoreCase:true);Assert.Equal("Transfer",reader.GetString(3));Assert.True(reader.IsDBNull(4));Assert.True(reader.IsDBNull(5));
        }
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var source=new SqlCommand("SELECT OccurredAt FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PayrollPayment' AND TenantId=@Tenant AND BusinessId=@Business",connection);
            source.Parameters.AddWithValue("@Id",request.PaymentBatchId);source.Parameters.AddWithValue("@Tenant",fixture.TenantId);source.Parameters.AddWithValue("@Business",fixture.BusinessId);
            var at=(DateTimeOffset)(await source.ExecuteScalarAsync())!;
            Assert.Equal(end,DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(at,"America/Bogota").DateTime));
        }
        using(var changed=await client.PostAsJsonAsync("/api/commerce/v1/payroll/payments",request with {PaymentMethodOptionId=cash.OptionId})) Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
        // Concurrent attempts for the remaining cash employee cannot pay twice.
        var cashRequests=Enumerable.Range(0,2).Select(_=>request with {PaymentBatchId=Guid.NewGuid(),PaymentMethodOptionId=cash.OptionId,PaymentMethodId=null,ReferenceNumber="CASH-GROUP"}).ToArray();
        var responses=await Task.WhenAll(cashRequests.Select(value=>client.PostAsJsonAsync("/api/commerce/v1/payroll/payments",value)));
        try
        {
            Assert.Single(responses.Where(response=>response.IsSuccessStatusCode));
            Assert.Single(responses.Where(response=>response.StatusCode==HttpStatusCode.Conflict));
            var cashResult=(await responses.Single(response=>response.IsSuccessStatusCode).Content.ReadFromJsonAsync<PayrollPaymentBatchView>())!;
            Assert.Equal(1,cashResult.EmployeeCount);Assert.Equal(100m,cashResult.TotalAmount);Assert.Empty(cashResult.RemainingEmployees);
            var cashEntry=(await client.GetFromJsonAsync<AccountingEntryView>($"/api/commerce/v1/accounting/entries/by-document/{cashResult.PaymentBatchId}"))!;
            Assert.Equal(100m,cashEntry.DebitTotal);Assert.Equal(100m,cashEntry.CreditTotal);
        }
        finally{foreach(var response in responses)response.Dispose();}
        Assert.Empty((await store.GetRunAsync(user,runId,CancellationToken.None)).PayableEmployees);
        var evidence=Path.Combine(AppContext.BaseDirectory,"TestResults");Directory.CreateDirectory(evidence);
        var readP95=readSamples.Order().ElementAt(18);
        await File.WriteAllTextAsync(Path.Combine(evidence,$"payroll-payment-groups-{count}-{configured}.json"),JsonSerializer.Serialize(new {count,configured,acceptanceMs,acceptanceCommandMs=commandTimes,acceptanceCommands=2,readSamples=readSamples.Count,readP95,readCommands=1}));
        Assert.True(acceptanceMs<2000,$"Acceptance exceeded 2 s: {acceptanceMs:F1} ms");
        Assert.True(readP95<1000,$"Payment preview p95 exceeded 1 s: {readP95:F1} ms");
    }
    private async Task<AccountingAccountView> CreateBankAuxiliaryAsync(HttpClient client)
    {
        var types=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/bank-account-type"))!;
        var bankId=Guid.NewGuid();
        using var saved=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
            new SaveBankAccountRequest(bankId,null,types.Single(type=>type.Code=="Checking").Id,"Banco nómina",bankId.ToString("N"),"Banco nómina",false,true,null));
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        return await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1197","Asset","Bank","Debit",bankId);
    }

    private async Task AssertPaymentOutcomeAsync(HttpClient client,Guid batchId,Guid partyId,DateOnly start,DateOnly end,bool paid,string status)
    {
        var options=(await client.GetFromJsonAsync<PayrollWorkspaceOptions>("/api/commerce/v1/payroll/options"))!;
        var batch=Assert.Single(options.PaymentBatches,value=>value.PaymentBatchId==batchId);
        Assert.Equal(paid,batch.IsPaid);Assert.Equal(status,batch.AccountingStatus);
        var query=$"from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}&partyId={partyId:D}";
        var summary=(await client.GetFromJsonAsync<PayrollReportResult>($"/api/commerce/v1/payroll/reports/payroll-summary?{query}"))!;
        Assert.Equal(paid?"Pagado":"Pendiente",Assert.IsType<JsonElement>(Assert.Single(summary.Rows)["paymentStatus"]).GetString());
        var payments=(await client.GetFromJsonAsync<PayrollReportResult>($"/api/commerce/v1/payroll/reports/payments?{query}"))!;
        Assert.Equal(paid?1:0,payments.Rows.Count);
    }

    private async Task AssertDisabledAccountingRejectsAsync(IPayrollStore store,PayrollUserIdentity user,CreatePayrollPaymentBatchRequest request,Guid runId)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("""
            DECLARE @Original nvarchar(max)=(SELECT Status,EffectiveFrom,ActivatedAt,ActivatedByUserId
              FROM dbo.AccountingTenantSettings WHERE TenantId=@Tenant FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            UPDATE dbo.AccountingTenantSettings SET Status=N'Disabled',EffectiveFrom=NULL,ActivatedAt=NULL,ActivatedByUserId=NULL WHERE TenantId=@Tenant;
            SELECT @Original;
            """,connection);
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        var original=(string)(await command.ExecuteScalarAsync())!;
        try
        {
            Assert.Empty((await store.GetRunAsync(user,runId,CancellationToken.None)).PayableEmployees);
            await Assert.ThrowsAsync<PayrollConflictException>(()=>store.CreatePaymentBatchAsync(user,request,CancellationToken.None));
        }
        finally
        {
            command.CommandText="""
                UPDATE settings SET Status=original.Status,EffectiveFrom=original.EffectiveFrom,ActivatedAt=original.ActivatedAt,ActivatedByUserId=original.ActivatedByUserId
                FROM dbo.AccountingTenantSettings settings CROSS APPLY OPENJSON(@Original) WITH(Status nvarchar(16),EffectiveFrom date,
                  ActivatedAt datetimeoffset,ActivatedByUserId uniqueidentifier) original WHERE settings.TenantId=@Tenant;
                """;
            command.Parameters.AddWithValue("@Original",original);Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
    }

}
