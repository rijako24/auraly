using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Application.Fiscal;

namespace Auraly.Commerce.Payroll.Application;

public sealed record PayrollRunCalculationData(
    Guid PayrollRunId,
    IReadOnlyList<PayrollEmployeeCalculationInput> Employees,
    byte[] RowVersion)
{
    public int PeriodDays { get; init; }
    public bool IsPartial { get; init; }
    public byte[] ContextVersionHash { get; init; } = [];
    public IReadOnlyDictionary<Guid, PayrollEmployeeCalculationSource> Sources { get; init; }
        = new Dictionary<Guid, PayrollEmployeeCalculationSource>();
}

public sealed record PayrollEmployeeCalculationSource(decimal MaximumContractDays, byte[] NoveltyVersionHash, Guid RuleSetId);

public interface IPayrollStore
{
    Task<PayrollProfilePage> PageProfilesAsync(PayrollUserIdentity user,int page,int pageSize,string? search,bool? isActive,CancellationToken ct);
    Task<PayrollProfileView> GetProfileAsync(PayrollUserIdentity user,Guid id,CancellationToken ct);
    Task<PayrollProfileView> SaveProfileAsync(PayrollUserIdentity user,SavePayrollProfileRequest request,CancellationToken ct);
    Task<PayrollWorkspaceOptions> GetOptionsAsync(PayrollUserIdentity user, CancellationToken ct);
    Task<PayrollEmploymentPage> PageEmploymentsAsync(PayrollUserIdentity user, int page,
        int pageSize, string? search, bool? isActive, Guid? employmentId, CancellationToken ct);
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
    Task<PayrollNoveltyView> SaveNoveltyAsync(PayrollUserIdentity user, SavePayrollNoveltyRequest request,
        CancellationToken ct);
    Task<PayrollNoveltyView> VoidNoveltyAsync(PayrollUserIdentity user, Guid noveltyId,
        byte[] rowVersion, CancellationToken ct);
    Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(PayrollUserIdentity user,
        CreatePayrollPaymentBatchRequest request, CancellationToken ct);
    Task<PayrollRunView> CreateRunAsync(PayrollUserIdentity user,
        CreatePayrollRunRequest request, CancellationToken ct);
    Task<PayrollRunView> GetRunAsync(PayrollUserIdentity user, Guid runId,
        CancellationToken ct);
    Task<IReadOnlyList<PayrollRunSummary>> ListRunsAsync(PayrollUserIdentity user,
        CancellationToken ct);
    Task<PayrollRunEmploymentPage> PageRunEmploymentsAsync(PayrollUserIdentity user,
        Guid runId, int page, int pageSize, string? search, CancellationToken ct,
        PayrollRunEmploymentFilter? filter = null);
    Task<PayrollRunCalculationData> LoadCalculationDataAsync(PayrollUserIdentity user,
        Guid runId, CancellationToken ct, CalculatePayrollRunRequest? request = null);
    Task<PayrollRunView> SaveCalculationAsync(PayrollUserIdentity user, PayrollRunCalculationData data,
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
    public Task<PayrollProfilePage> PageProfilesAsync(PayrollUserIdentity user,int page,int pageSize,string? search,bool? isActive,CancellationToken ct=default)
    {
        Demand(user,PayrollPermissionCodes.Read);
        if(page<1 || pageSize is <1 or >100 || ((long)page-1)*pageSize>int.MaxValue || search?.Length>160)
            throw new PayrollValidationException("Indica una página y búsqueda válidas para los perfiles.");
        return store.PageProfilesAsync(user,page,pageSize,string.IsNullOrWhiteSpace(search)?null:search.Trim(),isActive,ct);
    }
    public Task<PayrollProfileView> GetProfileAsync(PayrollUserIdentity user,Guid id,CancellationToken ct=default)
    {
        Demand(user,PayrollPermissionCodes.Read);
        return store.GetProfileAsync(user,id,ct);
    }
    public Task<PayrollProfileView> SaveProfileAsync(PayrollUserIdentity user,SavePayrollProfileRequest request,CancellationToken ct=default)
    {
        Demand(user,PayrollPermissionCodes.Configure);
        if(request.ProfileId==Guid.Empty || request.RuleSetId==Guid.Empty || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length>32 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length>160 || request.Mappings is null || request.Mappings.Count>500 ||
            request.Mappings.Any(value=>value is null || value.MappingId==Guid.Empty || value.EffectiveTo<value.EffectiveFrom ||
                value.BeneficiaryPartyId==Guid.Empty || value.DebitAccountId is null && value.CreditAccountId is null && value.BeneficiaryPartyId is null) ||
            request.Mappings.Select(value=>value.MappingId).Distinct().Count()!=request.Mappings.Count)
            throw new PayrollValidationException("Revisa el código, el nombre y hasta 500 asignaciones contables con vigencias e identificadores distintos.");
        return store.SaveProfileAsync(user,request with{Code=request.Code.Trim().ToUpperInvariant(),Name=request.Name.Trim()},ct);
    }
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

    public Task<PayrollEmploymentPage> PageEmploymentsAsync(PayrollUserIdentity user,
        int page, int pageSize, string? search, bool? isActive, Guid? employmentId,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Read);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new PayrollValidationException("La paginación de trabajadores no es válida.");
        return store.PageEmploymentsAsync(user, page, pageSize,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), isActive, employmentId, ct);
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

