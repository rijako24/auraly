using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;

namespace Auraly.Commerce.Accounting.Application;

public interface IAccountingStore
{
    Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingCostCenterView>> ListCostCentersAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingPeriodView>> ListPeriodsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingMappingView>> ListMappingsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingCategoryDefinition>> ListCategoryDefinitionsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingDefaultsResult> EnsureDefaultsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingAccountView> CreateAccountAsync(AccountingUserIdentity user, CreateAccountingAccountRequest request, CancellationToken cancellationToken);
    Task<AccountingCostCenterView> CreateCostCenterAsync(AccountingUserIdentity user, CreateCostCenterRequest request, CancellationToken cancellationToken);
    Task<AccountingPeriodView> CreatePeriodAsync(AccountingUserIdentity user, CreateAccountingPeriodRequest request, CancellationToken cancellationToken);
    Task SetMappingAsync(AccountingUserIdentity user, SetAccountMappingRequest request, CancellationToken cancellationToken);
    Task ClosePeriodAsync(AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken);
    Task<AccountingPostingView?> RetryPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<AccountingEntryView?> GetEntryAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed class AccountingService(IAccountingStore store)
{
    public Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListAccountsAsync(user, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingCostCenterView>> ListCostCentersAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListCostCentersAsync(user, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingPeriodView>> ListPeriodsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListPeriodsAsync(user, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingMappingView>> ListMappingsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListMappingsAsync(user, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingCategoryDefinition>> ListCategoryDefinitionsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListCategoryDefinitionsAsync(user, cancellationToken);
    }

    public Task<AccountingDefaultsResult> EnsureDefaultsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        return store.EnsureDefaultsAsync(user, cancellationToken);
    }

    public Task<AccountingAccountView> CreateAccountAsync(AccountingUserIdentity user, CreateAccountingAccountRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.TenantId != user.TenantId || request.AccountId == Guid.Empty)
            throw new AccountingForbiddenException("The account belongs to another legal entity or has no ID.");
        ValidateText(request.Code, 32, "Account code");
        ValidateText(request.Name, 200, "Account name");
        if (request.AccountType is not ("Asset" or "Liability" or "Equity" or "Revenue" or "Expense" or "ContraRevenue"))
            throw new AccountingValidationException("The account type is invalid.");
        return store.CreateAccountAsync(user, request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingCostCenterView> CreateCostCenterAsync(AccountingUserIdentity user, CreateCostCenterRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.BusinessId != user.BusinessId || request.CostCenterId == Guid.Empty)
            throw new AccountingForbiddenException("The cost center belongs to another business or has no ID.");
        ValidateText(request.Code, 32, "Cost center code");
        ValidateText(request.Name, 160, "Cost center name");
        return store.CreateCostCenterAsync(user, request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingPeriodView> CreatePeriodAsync(AccountingUserIdentity user, CreateAccountingPeriodRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.PeriodsManage);
        if (request.TenantId != user.TenantId || request.PeriodId == Guid.Empty)
            throw new AccountingForbiddenException("The period belongs to another legal entity or has no ID.");
        ValidateText(request.Name, 80, "Period name");
        try { AccountingPeriodRules.Validate(request.StartsOn, request.EndsOn); }
        catch (AccountingRuleException exception) { throw new AccountingValidationException(exception.Message); }
        return store.CreatePeriodAsync(user, request with { Name = request.Name.Trim() }, cancellationToken);
    }

    public Task SetMappingAsync(AccountingUserIdentity user, SetAccountMappingRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.TenantId != user.TenantId || request.AccountId == Guid.Empty || request.BusinessId is { } businessId && businessId != user.BusinessId)
            throw new AccountingForbiddenException("The mapping is outside the authenticated scope.");
        ValidateText(request.Category, 64, "Accounting category");
        if (request.EffectiveTo < request.EffectiveFrom)
            throw new AccountingValidationException("The mapping validity range is invalid.");
        return store.SetMappingAsync(user, request with { Category = request.Category.Trim() }, cancellationToken);
    }

    public Task ClosePeriodAsync(AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.PeriodsManage);
        if (periodId == Guid.Empty) throw new AccountingValidationException("PeriodId is required.");
        return store.ClosePeriodAsync(user, periodId, cancellationToken);
    }

    public Task<AccountingPostingView?> RetryPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Retry);
        if (documentId == Guid.Empty) throw new AccountingValidationException("DocumentId is required.");
        return store.RetryPostingAsync(user, documentId, cancellationToken);
    }

    public Task<AccountingEntryView?> GetEntryAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (documentId == Guid.Empty) throw new AccountingValidationException("DocumentId is required.");
        return store.GetEntryAsync(user, documentId, cancellationToken);
    }

    public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (from == default || to < from) throw new AccountingValidationException("The report date range is invalid.");
        return store.GetTrialBalanceAsync(user, from, to, cancellationToken);
    }

    private static void Demand(AccountingUserIdentity user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(permission)) throw new AccountingForbiddenException($"Permission '{permission}' is required.");
    }

    private static void ValidateText(string value, int maximum, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new AccountingValidationException($"{label} is required and limited to {maximum} characters.");
    }
}

public sealed class AccountingForbiddenException(string message) : Exception(message);
public sealed class AccountingValidationException(string message) : Exception(message);
public sealed class AccountingConflictException(string message) : Exception(message);
