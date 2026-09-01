using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;

namespace Auraly.Commerce.Accounting.Application;

public interface IAccountingStore
{
    Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankAccountView>> ListBankAccountsAsync(AccountingUserIdentity user, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankAccountView>> ListActiveBankAccountsForTenantAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> IsAccountingEnabledAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<BankAccountView> SaveBankAccountAsync(AccountingUserIdentity user, SaveBankAccountRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingCostCenterView>> ListCostCentersAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingPeriodView>> ListPeriodsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingMappingView>> ListMappingsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingCategoryDefinition>> ListCategoryDefinitionsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingDefaultsResult> EnsureDefaultsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingReadinessView> GetReadinessAsync(AccountingUserIdentity user, DateOnly? effectiveFrom, string? openingBalanceMode, CancellationToken cancellationToken);
    Task<AccountingReadinessView> ActivateAsync(AccountingUserIdentity user, ActivateAccountingRequest request, CancellationToken cancellationToken);
    Task<AccountingOpeningBalanceView?> GetOpeningBalanceAsync(AccountingUserIdentity user, DateOnly effectiveOn, CancellationToken cancellationToken);
    Task<AccountingOpeningBalanceView> SaveOpeningBalanceAsync(AccountingUserIdentity user, SaveAccountingOpeningBalanceRequest request, CancellationToken cancellationToken);
    Task<AccountingOpeningBalanceView> ApproveOpeningBalanceAsync(AccountingUserIdentity user, Guid batchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingOpeningBalancePosting>> ListPendingOpeningPostingsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request, CancellationToken cancellationToken);
    Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken cancellationToken);
    Task<AccountingAccountView> CreateAccountAsync(AccountingUserIdentity user, CreateAccountingAccountRequest request, CancellationToken cancellationToken);
    Task<AccountingCostCenterView> CreateCostCenterAsync(AccountingUserIdentity user, CreateCostCenterRequest request, CancellationToken cancellationToken);
    Task<AccountingPeriodView> CreatePeriodAsync(AccountingUserIdentity user, CreateAccountingPeriodRequest request, CancellationToken cancellationToken);
    Task SetMappingAsync(AccountingUserIdentity user, SetAccountMappingRequest request, CancellationToken cancellationToken);
    Task ClosePeriodAsync(AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken);
    Task<AccountingPostingView?> RetryPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<AccountingEntryView?> GetEntryAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(AccountingUserIdentity user, DateOnly asOf, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public sealed class AccountingService(
    IAccountingStore store,
    AccountingProcessingCoordinator processing)
{
    public async Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(
        AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (request.AdjustmentId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.SubledgerId == Guid.Empty || request.CounterpartAccountId == Guid.Empty)
            throw new AccountingValidationException("The account adjustment scope is invalid.");
        if (request.SubledgerKind is not (AccountingSubledgerKinds.Receivable or AccountingSubledgerKinds.Payable) ||
            request.Direction is not (AccountingAdjustmentDirections.Increase or AccountingAdjustmentDirections.Decrease))
            throw new AccountingValidationException("The account adjustment type is invalid.");
        if (request.Amount <= 0)
            throw new AccountingValidationException("The adjustment amount must be positive.");
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Description");
        var result = await store.ConfirmAccountAdjustmentAsync(user, request, cancellationToken);
        if (!result.IsDuplicate)
            await processing.RequestPostingAsync(user.BusinessId, result.DocumentId,
                result.DocumentType, cancellationToken);
        return result;
    }

    public async Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (request.VoucherId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.Lines is null || request.Lines.Count < 2)
            throw new AccountingValidationException("A manual voucher requires at least two lines.");
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Description");
        foreach (var line in request.Lines)
        {
            if (line.AccountId == Guid.Empty || line.Debit < 0 || line.Credit < 0 ||
                (line.Debit > 0) == (line.Credit > 0))
                throw new AccountingValidationException(
                    "Each manual line requires exactly one positive debit or credit.");
            ValidateText(line.Description, 500, "Line description");
        }
        var debit = request.Lines.Sum(line => line.Debit);
        var credit = request.Lines.Sum(line => line.Credit);
        if (debit <= 0 || decimal.Round(debit, 4) != decimal.Round(credit, 4))
            throw new AccountingValidationException("The manual voucher is not balanced.");
        var result = await store.ConfirmManualVoucherAsync(user, request, cancellationToken);
        if (!result.IsDuplicate)
            await processing.RequestPostingAsync(user.BusinessId, result.DocumentId,
                result.DocumentType, cancellationToken);
        return result;
    }
    public Task<AccountingReadinessView> GetReadinessAsync(
        AccountingUserIdentity user, DateOnly? effectiveFrom = null,
        string? openingBalanceMode = null,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.GetReadinessAsync(user, effectiveFrom, openingBalanceMode, cancellationToken);
    }

    public async Task<AccountingReadinessView> ActivateAsync(
        AccountingUserIdentity user, ActivateAccountingRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Activate);
        if (request.EffectiveFrom == default)
            throw new AccountingValidationException("EffectiveFrom is required.");
        if (request.FunctionalCurrencyCode is not "COP")
            throw new AccountingValidationException(
                "The initial accounting activation supports COP as functional currency.");
        if (request.OpeningBalanceMode is not ("ZeroDeclared" or "ImportedAndApproved"))
            throw new AccountingValidationException("The opening balance mode is invalid.");
        var result = await store.ActivateAsync(user, request, cancellationToken);
        if (request.OpeningBalanceMode == "ImportedAndApproved")
        {
            foreach (var pending in await store.ListPendingOpeningPostingsAsync(user, cancellationToken))
                await processing.RequestPostingAsync(pending.BusinessId, pending.BatchId,
                    AccountingManualDocumentTypes.OpeningBalance, cancellationToken);
            result = await store.GetReadinessAsync(user, request.EffectiveFrom,
                request.OpeningBalanceMode, cancellationToken);
        }
        return result;
    }

    public Task<AccountingOpeningBalanceView?> GetOpeningBalanceAsync(
        AccountingUserIdentity user, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (effectiveOn == default) throw new AccountingValidationException("EffectiveOn is required.");
        return store.GetOpeningBalanceAsync(user, effectiveOn, cancellationToken);
    }

    public Task<AccountingOpeningBalanceView> SaveOpeningBalanceAsync(
        AccountingUserIdentity user, SaveAccountingOpeningBalanceRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.BatchId == Guid.Empty || request.BusinessId != user.BusinessId || request.EffectiveOn == default)
            throw new AccountingForbiddenException("The opening balance scope is invalid.");
        if (request.CurrencyCode is not "COP")
            throw new AccountingValidationException("The initial accounting opening balance supports COP.");
        ValidateText(request.Description, 300, "Description");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new AccountingValidationException("The opening balance requires at least one line.");
        foreach (var line in request.Lines)
        {
            if (line.AccountId == Guid.Empty || line.Debit < 0 || line.Credit < 0 ||
                (line.Debit > 0) == (line.Credit > 0))
                throw new AccountingValidationException("Each opening balance line requires exactly one positive debit or credit.");
            ValidateText(line.Description, 300, "Line description");
        }
        return store.SaveOpeningBalanceAsync(user, request, cancellationToken);
    }