    public Task<PayrollNoveltyView> SaveNoveltyAsync(PayrollUserIdentity user,
        SavePayrollNoveltyRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Manage);
        if (request.NoveltyId == Guid.Empty || request.EmploymentId == Guid.Empty ||
            request.ConceptId == Guid.Empty || request.Quantity < 0 ||
            request.TotalAmount < 0 || request.EndDate < request.StartDate ||
            (request.RowVersion is not null && request.RowVersion.Length != 8))
            throw new PayrollValidationException("La novedad no es válida.");
        return store.SaveNoveltyAsync(user, request with
        {
            Notes = Optional(request.Notes, 500),
            EvidenceUrl = Optional(request.EvidenceUrl, 1000)
        }, ct);
    }

    public Task<PayrollNoveltyView> VoidNoveltyAsync(PayrollUserIdentity user, Guid noveltyId,
        byte[] rowVersion, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Manage);
        if (noveltyId == Guid.Empty || rowVersion is null || rowVersion.Length != 8)
            throw new PayrollValidationException("La novedad y su versión son obligatorias.");
        return store.VoidNoveltyAsync(user, noveltyId, rowVersion, ct);
    }

    public async Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(
        PayrollUserIdentity user, CreatePayrollPaymentBatchRequest request,
        CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Pay);
        if (request.PaymentBatchId == Guid.Empty || request.PayrollRunId == Guid.Empty ||
            request.PaymentMethodOptionId == Guid.Empty || request.PaymentMethodId == Guid.Empty || request.PaymentDate == default)
            throw new PayrollValidationException("Los datos del pago no son válidos.");
        var result = await store.CreatePaymentBatchAsync(user, request with
        {
            ReferenceNumber = Text(request.ReferenceNumber, 100, "Referencia de pago")
        }, ct);
        if (result.AccountingSignalPublished) return result;
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

    public Task<PayrollRunEmploymentPage> PageRunEmploymentsAsync(PayrollUserIdentity user,
        Guid runId, int page, int pageSize, string? search, CancellationToken ct = default,
        PayrollRunEmploymentFilter? filter = null)
    {
        Demand(user, PayrollPermissionCodes.Read);
        if (runId == Guid.Empty || page < 1 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            throw new PayrollValidationException("La página debe tener entre 1 y 50 trabajadores.");
        if (search?.Length > 160) throw new PayrollValidationException("La búsqueda admite hasta 160 caracteres.");
        filter ??= new();
        if (filter.ContractNumber?.Length > 64 || filter.SortBy is not ("employeeName" or "identification" or "contractNumber" or "startDate" or "endDate")
            || filter.SortDirection is not ("asc" or "desc"))
            throw new PayrollValidationException("El filtro u orden de trabajadores no es válido.");
        return store.PageRunEmploymentsAsync(user, runId, page, pageSize, search?.Trim(), ct,
            filter with { ContractNumber = filter.ContractNumber?.Trim() });
    }

    public Task<PayrollRunView> CalculateRunAsync(PayrollUserIdentity user,
        Guid runId, CancellationToken ct = default)
        => CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(), ct);

    public async Task<PayrollRunView> CalculateRunAsync(PayrollUserIdentity user,
        Guid runId, CalculatePayrollRunRequest request, CancellationToken ct = default)
    {
        Demand(user, PayrollPermissionCodes.Calculate);
        if (runId == Guid.Empty) throw new PayrollValidationException("La liquidación es obligatoria.");
        var changes = request.SelectionChanges ?? [];
        var dayChanges = request.WorkedDaysChanges ?? [];
        var recalculate = request.RecalculateEmploymentIds ?? [];
        if (changes.Count > PayrollPaymentLimits.MaximumEmployees || changes.Any(value => value is null || value.EmploymentId == Guid.Empty)
            || changes.Select(value => value.EmploymentId).Distinct().Count() != changes.Count)
            throw new PayrollValidationException("La selección admite hasta 500 contratos distintos.");
        if (dayChanges.Count > PayrollPaymentLimits.MaximumEmployees || dayChanges.Any(value => value is null || value.EmploymentId == Guid.Empty || value.ConceptId == Guid.Empty)
            || dayChanges.Select(value => value.EmploymentId).Distinct().Count() != dayChanges.Count)
            throw new PayrollValidationException("La edición de días admite hasta 500 contratos distintos.");
        if (recalculate.Count > PayrollPaymentLimits.MaximumEmployees || recalculate.Any(id => id == Guid.Empty)
            || recalculate.Distinct().Count() != recalculate.Count)
            throw new PayrollValidationException("El recálculo admite hasta 500 contratos distintos.");
        if (recalculate.Count > 0 && (request.SelectAll.HasValue || changes.Count > 0 || dayChanges.Count > 0))
            throw new PayrollValidationException("Recalcula los trabajadores sin cambiar simultáneamente su selección o sus días.");
        if ((request.SelectAll.HasValue || changes.Count > 0 || dayChanges.Count > 0 || recalculate.Count > 0) && request.RowVersion is not { Length: 8 })
            throw new PayrollValidationException("La versión de la liquidación es obligatoria para cambiar la selección o los días.");
        if (request.RowVersion is not null && request.RowVersion.Length != 8)
            throw new PayrollValidationException("La versión de la liquidación no es válida.");
        var data = await store.LoadCalculationDataAsync(user, runId, ct, request);
        if (data.Employees.Count == 0)
            throw new PayrollValidationException("Selecciona al menos un trabajador elegible para el período.");
        if (recalculate.Count > 0 && recalculate.Except(data.Employees.Select(value => value.EmploymentId)).Any())
            throw new PayrollValidationException("Sólo se pueden recalcular trabajadores seleccionados en esta liquidación.");
        try
        {
            if (dayChanges.Count > 0)
            {
                var byEmployment = dayChanges.ToDictionary(value => value.EmploymentId);
                if (byEmployment.Keys.Except(data.Employees.Select(value => value.EmploymentId)).Any())
                    throw new PayrollValidationException("Sólo se pueden editar los días de trabajadores seleccionados en esta liquidación.");
                data = data with
                {
                    Employees = data.Employees.Select(input => byEmployment.TryGetValue(input.EmploymentId, out var change)
                        ? calculator.WithConceptWorkedDays(input, change.ConceptId, change.WorkedDays, data.PeriodDays) : input).ToArray()
                };
            }
            var calculations = data.Employees.Select(calculator.Calculate).ToArray();
            return await store.SaveCalculationAsync(user, data, calculations, ct);
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
        if (acceptance.AccountingSignalPublished) return acceptance;
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
