using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Payroll")]
public sealed partial class PayrollVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    [Trait("EngineCertification", "EndToEnd")]
    public async Task Payroll_flows_from_employee_configuration_to_accounting_fiscal_and_reporting()
    {
        var partyId = await CreateEmployeePartyAsync();
        using var client = fixture.CreateAdminClient(
            PayrollPermissionCodes.Read, PayrollPermissionCodes.Manage,
            PayrollPermissionCodes.Calculate, PayrollPermissionCodes.Approve,
             PayrollPermissionCodes.Pay, PayrollPermissionCodes.Configure,
             PayrollPermissionCodes.Fiscal, AccountingPermissionCodes.Read,
             AccountingPermissionCodes.Configure,
             PartyWorkspacePermissionCodes.Read);

        var options = await GetAsync<PayrollWorkspaceOptions>(client,
            "/api/commerce/v1/payroll/options");
        var employeeOptions = await GetAsync<PartyWorkspacePage>(client,
            $"/api/commerce/v1/parties?page=1&pageSize=25&role=Employee&partyId={partyId:D}");
        var employeeOption = Assert.Single(employeeOptions.Items,
            value => value.PartyId == partyId);
        Assert.NotNull(employeeOption.EmployeeId);
        var catalog = options.Catalogs;
        Guid Option(string catalogCode, string code) => catalog[catalogCode]
            .Single(value => value.Code == code).OptionId;

        var legacy = await CreateLegacyInvalidEmploymentAsync(
            Option(PayrollCatalogCodes.ContractType, "Indefinite"),
            Option(PayrollCatalogCodes.SalaryType, "Ordinary"),
            Option(PayrollCatalogCodes.PayFrequency, "Monthly"),
            Option(PayrollCatalogCodes.RiskClass, "I"),
            Option(PayrollCatalogCodes.WorkerType, "01"),
            Option(PayrollCatalogCodes.WorkerSubtype, "00"),
            Option(PayrollCatalogCodes.PaymentMethod, "Transfer"));
        var deactivatedLegacy = await PutAsync<PayrollEmploymentView>(client,
            $"/api/commerce/v1/payroll/employments/{legacy.EmploymentId:D}",
            new SavePayrollEmploymentRequest(legacy.EmploymentId, legacy.PartyId,
                fixture.BusinessId, null,
                Option(PayrollCatalogCodes.ContractType, "Indefinite"),
                Option(PayrollCatalogCodes.SalaryType, "Ordinary"),
                Option(PayrollCatalogCodes.PayFrequency, "Monthly"),
                Option(PayrollCatalogCodes.RiskClass, "I"),
                Option(PayrollCatalogCodes.WorkerType, "01"),
                Option(PayrollCatalogCodes.WorkerSubtype, "00"),
                Option(PayrollCatalogCodes.PaymentMethod, "Transfer"),
                "LEGACY-INCOMPLETE", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 7, 31), 1_000_000m, null, null,
                null, null, null, false, legacy.RowVersion));
        Assert.False(deactivatedLegacy.IsActive);
        Assert.Null(deactivatedLegacy.EmployeeId);

        var ruleSetId = Guid.NewGuid();
        var ruleSet = await PutAsync<PayrollRuleSetView>(client,
            $"/api/commerce/v1/payroll/rule-sets/{ruleSetId:D}",
            new SavePayrollRuleSetRequest(ruleSetId, "CO", "CERT-2026", "Reglas certificación",
                new DateOnly(2026, 1, 1), null, "Prueba vertical versionada",
                Rules().Select(value => new SavePayrollRuleParameterRequest(
                    value.Key, value.Value, "Value", null)).ToArray(), null));
        ruleSet = await PostAsync<PayrollRuleSetView>(client,
            $"/api/commerce/v1/payroll/rule-sets/{ruleSetId:D}/approve",
            new { rowVersion = ruleSet.RowVersion });
        Assert.Equal("Approved", ruleSet.Status);

        using (var partialCoverage = await client.PostAsJsonAsync(
                   "/api/commerce/v1/payroll/runs",
                   new CreatePayrollRunRequest(Guid.NewGuid(), fixture.BusinessId,
                       Option(PayrollCatalogCodes.PayFrequency, "Monthly"), "Regular", null,
                       new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31), new DateOnly(2025, 12, 31))))
        {
            Assert.True(partialCoverage.IsSuccessStatusCode,await partialCoverage.Content.ReadAsStringAsync());
            Assert.Null((await partialCoverage.Content.ReadFromJsonAsync<PayrollRunView>())!.RuleSetId);
        }

        var concepts = await EnsureCalculationConceptsAsync(client,options);

        var loanConceptId = Guid.NewGuid();
        await PutAsync<PayrollConceptView>(client,
            $"/api/commerce/v1/payroll/concepts/{loanConceptId:D}",
            new SavePayrollConceptRequest(loanConceptId, "EMPLOYEE_LOAN", "Préstamo empleado",
                Option(PayrollCatalogCodes.ConceptNature, "Deduction"),
                Option(PayrollCatalogCodes.CalculationMethod, "FixedAmount"),
                Option(PayrollCatalogCodes.ConceptTreatment, "AuthorizedDeduction"), null,
                Option(PayrollCatalogCodes.AccountingCategory, "EmployeeLoansReceivable"), null,
                false, false, false, false, true,
                new DateOnly(2026, 1, 1), null, true, null));

        var employmentId = Guid.NewGuid();
        var employmentRequest = new SavePayrollEmploymentRequest(employmentId, partyId, fixture.BusinessId, null,
                Option(PayrollCatalogCodes.ContractType, "Indefinite"),
                Option(PayrollCatalogCodes.SalaryType, "Ordinary"),
                Option(PayrollCatalogCodes.PayFrequency, "Monthly"),
                Option(PayrollCatalogCodes.RiskClass, "I"),
                Option(PayrollCatalogCodes.WorkerType, "01"),
                Option(PayrollCatalogCodes.WorkerSubtype, "00"),
                Option(PayrollCatalogCodes.PaymentMethod, "Transfer"),
                "CERT-EMP-001", new DateOnly(2026, 8, 1), null, 3_000_000m,
                  null, null, Option(PayrollCatalogCodes.Bank, "Bancolombia"),
                  Option(PayrollCatalogCodes.BankAccountType, "Savings"),
                  "12345678901", true, null, Configuration:await CreateProfileFixtureAsync(ruleSetId));
        var employment = await PutAsync<PayrollEmploymentView>(client,
            $"/api/commerce/v1/payroll/employments/{employmentId:D}", employmentRequest);
        Assert.Equal(employeeOption.EmployeeId, employment.EmployeeId);

        var agreementId = Guid.NewGuid();
        await PutAsync<PayrollDeductionAgreementView>(client,
            $"/api/commerce/v1/payroll/deduction-agreements/{agreementId:D}",
            new SavePayrollDeductionAgreementRequest(agreementId, employmentId, loanConceptId,
                Option(PayrollCatalogCodes.DeductionAuthority, "WrittenAuthorization"), null,
                "AUTH-CERT-001", "https://evidence.auraly.test/payroll/auth-cert-001",
                new DateOnly(2026, 8, 1), null, 500_000m, 100_000m, 100, true, true, null));
        var payrollCenterId=Guid.NewGuid();
        var noveltyCenter=await PostAsync<AccountingCostCenterView>(client,"/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(payrollCenterId,fixture.TenantId,"PN"+Guid.NewGuid().ToString("N")[..18],"Centro de nómina",null,false));
        var noveltyRequest=new SavePayrollNoveltyRequest(Guid.NewGuid(), employmentId, loanConceptId,
            Option(PayrollCatalogCodes.NoveltyType, "Amount"), null, agreementId,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 0m, null,
            100_000m, "Cuota autorizada", "https://evidence.auraly.test/payroll/novelty"){CostCenterId=payrollCenterId};
        foreach(var invalid in new[]{
            noveltyRequest with {NoveltyId=Guid.NewGuid(),Quantity=-1},
            noveltyRequest with {NoveltyId=Guid.NewGuid(),CostCenterId=Guid.NewGuid()},
            noveltyRequest with {NoveltyId=Guid.NewGuid(),Quantity=0.0000001m},
            noveltyRequest with {NoveltyId=Guid.NewGuid(),NoveltyTypeOptionId=Option(PayrollCatalogCodes.NoveltyType,"Hours")}})
        {
            using var rejected=await client.PostAsJsonAsync("/api/commerce/v1/payroll/novelties",invalid);
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        }
        PayrollNoveltyView savedNovelty;
        using(var writes=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("INSERT payroll.Novelties",StringComparison.Ordinal) ||
                command.CommandText.Contains("SELECT method.Code,kind.Code",StringComparison.Ordinal)))
        using (var novelty = await client.PostAsJsonAsync("/api/commerce/v1/payroll/novelties",noveltyRequest))
        {
            Assert.True(novelty.IsSuccessStatusCode, await novelty.Content.ReadAsStringAsync());
            Assert.Equal(2,writes.Count);
            savedNovelty=(await novelty.Content.ReadFromJsonAsync<PayrollNoveltyView>())!;
            Assert.Equal(8,savedNovelty.RowVersion.Length);
            Assert.Equal(payrollCenterId,savedNovelty.CostCenterId);Assert.Equal(noveltyCenter.Name,savedNovelty.CostCenterName);
        }

        var invalidCalendarId=Guid.NewGuid();
        using(var invalidCalendar=await client.PostAsJsonAsync("/api/commerce/v1/payroll/runs",
            new CreatePayrollRunRequest(invalidCalendarId,fixture.BusinessId,
                Option(PayrollCatalogCodes.PayFrequency,"Monthly"),"Regular",null,
                new(2026,8,1),new(2026,8,30),new(2026,8,31))))
            Assert.Equal(HttpStatusCode.BadRequest,invalidCalendar.StatusCode);
        await using(var verifyCalendar=new SqlConnection(fixture.ConnectionString))
        {
            await verifyCalendar.OpenAsync();
            await using var absent=new SqlCommand("SELECT COUNT(*) FROM payroll.Runs WHERE PayrollRunId=@Id",verifyCalendar);
            absent.Parameters.AddWithValue("@Id",invalidCalendarId);Assert.Equal(0,await absent.ExecuteScalarAsync());
        }

        var runId = Guid.NewGuid();
        var run = await PostAsync<PayrollRunView>(client, "/api/commerce/v1/payroll/runs",
            new CreatePayrollRunRequest(runId, fixture.BusinessId,
                Option(PayrollCatalogCodes.PayFrequency, "Monthly"), "Regular", null,
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31)));
        var candidates = await GetAsync<PayrollRunEmploymentPage>(client,
            $"/api/commerce/v1/payroll/runs/{runId:D}/employments?page=1&pageSize=1&search=CERT-EMP&contractNumber=CERT-EMP-001&sortBy=contractNumber&sortDirection=desc");
        Assert.Equal(employmentId, Assert.Single(candidates.Items).EmploymentId);
        Assert.Equal(1, candidates.TotalCount); Assert.Equal(run.RowVersion, candidates.RowVersion);
        var filteredOut=await GetAsync<PayrollRunEmploymentPage>(client,
            $"/api/commerce/v1/payroll/runs/{runId:D}/employments?page=1&pageSize=1&costCenterId={Guid.NewGuid()}&workSite=NoExiste");
        Assert.Empty(filteredOut.Items);Assert.Equal(0,filteredOut.TotalCount);
        using(var invalidCenter=await client.GetAsync($"/api/commerce/v1/payroll/runs/{runId:D}/employments?costCenterId={Guid.Empty}"))
            Assert.Equal(HttpStatusCode.BadRequest,invalidCenter.StatusCode);
        using (var invalidSort = await client.GetAsync($"/api/commerce/v1/payroll/runs/{runId:D}/employments?sortBy=notAColumn"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);
        run = await PostAsync<PayrollRunView>(client,
            $"/api/commerce/v1/payroll/runs/{runId:D}/calculate",
            new CalculatePayrollRunRequest(run.RowVersion, SelectAll: false,
                SelectionChanges: [new(employmentId, true)]));
        Assert.Equal("Calculated", run.Status);
        Assert.Equal(100_000m + 240_000m, run.TotalDeductions);
        var fixedDeduction=Assert.Single(Assert.Single(run.Employees).Lines,line=>line.ConceptId==loanConceptId);
        Assert.Equal(0,fixedDeduction.Quantity);Assert.Equal(100_000,fixedDeduction.Amount);
        Assert.Equal(payrollCenterId,fixedDeduction.CostCenterId);Assert.Equal(noveltyCenter.Name,fixedDeduction.CostCenterName);
        Assert.Equal(noveltyRequest.Notes,fixedDeduction.Notes);Assert.Equal(noveltyRequest.EvidenceUrl,fixedDeduction.EvidenceUrl);
        await AssertNoveltyEditingAsync(client,noveltyRequest,savedNovelty,run);
        using (var withoutBody = await client.PostAsync($"/api/commerce/v1/payroll/runs/{runId:D}/calculate", null))
        {
            Assert.True(withoutBody.IsSuccessStatusCode, await withoutBody.Content.ReadAsStringAsync());
            run = (await withoutBody.Content.ReadFromJsonAsync<PayrollRunView>())!;
            Assert.Equal("Calculated", run.Status);
        }

        // Two calculated drafts can reference the same novelty. Only one may consume it.
        var overlappingId = await SeedLegacyDraftAsync(run,new(2026,8,30));
        var overlapping = await PostAsync<PayrollRunView>(client,
            $"/api/commerce/v1/payroll/runs/{overlappingId:D}/calculate", new { });
        await AssertIndividualReceiptAsync(client,run,"Calculated",false);

        // Approval must validate the actual remaining authority, not its earlier snapshot.
        var agreements = (await GetAsync<PayrollHistoryPage<PayrollDeductionAgreementSummary>>(client,
            $"/api/commerce/v1/payroll/deduction-agreements?page=1&pageSize=20&employmentId={employmentId}")).Items;
        var agreement = Assert.Single(agreements, item => item.DeductionAgreementId == agreementId);
        var agreementRequest = new SavePayrollDeductionAgreementRequest(agreementId,
            employmentId, loanConceptId, agreement.AuthorityOptionId, null,
            agreement.ReferenceNumber, agreement.EvidenceUrl, agreement.EffectiveFrom,
            agreement.EffectiveTo, 50_000m, 50_000m, agreement.Priority,
            agreement.MustProtectMinimumNetPay, true, agreement.RowVersion);
        var reducedAgreement = await PutAsync<PayrollDeductionAgreementView>(client,
            $"/api/commerce/v1/payroll/deduction-agreements/{agreementId:D}", agreementRequest);
        using (var invalidApproval = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/commerce/v1/payroll/runs/{runId:D}/approve")
               { Content = JsonContent.Create(new { rowVersion = run.RowVersion }) })
        {
            invalidApproval.Headers.Add("Idempotency-Key", $"cert-{runId:D}");
            using var rejected = await client.SendAsync(invalidApproval);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            var reason=await rejected.Content.ReadAsStringAsync();
            Assert.True(reason.Contains("saldo autorizado",StringComparison.Ordinal),reason);
        }
        var inactiveAgreement = await PutAsync<PayrollDeductionAgreementView>(client,
            $"/api/commerce/v1/payroll/deduction-agreements/{agreementId:D}",
            agreementRequest with { AuthorizedTotal = 500_000m, InstallmentAmount = 100_000m,
                IsActive = false, RowVersion = reducedAgreement.RowVersion });
        using (var inactiveApproval = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/commerce/v1/payroll/runs/{runId:D}/approve")
               { Content = JsonContent.Create(new { rowVersion = run.RowVersion }) })
        {
            inactiveApproval.Headers.Add("Idempotency-Key", $"cert-{runId:D}");
            using var rejected = await client.SendAsync(inactiveApproval);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Contains("autorización", await rejected.Content.ReadAsStringAsync());
        }
        await PutAsync<PayrollDeductionAgreementView>(client,
            $"/api/commerce/v1/payroll/deduction-agreements/{agreementId:D}",
            agreementRequest with { AuthorizedTotal = 500_000m, InstallmentAmount = 100_000m,
                IsActive = true, RowVersion = inactiveAgreement.RowVersion });

        using (var staleVersion = new HttpRequestMessage(HttpMethod.Post,$"/api/commerce/v1/payroll/runs/{runId:D}/approve")
               { Content = JsonContent.Create(new { rowVersion = run.RowVersion }) })
        {
            staleVersion.Headers.Add("Idempotency-Key",$"cert-{runId:D}");
            using var rejected=await client.SendAsync(staleVersion);
            Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
            Assert.Contains("huella",await rejected.Content.ReadAsStringAsync());
        }
        run=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{runId:D}/calculate",new{});
        run=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{runId:D}/calculate",
            new CalculatePayrollRunRequest(run.RowVersion,WorkedDaysChanges:[new(employmentId,30)]));
        Assert.True(run.IsPartialCalculation);Assert.Equal(1,run.EmployeeCount);Assert.Single(run.Employees);
        run=await AddZeroDayWorkerAsync(client,options,run,employmentRequest);

        await PutAsync<AccountingCostCenterAssignmentView>(client,
            "/api/commerce/v1/accounting/cost-center-assignments",
            new SaveAccountingCostCenterAssignmentRequest(Guid.NewGuid(), fixture.BusinessId,
                payrollCenterId, AccountingCostCenterOperationKinds.All, true));

        using var approveRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/payroll/runs/{runId:D}/approve")
        {
            Content = JsonContent.Create(new { rowVersion = run.RowVersion })
        };
        approveRequest.Headers.Add("Idempotency-Key", $"cert-{runId:D}");
        using (var approve = await client.SendAsync(approveRequest))
            Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        await AssertZeroNoveltiesConsumedAsync(run);
        var consumedNovelty=Assert.Single((await GetAsync<PayrollRunView>(client,
            $"/api/commerce/v1/payroll/runs/{runId:D}")).Employees.SelectMany(employee=>employee.Novelties),item=>item.NoveltyId==noveltyRequest.NoveltyId);
        Assert.Equal("Consumed",consumedNovelty.Status);
        using(var updateConsumed=await client.PutAsJsonAsync($"/api/commerce/v1/payroll/novelties/{noveltyRequest.NoveltyId:D}",
            noveltyRequest with {RowVersion=consumedNovelty.RowVersion}))
            Assert.Equal(HttpStatusCode.Conflict,updateConsumed.StatusCode);
        using(var voidConsumed=await client.PostAsJsonAsync($"/api/commerce/v1/payroll/novelties/{noveltyRequest.NoveltyId:D}/void",
            new {rowVersion=consumedNovelty.RowVersion}))
            Assert.Equal(HttpStatusCode.Conflict,voidConsumed.StatusCode);
        await AssertBalancedEntryAsync(runId, PayrollAccountingDocumentTypes.Accrual);
        await AssertPayrollCostCenterAsync(runId, payrollCenterId, noveltyCenter.Code, noveltyCenter.Name);

        using (var duplicate = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/commerce/v1/payroll/runs/{overlappingId:D}/approve")
               { Content = JsonContent.Create(new { rowVersion = overlapping.RowVersion }) })
        {
            duplicate.Headers.Add("Idempotency-Key", $"cert-{overlappingId:D}");
            using var rejected = await client.SendAsync(duplicate);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Contains("ya fue liquidada", await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal("Calculated", (await GetAsync<PayrollRunView>(client,
            $"/api/commerce/v1/payroll/runs/{overlappingId:D}")).Status);

        var listedRuns = (await GetAsync<PayrollRunPage>(client,
            "/api/commerce/v1/payroll/runs?page=1&pageSize=20")).Items;
        var listedRun = Assert.Single(listedRuns, value => value.PayrollRunId == runId);
        Assert.Equal("Approved", listedRun.Status);
        Assert.Equal(2, listedRun.EmployeeCount);

        var batchId = Guid.NewGuid();
        var paymentRequest = new CreatePayrollPaymentBatchRequest(batchId,runId,
            Option(PayrollCatalogCodes.PaymentMethod,"Transfer"),new DateOnly(2026,8,31),"BANK-CERT-001");
        await AssertPaymentRequiresPostedAccrualAsync(client,paymentRequest);
        var payment=await PostAsync<PayrollPaymentBatchView>(client, "/api/commerce/v1/payroll/payments",
            paymentRequest);
        Assert.Equal(1,payment.EmployeeCount);Assert.Equal(run.NetPayable,payment.TotalAmount);
        await AssertBalancedEntryAsync(batchId, PayrollAccountingDocumentTypes.Payment);
        await AssertPayrollCostCenterAsync(batchId, payrollCenterId, noveltyCenter.Code, noveltyCenter.Name);
        await AssertIndividualReceiptAsync(client,run,"Approved",true);

        // The standard accounting auxiliary must reconcile payroll by cost center.
        var entry = await GetAsync<AccountingEntryView>(client,
            $"/api/commerce/v1/accounting/entries/by-document/{runId:D}");
        var salaryAccount = entry.Lines.First(line => line.Debit > 0).AccountCode;
        var movements = await GetAsync<AccountMovementRow[]>(client,
            $"/api/commerce/v1/accounting/reports/account-movements?accountCode={salaryAccount}&from=2026-08-01&to=2026-08-31&costCenterId={payrollCenterId:D}");
        Assert.Contains(movements, item => item.SourceDocumentId == runId && item.CostCenterId == payrollCenterId);
        Assert.All(movements, item => Assert.Equal(payrollCenterId, item.CostCenterId));

        // Renaming the master and replaying approval must preserve posted classification.
        var centers = await GetAsync<AccountingCostCenterPage>(client,
            $"/api/commerce/v1/accounting/cost-centers?costCenterId={payrollCenterId}&pageSize=1");
        var center = Assert.Single(centers.Items);
        await PutAsync<AccountingCostCenterView>(client,
            $"/api/commerce/v1/accounting/cost-centers/{payrollCenterId:D}",
            new UpdateCostCenterRequest(center.Code, "Nombre posterior", null, false, true, center.RowVersion));
        using(var replayCommands=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("payroll.",StringComparison.Ordinal)))
        using (var replay = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/commerce/v1/payroll/runs/{runId:D}/approve")
               { Content = JsonContent.Create(new { rowVersion = run.RowVersion }) })
        {
            replay.Headers.Add("Idempotency-Key", $"cert-{runId:D}");
            using var response = await client.SendAsync(replay);
            response.EnsureSuccessStatusCode();
            Assert.Equal(1,replayCommands.Count);
        }
        await AssertPayrollCostCenterAsync(runId, payrollCenterId, noveltyCenter.Code, noveltyCenter.Name);
        await AssertPayrollCostCenterAsync(batchId, payrollCenterId, noveltyCenter.Code, noveltyCenter.Name);
        var replayedEntry = await GetAsync<AccountingEntryView>(client,
            $"/api/commerce/v1/accounting/entries/by-document/{runId:D}");
        Assert.Equal(entry.EntryId, replayedEntry.EntryId);

        var issuer = options.FiscalIssuers.Single(value => value.FiscalIssuerConfigurationId == fixture.FiscalIssuerConfigurationId);
        await PutAsync<ElectronicPayrollConfigurationView>(client,
            "/api/commerce/v1/payroll/electronic-configuration",
            new SaveElectronicPayrollConfigurationRequest(fixture.BusinessId,
                issuer.FiscalIssuerConfigurationId, issuer.SoftwareIdentificationCode,
                "AURALY_DIAN_SOFTWARE_PIN", issuer.TestSetId, "NIE", 1,
                "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
                true, null));
        await PutAsync<PayrollSettingsView>(client, "/api/commerce/v1/payroll/settings",
            new SavePayrollSettingsRequest(false, true, null));
        // Later contract changes must not rewrite the approved fiscal source.
        await PutAsync<PayrollEmploymentView>(client,
            $"/api/commerce/v1/payroll/employments/{employmentId:D}",
            employmentRequest with { MonthlySalary = 4_000_000m, ContractNumber = "CHANGED-AFTER-APPROVAL",
                RowVersion = employment.RowVersion });
        var electronic = await PostAsync<ElectronicPayrollPeriodView>(client,
            "/api/commerce/v1/payroll/electronic-periods",
            new GenerateElectronicPayrollPeriodRequest(Guid.NewGuid(), fixture.BusinessId, 2026, 8));
        Assert.Single(electronic.Documents);
        await using (var fiscalConnection = new SqlConnection(fixture.ConnectionString))
        {
            await fiscalConnection.OpenAsync();
            await using var snapshotQuery = fiscalConnection.CreateCommand();
            snapshotQuery.CommandText = "SELECT SourceSnapshotJson FROM payroll.ElectronicDocuments WHERE ElectronicPayrollDocumentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId";
            snapshotQuery.Parameters.AddWithValue("@Id", electronic.Documents[0].ElectronicPayrollDocumentId);
            snapshotQuery.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            snapshotQuery.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            var snapshot = PayrollContractSerializer.DeserializeElectronic((string)(await snapshotQuery.ExecuteScalarAsync())!);
            Assert.Equal(3_000_000m, snapshot.MonthlySalary);
            Assert.Equal("CERT-EMP-001", snapshot.EmployeeCode);
            Assert.Equal(run.TotalEarnings, snapshot.Earnings);
            Assert.All(snapshot.Lines,line=>Assert.True(line.Amount>0));
        }
        Assert.Contains(fixture.DrainFiscalSignals(), signal =>
            signal.Signal.DocumentId == electronic.Documents[0].FiscalDocumentId);

        var definitions = await GetAsync<PayrollReportDefinitionView[]>(client,
            "/api/commerce/v1/payroll/reports/definitions");
        Assert.Equal(10, definitions.Length);
        var reports = new Dictionary<string, PayrollReportResult>(StringComparer.Ordinal);
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();string? masterName;string contract,frozenName,frozenContract;decimal salary,frozenSalary;
            await using(var read=new SqlCommand("""
                SELECT party.DisplayName,employment.ContractNumber,employment.MonthlySalary,
                  JSON_VALUE(result.EmployeeSnapshotJson,'$.EmployeeName'),
                  JSON_VALUE(result.EmployeeSnapshotJson,'$.ContractNumber'),
                  CONVERT(decimal(19,4),JSON_VALUE(result.EmployeeSnapshotJson,'$.MonthlySalary'))
                FROM payroll.RunEmployees result
                JOIN payroll.Employments employment ON employment.EmploymentId=result.EmploymentId
                JOIN dbo.Parties party ON party.PartyId=result.PartyId AND party.TenantId=result.TenantId
                WHERE result.PayrollRunId=@Run AND result.PartyId=@Party;
                """,connection))
            {
                read.Parameters.AddWithValue("@Run",run.PayrollRunId);read.Parameters.AddWithValue("@Party",partyId);
                await using var reader=await read.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
                masterName=reader.IsDBNull(0)?null:reader.GetString(0);contract=reader.GetString(1);salary=reader.GetDecimal(2);frozenName=reader.GetString(3);
                frozenContract=reader.GetString(4);frozenSalary=reader.GetDecimal(5);
            }
            await using var edit=new SqlCommand("""
                UPDATE dbo.Parties SET DisplayName=@Name WHERE PartyId=@Party AND TenantId=@Tenant;
                UPDATE payroll.Employments SET ContractNumber=@Contract,MonthlySalary=@Salary WHERE PartyId=@Party AND TenantId=@Tenant;
                """,connection);
            edit.Parameters.AddWithValue("@Party",partyId);edit.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            edit.Parameters.AddWithValue("@Name","Nombre posterior a la liquidación");
            edit.Parameters.AddWithValue("@Contract","POSTERIOR-"+Guid.NewGuid().ToString("N"));edit.Parameters.AddWithValue("@Salary",salary+1);
            try
            {
                await edit.ExecuteNonQueryAsync();
                foreach(var definition in definitions)
                {
                    var report=await GetAsync<PayrollReportResult>(client,
                        $"/api/commerce/v1/payroll/reports/{definition.Code}?from=2026-08-01&to=2026-08-31&partyId={partyId:D}");
                    Assert.NotEmpty(report.Rows);Assert.All(report.Rows,row=>Assert.Equal(frozenName,Text(row,"employeeName")));
                    reports.Add(definition.Code,report);
                }
                Assert.All(reports["payroll-receipt"].Rows,row=>{
                    Assert.Equal(frozenContract,Text(row,"contractNumber"));Assert.Equal(frozenSalary,Amount(row,"monthlySalary"));
                });
            }
            finally
            {
                edit.Parameters["@Name"].Value=(object?)masterName??DBNull.Value;edit.Parameters["@Contract"].Value=contract;edit.Parameters["@Salary"].Value=salary;
                await edit.ExecuteNonQueryAsync();
            }
        }

        var summary = Assert.Single(reports["payroll-summary"].Rows);
        Assert.Equal(run.TotalEarnings, Amount(summary, "earnings"));
        Assert.Equal(run.TotalDeductions, Amount(summary, "deductions"));
        Assert.Equal(run.NetPayable, Amount(summary, "netPayable"));

        var detail = reports["concept-detail"].Rows;
        Assert.Equal(run.TotalEarnings, Sum(detail, "amount", "nature", "Earning"));
        Assert.Equal(run.TotalDeductions, Sum(detail, "amount", "nature", "Deduction"));
        Assert.Equal(run.TotalEmployerContributions,
            Sum(detail, "amount", "nature", "EmployerContribution"));
        Assert.Equal(run.TotalProvisions, Sum(detail, "amount", "nature", "Provision"));
        Assert.Equal(run.TotalDeductions, Sum(reports["deductions"].Rows, "amount"));
        Assert.Equal(run.TotalEmployerContributions,
            Sum(reports["employer-contributions"].Rows, "amount"));

        var provisions = reports["provisions"].Rows;
        Assert.Equal(run.TotalProvisions, Sum(provisions, "amount"));
        Assert.Equal(263_499.99m, ConceptAmount(provisions, "SEVERANCE"));
        Assert.Equal(31_620m, ConceptAmount(provisions, "SEVERANCE_INTEREST"));
        Assert.Equal(263_499.99m, ConceptAmount(provisions, "BONUS"));
        Assert.Equal(125_000.01m, ConceptAmount(provisions, "VACATION"));

        var laborCost = Assert.Single(reports["labor-cost"].Rows);
        Assert.Equal(run.TotalEarnings, Amount(laborCost, "earnings"));
        Assert.Equal(run.TotalEmployerContributions,
            Amount(laborCost, "employerContributions"));
        Assert.Equal(run.TotalProvisions, Amount(laborCost, "provisions"));
        Assert.Equal(run.TotalEarnings + run.TotalEmployerContributions + run.TotalProvisions,
            Amount(laborCost, "totalLaborCost"));
        Assert.Equal(run.NetPayable,
            Amount(Assert.Single(reports["payments"].Rows), "amount"));
        var withholding = Assert.Single(reports["income-withholding"].Rows);
        Assert.Equal(run.TotalEarnings, Amount(withholding, "employmentIncome"));
        Assert.Equal(0m, Amount(withholding, "withholding"));
        Assert.Equal(run.TotalDeductions, Amount(withholding, "totalDeductions"));
        var electronicStatus = Assert.Single(reports["electronic-status"].Rows);
        Assert.Equal(electronic.Documents[0].ElectronicPayrollDocumentId.ToString(),
            Text(electronicStatus, "id"));
        Assert.Equal(electronic.Documents[0].Status,
            Text(electronicStatus, "payrollStatus"));
        Assert.Equal("PendingGeneration", Text(electronicStatus, "fiscalStatus"));
    }

    private async Task AssertNoveltyEditingAsync(HttpClient client,SavePayrollNoveltyRequest original,
        PayrollNoveltyView saved,PayrollRunView calculated)
    {
        var path=$"/api/commerce/v1/payroll/novelties/{original.NoveltyId:D}";
        using(var scope=fixture.CreateScope())
        {
            var store=scope.ServiceProvider.GetRequiredService<IPayrollStore>();
            var owner=new PayrollUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                new HashSet<string>{PayrollPermissionCodes.Manage});
            foreach(var outsider in new[]{owner with{TenantId=Guid.NewGuid()},owner with{BusinessId=Guid.NewGuid()}})
            {
                await Assert.ThrowsAsync<PayrollNotFoundException>(()=>store.VoidNoveltyAsync(outsider,original.NoveltyId,saved.RowVersion,CancellationToken.None));
                await Assert.ThrowsAsync<PayrollNotFoundException>(()=>store.SaveNoveltyAsync(outsider,original with{RowVersion=saved.RowVersion},CancellationToken.None));
            }
        }
        var changed=await PutAsync<PayrollNoveltyView>(client,path,
            original with {RowVersion=saved.RowVersion,TotalAmount=120_000m,Notes="Cuota corregida"});
        Assert.Equal(120_000m,changed.TotalAmount);Assert.Equal(0,changed.Quantity);
        Assert.False(saved.RowVersion.AsSpan().SequenceEqual(changed.RowVersion));
        using(var stale=await client.PutAsJsonAsync(path,original with {RowVersion=saved.RowVersion}))
            Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using(var staleVoid=await client.PostAsJsonAsync(path+"/void",new {rowVersion=saved.RowVersion}))
            Assert.Equal(HttpStatusCode.Conflict,staleVoid.StatusCode);
        using(var approve=new HttpRequestMessage(HttpMethod.Post,$"/api/commerce/v1/payroll/runs/{calculated.PayrollRunId:D}/approve")
            {Content=JsonContent.Create(new {rowVersion=calculated.RowVersion})})
        {
            approve.Headers.Add("Idempotency-Key",$"changed-{calculated.PayrollRunId:D}");
            using var rejected=await client.SendAsync(approve);
            Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
            Assert.Contains("huella",await rejected.Content.ReadAsStringAsync());
        }
        foreach(var invalid in new[]{
            new CalculatePayrollRunRequest(calculated.RowVersion,RecalculateEmploymentIds:[original.EmploymentId,original.EmploymentId]),
            new CalculatePayrollRunRequest(calculated.RowVersion,RecalculateEmploymentIds:[Guid.NewGuid()]),
            new CalculatePayrollRunRequest(calculated.RowVersion,SelectAll:true,RecalculateEmploymentIds:[original.EmploymentId]),
            new CalculatePayrollRunRequest(RecalculateEmploymentIds:[original.EmploymentId])})
        {
            using var rejected=await client.PostAsJsonAsync($"/api/commerce/v1/payroll/runs/{calculated.PayrollRunId:D}/calculate",invalid);
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        }
        var updated=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{calculated.PayrollRunId:D}/calculate",
            new CalculatePayrollRunRequest(calculated.RowVersion,RecalculateEmploymentIds:[original.EmploymentId]));
        Assert.True(updated.IsPartialCalculation);Assert.Single(updated.Employees);
        Assert.Equal(120_000m,Assert.Single(Assert.Single(updated.Employees).Lines,line=>line.NoveltyId==original.NoveltyId).Amount);
        await PutAsync<PayrollNoveltyView>(client,path,original with {RowVersion=changed.RowVersion});
        var extra=await PostAsync<PayrollNoveltyView>(client,"/api/commerce/v1/payroll/novelties",
            original with {NoveltyId=Guid.NewGuid(),TotalAmount=5_000m});
        updated=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{calculated.PayrollRunId:D}/calculate",new{});
        Assert.Equal(2,Assert.Single(updated.Employees).Lines.Count(line=>line.ConceptId==original.ConceptId));
        PayrollNoveltyView retired;
        using(var writes=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("UPDATE payroll.Novelties SET Status=N'Voided'",StringComparison.Ordinal)))
        {
            retired=await PostAsync<PayrollNoveltyView>(client,$"/api/commerce/v1/payroll/novelties/{extra.NoveltyId:D}/void",new{rowVersion=extra.RowVersion});
            Assert.Equal(1,writes.Count);
        }
        Assert.Equal("Voided",retired.Status);
        var replay=await PostAsync<PayrollNoveltyView>(client,$"/api/commerce/v1/payroll/novelties/{extra.NoveltyId:D}/void",new{rowVersion=extra.RowVersion});
        Assert.Equal(retired.RowVersion,replay.RowVersion);
        using(var approve=new HttpRequestMessage(HttpMethod.Post,$"/api/commerce/v1/payroll/runs/{updated.PayrollRunId:D}/approve")
            {Content=JsonContent.Create(new {rowVersion=updated.RowVersion})})
        {
            approve.Headers.Add("Idempotency-Key",$"voided-{updated.PayrollRunId:D}");
            using var rejected=await client.SendAsync(approve);Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
        }
    }

    private async Task<Dictionary<string,Guid>> EnsureCalculationConceptsAsync(HttpClient client,PayrollWorkspaceOptions options)
    {
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(value=>value.Code==code).OptionId;
        var concepts = options.Concepts.Where(value=>value.SystemRoleCode is not null).ToDictionary(value=>value.SystemRoleCode!,value=>value.ConceptId,StringComparer.Ordinal);
        foreach (var definition in Concepts())
        {
            if(concepts.ContainsKey(definition.Role))continue;
            var conceptId = Guid.NewGuid();
            concepts.Add(definition.Role, conceptId);
            await PutAsync<PayrollConceptView>(client,
                $"/api/commerce/v1/payroll/concepts/{conceptId:D}",
                new SavePayrollConceptRequest(conceptId, definition.Code, definition.Name,
                    Option(PayrollCatalogCodes.ConceptNature, definition.Nature),
                    Option(PayrollCatalogCodes.CalculationMethod, "Statutory"),
                    Option(PayrollCatalogCodes.ConceptTreatment,
                        definition.Nature == "Deduction" ? "StatutoryDeduction" : "Salary"),
                    definition.Dian is null ? null : Option(PayrollCatalogCodes.DianConcept, definition.Dian),
                    Option(PayrollCatalogCodes.AccountingCategory, definition.Accounting),
                    Option(PayrollCatalogCodes.SystemConceptRole, definition.Role),
                    definition.Role == PayrollSystemConceptRoles.BasicSalary,
                    definition.Nature == "Earning", definition.Nature == "Earning",
                    definition.Role == PayrollSystemConceptRoleCodes.LaborWithholding,
                    false, new DateOnly(2026, 1, 1), null, true, null));
        }

        return concepts;
    }

    private static decimal ConceptAmount(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string code) =>
        Amount(Assert.Single(rows, row => Text(row, "conceptCode") == code), "amount");

    private static decimal Sum(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string amountKey, string? filterKey = null, string? filterValue = null) =>
        rows.Where(row => filterKey is null || Text(row, filterKey) == filterValue)
            .Sum(row => Amount(row, amountKey));

    private static decimal Amount(IReadOnlyDictionary<string, object?> row, string key) =>
        row[key] switch
        {
            decimal value => value,
            JsonElement value => value.GetDecimal(),
            _ => throw new InvalidOperationException($"'{key}' is not a decimal report value.")
        };

    private static string? Text(IReadOnlyDictionary<string, object?> row, string key) =>
        row[key] switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString(),
            null => null,
            _ => throw new InvalidOperationException($"'{key}' is not a text report value.")
        };

    private async Task<Guid> CreateEmployeePartyAsync(string identification="1032456799")
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,
              IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,
              FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            SELECT @PartyId,@TenantId,N'NaturalPerson',CountryId,N'CC',@Identification,
              @Identification,N'EMPLEADA CERTIFICACIÓN',N'EMPLEADA',N'CERTIFICACIÓN',
              N'Complete',1,@UserId,SYSUTCDATETIME()
            FROM dbo.Countries WHERE Code=N'CO';
            INSERT dbo.Employees(EmployeeId,TenantId,PartyId,Name,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,N'EMPLEADA CERTIFICACIÓN',1,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@PartyId", id);
        command.Parameters.AddWithValue("@Identification",identification);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<(Guid PartyId, Guid EmploymentId, byte[] RowVersion)>
        CreateLegacyInvalidEmploymentAsync(Guid contractType, Guid salaryType,
            Guid frequency, Guid riskClass, Guid workerType, Guid? workerSubtype,
            Guid paymentMethod)
    {
        var partyId = Guid.NewGuid();
        var employmentId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,
              IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,
              CompletionStatus,IsActive,CreatedBy,CreatedAt)
            SELECT @PartyId,@TenantId,N'NaturalPerson',CountryId,N'CC',N'1032456700',
              N'1032456700',N'LEGACY INCOMPLETE',N'Incomplete',1,@UserId,SYSUTCDATETIME()
            FROM dbo.Countries WHERE Code=N'CO';

            INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,EmployeeId,
              ContractTypeOptionId,SalaryTypeOptionId,PayFrequencyOptionId,RiskClassOptionId,
              WorkerTypeOptionId,WorkerSubtypeOptionId,PaymentMethodOptionId,ContractNumber,
              StartDate,MonthlySalary,IsActive,CreatedBy,CreatedAt)
            VALUES(@EmploymentId,@TenantId,@PartyId,@BusinessId,NULL,@ContractType,@SalaryType,
              @Frequency,@RiskClass,@WorkerType,@WorkerSubtype,@PaymentMethod,N'LEGACY-INCOMPLETE',
              '2026-01-01',1000000,1,@UserId,SYSUTCDATETIME());

            SELECT RowVersion FROM payroll.Employments WHERE EmploymentId=@EmploymentId;
            """;
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@EmploymentId", employmentId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@ContractType", contractType);
        command.Parameters.AddWithValue("@SalaryType", salaryType);
        command.Parameters.AddWithValue("@Frequency", frequency);
        command.Parameters.AddWithValue("@RiskClass", riskClass);
        command.Parameters.AddWithValue("@WorkerType", workerType);
        command.Parameters.AddWithValue("@WorkerSubtype", (object?)workerSubtype ?? DBNull.Value);
        command.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
        var rowVersion = (byte[])(await command.ExecuteScalarAsync())!;
        return (partyId, employmentId, rowVersion);
    }

    private async Task AssertPaymentRequiresPostedAccrualAsync(HttpClient client,CreatePayrollPaymentBatchRequest request)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingPostingJobs SET Status=@Status
            WHERE TenantId=@Tenant AND BusinessId=@Business AND SourceDocumentId=@Run
              AND SourceDocumentType=N'PayrollAccrual';
            """,connection);
        command.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        command.Parameters.AddWithValue("@Business",fixture.BusinessId);
        command.Parameters.AddWithValue("@Run",request.PayrollRunId);
        var status = command.Parameters.Add("@Status",System.Data.SqlDbType.NVarChar,64);
        try
        {
            // Approval alone and commercial effects without accounting cannot
            // enable payment. Inject each existing job state in this fixture.
            foreach (var pending in new[] { AccountingPostingStatuses.Pending,
                AccountingPostingStatuses.PendingConfiguration,AccountingPostingStatuses.CommercialEffectsApplied })
            {
                status.Value = pending;
                Assert.Equal(1,await command.ExecuteNonQueryAsync());
                using var response = await client.PostAsJsonAsync("/api/commerce/v1/payroll/payments",request);
                Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
                Assert.Contains("contabilizada",await response.Content.ReadAsStringAsync());
                await using var effects = new SqlCommand("""
                    SELECT (SELECT COUNT(*) FROM payroll.PaymentBatches WHERE PaymentBatchId=@Id)
                      +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM payroll.OutboxMessages WHERE AggregateId=@Id);
                    """,connection);
                effects.Parameters.AddWithValue("@Id",request.PaymentBatchId);
                Assert.Equal(0,(int)(await effects.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            status.Value = AccountingPostingStatuses.Posted;
            Assert.Equal(1,await command.ExecuteNonQueryAsync());
        }
    }

    private async Task AssertIndividualReceiptAsync(HttpClient client,PayrollRunView run,string status,bool paid)
    {
        var employee=Assert.Single(run.Employees,worker=>worker.ContractNumber=="CERT-EMP-001");
        var query=$"from={run.PeriodStart:yyyy-MM-dd}&to={run.PeriodEnd:yyyy-MM-dd}&employeeResultId={employee.PayrollRunEmployeeId}";
        PayrollReportResult receipt;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("reporting.PayrollReportDefinitions",StringComparison.Ordinal)
                ||command.CommandText.Contains("[paidDate]",StringComparison.Ordinal)))
        {
            receipt=await GetAsync<PayrollReportResult>(client,$"/api/commerce/v1/payroll/reports/payroll-receipt?{query}");
            Assert.Equal(2,commands.Count);
        }
        Assert.NotEmpty(receipt.Rows);
        Assert.All(receipt.Rows,row=>{
            Assert.Equal(employee.PayrollRunEmployeeId.ToString("D"),Assert.IsType<JsonElement>(row["payrollRunEmployeeId"]).GetString());
            Assert.Equal(status,Assert.IsType<JsonElement>(row["runStatus"]).GetString());
            if(paid)Assert.Equal(run.PaymentDate.ToString("yyyy-MM-dd"),Assert.IsType<JsonElement>(row["paidDate"]).GetString());
            else Assert.Null(row["paidDate"]);
        });
        using var scope=fixture.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<PayrollReportingService>();
        var other=new PayrollUserIdentity(fixture.UserId,Guid.NewGuid(),fixture.BusinessId,new HashSet<string>{PayrollPermissionCodes.Read});
        Assert.Empty((await service.RunAsync(other,"payroll-receipt",run.PeriodStart,run.PeriodEnd,null,default,employee.PayrollRunEmployeeId)).Rows);
        using var unsupported=await client.GetAsync($"/api/commerce/v1/payroll/reports/payroll-summary?{query}");
        Assert.Equal(HttpStatusCode.BadRequest,unsupported.StatusCode);
    }

    private async Task AssertPayrollCostCenterAsync(Guid documentId, Guid expectedCenterId,string expectedCode,string expectedName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT j.ResolvedCostCenterId,l.CostCenterId,l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot
            FROM dbo.AccountingPostingJobs j
            JOIN dbo.AccountingEntries e ON e.SourceDocumentId=j.SourceDocumentId
              AND e.SourceDocumentType=j.SourceDocumentType AND e.BusinessId=j.BusinessId
            JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            WHERE j.SourceDocumentId=@Id AND j.TenantId=@TenantId AND j.BusinessId=@BusinessId;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal(expectedCenterId, reader.GetGuid(0));
            Assert.Equal(expectedCenterId, reader.GetGuid(1));
            Assert.Equal(expectedCode, reader.GetString(2));
            Assert.Equal(expectedName, reader.GetString(3));
            count++;
        }
        Assert.True(count >= 2);
    }

    private async Task AssertBalancedEntryAsync(Guid documentId, string documentType)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT j.SourceDocumentType,j.Status,j.LastErrorCode,j.LastErrorMessage,
              e.DebitTotal,e.CreditTotal
            FROM dbo.AccountingPostingJobs j
            LEFT JOIN dbo.AccountingEntries e ON e.SourceDocumentId=j.SourceDocumentId
              AND e.SourceDocumentType=j.SourceDocumentType
            WHERE j.SourceDocumentId=@Id;
            """;
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"No accounting job exists for {documentId:D}.");
        Assert.Equal(documentType, reader.GetString(0));
        Assert.True(reader.GetString(1) == "Posted",
            $"Accounting job is {reader.GetString(1)}: {(reader.IsDBNull(2) ? null : reader.GetString(2))} · {(reader.IsDBNull(3) ? null : reader.GetString(3))}");
        Assert.Equal(reader.GetDecimal(4), reader.GetDecimal(5));
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri) =>
        await client.GetFromJsonAsync<T>(uri) ?? throw new InvalidOperationException($"Empty response from {uri}.");

    private static async Task<T> PutAsync<T>(HttpClient client, string uri, object body)
    {
        using var response = await client.PutAsJsonAsync(uri, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException($"Empty response from {uri}.");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string uri, object body)
    {
        using var response = await client.PostAsJsonAsync(uri, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException($"Empty response from {uri}.");
    }

    private static IReadOnlyDictionary<string, decimal> Rules() =>
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [PayrollRuleParameterCodes.MonthlyDays] = 30m,
            [PayrollRuleParameterCodes.MinimumMonthlySalary] = 1_500_000m,
            [PayrollRuleParameterCodes.TransportAllowance] = 162_000m,
            [PayrollRuleParameterCodes.TransportAllowanceSalaryLimitMultiple] = 2m,
            [PayrollRuleParameterCodes.EmployeeHealthRate] = .04m,
            [PayrollRuleParameterCodes.EmployeePensionRate] = .04m,
            [PayrollRuleParameterCodes.EmployerHealthRate] = .085m,
            [PayrollRuleParameterCodes.EmployerPensionRate] = .12m,
            [PayrollRuleParameterCodes.CompensationFundRate] = .04m,
            [PayrollRuleParameterCodes.SenaRate] = .02m,
            [PayrollRuleParameterCodes.IcbfRate] = .03m,
            [PayrollRuleParameterCodes.SeveranceRate] = .08333333m,
            [PayrollRuleParameterCodes.SeveranceInterestRate] = .12m,
            [PayrollRuleParameterCodes.ServiceBonusRate] = .08333333m,
            [PayrollRuleParameterCodes.VacationRate] = .04166667m,
            [PayrollRuleParameterCodes.IntegralSalaryContributionBaseRate] = .70m,
            [PayrollRuleParameterCodes.MaximumContributionBaseMinimumWages] = 25m,
            [PayrollRuleParameterCodes.NonSalaryExclusionThresholdRate] = .40m
        };

    private static IReadOnlyList<ConceptSeed> Concepts() =>
    [
        new(PayrollSystemConceptRoles.BasicSalary,"BASIC","Salario básico","Earning","SalaryExpense","BasicSalary"),
        new(PayrollSystemConceptRoles.TransportAllowance,"TRANSPORT","Auxilio transporte","Earning","TransportAllowanceExpense","TransportAllowance"),
        new(PayrollSystemConceptRoles.EmployeeHealth,"HEALTH_EMPLOYEE","Salud trabajador","Deduction","EmployeeContributionsPayable","HealthDeduction"),
        new(PayrollSystemConceptRoles.EmployeePension,"PENSION_EMPLOYEE","Pensión trabajador","Deduction","EmployeeContributionsPayable","PensionDeduction"),
        new(PayrollSystemConceptRoles.EmployerHealth,"HEALTH_EMPLOYER","Salud empleador","EmployerContribution","EmployerHealthPayable",null),
        new(PayrollSystemConceptRoles.EmployerPension,"PENSION_EMPLOYER","Pensión empleador","EmployerContribution","EmployerPensionPayable",null),
        new(PayrollSystemConceptRoles.OccupationalRisk,"ARL","Riesgos laborales","EmployerContribution","OccupationalRiskPayable",null),
        new(PayrollSystemConceptRoles.CompensationFund,"CCF","Caja compensación","EmployerContribution","ParafiscalContributionsPayable",null),
        new(PayrollSystemConceptRoles.Sena,"SENA","SENA","EmployerContribution","ParafiscalContributionsPayable",null),
        new(PayrollSystemConceptRoles.Icbf,"ICBF","ICBF","EmployerContribution","ParafiscalContributionsPayable",null),
        new(PayrollSystemConceptRoles.SeveranceProvision,"SEVERANCE","Cesantías","Provision","BenefitsProvisionPayable",null),
        new(PayrollSystemConceptRoles.SeveranceInterestProvision,"SEVERANCE_INTEREST","Intereses cesantías","Provision","BenefitsProvisionPayable",null),
        new(PayrollSystemConceptRoles.ServiceBonusProvision,"BONUS","Prima servicios","Provision","BenefitsProvisionPayable",null),
        new(PayrollSystemConceptRoles.VacationProvision,"VACATION","Vacaciones","Provision","BenefitsProvisionPayable",null),
        new(PayrollSystemConceptRoleCodes.LaborWithholding,"WITHHOLDING","Retención laboral","Deduction","PayrollWithholdingPayable","OtherDeduction")
    ];

    private sealed record ConceptSeed(string Role, string Code, string Name,
        string Nature, string Accounting, string? Dian);
}
