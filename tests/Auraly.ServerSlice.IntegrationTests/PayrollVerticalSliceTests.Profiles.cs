using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    [Fact]
    public async Task Contract_profile_uses_period_versions_current_posting_accounts_and_original_payable_when_paid()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure,
            PayrollPermissionCodes.Manage,PayrollPermissionCodes.Calculate,PayrollPermissionCodes.Approve,
            PayrollPermissionCodes.Pay,AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure);
        var options=await GetAsync<PayrollWorkspaceOptions>(client,"/api/commerce/v1/payroll/options");
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(row=>row.Code==code).OptionId;
        var concepts=await EnsureCalculationConceptsAsync(client,options);var family="PROFILE-"+Guid.NewGuid().ToString("N");
        async Task<PayrollRuleSetView> Version(DateOnly from,decimal health)
        {
            var values=Rules().ToDictionary(row=>row.Key,row=>row.Value);values[PayrollRuleParameterCodes.EmployeeHealthRate]=health;
            var id=Guid.NewGuid();PayrollRuleSetView draft;
            using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter:command=>command.CommandText.Contains("INSERT payroll.RuleParameters",StringComparison.Ordinal)
                    ||command.CommandText.Contains("SELECT RuleSetId,CountryCode,Code,Name,EffectiveFrom",StringComparison.Ordinal)))
            {
                draft=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{id}",
                    new SavePayrollRuleSetRequest(id,"CO",family,"Parámetros de prueba por vigencia",from,null,
                        "Datos sintéticos para verificar versionamiento, no tarifas legales",
                        values.Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray(),null));
                Assert.Equal(2,commands.Count);
            }
            return await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{id}/approve",new{rowVersion=draft.RowVersion});
        }
        var firstRules=await Version(new(2026,1,1),.04m);
        // Represent a published global template without changing system seeds.
        // The public version command remains tenant-scoped. Its later version
        // must supersede this template without editing profiles or contracts.
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var global=new SqlCommand("UPDATE payroll.RuleSets SET TenantId=NULL WHERE RuleSetId=@Id AND TenantId=@Tenant;",connection);
            global.Parameters.AddWithValue("@Id",firstRules.RuleSetId);global.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            Assert.Equal(1,await global.ExecuteNonQueryAsync());
        }
        var configuration=await CreateProfileFixtureAsync(firstRules.RuleSetId);
        var center=await PostAsync<AccountingCostCenterView>(client,"/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(Guid.NewGuid(),fixture.TenantId,"P"+Guid.NewGuid().ToString("N")[..20],"Centro contractual de prueba",null,false));
        configuration=configuration with{CostCenterId=center.CostCenterId};
        var profile=await GetAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{configuration.AccountingProfileId}");
        var inactiveId=Guid.NewGuid();var search=profile.Code[..8];
        await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{inactiveId}",
            new SavePayrollProfileRequest(inactiveId,search+"-INACTIVE","AAAA perfil inactivo",firstRules.RuleSetId,false,null,[]));
        var activeProfiles=await GetAsync<PayrollProfilePage>(client,$"/api/commerce/v1/payroll/profiles?page=1&pageSize=1&search={search}&isActive=true");
        Assert.Equal(1,activeProfiles.TotalCount);Assert.Equal(profile.ProfileId,Assert.Single(activeProfiles.Items).ProfileId);
        var inactiveProfiles=await GetAsync<PayrollProfilePage>(client,$"/api/commerce/v1/payroll/profiles?page=1&pageSize=1&search={search}&isActive=false");
        Assert.Equal(1,inactiveProfiles.TotalCount);Assert.Equal(inactiveId,Assert.Single(inactiveProfiles.Items).ProfileId);
        var firstExpense=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"5196","Expense","General","Debit");
        var currentExpense=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"5197","Expense","General","Debit");
        var firstNet=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2496","Liability","Payable","Credit");
        var changedNet=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2497","Liability","Payable","Credit");
        PayrollProfileMapping[] mappings=[new(Guid.NewGuid(),concepts[PayrollSystemConceptRoles.BasicSalary],firstExpense.AccountId,null,new(2026,1,1),null),
            new(Guid.NewGuid(),null,null,firstNet.AccountId,new(2026,1,1),null),..profile.Mappings];
        SavePayrollProfileRequest Update(PayrollProfileView value,IReadOnlyList<PayrollProfileMapping> rows)=>
            new(value.ProfileId,value.Code,value.Name,value.RuleSetId,value.IsActive,value.RowVersion,rows);
        profile=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",Update(profile,mappings));
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",
            Update(profile,[mappings[0],mappings[0] with{MappingId=Guid.NewGuid()}])))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",
            Update(profile,[mappings[0] with{DebitAccountId=firstNet.AccountId}])))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var employmentId=Guid.NewGuid();var person=await CreateEmployeePartyAsync(Guid.NewGuid().ToString("N"));
        var employmentRequest=new SavePayrollEmploymentRequest(employmentId,person,fixture.BusinessId,null,
            Option(PayrollCatalogCodes.ContractType,"Indefinite"),Option(PayrollCatalogCodes.SalaryType,"Ordinary"),
            Option(PayrollCatalogCodes.PayFrequency,"Monthly"),Option(PayrollCatalogCodes.RiskClass,"I"),
            Option(PayrollCatalogCodes.WorkerType,"01"),Option(PayrollCatalogCodes.WorkerSubtype,"00"),Option(PayrollCatalogCodes.PaymentMethod,"Cash"),
            "PROFILE-"+employmentId.ToString("N"),new(2026,1,1),null,3_000_000,null,null,null,null,null,true,null,
            AppliesTransportAllowance:false,Configuration:configuration);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{Configuration=configuration with{HealthPartyId=person}}))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var employment=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",employmentRequest);
        Assert.Equal(configuration,employment.Configuration!.Values);Assert.Equal("EPS de prueba",employment.Configuration.HealthName);
        var page=await GetAsync<PayrollEmploymentPage>(client,$"/api/commerce/v1/payroll/employments?page=1&pageSize=1&employmentId={employmentId}");
        Assert.Equal(configuration,Assert.Single(page.Items).Configuration!.Values);
        using(var payrollOnly=fixture.CreateAdminClient(PayrollPermissionCodes.Manage))
        {
            using var general=await payrollOnly.GetAsync("/api/commerce/v1/parties/role-options?role=Any&pageSize=10");
            Assert.Equal(HttpStatusCode.Forbidden,general.StatusCode);
            var institutions=await GetAsync<Auraly.Contracts.Parties.PartyRoleOptionPage>(payrollOnly,
                $"/api/commerce/v1/parties/role-options?role=Any&partyType=Organization&partyId={configuration.HealthPartyId}&pageSize=1");
            Assert.Equal(configuration.HealthPartyId,Assert.Single(institutions.Items).PartyId);
            var excluded=await GetAsync<Auraly.Contracts.Parties.PartyRoleOptionPage>(payrollOnly,
                $"/api/commerce/v1/parties/role-options?role=Any&partyType=Organization&partyId={person}&pageSize=1");
            Assert.Empty(excluded.Items);Assert.Equal(0,excluded.TotalCount);
            var workers=await GetAsync<Auraly.Contracts.Parties.PartyRoleOptionPage>(payrollOnly,
                $"/api/commerce/v1/parties/role-options?role=Employee&partyId={person}&pageSize=1");
            Assert.Equal(person,Assert.Single(workers.Items).PartyId);
        }
        using var scope=fixture.CreateScope();var service=scope.ServiceProvider.GetRequiredService<PayrollService>();
        var store=scope.ServiceProvider.GetRequiredService<IPayrollStore>();
        var accounting=scope.ServiceProvider.GetRequiredService<AccountingService>();
        var processor=scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var actor=new PayrollUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{PayrollPermissionCodes.Read,PayrollPermissionCodes.Calculate,PayrollPermissionCodes.Approve});
        async Task<PayrollRunView> Calculate(int month)
        {
            var start=new DateOnly(2026,month,1);var end=start.AddMonths(1).AddDays(-1);
            var run=await service.CreateRunAsync(actor,new(Guid.NewGuid(),fixture.BusinessId,Option(PayrollCatalogCodes.PayFrequency,"Monthly"),"Regular",null,start,end,end));
            Assert.Null(run.RuleSetId);
            if(month==11)
            {
                using var obsolete=await client.PostAsJsonAsync("/api/commerce/v1/payroll/runs",new {
                    payrollRunId=Guid.NewGuid(),businessId=fixture.BusinessId,ruleSetId=firstRules.RuleSetId,
                    payFrequencyOptionId=Option(PayrollCatalogCodes.PayFrequency,"Monthly"),runKind="Regular",
                    originalPayrollRunId=(Guid?)null,periodStart=start,periodEnd=end,paymentDate=end});
                Assert.Equal(HttpStatusCode.BadRequest,obsolete.StatusCode);
                var unconfigured=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",
                    employmentRequest with{RowVersion=employment.RowVersion,Configuration=configuration with{AccountingProfileId=null}});
                var candidates=await GetAsync<PayrollRunEmploymentPage>(client,
                    $"/api/commerce/v1/payroll/runs/{run.PayrollRunId}/employments?page=1&pageSize=50&search={employmentRequest.ContractNumber}");
                Assert.Contains(candidates.Items,item=>item.EmploymentId==employmentId);
                await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(actor,run.PayrollRunId,
                    new CalculatePayrollRunRequest(run.RowVersion,false,[new(employmentId,true)])));
                employment=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",
                    employmentRequest with{RowVersion=unconfigured.RowVersion});
            }
            return await service.CalculateRunAsync(actor,run.PayrollRunId,new CalculatePayrollRunRequest(run.RowVersion,false,[new(employmentId,true)]));
        }
        var firstRun=await Calculate(11);
        Assert.Equal(120_000,Assert.Single(firstRun.Employees).Lines.Single(row=>row.ConceptCode=="HEALTH_EMPLOYEE").Amount);
        var futureRules=await Version(new(2026,12,1),.05m);
        // Exercise malformed scalar evidence before consuming the real draft.
        // The batched projection must retain JSON_VALUE's length semantics and
        // must reject a missing snapshot version rather than accept SQL UNKNOWN.
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var read=new SqlCommand("SELECT EmployeeSnapshotJson FROM payroll.RunEmployees WHERE PayrollRunId=@Run;",connection);
            read.Parameters.AddWithValue("@Run",firstRun.PayrollRunId);
            var original=(string)(await read.ExecuteScalarAsync())!;
            foreach(var field in new[]{"SnapshotVersion","EffectiveRuleSetId"})
            {
                try
                {
                    await using var damage=new SqlCommand("UPDATE payroll.RunEmployees SET EmployeeSnapshotJson=JSON_MODIFY(@Original,@Path,@Value) WHERE PayrollRunId=@Run;",connection);
                    damage.Parameters.AddWithValue("@Run",firstRun.PayrollRunId);damage.Parameters.AddWithValue("@Original",original);
                    damage.Parameters.AddWithValue("@Path","$."+field);
                    damage.Parameters.Add("@Value",System.Data.SqlDbType.NVarChar,-1).Value=field=="SnapshotVersion"
                        ?DBNull.Value:firstRules.RuleSetId.ToString()+new string('x',4001);
                    Assert.Equal(1,await damage.ExecuteNonQueryAsync());
                    await Assert.ThrowsAsync<PayrollConflictException>(()=>store.ApproveRunAsync(actor,firstRun.PayrollRunId,
                        "invalid-snapshot-"+field,firstRun.RowVersion,default));
                }
                finally
                {
                    await using var restore=new SqlCommand("UPDATE payroll.RunEmployees SET EmployeeSnapshotJson=@Original WHERE PayrollRunId=@Run;",connection);
                    restore.Parameters.AddWithValue("@Run",firstRun.PayrollRunId);restore.Parameters.AddWithValue("@Original",original);
                    await restore.ExecuteNonQueryAsync();
                }
            }
        }
        var accepted=await store.ApproveRunAsync(actor,firstRun.PayrollRunId,"profile-post-"+firstRun.PayrollRunId,firstRun.RowVersion,default);
        mappings[0]=mappings[0] with{DebitAccountId=currentExpense.AccountId};
        var oldProfile=profile;
        profile=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",Update(profile,mappings));
        using(var stale=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",Update(oldProfile,mappings)))
            Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        // No general center is needed when all source lines carry the contract's
        // explicit center. An unavailable explicit center must still block posting.
        await using(var centers=new SqlConnection(fixture.ConnectionString))
        {
            await centers.OpenAsync();string defaultCenters,assignments;
            await using(var disable=new SqlCommand("""
                DECLARE @Centers nvarchar(max)=(SELECT CostCenterId FROM dbo.AccountingCostCenters WHERE TenantId=@Tenant AND IsDefault=1 FOR JSON PATH);
                DECLARE @Assignments nvarchar(max)=(SELECT AssignmentId FROM dbo.AccountingCostCenterAssignments WHERE TenantId=@Tenant AND BusinessId=@Business AND IsActive=1 FOR JSON PATH);
                UPDATE dbo.AccountingCostCenters SET IsDefault=0 WHERE TenantId=@Tenant AND IsDefault=1;
                UPDATE dbo.AccountingCostCenterAssignments SET IsActive=0 WHERE TenantId=@Tenant AND BusinessId=@Business AND IsActive=1;
                UPDATE dbo.AccountingCostCenters SET IsActive=0 WHERE TenantId=@Tenant AND CostCenterId=@Explicit;
                SELECT COALESCE(@Centers,N'[]'),COALESCE(@Assignments,N'[]');
                """,centers))
            {
                disable.Parameters.AddWithValue("@Tenant",fixture.TenantId);disable.Parameters.AddWithValue("@Business",fixture.BusinessId);
                disable.Parameters.AddWithValue("@Explicit",center.CostCenterId);
                await using var result=await disable.ExecuteReaderAsync();Assert.True(await result.ReadAsync());
                defaultCenters=result.GetString(0);assignments=result.GetString(1);
            }
            try
            {
                await processor.ProcessAsync(accepted.PayrollRunId,PayrollAccountingDocumentTypes.Accrual,fixture.BusinessId,default);
                await using(var unavailable=new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Run AND LastErrorCode=N'PayrollProfileConfigurationInvalid';
                    SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Run;
                    UPDATE dbo.AccountingCostCenters SET IsActive=1 WHERE CostCenterId=@Center;
                    """,centers))
                {
                    unavailable.Parameters.AddWithValue("@Run",accepted.PayrollRunId);unavailable.Parameters.AddWithValue("@Center",center.CostCenterId);
                    await using var result=await unavailable.ExecuteReaderAsync();Assert.True(await result.ReadAsync());Assert.Equal(1,result.GetInt32(0));
                    await result.NextResultAsync();Assert.True(await result.ReadAsync());Assert.Equal(0,result.GetInt32(0));
                    await result.NextResultAsync();
                }
                await processor.ProcessAsync(accepted.PayrollRunId,PayrollAccountingDocumentTypes.Accrual,fixture.BusinessId,default);
            }
            finally
            {
                await using var restore=new SqlCommand("""
                    UPDATE dbo.AccountingCostCenters SET IsActive=1 WHERE CostCenterId=@Center;
                    UPDATE center SET IsDefault=1 FROM dbo.AccountingCostCenters center
                      JOIN OPENJSON(@Centers) WITH(CostCenterId uniqueidentifier) original ON original.CostCenterId=center.CostCenterId;
                    UPDATE assignment SET IsActive=1 FROM dbo.AccountingCostCenterAssignments assignment
                      JOIN OPENJSON(@Assignments) WITH(AssignmentId uniqueidentifier) original ON original.AssignmentId=assignment.AssignmentId;
                    """,centers);
                restore.Parameters.AddWithValue("@Center",center.CostCenterId);restore.Parameters.AddWithValue("@Centers",defaultCenters);
                restore.Parameters.AddWithValue("@Assignments",assignments);await restore.ExecuteNonQueryAsync();
            }
        }
        await AssertBalancedEntryAsync(firstRun.PayrollRunId,PayrollAccountingDocumentTypes.Accrual);
        var accountingActor=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{AccountingPermissionCodes.Read});
        var entry=await accounting.GetEntryAsync(accountingActor,firstRun.PayrollRunId);
        Assert.NotNull(entry);
        Assert.Contains(entry.Lines,line=>line.AccountId==currentExpense.AccountId&&line.Debit==3_000_000&&line.CostCenterId==center.CostCenterId);
        Assert.DoesNotContain(entry.Lines,line=>line.AccountId==firstExpense.AccountId);
        Assert.Contains(entry.Lines,line=>line.AccountId==firstNet.AccountId&&line.PartyId==person&&line.Credit==firstRun.NetPayable);
        Assert.Contains(entry.Lines,line=>line.PartyId==configuration.HealthPartyId&&line.Credit==120_000);
        mappings[1]=mappings[1] with{CreditAccountId=changedNet.AccountId};
        await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{profile.ProfileId}",Update(profile,mappings));
        var batch=Guid.NewGuid();var paymentRequest=new CreatePayrollPaymentBatchRequest(batch,firstRun.PayrollRunId,
            Option(PayrollCatalogCodes.PaymentMethod,"Cash"),new(2026,11,30),"PROFILE-CASH-PAY");
        var paid=await PostAsync<PayrollPaymentBatchView>(client,"/api/commerce/v1/payroll/payments",paymentRequest);
        Assert.Equal(firstRun.NetPayable,paid.TotalAmount);await AssertBalancedEntryAsync(batch,PayrollAccountingDocumentTypes.Payment);
        var paymentEntry=(await accounting.GetEntryAsync(accountingActor,batch))!;
        Assert.Contains(paymentEntry.Lines,line=>line.AccountId==firstNet.AccountId&&line.Debit==firstRun.NetPayable&&line.CostCenterId==center.CostCenterId);
        Assert.DoesNotContain(paymentEntry.Lines,line=>line.AccountId==changedNet.AccountId);
        await PostAsync<PayrollPaymentBatchView>(client,"/api/commerce/v1/payroll/payments",paymentRequest);
        await processor.ProcessAsync(firstRun.PayrollRunId,PayrollAccountingDocumentTypes.Accrual,fixture.BusinessId,default);
        Assert.Equal(entry.EntryId,(await accounting.GetEntryAsync(accountingActor,firstRun.PayrollRunId))!.EntryId);
        Assert.Equal(paymentEntry.EntryId,(await accounting.GetEntryAsync(accountingActor,batch))!.EntryId);
        var nextRun=await Calculate(12);
        Assert.Equal(150_000,Assert.Single(nextRun.Employees).Lines.Single(row=>row.ConceptCode=="HEALTH_EMPLOYEE").Amount);
        Assert.Equal(configuration.AccountingProfileId,(await GetAsync<PayrollEmploymentPage>(client,
            $"/api/commerce/v1/payroll/employments?page=1&pageSize=1&employmentId={employmentId}")).Items[0].Configuration!.Values.AccountingProfileId);
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{futureRules.RuleSetId}/retire",new{rowVersion=futureRules.RowVersion});
        await Assert.ThrowsAsync<PayrollConflictException>(()=>store.ApproveRunAsync(actor,nextRun.PayrollRunId,"retired-profile",nextRun.RowVersion,default));
        await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(actor,nextRun.PayrollRunId));
        var firstRunHistory=await service.GetRunAsync(actor,firstRun.PayrollRunId);
        Assert.Equal(firstRun.NetPayable,firstRunHistory.NetPayable);Assert.Equal("Approved",firstRunHistory.Status);
    }
}
