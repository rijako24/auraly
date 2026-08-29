using System.Text.Json;

namespace Auraly.Commerce.Payroll.Contracts;

public static class PayrollPermissionCodes
{
    public const string Read = "payroll.read";
    public const string Manage = "payroll.manage";
    public const string Calculate = "payroll.calculate";
    public const string Approve = "payroll.approve";
    public const string Pay = "payroll.pay";
    public const string Configure = "payroll.configure";
    public const string Fiscal = "payroll.fiscal";
}

public static class PayrollCatalogCodes
{
    public const string ContractType = "payroll-contract-type";
    public const string SalaryType = "payroll-salary-type";
    public const string PayFrequency = "payroll-pay-frequency";
    public const string RiskClass = "payroll-risk-class";
    public const string WorkerType = "payroll-worker-type";
    public const string WorkerSubtype = "payroll-worker-subtype";
    public const string PaymentMethod = "payroll-payment-method";
    public const string Bank = "payroll-bank";
    public const string BankAccountType = "payroll-bank-account-type";
    public const string ConceptNature = "payroll-concept-nature";
    public const string CalculationMethod = "payroll-calculation-method";
    public const string ConceptTreatment = "payroll-concept-treatment";
    public const string DeductionAuthority = "payroll-deduction-authority";
    public const string NoveltyType = "payroll-novelty-type";
    public const string AccountingCategory = "payroll-accounting-category";
    public const string DianConcept = "payroll-dian-concept";
    public const string SystemConceptRole = "payroll-system-concept-role";
    public const string RuleParameter = "payroll-rule-parameter";
    public const string IdentificationType = "payroll-identification-type";
}

public static class PayrollAccountingDocumentTypes
{
    public const string Accrual = "PayrollAccrual";
    public const string Payment = "PayrollPayment";
    public const string Adjustment = "PayrollAdjustment";
}

public static class PayrollAccountingCategories
{
    public const string NetPayable = "NetPayrollPayable";
}

public static class PayrollSystemConceptRoleCodes
{
    public const string LaborWithholding = "LaborWithholding";
}

public sealed record PayrollUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record PayrollCatalogOption(
    Guid OptionId,
    string CatalogCode,
    string Code,
    string Label,
    string? Description,
    string? MetadataCode,
    string? DianCode,
    bool IsActive,
    int SortOrder);

public sealed record PayrollWorkspaceOptions(
    IReadOnlyDictionary<string, IReadOnlyList<PayrollCatalogOption>> Catalogs,
    IReadOnlyList<PayrollConceptView> Concepts,
    IReadOnlyList<PayrollEmploymentOption> Employments,
    IReadOnlyList<PayrollPartyOption> Parties,
    IReadOnlyList<PayrollRuleSetView> RuleSets,
    PayrollSettingsView? Settings,
    ElectronicPayrollConfigurationView? ElectronicConfiguration,
    IReadOnlyList<FiscalIssuerOption> FiscalIssuers,
    IReadOnlyList<PayrollDeductionAgreementSummary> DeductionAgreements,
    IReadOnlyList<PayrollNoveltyView> Novelties,
    IReadOnlyList<PayrollPaymentBatchView> PaymentBatches,
    IReadOnlyList<ElectronicPayrollPeriodView> ElectronicPeriods);

public sealed record FiscalIssuerOption(
    Guid FiscalIssuerConfigurationId,
    int Version,
    string LegalName,
    string SoftwareIdentificationCode,
    string SoftwarePinSecretReference,
    int Environment,
    Guid? TestSetId,
    bool IsActive);

public sealed record ElectronicPayrollConfigurationView(
    Guid BusinessId,
    Guid FiscalIssuerConfigurationId,
    string SoftwareIdentificationCode,
    string SoftwarePinSecretReference,
    Guid? TestSetId,
    string Prefix,
    long NextConsecutive,
    string QrValidationUrl,
    bool IsActive,
    byte[] RowVersion);

public sealed record SaveElectronicPayrollConfigurationRequest(
    Guid BusinessId,
    Guid FiscalIssuerConfigurationId,
    string SoftwareIdentificationCode,
    string SoftwarePinSecretReference,
    Guid? TestSetId,
    string Prefix,
    long NextConsecutive,
    string QrValidationUrl,
    bool IsActive,
    byte[]? RowVersion);

public sealed record PayrollPartyOption(
    Guid PartyId,
    Guid EmployeeId,
    string Identification,
    string Name);