    public Task<AccountingOpeningBalanceView> ApproveOpeningBalanceAsync(
        AccountingUserIdentity user, Guid batchId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Activate);
        if (batchId == Guid.Empty) throw new AccountingValidationException("BatchId is required.");
        return store.ApproveOpeningBalanceAsync(user, batchId, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListAccountsAsync(user, cancellationToken);
    }

    public Task<IReadOnlyList<BankAccountView>> ListBankAccountsAsync(
        AccountingUserIdentity user, bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListBankAccountsAsync(user, includeInactive, cancellationToken);
    }

    public async Task<PosAccountingSettlementConfiguration> GetPosSettlementConfigurationAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new AccountingForbiddenException("The enrolled device has no tenant scope.");
        var enabled = await store.IsAccountingEnabledAsync(tenantId, cancellationToken);
        var accounts = enabled
            ? await store.ListActiveBankAccountsForTenantAsync(tenantId, cancellationToken)
            : [];
        return new(enabled, accounts);
    }

    public Task<BankAccountView> SaveBankAccountAsync(
        AccountingUserIdentity user, SaveBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.BankAccountId == Guid.Empty || request.AccountingAccountId == Guid.Empty ||
            request.AccountTypeOptionId == Guid.Empty)
            throw new AccountingValidationException("The bank account scope is invalid.");
        ValidateText(request.BankName, 120, "Bank name");
        ValidateText(request.AccountNumber, 64, "Account number");
        ValidateText(request.DisplayName, 160, "Display name");
        return store.SaveBankAccountAsync(user, request with
        {
            BankName = request.BankName.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            DisplayName = request.DisplayName.Trim()
        }, cancellationToken);
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

    public Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        ValidateText(accountCode, 30, "Account code");
        if (from == default || to < from) throw new AccountingValidationException("The report date range is invalid.");
        return store.GetAccountMovementsAsync(user, accountCode.Trim(), from, to, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidateReport(user, from, to);
        return store.GetJournalAsync(user, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidateReport(user, from, to);
        return store.GetGeneralLedgerAsync(user, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(
        AccountingUserIdentity user, DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (asOf == default) throw new AccountingValidationException("The report date is invalid.");
        return store.GetBalanceSheetAsync(user, asOf, cancellationToken);
    }

    public Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidateReport(user, from, to);
        return store.GetIncomeStatementAsync(user, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidateReport(user, from, to);
        return store.GetExceptionsAsync(user, from, to, cancellationToken);
    }

    private static void ValidateReport(AccountingUserIdentity user, DateOnly from, DateOnly to)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (from == default || to < from)
            throw new AccountingValidationException("The report date range is invalid.");
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
