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
    public async Task New_profile_saves_its_parameters_atomically_without_selecting_another_configuration()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        SavePayrollProfileRequest Request()
        {
            var id=Guid.NewGuid();var ruleId=Guid.NewGuid();
            return new(id,"INLINE"+id.ToString("N")[..12],"Perfil con porcentajes propios",ruleId,true,null,[])
            {
                Configuration=new(ruleId,"CO","IGNORED","Nombre sustituido por el del perfil",new(2026,1,1),null,
                    "Parámetros sintéticos, no tarifas legales",Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray(),null)
            };
        }
        var request=Request();PayrollProfileView saved;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("payroll.",StringComparison.Ordinal)))
        {
            saved=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{request.ProfileId}",request);
            Assert.Equal(6,commands.Count);
        }
        Assert.NotNull(saved.CreatedRuleSet);Assert.Equal("Approved",saved.CreatedRuleSet.Status);
        Assert.Equal($"PROFILE-{saved.ProfileId:N}".ToUpperInvariant(),saved.CreatedRuleSet.Code);Assert.Equal(saved.Name,saved.CreatedRuleSet.Name);
        Assert.Equal(Rules().Count,saved.CreatedRuleSet.Parameters.Count);
        Assert.Equal(saved.RuleSetId,saved.CreatedRuleSet.RuleSetId);
        var nextId=Guid.NewGuid();var changed=request with{RowVersion=saved.RowVersion,Name="Perfil editado en la misma ficha",
            Configuration=request.Configuration! with{RuleSetId=nextId,Code="OTHER-SERIES",CountryCode="PE",EffectiveFrom=new(2027,1,1),
                Parameters=request.Configuration!.Parameters.Select(row=>row.Code==PayrollRuleParameterCodes.EmployeeHealthRate?row with{NumericValue=.05m}:row).ToArray()}};
        PayrollProfileView updated;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("payroll.",StringComparison.Ordinal)))
        {
            updated=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{saved.ProfileId}",changed);
            Assert.Equal(7,commands.Count);
        }
        Assert.Equal(saved.RuleSetId,updated.RuleSetId);Assert.Equal(nextId,updated.CreatedRuleSet!.RuleSetId);
        Assert.Equal(saved.CreatedRuleSet.Code,updated.CreatedRuleSet.Code);Assert.Equal("CO",updated.CreatedRuleSet.CountryCode);
        Assert.Equal(.05m,updated.CreatedRuleSet.Parameters.Single(row=>row.Code==PayrollRuleParameterCodes.EmployeeHealthRate).NumericValue);
        var history=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{saved.ProfileId}/rule-sets?page=1&pageSize=1");
        Assert.Equal(2,history.TotalCount);Assert.Equal(nextId,Assert.Single(history.Items).RuleSetId);
        using(var stale=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{saved.ProfileId}",changed))
            Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var correctionId=Guid.NewGuid();
        var correction=changed with{RowVersion=updated.RowVersion,Configuration=changed.Configuration! with{
            RuleSetId=correctionId,Parameters=changed.Configuration!.Parameters.Select(row=>
                row.Code==PayrollRuleParameterCodes.EmployeeHealthRate?row with{NumericValue=.06m}:row).ToArray()}};
        var corrected=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{saved.ProfileId}",correction);
        Assert.Equal(correctionId,corrected.CreatedRuleSet!.RuleSetId);
        Assert.Equal(.06m,corrected.CreatedRuleSet.Parameters.Single(row=>row.Code==PayrollRuleParameterCodes.EmployeeHealthRate).NumericValue);
        history=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{saved.ProfileId}/rule-sets?page=1&pageSize=20");
        Assert.Equal(3,history.TotalCount);Assert.Equal(correctionId,history.Items[0].RuleSetId);
        Assert.Equal(.05m,history.Items.Single(row=>row.RuleSetId==nextId).Parameters.Single(row=>row.Code==PayrollRuleParameterCodes.EmployeeHealthRate).NumericValue);
        await AssertProfileResolutionAsync(saved.ProfileId,new(2026,12,1),saved.RuleSetId);
        await AssertProfileResolutionAsync(saved.ProfileId,new(2027,1,1),correctionId);
        var broken=Request();broken=broken with{Mappings=[new(Guid.NewGuid(),null,null,Guid.NewGuid(),new(2026,1,1),null)]};
        using var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{broken.ProfileId}",broken);
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var check=new SqlCommand("""
            SELECT(SELECT COUNT(*) FROM payroll.AccountingProfiles WHERE ProfileId=@Profile)+
              (SELECT COUNT(*) FROM payroll.RuleSets WHERE RuleSetId=@Rules)+
              (SELECT COUNT(*) FROM payroll.RuleParameters WHERE RuleSetId=@Rules);
            """,connection);
        check.Parameters.AddWithValue("@Profile",broken.ProfileId);check.Parameters.AddWithValue("@Rules",broken.RuleSetId);
        Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync()));
        var incomplete=Request();incomplete=incomplete with{Configuration=incomplete.Configuration! with{Parameters=[]}};
        using var missing=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{incomplete.ProfileId}",incomplete);
        Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        var precise=Request();precise=precise with{Configuration=precise.Configuration! with{
            Parameters=precise.Configuration!.Parameters.Select(row=>row.Code==PayrollRuleParameterCodes.EmployeeHealthRate?row with{NumericValue=.0400000001m}:row).ToArray()}};
        using var precision=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{precise.ProfileId}",precise);
        Assert.Equal(HttpStatusCode.BadRequest,precision.StatusCode);
        var competing=Request();
        var responses=await Task.WhenAll(client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{competing.ProfileId}",competing),
            client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{competing.ProfileId}",competing));
        try
        {
            Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.OK);
            Assert.Single(responses,response=>response.StatusCode==HttpStatusCode.Conflict);
        }
        finally{foreach(var response in responses)response.Dispose();}
        check.Parameters["@Profile"].Value=competing.ProfileId;check.Parameters["@Rules"].Value=competing.RuleSetId;
        Assert.Equal(2+Rules().Count,Convert.ToInt32(await check.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Editing_a_shared_profile_keeps_other_profiles_and_earlier_periods_on_their_configured_base()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var rulesId=Guid.NewGuid();
        var draft=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{rulesId}",
            new SavePayrollRuleSetRequest(rulesId,"CO","BASE"+rulesId.ToString("N")[..12],"Base compartida sintética",new(2026,1,1),null,
                "Datos de prueba",Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray(),null));
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{rulesId}/approve",new{rowVersion=draft.RowVersion});
        async Task<PayrollProfileView> Profile()
        {
            var id=Guid.NewGuid();
            return await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{id}",
                new SavePayrollProfileRequest(id,id.ToString("N"),"Perfil de prueba",rulesId,true,null,[]));
        }
        var first=await Profile();var neighbor=await Profile();var ownId=Guid.NewGuid();
        await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{first.ProfileId}",
            new SavePayrollProfileRequest(first.ProfileId,first.Code,first.Name,first.RuleSetId,true,first.RowVersion,[]){
                Configuration=new(ownId,"CO",draft.Code,first.Name,new(2027,1,1),null,"Corrección del perfil",
                    Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,
                        row.Key==PayrollRuleParameterCodes.EmployeeHealthRate?.05m:row.Value,"Value",null)).ToArray(),null)});
        await AssertProfileResolutionAsync(first.ProfileId,new(2026,12,1),rulesId);
        await AssertProfileResolutionAsync(first.ProfileId,new(2027,1,1),ownId);
        await AssertProfileResolutionAsync(neighbor.ProfileId,new(2027,1,1),rulesId);
        var history=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{first.ProfileId}/rule-sets?page=1&pageSize=20");
        Assert.Equal(2,history.TotalCount);Assert.Equal(ownId,history.Items[0].RuleSetId);
    }

    [Fact]
    public async Task Editing_a_profile_with_retired_configuration_publishes_a_revision_without_restoring_the_old_values()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var profileId=Guid.NewGuid();var rulesId=Guid.NewGuid();
        var parameters=Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray();
        var saved=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{profileId}",
            new SavePayrollProfileRequest(profileId,"RET"+profileId.ToString("N")[..12],"Perfil retirado de prueba",rulesId,true,null,[]){
                Configuration=new(rulesId,"CO","IGNORED","Configuración sintética",new(2026,1,1),null,"Datos sintéticos",parameters,null)});
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{rulesId}/retire",new{rowVersion=saved.CreatedRuleSet!.RowVersion});
        var revision=Guid.NewGuid();
        var updated=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{profileId}",
            new SavePayrollProfileRequest(profileId,saved.Code,saved.Name,rulesId,true,saved.RowVersion,[]){
                Configuration=new(revision,"CO","IGNORED",saved.Name,new(2026,1,1),null,"Corrección explícita",parameters,null)});
        Assert.Equal(rulesId,updated.RuleSetId);
        Assert.Equal(revision,updated.CreatedRuleSet!.RuleSetId);
        Assert.Equal("Approved",updated.CreatedRuleSet.Status);
        await AssertProfileResolutionAsync(profileId,new(2026,1,1),revision);
        var history=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{profileId}/rule-sets?page=1&pageSize=20");
        Assert.Equal("Retired",history.Items.Single(item=>item.RuleSetId==rulesId).Status);
        var other=Guid.NewGuid();
        using var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/profiles/{other}",
            new SavePayrollProfileRequest(other,"NEW"+other.ToString("N")[..12],"Otro perfil",rulesId,true,null,[]));
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }

    private async Task AssertProfileResolutionAsync(Guid profile,DateOnly from,Guid expected)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("SELECT RuleSetId,Issue FROM payroll.ProfileRuleConfiguration(@Tenant,@Profile,@From,@From);",connection);
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId);command.Parameters.AddWithValue("@Profile",profile);
        command.Parameters.AddWithValue("@From",from.ToDateTime(TimeOnly.MinValue));
        await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
        Assert.Equal(expected,reader.GetGuid(0));Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task Payroll_profile_pages_remain_bounded_with_500_profiles()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var prefix="PERF"+Guid.NewGuid().ToString("N")[..12];
        var id=Guid.NewGuid();
        var draft=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{id}",
            new SavePayrollRuleSetRequest(id,"CO",prefix,"Parámetros sintéticos de rendimiento",new(2026,1,1),null,
                "Datos sintéticos de prueba",Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray(),null));
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{id}/approve",new{rowVersion=draft.RowVersion});
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed=new SqlCommand("""
                INSERT payroll.AccountingProfiles(ProfileId,TenantId,Code,Name,RuleSetId,IsActive,UpdatedBy,UpdatedAt)
                SELECT NEWID(),@Tenant,@Prefix+RIGHT(N'0000'+CONVERT(nvarchar(4),n),4),N'Perfil '+CONVERT(nvarchar(4),n),@Rules,1,@User,SYSUTCDATETIME()
                FROM(SELECT TOP(500) ROW_NUMBER() OVER(ORDER BY a.n,b.n,c.n) n
                  FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) a(n)
                  CROSS JOIN(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) b(n)
                  CROSS JOIN(VALUES(0),(1),(2),(3),(4)) c(n)) generated;
                """,connection);
            seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Prefix",prefix);
            seed.Parameters.AddWithValue("@Rules",id);seed.Parameters.AddWithValue("@User",fixture.UserId);
            Assert.Equal(500,await seed.ExecuteNonQueryAsync());
        }
        var samples=new List<double>();
        for(var sample=0;sample<20;sample++)
        {
            using var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter:command=>command.CommandText.Contains("FROM payroll.AccountingProfiles",StringComparison.Ordinal));
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var page=await GetAsync<PayrollProfilePage>(client,$"/api/commerce/v1/payroll/profiles?page={sample%2+1}&pageSize=50&search={prefix}&sortBy=code&sortDirection=asc");
            samples.Add(clock.Elapsed.TotalMilliseconds);
            Assert.Equal(1,commands.Count);Assert.Equal(500,page.TotalCount);Assert.Equal(10,page.TotalPages);
            Assert.Equal(50,page.Items.Count);Assert.All(page.Items,row=>Assert.Empty(row.Mappings));
            Assert.Equal(prefix+(sample%2==0?"0001":"0051"),page.Items[0].Code);
        }
        var p95=samples.Order().ElementAt(18);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"payroll-profile-page-performance.json"),
            System.Text.Json.JsonSerializer.Serialize(new{profiles=500,pageSize=50,commands=1,p95,samples}));
        Assert.True(p95<1000,$"La página de perfiles superó su presupuesto: {p95:F1} ms.");
    }

    [Theory]
    [InlineData("code","asc")]
    [InlineData("code","desc")]
    [InlineData("name","asc")]
    [InlineData("name","desc")]
    [InlineData("status","asc")]
    [InlineData("status","desc")]
    public async Task Payroll_profile_pages_apply_server_order_before_limiting(string sortBy,string direction)
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var prefix="SORT"+Guid.NewGuid().ToString("N")[..12];
        var ruleId=Guid.NewGuid();
        var draft=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{ruleId}",
            new SavePayrollRuleSetRequest(ruleId,"CO",prefix,"Parámetros sintéticos de ordenación",new(2026,1,1),null,
                "Datos sintéticos de prueba",Rules().Select(row=>new SavePayrollRuleParameterRequest(row.Key,row.Value,"Value",null)).ToArray(),null));
        var rules=await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{ruleId}/approve",new{rowVersion=draft.RowVersion});
        var alphaId=Guid.NewGuid();
        var alpha=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{alphaId}",
            new SavePayrollProfileRequest(alphaId,prefix+"-A","Zulu "+prefix,rules.RuleSetId,true,null,[]));
        var zuluId=Guid.NewGuid();
        var zulu=await PutAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{zuluId}",
            new SavePayrollProfileRequest(zuluId,prefix+"-Z","Alpha "+prefix,rules.RuleSetId,false,null,[]));
        var expected=sortBy=="code"?alpha:zulu;
        if(direction=="desc")expected=expected==alpha?zulu:alpha;
        PayrollProfilePage page;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("FROM payroll.AccountingProfiles",StringComparison.Ordinal)))
        {
            page=await GetAsync<PayrollProfilePage>(client,$"/api/commerce/v1/payroll/profiles?page=1&pageSize=1&search={prefix}&sortBy={sortBy}&sortDirection={direction}");
            Assert.Equal(1,commands.Count);
        }
        Assert.Equal(2,page.TotalCount);Assert.Equal(2,page.TotalPages);
        Assert.Equal(expected.ProfileId,Assert.Single(page.Items).ProfileId);Assert.Empty(page.Items[0].Mappings);
        var second=await GetAsync<PayrollProfilePage>(client,$"/api/commerce/v1/payroll/profiles?page=2&pageSize=1&search={prefix}&sortBy={sortBy}&sortDirection={direction}");
        Assert.NotEqual(expected.ProfileId,Assert.Single(second.Items).ProfileId);
        using var invalid=await client.GetAsync("/api/commerce/v1/payroll/profiles?sortBy=unrecognized");
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }

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
        var localDraftId=Guid.NewGuid();
        var localDraft=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{localDraftId}",
            new SavePayrollRuleSetRequest(localDraftId,firstRules.CountryCode,firstRules.Code,"Borrador de la empresa con fecha de la base general",
                firstRules.EffectiveFrom,null,"Datos sintéticos para comprobar agrupación y alcance",
                firstRules.Parameters.Select(row=>new SavePayrollRuleParameterRequest(row.Code,row.NumericValue,row.UnitCode,row.Description)).ToArray(),null));
        Assert.False(localDraft.IsGlobal);
        var center=await PostAsync<AccountingCostCenterView>(client,"/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(Guid.NewGuid(),fixture.TenantId,"P"+Guid.NewGuid().ToString("N")[..20],"Centro contractual de prueba",null,false));
        configuration=configuration with{CostCenterId=center.CostCenterId};
        var profile=await GetAsync<PayrollProfileView>(client,$"/api/commerce/v1/payroll/profiles/{configuration.AccountingProfileId}");
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("DECLARE @Country",StringComparison.Ordinal)))
        {
            var versions=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{profile.ProfileId}/rule-sets?page=1&pageSize=1");
            Assert.Equal(2,versions.TotalCount);Assert.Single(versions.Items);Assert.Equal(1,commands.Count);
            var other=await GetAsync<PayrollRuleSetPage>(client,$"/api/commerce/v1/payroll/profiles/{profile.ProfileId}/rule-sets?page=2&pageSize=1");
            Assert.Single(other.Items);Assert.Equal(2,commands.Count);
            var all=versions.Items.Concat(other.Items).ToArray();
            Assert.True(all.Single(row=>row.RuleSetId==firstRules.RuleSetId).IsGlobal);
            Assert.All(all,row=>Assert.Equal(firstRules.Parameters.Count,row.Parameters.Count));
            Assert.NotEqual(versions.Items[0].RuleSetId,other.Items[0].RuleSetId);
        }
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
        var supervisor=await CreateEmployeePartyAsync(Guid.NewGuid().ToString("N"));
        configuration=configuration with{Details=new("Auxiliar de prueba","Sede de prueba",new(2026,6,30),null,
            new(2026,2,28),"ARL-TEST",supervisor,new(2025,12,31),2.5m)};
        var employmentRequest=new SavePayrollEmploymentRequest(employmentId,person,fixture.BusinessId,null,
            Option(PayrollCatalogCodes.ContractType,"Indefinite"),Option(PayrollCatalogCodes.SalaryType,"Ordinary"),
            Option(PayrollCatalogCodes.PayFrequency,"Monthly"),Option(PayrollCatalogCodes.RiskClass,"I"),
            Option(PayrollCatalogCodes.WorkerType,"01"),Option(PayrollCatalogCodes.WorkerSubtype,"00"),Option(PayrollCatalogCodes.PaymentMethod,"Cash"),
            "PROFILE-"+employmentId.ToString("N"),new(2026,1,1),null,3_000_000,null,null,null,null,null,true,null,
            AppliesTransportAllowance:false,Configuration:configuration);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{Configuration=configuration with{HealthPartyId=person}}))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{Configuration=configuration with{Details=configuration.Details! with{SupervisorPartyId=person}}}))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{Configuration=configuration with{Details=configuration.Details! with{SupervisorPartyId=Guid.NewGuid()}}}))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var employment=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",employmentRequest);
        Assert.Equal(configuration,employment.Configuration!.Values);Assert.Equal("EPS de prueba",employment.Configuration.HealthName);
        Assert.False(string.IsNullOrWhiteSpace(employment.Configuration.SupervisorName));
        var preserved=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{RowVersion=employment.RowVersion,Configuration=configuration with{Details=null}});
        Assert.Equal(configuration.Details,preserved.Configuration!.Values.Details);employment=preserved;
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
        Assert.False(futureRules.IsGlobal);
        // Exercise malformed scalar evidence before consuming the real draft.
        // The batched projection must retain JSON_VALUE's length semantics and
        // must reject a missing snapshot version rather than accept SQL UNKNOWN.
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var read=new SqlCommand("SELECT EmployeeSnapshotJson FROM payroll.RunEmployees WHERE PayrollRunId=@Run;",connection);
            read.Parameters.AddWithValue("@Run",firstRun.PayrollRunId);
            var original=(string)(await read.ExecuteScalarAsync())!;
            using(var snapshot=System.Text.Json.JsonDocument.Parse(original))
            {
                var details=snapshot.RootElement.GetProperty("Configuration").GetProperty("Values").GetProperty("Details");
                Assert.Equal("Auxiliar de prueba",details.GetProperty("JobTitle").GetString());
                Assert.Equal(2.5m,details.GetProperty("VacationDaysUsed").GetDecimal());
                Assert.True(snapshot.RootElement.GetProperty("PaysPensionContributions").GetBoolean());
            }
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
        employmentRequest=employmentRequest with{WorkerSubtypeOptionId=Option(PayrollCatalogCodes.WorkerSubtype,"01"),
            Configuration=configuration with{Details=configuration.Details! with{JobTitle="Cargo posterior"}}};
        employment=await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",
            employmentRequest with{RowVersion=employment.RowVersion});
        var nextRun=await Calculate(12);
        Assert.Equal(150_000,Assert.Single(nextRun.Employees).Lines.Single(row=>row.ConceptCode=="HEALTH_EMPLOYEE").Amount);
        Assert.DoesNotContain(Assert.Single(nextRun.Employees).Lines,line=>line.ConceptCode is "PENSION_EMPLOYEE" or "PENSION_EMPLOYER");
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using(var snapshot=new SqlCommand("""
                SELECT JSON_VALUE(EmployeeSnapshotJson,'$.Configuration.Values.Details.JobTitle')
                FROM payroll.RunEmployees WHERE PayrollRunId=@Run;
                """,connection))
            {
                snapshot.Parameters.AddWithValue("@Run",firstRun.PayrollRunId);
                Assert.Equal("Auxiliar de prueba",await snapshot.ExecuteScalarAsync());
            }
            await using var change=new SqlCommand("UPDATE payroll.CatalogOptions SET MetadataCode=@Metadata WHERE OptionId=@Subtype;",connection);
            change.Parameters.AddWithValue("@Subtype",Option(PayrollCatalogCodes.WorkerSubtype,"01"));
            change.Parameters.AddWithValue("@Metadata","ChangedForTest");
            try
            {
                await change.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<PayrollConflictException>(()=>store.ApproveRunAsync(actor,nextRun.PayrollRunId,
                    "changed-pension-trait",nextRun.RowVersion,default));
            }
            finally{change.Parameters["@Metadata"].Value="PensionExempt";await change.ExecuteNonQueryAsync();}
        }
        Assert.Equal(configuration.AccountingProfileId,(await GetAsync<PayrollEmploymentPage>(client,
            $"/api/commerce/v1/payroll/employments?page=1&pageSize=1&employmentId={employmentId}")).Items[0].Configuration!.Values.AccountingProfileId);
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{futureRules.RuleSetId}/retire",new{rowVersion=futureRules.RowVersion});
        await Assert.ThrowsAsync<PayrollConflictException>(()=>store.ApproveRunAsync(actor,nextRun.PayrollRunId,"retired-profile",nextRun.RowVersion,default));
        await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(actor,nextRun.PayrollRunId));
        var firstRunHistory=await service.GetRunAsync(actor,firstRun.PayrollRunId);
        Assert.Equal(firstRun.NetPayable,firstRunHistory.NetPayable);Assert.Equal("Approved",firstRunHistory.Status);
    }
}
