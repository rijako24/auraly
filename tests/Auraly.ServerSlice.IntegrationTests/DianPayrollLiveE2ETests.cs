using System.Data;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Infrastructure;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Fiscal;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Trait("LiveExternal", "DIAN")]
public sealed class DianPayrollLiveE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task Every_native_payroll_report_executes_through_reporting_engine()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("AURALY_RUN_DIAN_PAYROLL_E2E"),
                "1", StringComparison.Ordinal))
            return;

        var connectionString = RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_CONNECTION");
        var identity = new PayrollUserIdentity(Guid.NewGuid(),
            Guid.Parse(RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_TENANT_ID")),
            Guid.Parse(RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_BUSINESS_ID")),
            new HashSet<string> { PayrollPermissionCodes.Read });
        var engine = new PayrollReportingService(
            new SqlPayrollReportingStore(new PayrollSqlConnectionFactory(connectionString)));

        var definitions = await engine.ListDefinitionsAsync(identity);
        Assert.Equal(10, definitions.Count);
        foreach (var definition in definitions)
        {
            var result = await engine.RunAsync(identity, definition.Code,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null);
            Assert.Equal(definition.Code, result.Definition.Code);
        }
    }

    [Fact]
    public async Task Approved_payroll_is_generated_signed_zipped_and_received_by_dian_habilitation()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("AURALY_RUN_DIAN_PAYROLL_E2E"),
                "1", StringComparison.Ordinal))
            return;

        var connectionString = RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_CONNECTION");
        var tenantId = Guid.Parse(RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_TENANT_ID"));
        var businessId = Guid.Parse(RequiredEnvironment("AURALY_DIAN_PAYROLL_E2E_BUSINESS_ID"));
        var userId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        await SeedApprovedPayrollAsync(connectionString, tenantId, businessId, userId);

        var identity = new PayrollUserIdentity(userId, tenantId, businessId,
            new HashSet<string> { PayrollPermissionCodes.Read, PayrollPermissionCodes.Fiscal });
        var payrollStore = new SqlPayrollStore(
            new PayrollSqlConnectionFactory(connectionString),
            new Uuid7AuralyIdGenerator(TimeProvider.System), TimeProvider.System);
        var period = await payrollStore.GenerateElectronicPeriodAsync(identity,
            new GenerateElectronicPayrollPeriodRequest(periodId, businessId, 2026, 8),
            CancellationToken.None);
        var document = Assert.Single(period.Documents);
        Assert.NotNull(document.FiscalDocumentId);

        var connections = new SqlServerConnectionFactory(connectionString);
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        var certificateProvider = new WindowsFiscalSigningCertificateProvider();
        var generation = new FiscalGenerationWorker(
            new SqlFiscalGenerationWorkStore(connections, ids),
            new EnvironmentFiscalSoftwarePinProvider(),
            new DianInvoiceUblBuilder(), new DianCreditNoteUblBuilder(),
            new DianDebitNoteUblBuilder(), new DianSchemaValidator(),
            new DianPayrollXmlBuilder(), new DianPayrollSchemaValidator(),
            new DianXadesSigner(certificateProvider), TimeProvider.System);
        Assert.True(await generation.ProcessAsync(
            businessId, document.FiscalDocumentId.Value, "live-payroll-generator"));

        var clients = new DianWcfClientFactory(certificateProvider);
        var habilitation = new DianHabilitationTransport(
            new SqlDianHabilitationConfigurationProvider(connections), clients);
        var production = new DianProductionTransport(
            new SqlDianProductionConfigurationProvider(connections), clients);
        var submission = new FiscalSubmissionWorker(
            new SqlFiscalSubmissionWorkStore(connections, ids),
            habilitation, production, new FiscalSubmissionPackageBuilder(), TimeProvider.System);
        var result = await submission.ProcessAsync(
            businessId, document.FiscalDocumentId.Value, "live-payroll-submitter");
        Assert.True(result.WorkFound);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT p.Status,p.TrackId,a.MayHaveReachedDian,a.StatusCode,a.StatusDescription
            FROM dbo.FiscalDocumentProcesses p
            JOIN dbo.FiscalTransmissionAttempts a ON a.DocumentId=p.DocumentId
            WHERE p.DocumentId=@DocumentId AND a.AttemptNumber=1;
            """, connection);
        command.Parameters.AddWithValue("@DocumentId", document.FiscalDocumentId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var status = reader.GetString(0);
        var trackId = reader.IsDBNull(1) ? null : reader.GetString(1);
        var mayHaveReachedDian = reader.GetBoolean(2);
        var responseCode = reader.IsDBNull(3) ? null : reader.GetString(3);
        var responseMessage = reader.IsDBNull(4) ? null : reader.GetString(4);
        output.WriteLine("DIAN status={0}; response={1}; message={2}; trackAssigned={3}",
            status, responseCode, responseMessage, !string.IsNullOrWhiteSpace(trackId));

        Assert.True(mayHaveReachedDian);
        Assert.False(string.IsNullOrWhiteSpace(trackId));
        Assert.Equal(FiscalDocumentStatusCodes.PendingDianResult, status);
    }

    private static async Task SeedApprovedPayrollAsync(
        string connectionString, Guid tenantId, Guid businessId, Guid userId)
    {
        var partyId = Guid.NewGuid();
        var employmentId = Guid.NewGuid();
        var ruleSetId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var runEmployeeId = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @CountryId uniqueidentifier=(SELECT TOP(1) CountryId FROM dbo.Countries WHERE Code=N'CO');
            DECLARE @Contract uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-contract-type' AND Code=N'Indefinite');
            DECLARE @Salary uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-salary-type' AND Code=N'Ordinary');
            DECLARE @Frequency uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-pay-frequency' AND Code=N'Monthly');
            DECLARE @Risk uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-risk-class' AND Code=N'I');
            DECLARE @Worker uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-worker-type' AND Code=N'01');
            DECLARE @Subtype uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-worker-subtype' AND Code=N'00');
            DECLARE @Payment uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-payment-method' AND Code=N'BankTransfer');
            DECLARE @Nature uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-concept-nature' AND Code=N'Earning');
            DECLARE @Method uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-calculation-method' AND Code=N'FixedAmount');
            DECLARE @Treatment uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-concept-treatment' AND Code=N'Salary');
            DECLARE @Dian uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-dian-concept' AND Code=N'BasicSalary');
            DECLARE @Accounting uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-accounting-category' AND Code=N'SalaryExpense');
            DECLARE @Role uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-system-concept-role' AND Code=N'BasicSalary');
            DECLARE @Issuer uniqueidentifier=(SELECT FiscalIssuerConfigurationId FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId AND Environment=2 AND IsActive=1);
            IF @CountryId IS NULL OR @Issuer IS NULL OR @Frequency IS NULL THROW 52000,N'Live payroll prerequisites are incomplete.',1;

            INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
              Identification,NormalizedIdentification,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',N'1032456789',N'1032456789',
              N'ANA PRUEBA DIAN',N'ANA',N'PRUEBA',N'Complete',1,@UserId,SYSUTCDATETIME());
            INSERT payroll.Settings(TenantId,IsEmployerExemptFromHealthSenaIcbf,ElectronicPayrollEnabled,UpdatedBy,UpdatedAt)
            VALUES(@TenantId,0,1,@UserId,SYSUTCDATETIME());
            INSERT payroll.RuleSets(RuleSetId,TenantId,CountryCode,Code,Name,EffectiveFrom,SourceReference,Status,CreatedBy,CreatedAt,ApprovedBy,ApprovedAt)
            VALUES(@RuleSetId,@TenantId,'CO',CONCAT(N'LIVE-',CONVERT(nvarchar(36),@RuleSetId)),N'Prueba DIAN', '2026-01-01',N'Live E2E',N'Approved',@UserId,SYSUTCDATETIME(),@UserId,SYSUTCDATETIME());
            INSERT payroll.Concepts(ConceptId,TenantId,Code,Name,NatureOptionId,CalculationMethodOptionId,TreatmentOptionId,
              DianConceptOptionId,AccountingCategoryOptionId,SystemRoleOptionId,IsSalaryBase,IsSocialSecurityBase,
              IsBenefitsBase,IsTaxWithholdingBase,RequiresDeductionAgreement,EffectiveFrom,IsActive,CreatedBy,CreatedAt)
            VALUES(@ConceptId,@TenantId,CONCAT(N'BAS',RIGHT(CONVERT(nvarchar(36),@ConceptId),8)),N'Salario básico',@Nature,@Method,@Treatment,
              @Dian,@Accounting,@Role,1,1,1,1,0,'2026-01-01',1,@UserId,SYSUTCDATETIME());
            INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,ContractTypeOptionId,SalaryTypeOptionId,
              PayFrequencyOptionId,RiskClassOptionId,WorkerTypeOptionId,WorkerSubtypeOptionId,PaymentMethodOptionId,
              ContractNumber,StartDate,MonthlySalary,BankAccountReference,IsActive,CreatedBy,CreatedAt)
            VALUES(@EmploymentId,@TenantId,@PartyId,@BusinessId,@Contract,@Salary,@Frequency,@Risk,@Worker,@Subtype,@Payment,
              CONCAT(N'LIVE-',CONVERT(nvarchar(36),@EmploymentId)),'2026-01-01',2000000,N'000123456789',1,@UserId,SYSUTCDATETIME());
            INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,PeriodStart,PeriodEnd,
              PaymentDate,Status,CalculationVersion,InputHash,TotalEarnings,TotalDeductions,TotalEmployerContributions,
              TotalProvisions,NetPayable,CreatedBy,CreatedAt,CalculatedAt,ApprovedBy,ApprovedAt)
            VALUES(@RunId,@TenantId,@BusinessId,@RuleSetId,@Frequency,N'Regular','2026-08-01','2026-08-31','2026-08-31',
              N'Approved',1,0x00,2000000,0,0,0,2000000,@UserId,SYSUTCDATETIME(),SYSUTCDATETIME(),@UserId,SYSUTCDATETIME());
            INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,EmployeeSnapshotJson,
              RuleSnapshotJson,WorkedDays,Earnings,Deductions,EmployerContributions,Provisions,NetPayable,CalculationHash)
            VALUES(@RunEmployeeId,@TenantId,@RunId,@EmploymentId,@PartyId,N'{}',N'{}',30,2000000,0,0,0,2000000,CONVERT(binary(32),0x00));
            INSERT payroll.RunLines(PayrollRunLineId,PayrollRunEmployeeId,ConceptId,LineNumber,NatureCode,ConceptCode,ConceptName,
              DianConceptCode,AccountingCategoryCode,Quantity,Rate,BaseAmount,Amount,IsEmployerCost,IsSalaryBase)
            VALUES(NEWID(),@RunEmployeeId,@ConceptId,1,N'Earning',N'BASIC',N'Salario básico',N'BasicSalary',N'SalaryExpense',30,NULL,2000000,2000000,0,1);
            INSERT payroll.ElectronicConfigurations(BusinessId,TenantId,FiscalIssuerConfigurationId,SoftwareIdentificationCode,
              SoftwarePinSecretReference,TestSetId,Prefix,NextConsecutive,QrValidationUrl,IsActive,UpdatedBy,UpdatedAt)
            SELECT @BusinessId,@TenantId,@Issuer,SoftwareIdentificationCode,SoftwarePinSecretReference,TestSetId,
              N'NIE',1,N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=',1,@UserId,SYSUTCDATETIME()
            FROM dbo.FiscalIssuerConfigurations WHERE FiscalIssuerConfigurationId=@Issuer;
            COMMIT TRANSACTION;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@EmploymentId", employmentId);
        command.Parameters.AddWithValue("@RuleSetId", ruleSetId);
        command.Parameters.AddWithValue("@ConceptId", conceptId);
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@RunEmployeeId", runEmployeeId);
        await command.ExecuteNonQueryAsync();
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}