public sealed record PayrollEmploymentOption(
    Guid EmploymentId,
    Guid PartyId,
    Guid BusinessId,
    string ContractNumber,
    string EmployeeName,
    decimal MonthlySalary,
    bool IsActive,
    Guid? EmployeeId,
    Guid ContractTypeOptionId,
    Guid SalaryTypeOptionId,
    Guid PayFrequencyOptionId,
    Guid RiskClassOptionId,
    Guid WorkerTypeOptionId,
    Guid? WorkerSubtypeOptionId,
    Guid PaymentMethodOptionId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? IntegralSalaryPercentage,
    string? BankAccountReference,
    Guid? BankOptionId,
    Guid? BankAccountTypeOptionId,
    string? BankAccountNumber,
    byte[] RowVersion);

public sealed record PayrollEmploymentPage(
    IReadOnlyList<PayrollEmploymentOption> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record SavePayrollEmploymentRequest(
    Guid EmploymentId,
    Guid PartyId,
    Guid BusinessId,
    Guid? EmployeeId,
    Guid ContractTypeOptionId,
    Guid SalaryTypeOptionId,
    Guid PayFrequencyOptionId,
    Guid RiskClassOptionId,
    Guid WorkerTypeOptionId,
    Guid? WorkerSubtypeOptionId,
    Guid PaymentMethodOptionId,
    string ContractNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal MonthlySalary,
    decimal? IntegralSalaryPercentage,
    string? BankAccountReference,
    Guid? BankOptionId,
    Guid? BankAccountTypeOptionId,
    string? BankAccountNumber,
    bool IsActive,
    byte[]? RowVersion);

public sealed record PayrollEmploymentView(
    Guid EmploymentId,
    Guid PartyId,
    Guid BusinessId,
    Guid? EmployeeId,
    string EmployeeName,
    Guid ContractTypeOptionId,
    Guid SalaryTypeOptionId,
    Guid PayFrequencyOptionId,
    Guid RiskClassOptionId,
    Guid WorkerTypeOptionId,
    Guid? WorkerSubtypeOptionId,
    Guid PaymentMethodOptionId,
    string ContractNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal MonthlySalary,
    decimal? IntegralSalaryPercentage,
    string? BankAccountReference,
    Guid? BankOptionId,
    Guid? BankAccountTypeOptionId,
    string? BankAccountNumber,
    bool IsActive,
    byte[] RowVersion);

public sealed record SavePayrollConceptRequest(
    Guid ConceptId,
    string Code,
    string Name,
    Guid NatureOptionId,
    Guid CalculationMethodOptionId,
    Guid TreatmentOptionId,
    Guid? DianConceptOptionId,
    Guid AccountingCategoryOptionId,
    Guid? SystemRoleOptionId,
    bool IsSalaryBase,
    bool IsSocialSecurityBase,
    bool IsBenefitsBase,
    bool IsTaxWithholdingBase,
    bool RequiresDeductionAgreement,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    byte[]? RowVersion);

public sealed record PayrollConceptView(
    Guid ConceptId,
    string Code,
    string Name,
    string NatureCode,
    string CalculationMethodCode,
    string TreatmentCode,
    string? DianConceptCode,
    string AccountingCategoryCode,
    string? SystemRoleCode,
    bool IsSalaryBase,
    bool IsSocialSecurityBase,
    bool IsBenefitsBase,
    bool IsTaxWithholdingBase,
    bool RequiresDeductionAgreement,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    byte[] RowVersion);

public sealed record SavePayrollRuleSetRequest(
    Guid RuleSetId,
    string CountryCode,
    string Code,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SourceReference,
    IReadOnlyList<SavePayrollRuleParameterRequest> Parameters,
    byte[]? RowVersion);

public sealed record SavePayrollRuleParameterRequest(string Code, decimal NumericValue, string UnitCode, string? Description);
public sealed record PayrollRuleParameterView(string Code, decimal NumericValue, string UnitCode, string? Description);
public sealed record PayrollRuleSetView(
    Guid RuleSetId,
    string CountryCode,
    string Code,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SourceReference,
    string Status,
    IReadOnlyList<PayrollRuleParameterView> Parameters,
    byte[] RowVersion);

public sealed record SavePayrollDeductionAgreementRequest(
    Guid DeductionAgreementId,
    Guid EmploymentId,
    Guid ConceptId,
    Guid AuthorityOptionId,
    Guid? BeneficiaryPartyId,
    string ReferenceNumber,
    string EvidenceUrl,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? AuthorizedTotal,
    decimal? InstallmentAmount,
    short Priority,
    bool MustProtectMinimumNetPay,
    bool IsActive,
    byte[]? RowVersion);

public sealed record PayrollDeductionAgreementView(
    Guid DeductionAgreementId,
    Guid EmploymentId,
    Guid ConceptId,
    Guid AuthorityOptionId,
    Guid? BeneficiaryPartyId,
    string ReferenceNumber,
    string EvidenceUrl,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? AuthorizedTotal,
    decimal? InstallmentAmount,
    decimal DeductedToDate,
    short Priority,
    bool MustProtectMinimumNetPay,
    bool IsActive,
    byte[] RowVersion);

public sealed record PayrollDeductionAgreementSummary(
    Guid DeductionAgreementId,
    Guid EmploymentId,
    string EmployeeName,
    Guid ConceptId,
    string ConceptName,
    Guid AuthorityOptionId,
    Guid? BeneficiaryPartyId,
    string AuthorityName,
    string ReferenceNumber,
    string EvidenceUrl,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal? AuthorizedTotal,
    decimal? InstallmentAmount,
    decimal DeductedToDate,
    short Priority,
    bool MustProtectMinimumNetPay,
    bool IsActive,
    byte[] RowVersion);

public sealed record SavePayrollSettingsRequest(
    bool IsEmployerExemptFromHealthSenaIcbf,
    bool ElectronicPayrollEnabled,
    byte[]? RowVersion);

public sealed record PayrollSettingsView(
    bool IsEmployerExemptFromHealthSenaIcbf,
    bool ElectronicPayrollEnabled,
    byte[] RowVersion);

public sealed record SavePayrollNoveltyRequest(
    Guid NoveltyId,
    Guid EmploymentId,
    Guid ConceptId,
    Guid NoveltyTypeOptionId,
    Guid? ReasonId,
    Guid? DeductionAgreementId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Quantity,
    decimal? UnitAmount,
    decimal TotalAmount,
    string? Notes,
    string? EvidenceUrl);

public sealed record PayrollNoveltyView(
    Guid NoveltyId,
    Guid EmploymentId,
    string EmployeeName,
    Guid ConceptId,
    string ConceptName,
    Guid NoveltyTypeOptionId,
    string NoveltyTypeName,
    Guid? DeductionAgreementId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Quantity,
    decimal? UnitAmount,
    decimal TotalAmount,
    string? Notes,
    string? EvidenceUrl,
    string Status);

public sealed record CreatePayrollPaymentBatchRequest(
    Guid PaymentBatchId,
    Guid PayrollRunId,
    Guid PaymentMethodOptionId,
    DateOnly PaymentDate,
    string ReferenceNumber);

public sealed record PayrollPaymentBatchView(
    Guid PaymentBatchId,
    Guid PayrollRunId,
    DateOnly PaymentDate,
    Guid PaymentMethodOptionId,
    string PaymentMethodName,
    string ReferenceNumber,
    string Status,
    int EmployeeCount,
    decimal TotalAmount,
    byte[] RowVersion);

public sealed record CreatePayrollRunRequest(
    Guid PayrollRunId,
    Guid BusinessId,
    Guid RuleSetId,
    Guid PayFrequencyOptionId,
    string RunKind,
    Guid? OriginalPayrollRunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PaymentDate);

public sealed record PayrollRunView(
    Guid PayrollRunId,
    Guid BusinessId,
    Guid RuleSetId,
    string RunKind,
    Guid? OriginalPayrollRunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PaymentDate,
    string Status,
    int CalculationVersion,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal TotalEmployerContributions,
    decimal TotalProvisions,
    decimal NetPayable,
    IReadOnlyList<PayrollRunEmployeeView> Employees,
    byte[] RowVersion);

public sealed record PayrollRunSummary(
    Guid PayrollRunId,
    string RunKind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PaymentDate,
    string Status,
    int EmployeeCount,
    decimal TotalEarnings,
    decimal TotalDeductions,
    decimal NetPayable,
    byte[] RowVersion);

public sealed record PayrollRunEmployeeView(
    Guid PayrollRunEmployeeId,
    Guid EmploymentId,
    Guid PartyId,
    string EmployeeName,
    decimal WorkedDays,
    decimal Earnings,
    decimal Deductions,
    decimal EmployerContributions,
    decimal Provisions,
    decimal NetPayable,
    IReadOnlyList<PayrollRunLineView> Lines);

public sealed record PayrollRunLineView(
    int LineNumber,
    Guid ConceptId,
    string ConceptCode,
    string ConceptName,
    string NatureCode,
    string? DianConceptCode,
    string AccountingCategoryCode,
    decimal Quantity,
    decimal? Rate,
    decimal? BaseAmount,
    decimal Amount,
    bool IsEmployerCost);

public sealed record PayrollRunAcceptance(
    Guid PayrollRunId,
    string Status,
    string RunKind,
    Guid? AccountingPostingJobId,
    bool IdempotentReplay);

public sealed record GenerateElectronicPayrollPeriodRequest(
    Guid ElectronicPeriodId,
    Guid BusinessId,
    short Year,
    byte Month);

public sealed record ElectronicPayrollDocumentView(
    Guid ElectronicPayrollDocumentId,
    Guid PartyId,
    string EmployeeName,
    string DocumentKind,
    Guid? FiscalDocumentId,
    string Status,
    string SourceHashHex);

public sealed record ElectronicPayrollPeriodView(
    Guid ElectronicPeriodId,
    short Year,
    byte Month,
    string Status,
    IReadOnlyList<ElectronicPayrollDocumentView> Documents,
    byte[] RowVersion);

public enum PayrollReportDataset
{
    PayrollSummary,
    PayrollReceipt,
    ConceptDetail,
    Deductions,
    EmployerContributions,
    Provisions,
    LaborCost,
    Payments,
    ElectronicStatus,
    IncomeAndWithholding
}

public sealed record PayrollReportColumnView(
    string Key,
    string Label,
    string Format,
    string Align);

public sealed record PayrollReportDefinitionView(
    string Code,
    string Name,
    string Description,
    PayrollReportDataset Dataset,
    IReadOnlyList<PayrollReportColumnView> Columns,
    int SortOrder);

public sealed record PayrollReportResult(
    PayrollReportDefinitionView Definition,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public sealed record ElectronicPayrollSnapshotLine(
    Guid PayrollRunId,
    Guid EmploymentId,
    Guid ConceptId,
    string ConceptCode,
    string ConceptName,
    string NatureCode,
    string? DianConceptCode,
    decimal Quantity,
    decimal? Rate,
    decimal? BaseAmount,
    decimal Amount,
    bool IsEmployerCost,
    bool IsSalaryBase);

public sealed record ElectronicPayrollSnapshot(
    Guid TenantId,
    Guid BusinessId,
    Guid PartyId,
    string EmployeeName,
    string EmployeeIdentification,
    string EmployeeIdentificationTypeCode,
    string EmployeeFirstName,
    string EmployeeOtherNames,
    string EmployeeFirstSurname,
    string EmployeeSecondSurname,
    Guid EmploymentId,
    string EmployeeCode,
    DateOnly EmploymentStart,
    DateOnly? EmploymentEnd,
    decimal MonthlySalary,
    bool IntegralSalary,
    string ContractTypeCode,
    string WorkerTypeCode,
    string WorkerSubtypeCode,
    bool HighRiskPension,
    string PaymentMethodCode,
    string? Bank,
    string? BankAccountType,
    string? BankAccountNumber,
    int PayrollPeriodCode,
    string SoftwareIdentificationCode,
    string SoftwarePinSecretReference,
    Guid? TestSetId,
    string FiscalPrefix,
    long FiscalConsecutive,
    DateTimeOffset GeneratedAt,
    string QrValidationUrl,
    short Year,
    byte Month,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<DateOnly> PaymentDates,
    IReadOnlyList<Guid> PayrollRunIds,
    decimal WorkedDays,
    decimal Earnings,
    decimal Deductions,
    decimal EmployerContributions,
    decimal Provisions,
    decimal NetPayable,
    IReadOnlyList<ElectronicPayrollSnapshotLine> Lines);

public sealed record PayrollAccountingLine(
    string Category,
    decimal Debit,
    decimal Credit,
    Guid? PartyId,
    string Description);

public sealed record PayrollAccountingPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid PayrollRunId,
    string RunKind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PaymentDate,
    string Description,
    IReadOnlyList<PayrollAccountingLine> Lines);

public static class PayrollContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(PayrollAccountingPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static PayrollAccountingPayload DeserializeAccounting(string payload) =>
        JsonSerializer.Deserialize<PayrollAccountingPayload>(payload, Options)
        ?? throw new InvalidOperationException("The payroll accounting payload is invalid.");

    public static string Serialize(ElectronicPayrollSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static ElectronicPayrollSnapshot DeserializeElectronic(string payload) =>
        JsonSerializer.Deserialize<ElectronicPayrollSnapshot>(payload, Options)
        ?? throw new InvalidOperationException("The electronic payroll snapshot is invalid.");

}
