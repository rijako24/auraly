using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Payroll")]
public sealed class PayrollVerticalSliceTests(ServerSliceFixture fixture)
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
            PayrollPermissionCodes.Fiscal, AccountingPermissionCodes.Read);

        var options = await GetAsync<PayrollWorkspaceOptions>(client,
            "/api/commerce/v1/payroll/options");
        var catalog = options.Catalogs;
        Guid Option(string catalogCode, string code) => catalog[catalogCode]
            .Single(value => value.Code == code).OptionId;

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

        var concepts = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var definition in Concepts())
        {
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
        await PutAsync<PayrollEmploymentView>(client,
            $"/api/commerce/v1/payroll/employments/{employmentId:D}",
            new SavePayrollEmploymentRequest(employmentId, partyId, fixture.BusinessId, null,
                Option(PayrollCatalogCodes.ContractType, "Indefinite"),
                Option(PayrollCatalogCodes.SalaryType, "Ordinary"),
                Option(PayrollCatalogCodes.PayFrequency, "Monthly"),
                Option(PayrollCatalogCodes.RiskClass, "I"),
                Option(PayrollCatalogCodes.WorkerType, "01"),
                Option(PayrollCatalogCodes.WorkerSubtype, "00"),
                Option(PayrollCatalogCodes.PaymentMethod, "BankTransfer"),
                "CERT-EMP-001", new DateOnly(2026, 8, 1), null, 3_000_000m,
                null, "Cuenta certificación", true, null));

        var agreementId = Guid.NewGuid();
        await PutAsync<PayrollDeductionAgreementView>(client,
            $"/api/commerce/v1/payroll/deduction-agreements/{agreementId:D}",
            new SavePayrollDeductionAgreementRequest(agreementId, employmentId, loanConceptId,
                Option(PayrollCatalogCodes.DeductionAuthority, "WrittenAuthorization"), null,
                "AUTH-CERT-001", "https://evidence.auraly.test/payroll/auth-cert-001",
                new DateOnly(2026, 8, 1), null, 500_000m, 100_000m, 100, true, true, null));
        using (var novelty = await client.PostAsJsonAsync(
                   "/api/commerce/v1/payroll/novelties",
                   new SavePayrollNoveltyRequest(Guid.NewGuid(), employmentId, loanConceptId,
                       Option(PayrollCatalogCodes.NoveltyType, "Amount"), null, agreementId,
                       new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 1m, null,
                       100_000m, "Cuota autorizada", "https://evidence.auraly.test/payroll/novelty")))
            Assert.True(novelty.IsSuccessStatusCode, await novelty.Content.ReadAsStringAsync());

        var runId = Guid.NewGuid();
        var run = await PostAsync<PayrollRunView>(client, "/api/commerce/v1/payroll/runs",
            new CreatePayrollRunRequest(runId, fixture.BusinessId, ruleSetId,
                Option(PayrollCatalogCodes.PayFrequency, "Monthly"), "Regular", null,
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31)));
        run = await PostAsync<PayrollRunView>(client,
            $"/api/commerce/v1/payroll/runs/{runId:D}/calculate", new { });
        Assert.Equal("Calculated", run.Status);
        Assert.Equal(100_000m + 240_000m, run.TotalDeductions);

        using var approveRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/payroll/runs/{runId:D}/approve")
        {
            Content = JsonContent.Create(new { rowVersion = run.RowVersion })
        };
        approveRequest.Headers.Add("Idempotency-Key", $"cert-{runId:D}");
        using (var approve = await client.SendAsync(approveRequest))
            Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        await AssertBalancedEntryAsync(runId, PayrollAccountingDocumentTypes.Accrual);

        var listedRuns = await GetAsync<PayrollRunSummary[]>(client,
            "/api/commerce/v1/payroll/runs");
        var listedRun = Assert.Single(listedRuns, value => value.PayrollRunId == runId);
        Assert.Equal("Approved", listedRun.Status);
        Assert.Equal(1, listedRun.EmployeeCount);

        var batchId = Guid.NewGuid();
        await PostAsync<PayrollPaymentBatchView>(client, "/api/commerce/v1/payroll/payments",
            new CreatePayrollPaymentBatchRequest(batchId, runId,
                Option(PayrollCatalogCodes.PaymentMethod, "BankTransfer"),
                new DateOnly(2026, 8, 31), "BANK-CERT-001"));
        await AssertBalancedEntryAsync(batchId, PayrollAccountingDocumentTypes.Payment);

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
        var electronic = await PostAsync<ElectronicPayrollPeriodView>(client,
            "/api/commerce/v1/payroll/electronic-periods",
            new GenerateElectronicPayrollPeriodRequest(Guid.NewGuid(), fixture.BusinessId, 2026, 8));
        Assert.Single(electronic.Documents);
        Assert.Contains(fixture.DrainFiscalSignals(), signal =>
            signal.Signal.DocumentId == electronic.Documents[0].FiscalDocumentId);

        var definitions = await GetAsync<PayrollReportDefinitionView[]>(client,
            "/api/commerce/v1/payroll/reports/definitions");
        Assert.Equal(10, definitions.Length);
        foreach (var definition in definitions)
        {
            var report = await GetAsync<PayrollReportResult>(client,
                $"/api/commerce/v1/payroll/reports/{definition.Code}?from=2026-08-01&to=2026-08-31&partyId={partyId:D}");
            Assert.NotEmpty(report.Rows);
        }
    }

    private async Task<Guid> CreateEmployeePartyAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,
              IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,
              FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            SELECT @PartyId,@TenantId,N'NaturalPerson',CountryId,N'CC',N'1032456799',
              N'1032456799',N'EMPLEADA CERTIFICACIÓN',N'EMPLEADA',N'CERTIFICACIÓN',
              N'Complete',1,@UserId,SYSUTCDATETIME()
            FROM dbo.Countries WHERE Code=N'CO';
            """;
        command.Parameters.AddWithValue("@PartyId", id);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
        return id;
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
