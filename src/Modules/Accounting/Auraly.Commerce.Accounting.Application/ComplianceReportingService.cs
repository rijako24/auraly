using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public interface IComplianceReportingStore
{
    Task<IReadOnlyList<ComplianceReportDefinitionView>> ListDefinitionsAsync(AccountingUserIdentity user, short? taxYear, CancellationToken token);
    Task<IReadOnlyList<ComplianceConceptMappingView>> ListMappingsAsync(AccountingUserIdentity user, short taxYear, string? formatCode, CancellationToken token);
    Task<ComplianceConceptMappingView> SetMappingAsync(AccountingUserIdentity user, Guid mappingId, SetComplianceConceptMappingRequest request, CancellationToken token);
    Task<ComplianceReportRunView> GenerateAsync(AccountingUserIdentity user, Guid runId, GenerateComplianceReportRequest request, CancellationToken token);
    Task<IReadOnlyList<ComplianceReportRunView>> ListRunsAsync(AccountingUserIdentity user, short? taxYear, CancellationToken token);
    Task<ComplianceReportArtifact?> GetArtifactAsync(AccountingUserIdentity user, Guid runId, CancellationToken token);
}

public sealed class ComplianceReportingService(
    IComplianceReportingStore store,
    IAuralyIdGenerator ids)
{
    public Task<IReadOnlyList<ComplianceReportDefinitionView>> ListDefinitionsAsync(
        AccountingUserIdentity user, short? taxYear, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (taxYear is < 2000 or > 2200)
            throw new AccountingValidationException("The tax year is invalid.");
        return store.ListDefinitionsAsync(user, taxYear, token);
    }

    public Task<IReadOnlyList<ComplianceConceptMappingView>> ListMappingsAsync(
        AccountingUserIdentity user, short taxYear, string? formatCode,
        CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        ValidateYear(taxYear);
        return store.ListMappingsAsync(user, taxYear, CleanOptional(formatCode), token);
    }

    public Task<ComplianceConceptMappingView> SetMappingAsync(
        AccountingUserIdentity user, SetComplianceConceptMappingRequest request,
        CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        ValidateYear(request.TaxYear);
        if (request.BusinessId is Guid businessId && businessId != user.BusinessId)
            throw new AccountingForbiddenException("The mapping belongs to another business.");
        if (request.AccountId == Guid.Empty || request.FormatVersion <= 0)
            throw new AccountingValidationException("The compliance mapping scope is invalid.");
        ValidateCode(request.AuthorityCode, 24, "Authority");
        ValidateCode(request.FormatCode, 24, "Format");
        ValidateCode(request.ConceptCode, 24, "Concept");
        ValidateCode(request.TargetField, 64, "Target field");
        return store.SetMappingAsync(user, ids.NewId(), request with
        {
            AuthorityCode = request.AuthorityCode.Trim().ToUpperInvariant(),
            FormatCode = request.FormatCode.Trim().ToUpperInvariant(),
            ConceptCode = request.ConceptCode.Trim().ToUpperInvariant(),
            TargetField = request.TargetField.Trim()
        }, token);
    }

    public Task<ComplianceReportRunView> GenerateAsync(
        AccountingUserIdentity user, GenerateComplianceReportRequest request,
        CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        ValidateYear(request.TaxYear);
        if (request.FormatVersion <= 0 || request.PeriodFrom == default ||
            request.PeriodTo < request.PeriodFrom ||
            request.PeriodFrom.Year != request.TaxYear || request.PeriodTo.Year != request.TaxYear)
            throw new AccountingValidationException("The report period must be inside the selected tax year.");
        ValidateCode(request.AuthorityCode, 24, "Authority");
        ValidateCode(request.FormatCode, 24, "Format");
        return store.GenerateAsync(user, ids.NewId(), request with
        {
            AuthorityCode = request.AuthorityCode.Trim().ToUpperInvariant(),
            FormatCode = request.FormatCode.Trim().ToUpperInvariant()
        }, token);
    }

    public Task<IReadOnlyList<ComplianceReportRunView>> ListRunsAsync(
        AccountingUserIdentity user, short? taxYear, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (taxYear.HasValue) ValidateYear(taxYear.Value);
        return store.ListRunsAsync(user, taxYear, token);
    }

    public Task<ComplianceReportArtifact?> GetArtifactAsync(
        AccountingUserIdentity user, Guid runId, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (runId == Guid.Empty)
            throw new AccountingValidationException("The report run ID is required.");
        return store.GetArtifactAsync(user, runId, token);
    }

    private static void Demand(AccountingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new AccountingForbiddenException($"Missing permission '{permission}'.");
    }

    private static void ValidateYear(short taxYear)
    {
        if (taxYear is < 2000 or > 2200)
            throw new AccountingValidationException("The tax year is invalid.");
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void ValidateCode(string value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            throw new AccountingValidationException($"{label} is required and cannot exceed {max} characters.");
    }
}
