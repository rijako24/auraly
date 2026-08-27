using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Application.Fiscal;

namespace Auraly.Commerce.Payroll.Application;

public sealed record PayrollRunCalculationData(
    Guid PayrollRunId,
    IReadOnlyList<PayrollEmployeeCalculationInput> Employees);

public interface IPayrollStore
{
    Task<PayrollWorkspaceOptions> GetOptionsAsync(PayrollUserIdentity user, CancellationToken ct);
    Task<PayrollEmploymentView> SaveEmploymentAsync(PayrollUserIdentity user,
        SavePayrollEmploymentRequest request, CancellationToken ct);
    Task<PayrollConceptView> SaveConceptAsync(PayrollUserIdentity user,
        SavePayrollConceptRequest request, CancellationToken ct);
    Task<PayrollRuleSetView> SaveRuleSetAsync(PayrollUserIdentity user,
        SavePayrollRuleSetRequest request, CancellationToken ct);
    Task<PayrollRuleSetView> ApproveRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct);
    Task<PayrollRuleSetView> RetireRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct);
    Task<PayrollDeductionAgreementView> SaveDeductionAgreementAsync(PayrollUserIdentity user,
        SavePayrollDeductionAgreementRequest request, CancellationToken ct);
    Task<PayrollSettingsView> SaveSettingsAsync(PayrollUserIdentity user,
        SavePayrollSettingsRequest request, CancellationToken ct);
    Task<ElectronicPayrollConfigurationView> SaveElectronicConfigurationAsync(
        PayrollUserIdentity user, SaveElectronicPayrollConfigurationRequest request,
        CancellationToken ct);
    Task SaveNoveltyAsync(PayrollUserIdentity user, SavePayrollNoveltyRequest request,
        CancellationToken ct);
    Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(PayrollUserIdentity user,
        CreatePayrollPaymentBatchRequest request, CancellationToken ct);
    Task<PayrollRunView> CreateRunAsync(PayrollUserIdentity user,
        CreatePayrollRunRequest request, CancellationToken ct);
    Task<PayrollRunView> GetRunAsync(PayrollUserIdentity user, Guid runId,
        CancellationToken ct);
    Task<IReadOnlyList<PayrollRunSummary>> ListRunsAsync(PayrollUserIdentity user,
        CancellationToken ct);
    Task<PayrollRunCalculationData> LoadCalculationDataAsync(PayrollUserIdentity user,
        Guid runId, CancellationToken ct);
    Task<PayrollRunView> SaveCalculationAsync(PayrollUserIdentity user, Guid runId,
        IReadOnlyList<PayrollEmployeeCalculation> calculations, CancellationToken ct);
    Task<PayrollRunAcceptance> ApproveRunAsync(PayrollUserIdentity user, Guid runId,
        string idempotencyKey, byte[] rowVersion, CancellationToken ct);
    Task<ElectronicPayrollPeriodView> GenerateElectronicPeriodAsync(
        PayrollUserIdentity user, GenerateElectronicPayrollPeriodRequest request,
        CancellationToken ct);
    Task MarkAccountingSignalPublishedAsync(Guid payrollRunId, CancellationToken ct);
    Task MarkElectronicSignalPublishedAsync(Guid electronicPeriodId, CancellationToken ct);
}

