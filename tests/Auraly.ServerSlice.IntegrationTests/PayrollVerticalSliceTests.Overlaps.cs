using System.Diagnostics;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    [Fact]
    public async Task Regular_approval_serializes_overlapping_periods_and_retains_the_worker_identity_after_a_contract_change()
    {
        using var client = fixture.CreateAdminClient(PayrollPermissionCodes.Read,
            PayrollPermissionCodes.Manage, PayrollPermissionCodes.Configure, PayrollPermissionCodes.Calculate);
        var options = await GetAsync<PayrollWorkspaceOptions>(client, "/api/commerce/v1/payroll/options");
        Guid Option(string catalog, string code) => options.Catalogs[catalog].Single(value => value.Code == code).OptionId;
        await EnsureCalculationConceptsAsync(client, options);
        var ruleId = Guid.NewGuid();
        var rule = await PutAsync<PayrollRuleSetView>(client, $"/api/commerce/v1/payroll/rule-sets/{ruleId:D}",
            new SavePayrollRuleSetRequest(ruleId, "CO", "OVERLAP-" + ruleId.ToString("N"), "Reglas de concurrencia",
                new(2026, 1, 1), null, "Prueba de períodos regulares",
                Rules().Select(value => new SavePayrollRuleParameterRequest(value.Key, value.Value, "Value", null)).ToArray(), null));
        await PostAsync<PayrollRuleSetView>(client, $"/api/commerce/v1/payroll/rule-sets/{ruleId:D}/approve", new { rowVersion = rule.RowVersion });
        var partyId = await CreateEmployeePartyAsync(Guid.NewGuid().ToString("N"));
        var employmentId = Guid.NewGuid();
        var employmentRequest = new SavePayrollEmploymentRequest(employmentId, partyId, fixture.BusinessId, null,
            Option(PayrollCatalogCodes.ContractType, "Indefinite"), Option(PayrollCatalogCodes.SalaryType, "Ordinary"),
            Option(PayrollCatalogCodes.PayFrequency, "Monthly"), Option(PayrollCatalogCodes.RiskClass, "I"),
            Option(PayrollCatalogCodes.WorkerType, "01"), Option(PayrollCatalogCodes.WorkerSubtype, "00"),
            Option(PayrollCatalogCodes.PaymentMethod, "Cash"), "OVERLAP-" + employmentId.ToString("N"),
            new(2026, 1, 1), null, 3_000_000, null, null, null, null, null, true, null, Configuration:await CreateProfileFixtureAsync(ruleId));
        var employment = await PutAsync<PayrollEmploymentView>(client,
            $"/api/commerce/v1/payroll/employments/{employmentId:D}", employmentRequest);
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PayrollService>();
        var store = scope.ServiceProvider.GetRequiredService<IPayrollStore>();
        var user = new PayrollUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PayrollPermissionCodes.Read, PayrollPermissionCodes.Calculate, PayrollPermissionCodes.Approve });

        async Task<PayrollRunView> Calculate(DateOnly start, DateOnly end, Guid frequency, Guid selectedEmployment)
        {
            var draft = await service.CreateRunAsync(user, new(Guid.NewGuid(), fixture.BusinessId,
                frequency, "Regular", null, start, end, end.AddDays(7)));
            return await service.CalculateRunAsync(user, draft.PayrollRunId,
                new CalculatePayrollRunRequest(draft.RowVersion, false, [new(selectedEmployment, true)]));
        }
        async Task<(PayrollRunAcceptance? Acceptance, PayrollConflictException? Conflict)> Approve(PayrollRunView run)
        {
            try
            {
                return (await store.ApproveRunAsync(user, run.PayrollRunId,
                    "overlap-" + run.PayrollRunId.ToString("N"), run.RowVersion, default), null);
            }
            catch (PayrollConflictException error) { return (null, error); }
        }

        var monthly = Option(PayrollCatalogCodes.PayFrequency, "Monthly");
        var first = await Calculate(new(2026, 9, 1), new(2026, 9, 30), monthly, employmentId);
        // Existing drafts can have different boundaries; approval must protect
        // the actual overlap rather than relying on the exact-period unique key.
        var legacyId=await SeedLegacyDraftAsync(first,new(2026,9,29));
        var legacy=await store.GetRunAsync(user,legacyId,default);
        var second=await service.CalculateRunAsync(user,legacyId,
            new CalculatePayrollRunRequest(legacy.RowVersion,false,[new(employmentId,true)]));
        var transportId=Assert.Single(second.Employees).Lines.Single(line=>line.ConceptCode=="TRANSPORT").ConceptId;
        // Days are independent per concept: both drafts must be valid before
        // racing approval, otherwise the day guard can mask the overlap proof.
        second = await service.CalculateRunAsync(user, second.PayrollRunId,
            new CalculatePayrollRunRequest(second.RowVersion, WorkedDaysChanges: [new(employmentId, 29)]));
        second = await service.CalculateRunAsync(user, second.PayrollRunId,
            new CalculatePayrollRunRequest(second.RowVersion, WorkedDaysChanges: [new(employmentId,29,transportId)]));
        Assert.All(Assert.Single(second.Employees).ConceptWorkedDays,days=>Assert.Equal(29,days.BaseDays));
        (PayrollRunAcceptance? Acceptance, PayrollConflictException? Conflict)[] outcomes;
        var elapsed = Stopwatch.StartNew();
        int commands;
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            outcomes = await Task.WhenAll(Approve(first), Approve(second));
            elapsed.Stop(); commands = counter.Count;
        }
        Assert.Equal(6, commands);
        var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "payroll-overlap.json"),
            JsonSerializer.Serialize(new { commands, milliseconds = elapsed.Elapsed.TotalMilliseconds }));
        Assert.True(elapsed.Elapsed.TotalSeconds < 2, $"Two competing acceptances exceeded 2 s: {elapsed.Elapsed.TotalMilliseconds:F1} ms.");
        var accepted = Assert.Single(outcomes, result => result.Acceptance is not null).Acceptance!;
        Assert.Contains("superpuestas", Assert.Single(outcomes, result => result.Conflict is not null).Conflict!.Message);
        var winner = accepted.PayrollRunId == first.PayrollRunId ? first : second;
        var loser = accepted.PayrollRunId == first.PayrollRunId ? second : first;
        var replay = await Approve(winner);
        Assert.Null(replay.Conflict); Assert.True(replay.Acceptance!.IdempotentReplay);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var state = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Winner),
              (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Winner),
              (SELECT COUNT(*) FROM payroll.OutboxMessages WHERE AggregateId=@Winner),
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Loser),
              (SELECT Status FROM payroll.Runs WHERE PayrollRunId=@Loser),
              (SELECT CONVERT(date,OccurredAt) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Winner);
            """, connection))
        {
            state.Parameters.AddWithValue("@Winner", winner.PayrollRunId); state.Parameters.AddWithValue("@Loser", loser.PayrollRunId);
            await using var reader = await state.ExecuteReaderAsync(); await reader.ReadAsync();
            Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(1, reader.GetInt32(1)); Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3)); Assert.Equal("Calculated", reader.GetString(4));
            Assert.Equal(winner.PeriodEnd, DateOnly.FromDateTime(reader.GetDateTime(5)));
        }
        var next = await Calculate(new(2026, 10, 1), new(2026, 10, 31), monthly, employmentId);
        Assert.Null((await Approve(next)).Conflict);

        // Historical regular accruals remain attached to the person even after
        // replacing the employment and its payment frequency.
        await PutAsync<PayrollEmploymentView>(client, $"/api/commerce/v1/payroll/employments/{employmentId:D}",
            employmentRequest with { IsActive = false, RowVersion = employment.RowVersion });
        var replacementId = Guid.NewGuid();
        var biweekly = Option(PayrollCatalogCodes.PayFrequency, "Semimonthly");
        await PutAsync<PayrollEmploymentView>(client, $"/api/commerce/v1/payroll/employments/{replacementId:D}",
            employmentRequest with { EmploymentId = replacementId, ContractNumber = "REPLACED-" + replacementId.ToString("N"),
                PayFrequencyOptionId = biweekly });
        var replacement = await Calculate(new(2026, 9, 16), new(2026, 9, 30), biweekly, replacementId);
        Assert.Contains("superpuestas", (await Approve(replacement)).Conflict!.Message);
    }
    // Earlier releases accepted arbitrary ranges. Seed that historical draft
    // explicitly; the current creation command must reject it, while reading,
    // calculating and overlap protection continue to operate on its real dates.
    private async Task<Guid> SeedLegacyDraftAsync(PayrollRunView template,DateOnly end)
    {
        var id=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("""
            INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,
              PeriodStart,PeriodEnd,PaymentDate,Status,CalculationVersion,TotalEarnings,TotalDeductions,
              TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
            SELECT @Id,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,N'Regular',PeriodStart,@End,
              PaymentDate,N'Draft',0,0,0,0,0,0,CreatedBy,SYSDATETIMEOFFSET()
            FROM payroll.Runs WHERE PayrollRunId=@Original AND TenantId=@Tenant;
            """,connection);
        command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@End",end.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Original",template.PayrollRunId);command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        Assert.Equal(1,await command.ExecuteNonQueryAsync());return id;
    }

}
