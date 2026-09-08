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
    Task<IReadOnlyList<AccountingCostCenterAssignmentView>> ListCostCenterAssignmentsAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<AccountingCostCenterAssignmentView> SaveCostCenterAssignmentAsync(AccountingUserIdentity user, SaveAccountingCostCenterAssignmentRequest request, CancellationToken cancellationToken);
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
    Task<AccountingCostCenterView> UpdateCostCenterAsync(AccountingUserIdentity user, Guid costCenterId, UpdateCostCenterRequest request, CancellationToken cancellationToken);
    Task<AccountingCostCenterView> SetCostCenterStatusAsync(AccountingUserIdentity user, Guid costCenterId, SetAccountingCostCenterStatusRequest request, CancellationToken cancellationToken);
    Task<AccountingPeriodView> CreatePeriodAsync(AccountingUserIdentity user, CreateAccountingPeriodRequest request, CancellationToken cancellationToken);
    Task SetMappingAsync(AccountingUserIdentity user, SetAccountMappingRequest request, CancellationToken cancellationToken);
    Task ClosePeriodAsync(AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken);
    Task<AccountingPostingView?> RetryPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<AccountingPostingView?> GetPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<AccountingEntryView?> GetEntryAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? costCenterId = null);
    Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(AccountingUserIdentity user, DateOnly asOf, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<AccountingDocumentPage> ListDocumentsAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IBankReconciliationStore
{
    Task<IReadOnlyList<BankReconciliationSummaryView>> ListAsync(AccountingUserIdentity user, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView?> GetAsync(AccountingUserIdentity user, Guid reconciliationId, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ImportAsync(AccountingUserIdentity user, ImportBankReconciliationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> AllocateAsync(AccountingUserIdentity user, Guid reconciliationId, CreateBankReconciliationAllocationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ReverseAllocationAsync(AccountingUserIdentity user, Guid reconciliationId, Guid matchId, ReverseBankReconciliationAllocationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> CloseAsync(AccountingUserIdentity user, Guid reconciliationId, ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ReopenAsync(AccountingUserIdentity user, Guid reconciliationId, ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken);
}

public sealed class AccountingService(
    IAccountingStore store,
    IBankReconciliationStore bankReconciliations,
    AccountingProcessingCoordinator processing)
{
    public Task<IReadOnlyList<BankReconciliationSummaryView>> ListBankReconciliationsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationRead);
        return bankReconciliations.ListAsync(user, cancellationToken);
    }

    public Task<BankReconciliationDetailView?> GetBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationRead);
        return bankReconciliations.GetAsync(user, reconciliationId, cancellationToken);
    }

    public Task<BankReconciliationDetailView> ImportBankReconciliationAsync(
        AccountingUserIdentity user, ImportBankReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationManage);
        if (request.ReconciliationId == Guid.Empty || request.BankAccountId == Guid.Empty ||
            request.PeriodFrom == default || request.PeriodTo < request.PeriodFrom ||
            request.Lines is null || request.Lines.Count == 0)
            throw new AccountingValidationException("El extracto y su periodo son obligatorios.");
        ValidateText(request.FileName, 260, "Nombre del archivo");
        if (request.Lines.Select(line => line.LineNumber).Distinct().Count() != request.Lines.Count ||
            request.Lines.Any(line => line.LineNumber <= 0 || line.Amount == 0 ||
                line.TransactionDate < request.PeriodFrom || line.TransactionDate > request.PeriodTo ||
                string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500))
            throw new AccountingValidationException("Las líneas del extracto no son válidas para el periodo.");
        var orderedLines = request.Lines.OrderBy(line => line.LineNumber).ToArray();
        var runningBalance = request.OpeningBalance;
        var hasLineBalances = orderedLines.Any(line => line.Balance.HasValue);
        if (hasLineBalances && orderedLines.Any(line => !line.Balance.HasValue))
            throw new AccountingValidationException("El saldo por movimiento debe venir completo o no incluirse.");
        foreach (var line in orderedLines)
        {
            runningBalance += line.Amount;
            if (line.Balance.HasValue && Math.Abs(line.Balance.Value - runningBalance) > .0001m)
                throw new AccountingValidationException($"El saldo de la línea {line.LineNumber} no corresponde al movimiento.");
        }
        if (Math.Abs(runningBalance - request.ClosingBalance) > .0001m)
            throw new AccountingValidationException("El saldo inicial, los movimientos y el saldo final del extracto no cuadran.");
        return bankReconciliations.ImportAsync(user, request, cancellationToken);
    }

    public Task<BankReconciliationDetailView> AllocateBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        CreateBankReconciliationAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationManage);
        if (reconciliationId == Guid.Empty || request.MatchId == Guid.Empty ||
            request.StatementLineId == Guid.Empty || request.EntryId == Guid.Empty ||
            request.EntryLineNumber <= 0 || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.RowVersion))
            throw new AccountingValidationException("El cruce bancario no es válido.");
        return bankReconciliations.AllocateAsync(user, reconciliationId, request, cancellationToken);
    }

    public Task<BankReconciliationDetailView> ReverseBankReconciliationAllocationAsync(
        AccountingUserIdentity user, Guid reconciliationId, Guid matchId,
        ReverseBankReconciliationAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationManage);
        ValidateText(request.Reason, 500, "Motivo");
        return bankReconciliations.ReverseAllocationAsync(user, reconciliationId, matchId, request, cancellationToken);
    }

    public Task<BankReconciliationDetailView> CloseBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationClose);
        return bankReconciliations.CloseAsync(user, reconciliationId, request, cancellationToken);
    }

    public Task<BankReconciliationDetailView> ReopenBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationClose);
        ValidateText(request.Reason ?? "", 500, "Motivo de reapertura");
        return bankReconciliations.ReopenAsync(user, reconciliationId, request, cancellationToken);
    }
    public Task<IReadOnlyList<AccountingCostCenterAssignmentView>> ListCostCenterAssignmentsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.ListCostCenterAssignmentsAsync(user, cancellationToken);
    }

    public Task<AccountingCostCenterAssignmentView> SaveCostCenterAssignmentAsync(
        AccountingUserIdentity user, SaveAccountingCostCenterAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.AssignmentId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.CostCenterId == Guid.Empty ||
            !AccountingCostCenterOperationKinds.IsValid(request.OperationKind))
            throw new AccountingValidationException("La asignación del centro de costo no es válida.");
        return store.SaveCostCenterAssignmentAsync(user, request, cancellationToken);
    }
    public async Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(
        AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (request.AdjustmentId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.SubledgerId == Guid.Empty || request.CounterpartAccountId == Guid.Empty)
            throw new AccountingValidationException("El ajuste de cartera no pertenece al alcance autorizado.");
        if (request.SubledgerKind is not (AccountingSubledgerKinds.Receivable or AccountingSubledgerKinds.Payable) ||
            request.Direction is not (AccountingAdjustmentDirections.Increase or AccountingAdjustmentDirections.Decrease))
            throw new AccountingValidationException("El tipo de ajuste de cartera no es válido.");
        if (request.Amount <= 0)
            throw new AccountingValidationException("El valor del ajuste debe ser positivo.");
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Descripción");
        var result = await store.ConfirmAccountAdjustmentAsync(user, request, cancellationToken);
        // The durable job is authoritative. Publishing is only a wake-up signal,
        // so a replay must publish again while that job is still pending. The
        // coordinator gate suppresses the signal once it is already posted.
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
            throw new AccountingValidationException("El comprobante manual requiere al menos dos partidas.");
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Descripción");
        foreach (var line in request.Lines)
        {
            if (line.AccountId == Guid.Empty || line.Debit < 0 || line.Credit < 0 ||
                (line.Debit > 0) == (line.Credit > 0))
                throw new AccountingValidationException(
                    "Each manual line requires exactly one positive debit or credit.");
            ValidateText(line.Description, 500, "Descripción de la partida");
        }
        var debit = request.Lines.Sum(line => line.Debit);
        var credit = request.Lines.Sum(line => line.Credit);
        if (debit <= 0 || decimal.Round(debit, 4) != decimal.Round(credit, 4))
            throw new AccountingValidationException("El comprobante manual no está cuadrado.");
        var result = await store.ConfirmManualVoucherAsync(user, request, cancellationToken);
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
            throw new AccountingValidationException("La fecha de inicio es obligatoria.");
        if (request.FunctionalCurrencyCode is not "COP")
            throw new AccountingValidationException(
                "La activación contable inicial admite COP como moneda funcional.");
        if (request.OpeningBalanceMode is not ("ZeroDeclared" or "ImportedAndApproved"))
            throw new AccountingValidationException("El modo de saldos iniciales no es válido.");
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
        if (effectiveOn == default) throw new AccountingValidationException("La fecha efectiva es obligatoria.");
        return store.GetOpeningBalanceAsync(user, effectiveOn, cancellationToken);
    }

    public Task<AccountingOpeningBalanceView> SaveOpeningBalanceAsync(
        AccountingUserIdentity user, SaveAccountingOpeningBalanceRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.BatchId == Guid.Empty || request.BusinessId != user.BusinessId || request.EffectiveOn == default)
            throw new AccountingForbiddenException("El saldo inicial no pertenece al alcance autorizado.");
        if (request.CurrencyCode is not "COP")
            throw new AccountingValidationException("El saldo inicial contable admite COP.");
        ValidateText(request.Description, 300, "Descripción");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new AccountingValidationException("El saldo inicial requiere al menos una partida.");
        foreach (var line in request.Lines)
        {
            if (line.AccountId == Guid.Empty || line.Debit < 0 || line.Credit < 0 ||
                (line.Debit > 0) == (line.Credit > 0))
                throw new AccountingValidationException("Cada partida del saldo inicial requiere exactamente un débito o un crédito positivo.");
            ValidateText(line.Description, 300, "Descripción de la partida");
        }
        return store.SaveOpeningBalanceAsync(user, request, cancellationToken);
    }

    public Task<AccountingOpeningBalanceView> ApproveOpeningBalanceAsync(
        AccountingUserIdentity user, Guid batchId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Activate);
        if (batchId == Guid.Empty) throw new AccountingValidationException("El identificador del lote es obligatorio.");
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
            throw new AccountingForbiddenException("El dispositivo inscrito no tiene una entidad legal asignada.");
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
            throw new AccountingValidationException("La cuenta bancaria, el auxiliar PUC y el tipo de cuenta son obligatorios.");
        ValidateText(request.BankName, 120, "Banco");
        ValidateText(request.AccountNumber, 64, "Número de cuenta");
        ValidateText(request.DisplayName, 160, "Nombre para mostrar");
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
            throw new AccountingForbiddenException("La cuenta pertenece a otra entidad legal o no tiene identificador.");
        ValidateText(request.Code, 32, "Código de cuenta");
        ValidateText(request.Name, 200, "Nombre de cuenta");
        if (request.AccountType is not ("Asset" or "Liability" or "Equity" or "Revenue" or "Expense" or "ContraRevenue"))
            throw new AccountingValidationException("El tipo de cuenta no es válido.");
        return store.CreateAccountAsync(user, request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingCostCenterView> CreateCostCenterAsync(AccountingUserIdentity user, CreateCostCenterRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.BusinessId != user.BusinessId || request.CostCenterId == Guid.Empty)
            throw new AccountingForbiddenException("El centro de costo pertenece a otra sede o no tiene identificador.");
        ValidateText(request.Code, 32, "Código del centro de costo");
        ValidateText(request.Name, 160, "Nombre del centro de costo");
        return store.CreateCostCenterAsync(user, request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingCostCenterView> SetCostCenterStatusAsync(
        AccountingUserIdentity user, Guid costCenterId,
        SetAccountingCostCenterStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (costCenterId == Guid.Empty)
            throw new AccountingValidationException("El identificador del centro de costo es obligatorio.");
        return store.SetCostCenterStatusAsync(user, costCenterId, request,
            cancellationToken);
    }

    public Task<AccountingCostCenterView> UpdateCostCenterAsync(
        AccountingUserIdentity user, Guid costCenterId, UpdateCostCenterRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (costCenterId == Guid.Empty)
            throw new AccountingValidationException("El identificador del centro de costo es obligatorio.");
        ValidateText(request.Code, 32, "Código del centro de costo");
        ValidateText(request.Name, 160, "Nombre del centro de costo");
        return store.UpdateCostCenterAsync(user, costCenterId,
            request with { Code = request.Code.Trim(), Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingPeriodView> CreatePeriodAsync(AccountingUserIdentity user, CreateAccountingPeriodRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.PeriodsManage);
        if (request.TenantId != user.TenantId || request.PeriodId == Guid.Empty)
            throw new AccountingForbiddenException("El periodo pertenece a otra entidad legal o no tiene identificador.");
        ValidateText(request.Name, 80, "Nombre del periodo");
        try { AccountingPeriodRules.Validate(request.StartsOn, request.EndsOn); }
        catch (AccountingRuleException exception) { throw new AccountingValidationException(exception.Message); }
        return store.CreatePeriodAsync(user, request with { Name = request.Name.Trim() }, cancellationToken);
    }

    public Task SetMappingAsync(AccountingUserIdentity user, SetAccountMappingRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (request.TenantId != user.TenantId || request.AccountId == Guid.Empty || request.BusinessId is { } businessId && businessId != user.BusinessId)
            throw new AccountingForbiddenException("La parametrización está fuera del alcance autorizado.");
        ValidateText(request.Category, 64, "Categoría contable");
        if (request.EffectiveTo < request.EffectiveFrom)
            throw new AccountingValidationException("El rango de vigencia de la parametrización no es válido.");
        return store.SetMappingAsync(user, request with { Category = request.Category.Trim() }, cancellationToken);
    }

    public Task ClosePeriodAsync(AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.PeriodsManage);
        if (periodId == Guid.Empty) throw new AccountingValidationException("El identificador del periodo es obligatorio.");
        return store.ClosePeriodAsync(user, periodId, cancellationToken);
    }

    public Task<AccountingPostingView?> RetryPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Retry);
        if (documentId == Guid.Empty) throw new AccountingValidationException("El identificador del documento es obligatorio.");
        return store.RetryPostingAsync(user, documentId, cancellationToken);
    }

    public Task<AccountingPostingView?> GetPostingAsync(
        AccountingUserIdentity user, Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (documentId == Guid.Empty)
            throw new AccountingValidationException("El identificador del documento es obligatorio.");
        return store.GetPostingAsync(user, documentId, cancellationToken);
    }

    public Task<AccountingEntryView?> GetEntryAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (documentId == Guid.Empty) throw new AccountingValidationException("El identificador del documento es obligatorio.");
        return store.GetEntryAsync(user, documentId, cancellationToken);
    }

    public Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (from == default || to < from) throw new AccountingValidationException("El rango de fechas del informe no es válido.");
        return store.GetTrialBalanceAsync(user, from, to, cancellationToken);
    }

    public Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default, Guid? costCenterId = null)
    {
        Demand(user, AccountingPermissionCodes.Read);
        ValidateText(accountCode, 30, "Código de cuenta");
        if (from == default || to < from) throw new AccountingValidationException("El rango de fechas del informe no es válido.");
        return store.GetAccountMovementsAsync(user, accountCode.Trim(), from, to, cancellationToken, costCenterId);
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
        if (asOf == default) throw new AccountingValidationException("La fecha del informe no es válida.");
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

    public Task<AccountingDocumentPage> ListDocumentsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType,
        string? status, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidateReport(user, from, to);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new AccountingValidationException("La paginación de documentos contables no es válida.");
        return store.ListDocumentsAsync(user, from, to, Normalize(documentType),
            Normalize(status), Normalize(search), page, pageSize, cancellationToken);
    }

    private static void ValidateReport(AccountingUserIdentity user, DateOnly from, DateOnly to)
    {
        Demand(user, AccountingPermissionCodes.Read);
        if (from == default || to < from)
            throw new AccountingValidationException("El rango de fechas del informe no es válido.");
    }

    private static void Demand(AccountingUserIdentity user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(permission)) throw new AccountingForbiddenException($"Se requiere el permiso '{permission}'.");
    }

    private static void ValidateText(string value, int maximum, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new AccountingValidationException($"{label} es obligatorio y admite máximo {maximum} caracteres.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AccountingForbiddenException(string message) : Exception(message);
public sealed class AccountingValidationException(string message) : Exception(message);
public sealed class AccountingConflictException(string message) : Exception(message);