public sealed class PayrollService(
    IPayrollStore store,
    PayrollCalculator calculator,
    AccountingProcessingCoordinator accounting,
    FiscalProcessingCoordinator fiscalProcessing)
{
    public Task<PayrollWorkspaceOptions> GetOptionsAsync(PayrollUserIdentity user,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Read);
        return store.GetOptionsAsync(user, ct);
    }

    public Task<PayrollEmploymentView> SaveEmploymentAsync(PayrollUserIdentity user,
        SavePayrollEmploymentRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Manage);
        if (request.EmploymentId == Guid.Empty || request.PartyId == Guid.Empty ||
            request.BusinessId != user.BusinessId || request.MonthlySalary <= 0 ||
            request.EndDate < request.StartDate)
            throw new PayrollValidationException("Los datos de la relación laboral no son válidos.");
        return store.SaveEmploymentAsync(user, request with
        {
            ContractNumber = Text(request.ContractNumber, 64, "Número de contrato"),
            BankAccountReference = Optional(request.BankAccountReference, 200),
            BankAccountNumber = Optional(request.BankAccountNumber, 64)
        }, ct);
    }

    public Task<PayrollConceptView> SaveConceptAsync(PayrollUserIdentity user,
        SavePayrollConceptRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        if (request.ConceptId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom)
            throw new PayrollValidationException("El concepto o su vigencia no son válidos.");
        return store.SaveConceptAsync(user, request with
        {
            Code = Code(request.Code, 32),
            Name = Text(request.Name, 160, "Nombre")
        }, ct);
    }

    public Task<PayrollRuleSetView> SaveRuleSetAsync(PayrollUserIdentity user,
        SavePayrollRuleSetRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        if (request.RuleSetId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom ||
            request.Parameters.Count == 0 || request.Parameters.Select(x => x.Code)
                .Distinct(StringComparer.Ordinal).Count() != request.Parameters.Count)
            throw new PayrollValidationException("El conjunto de reglas no es válido.");
        return store.SaveRuleSetAsync(user, request with
        {
            CountryCode = Code(request.CountryCode, 2),
            Code = Code(request.Code, 64),
            Name = Text(request.Name, 160, "Nombre"),
            SourceReference = Text(request.SourceReference, 500, "Fuente normativa"),
            Parameters = request.Parameters.Select(parameter => parameter with
            {
                Code = Text(parameter.Code, 64, "Código de parámetro"),
                UnitCode = Text(parameter.UnitCode, 32, "Unidad"),
                Description = Optional(parameter.Description, 300)
            }).ToArray()
        }, ct);
    }

    public Task<PayrollRuleSetView> ApproveRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        if (ruleSetId == Guid.Empty || rowVersion.Length != 8)
            throw new PayrollValidationException("La versión del conjunto de reglas no es válida.");
        return store.ApproveRuleSetAsync(user, ruleSetId, rowVersion, ct);
    }

    public Task<PayrollRuleSetView> RetireRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        if (ruleSetId == Guid.Empty || rowVersion.Length == 0)
            throw new PayrollValidationException("La versión de reglas no es válida.");
        return store.RetireRuleSetAsync(user, ruleSetId, rowVersion, ct);
    }

    public Task<PayrollDeductionAgreementView> SaveDeductionAgreementAsync(
        PayrollUserIdentity user, SavePayrollDeductionAgreementRequest request,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Manage);
        if (request.DeductionAgreementId == Guid.Empty || request.EmploymentId == Guid.Empty ||
            request.ConceptId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom ||
            request.AuthorizedTotal <= 0 || request.InstallmentAmount <= 0 ||
            request.Priority is < 1 or > 999)
            throw new PayrollValidationException("El acuerdo de deducción no es válido.");
        return store.SaveDeductionAgreementAsync(user, request with
        {
            ReferenceNumber = Text(request.ReferenceNumber, 100, "Referencia"),
            EvidenceUrl = Text(request.EvidenceUrl, 1000, "Evidencia")
        }, ct);
    }

    public Task<PayrollSettingsView> SaveSettingsAsync(PayrollUserIdentity user,
        SavePayrollSettingsRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        return store.SaveSettingsAsync(user, request, ct);
    }

    public Task<ElectronicPayrollConfigurationView> SaveElectronicConfigurationAsync(
        PayrollUserIdentity user, SaveElectronicPayrollConfigurationRequest request,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Configure);
        if (request.BusinessId != user.BusinessId ||
            request.FiscalIssuerConfigurationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SoftwareIdentificationCode) ||
            request.SoftwareIdentificationCode.Trim().Length > 64 ||
            string.IsNullOrWhiteSpace(request.SoftwarePinSecretReference) ||
            request.SoftwarePinSecretReference.Trim().Length > 512 ||
            request.NextConsecutive <= 0 ||
            string.IsNullOrWhiteSpace(request.Prefix) || request.Prefix.Trim().Length > 10 ||
            !Uri.TryCreate(request.QrValidationUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new PayrollValidationException(
                "La serie y configuración fiscal de nómina electrónica no son válidas.");
        return store.SaveElectronicConfigurationAsync(user, request with
        {
            Prefix = request.Prefix.Trim().ToUpperInvariant(),
            SoftwareIdentificationCode = request.SoftwareIdentificationCode.Trim(),
            SoftwarePinSecretReference = request.SoftwarePinSecretReference.Trim(),
            QrValidationUrl = request.QrValidationUrl.Trim().TrimEnd('/')
        }, ct);
    }

    public Task SaveNoveltyAsync(PayrollUserIdentity user,
        SavePayrollNoveltyRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Manage);
        if (request.NoveltyId == Guid.Empty || request.EmploymentId == Guid.Empty ||
            request.ConceptId == Guid.Empty || request.Quantity <= 0 ||
            request.TotalAmount < 0 || request.EndDate < request.StartDate)
            throw new PayrollValidationException("La novedad no es válida.");
        return store.SaveNoveltyAsync(user, request with
        {
            Notes = Optional(request.Notes, 500),
            EvidenceUrl = Optional(request.EvidenceUrl, 1000)
        }, ct);
    }

    public async Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(
        PayrollUserIdentity user, CreatePayrollPaymentBatchRequest request,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Pay);
        if (request.PaymentBatchId == Guid.Empty || request.PayrollRunId == Guid.Empty ||
            request.PaymentMethodOptionId == Guid.Empty)
            throw new PayrollValidationException("Los datos del pago no son válidos.");
        var result = await store.CreatePaymentBatchAsync(user, request with
        {
            ReferenceNumber = Text(request.ReferenceNumber, 100, "Referencia de pago")
        }, ct);
        await accounting.RequestPostingAsync(user.BusinessId, result.PaymentBatchId,
            PayrollAccountingDocumentTypes.Payment, ct);
        await store.MarkAccountingSignalPublishedAsync(result.PaymentBatchId, ct);
        return result;
    }

    public Task<PayrollRunView> CreateRunAsync(PayrollUserIdentity user,
        CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Calculate);
        if (request.PayrollRunId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.PeriodEnd < request.PeriodStart || request.PaymentDate < request.PeriodStart ||
            !Enum.TryParse<PayrollRunKind>(request.RunKind, false, out var kind) ||
            (kind == PayrollRunKind.Regular) == (request.OriginalPayrollRunId is not null))
            throw new PayrollValidationException("Los datos de la liquidación no son válidos.");
        return store.CreateRunAsync(user, request with { RunKind = kind.ToString() }, ct);
    }

    public Task<PayrollRunView> GetRunAsync(PayrollUserIdentity user, Guid runId,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Read);
        if (runId == Guid.Empty) throw new PayrollValidationException("La liquidación es obligatoria.");
        return store.GetRunAsync(user, runId, ct);
    }

    public Task<IReadOnlyList<PayrollRunSummary>> ListRunsAsync(PayrollUserIdentity user,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Read);
        return store.ListRunsAsync(user, ct);
    }

    public async Task<PayrollRunView> CalculateRunAsync(PayrollUserIdentity user,
        Guid runId, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Calculate);
        var data = await store.LoadCalculationDataAsync(user, runId, ct);
        if (data.Employees.Count == 0)
            throw new PayrollValidationException("No existen relaciones laborales elegibles para el período.");
        try
        {
            var calculations = data.Employees.Select(calculator.Calculate).ToArray();
            return await store.SaveCalculationAsync(user, runId, calculations, ct);
        }
        catch (PayrollCalculationException error)
        {
            throw new PayrollValidationException(error.Message);
        }
    }

    public async Task<PayrollRunAcceptance> ApproveRunAsync(PayrollUserIdentity user,
        Guid runId, string idempotencyKey, byte[] rowVersion,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Approve);
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 160 || rowVersion.Length != 8)
            throw new PayrollValidationException("Idempotency-Key y versión son obligatorios.");
        var acceptance = await store.ApproveRunAsync(user, runId,
            idempotencyKey.Trim(), rowVersion, ct);
        var documentType = acceptance.RunKind == PayrollRunKind.Adjustment.ToString()
            ? PayrollAccountingDocumentTypes.Adjustment
            : PayrollAccountingDocumentTypes.Accrual;
        await accounting.RequestPostingAsync(user.BusinessId, runId, documentType, ct);
        await store.MarkAccountingSignalPublishedAsync(runId, ct);
        return acceptance;
    }

    public async Task<ElectronicPayrollPeriodView> GenerateElectronicPeriodAsync(
        PayrollUserIdentity user,
        GenerateElectronicPayrollPeriodRequest request,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Fiscal);
        if (request.ElectronicPeriodId == Guid.Empty ||
            request.BusinessId != user.BusinessId ||
            request.Year < 2020 || request.Month is < 1 or > 12)
            throw new PayrollValidationException("El período de nómina electrónica no es válido.");
        var result = await store.GenerateElectronicPeriodAsync(user, request, ct);
        foreach (var document in result.Documents)
            await fiscalProcessing.RequestGenerationAsync(
                user.BusinessId, document.FiscalDocumentId ??
                    throw new PayrollConflictException(
                        "Un documento electrónico no tiene raíz fiscal."), ct);
        await store.MarkElectronicSignalPublishedAsync(result.ElectronicPeriodId, ct);
        return result;
    }

    private static void Demand(PayrollUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PayrollForbiddenException($"Se requiere el permiso '{permission}'.");
    }

    private static string Text(string? value, int max, string label) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max
            ? throw new PayrollValidationException($"{label} es obligatorio y admite máximo {max} caracteres.")
            : value.Trim();

    private static string Code(string? value, int max) =>
        Text(value, max, "Código").ToUpperInvariant();

    private static string? Optional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max
            ? throw new PayrollValidationException($"El valor admite máximo {max} caracteres.")
            : value.Trim();
}

public sealed class PayrollForbiddenException(string message) : Exception(message);
public sealed class PayrollValidationException(string message) : Exception(message);
public sealed class PayrollConflictException(string message) : Exception(message);
public sealed class PayrollNotFoundException(string message) : Exception(message);
