using Auraly.Commerce.Accounting.Contracts;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Commerce.Accounting.Domain;
using Auraly.Application.Fiscal;

namespace Auraly.Commerce.Accounting.Application;

public interface IAccountingStore
{
    Task<VoucherDraftPreparation> GetSupportCancellationCaptureAsync(AccountingUserIdentity user, Guid payableId,
        CancellationToken cancellationToken);
    Task<UnapplicationCandidate?> GetUnapplicationAsync(AccountingUserIdentity user, string kind,
        Guid positionId, Guid transactionId, CancellationToken cancellationToken);
    Task<AccountingOpenPositionPage> OpenPositionsAsync(AccountingUserIdentity user, AccountingOpenPositionQuery query, CancellationToken cancellationToken);
    Task<AccountingAccountOptionPage> AccountOptionsAsync(AccountingUserIdentity user, AccountingAccountOptionQuery query, CancellationToken cancellationToken);
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
    Task<VoucherDraftView?> GetVoucherDraftAsync(AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<VoucherDraftView> SaveVoucherDraftAsync(AccountingUserIdentity user, SaveVoucherDraftRequest request, CancellationToken cancellationToken);
    Task<VoucherFiscalPreview> PreviewFiscalVoucherAsync(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, CancellationToken cancellationToken);
    Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request, CancellationToken cancellationToken, string? draftVersion = null);
    Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken cancellationToken, string? draftVersion = null);
    Task<AccountingAccountView> CreateAccountAsync(AccountingUserIdentity user, CreateAccountingAccountRequest request, CancellationToken cancellationToken);
    Task<AccountingAccountView> UpdateAccountAsync(AccountingUserIdentity user, Guid accountId, UpdateAccountingAccountRequest request, CancellationToken cancellationToken);
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
    Task<AccountMovementPage> GetAccountMovementsPageAsync(AccountingUserIdentity user, AccountMovementQuery query, CancellationToken cancellationToken, int? maximumRows = null);
    Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(AccountingUserIdentity user, DateOnly asOf, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<AccountingDocumentPage> ListDocumentsAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken, Guid? partyId = null);
    Task<FinancialTraceabilityLinePage> ListFinancialTraceabilityLinesAsync(AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType, string? status, string? search, int page, int pageSize, CancellationToken cancellationToken, Guid? partyId = null);
}

public interface IBankReconciliationStore
{
    Task<BankReconciliationPage> ListAsync(AccountingUserIdentity user, BankReconciliationQuery query, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView?> GetAsync(AccountingUserIdentity user, Guid reconciliationId, BankReconciliationDetailQuery query, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ImportAsync(AccountingUserIdentity user, ImportBankReconciliationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> AllocateAsync(AccountingUserIdentity user, Guid reconciliationId, CreateBankReconciliationAllocationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ReverseAllocationAsync(AccountingUserIdentity user, Guid reconciliationId, Guid matchId, ReverseBankReconciliationAllocationRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> CloseAsync(AccountingUserIdentity user, Guid reconciliationId, ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken);
    Task<BankReconciliationDetailView> ReopenAsync(AccountingUserIdentity user, Guid reconciliationId, ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken);
}

public sealed partial class AccountingService(
    IAccountingStore store,
    IBankReconciliationStore bankReconciliations,
    AccountingProcessingCoordinator processing,
    IAuralyIdGenerator ids,
    FiscalProcessingCoordinator fiscal)
{
    public Task<AccountingOpenPositionPage> OpenPositionsAsync(AccountingUserIdentity user,
        AccountingOpenPositionQuery query, CancellationToken cancellationToken = default)
    {
        var sourceReadPermission = query.SubledgerKind switch
        {
            AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.SupplierCredit => PayablesPermissionCodes.Read,
            AccountingSubledgerKinds.Receivable or AccountingSubledgerKinds.CustomerCredit => ReceivablesPermissionCodes.Read,
            _ => null
        };
        if (!user.Permissions.Contains(AccountingPermissionCodes.Read)
            && (sourceReadPermission is null || !user.Permissions.Contains(sourceReadPermission)))
            throw new AccountingForbiddenException("No tienes permiso para consultar estas obligaciones.");
        if (query.SubledgerKind is not ("All" or AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable or
            AccountingSubledgerKinds.SupplierCredit or AccountingSubledgerKinds.CustomerCredit)
            || query.Page < 1 || query.PageSize is < 1 or > 50 || query.PartyId == Guid.Empty
            || query.AccountId == Guid.Empty || query.SubledgerId == Guid.Empty || query.CounterpartyRoleId == Guid.Empty || query.Search?.Length > 120
            || query.DueFrom > query.DueTo || query.ManualSupportOnly && query.SubledgerKind != AccountingSubledgerKinds.Payable
            || query.SubledgerKind == "All" && query.PartyId is null)
            throw new AccountingValidationException("Los filtros de documentos pendientes no son válidos.");
        return store.OpenPositionsAsync(user, query with { Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim() }, cancellationToken);
    }

    public Task<BankReconciliationPage> ListBankReconciliationsAsync(
        AccountingUserIdentity user, BankReconciliationQuery query, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationRead);
        ValidateBankPage(query.Page, query.PageSize);
        if (query.BankAccountId == Guid.Empty || query.Search?.Length > 160 ||
            query.SortBy is not ("periodTo" or "updatedAt" or "bankDisplayName") ||
            query.SortDirection is not ("asc" or "desc"))
            throw new AccountingValidationException("Los filtros de conciliación no son válidos.");
        return bankReconciliations.ListAsync(user, query with { Search = query.Search?.Trim() }, cancellationToken);
    }

    public Task<BankReconciliationDetailView?> GetBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId, BankReconciliationDetailQuery query, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationRead);
        foreach (var page in new[] { query.StatementPage, query.BookPage, query.AllocationPage, query.EventPage })
            ValidateBankPage(page, query.PageSize);
        if (query.StatementSearch?.Length > 160 || query.BookSearch?.Length > 160)
            throw new AccountingValidationException("La búsqueda de movimientos supera 160 caracteres.");
        return bankReconciliations.GetAsync(user, reconciliationId, query with
        { StatementSearch = query.StatementSearch?.Trim(), BookSearch = query.BookSearch?.Trim() }, cancellationToken);
    }

    private static void ValidateBankPage(int page, int pageSize)
    {
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > 100)
            throw new AccountingValidationException("La página debe ser positiva y admitir entre 1 y 100 filas.");
    }

    public Task<BankReconciliationDetailView> ImportBankReconciliationAsync(
        AccountingUserIdentity user, ImportBankReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationManage);
        if (request.ReconciliationId == Guid.Empty || request.BankAccountId == Guid.Empty ||
            request.PeriodFrom == default || request.PeriodTo < request.PeriodFrom ||
            request.Lines is null || request.Lines.Count is 0 or > 100_000)
            throw new AccountingValidationException("El extracto y su periodo son obligatorios.");
        ValidateText(request.FileName, 260, "Nombre del archivo");
        if (request.Lines.Select(line => line.LineNumber).Distinct().Count() != request.Lines.Count ||
            request.Lines.Any(line => line.LineNumber <= 0 || line.Amount == 0 ||
                line.TransactionDate < request.PeriodFrom || line.TransactionDate > request.PeriodTo ||
                string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500 || line.Reference?.Length > 160 ||
                !IsBankMoney(line.Amount) || (line.Balance.HasValue && !IsBankMoney(line.Balance.Value))) ||
            !IsBankMoney(request.OpeningBalance) || !IsBankMoney(request.ClosingBalance))
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

    private static bool IsBankMoney(decimal amount) =>
        amount is > -1_000_000_000_000_000m and < 1_000_000_000_000_000m && decimal.Round(amount, 4) == amount;

    public Task<BankReconciliationDetailView> AllocateBankReconciliationAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        CreateBankReconciliationAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.BankReconciliationManage);
        if (reconciliationId == Guid.Empty || request.MatchId == Guid.Empty ||
            request.StatementLineId == Guid.Empty || request.EntryId == Guid.Empty ||
            request.EntryLineNumber <= 0 || request.Amount <= 0 || !IsBankMoney(request.Amount) || string.IsNullOrWhiteSpace(request.RowVersion))
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
        Demand(user, AccountingPermissionCodes.ManualSend);
        ValidateAccountAdjustment(user, request);
        var result = await store.ConfirmAccountAdjustmentAsync(user, request, cancellationToken);
        if (!result.IsDuplicate)
            await processing.RequestPostingAsync(user.BusinessId, result.DocumentId,
                result.DocumentType, cancellationToken);
        return result;
    }

    private static void ValidateAccountAdjustment(AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request)
    {
        if (request.AdjustmentId == Guid.Empty || request.BusinessId != user.BusinessId ||
            request.SubledgerId == Guid.Empty || request.CounterpartAccountId == Guid.Empty)
            throw new AccountingValidationException("El ajuste de cartera no pertenece al alcance autorizado.");
        if (request.SubledgerKind is not (AccountingSubledgerKinds.Receivable or AccountingSubledgerKinds.Payable) ||
            request.Direction is not (AccountingAdjustmentDirections.Increase or AccountingAdjustmentDirections.Decrease))
            throw new AccountingValidationException("El tipo de ajuste de cartera no es válido.");
        if (request.Amount <= 0)
            throw new AccountingValidationException("El valor del ajuste debe ser positivo.");
        ValidateDraftReference(request.Reference);
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Descripción");
    }

    public async Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        Demand(user, AccountingPermissionCodes.ManualSend);
        if (request.PayloadVersion == 4)
            throw new AccountingValidationException("Guarda y envía la versión financiera desde la captura del comprobante; no admite aceptación directa sin su borrador.");
        ValidateManualVoucher(user, request);
        var result = await store.ConfirmManualVoucherAsync(user, request, cancellationToken);
        if (!result.IsDuplicate)
            await processing.RequestPostingAsync(user.BusinessId, result.DocumentId,
                result.DocumentType, cancellationToken);
        return result;
    }

    private static void ValidateManualVoucher(AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        bool requireFiscalReview = true)
    {
        if (request.PayloadVersion != 4 && (request.VoucherTypeCode is not null || request.WorkSessionId is not null ||
            request.CurrencyCode is not null || request.FinancialEffects is not null || request.FiscalCapture is not null ||
            request.Lines?.Any(line => line is not null && (line.ExistingPosition is not null || line.FinancialGroupId is not null ||
                line.DueDate is not null || line.PartySiteId is not null || line.PaymentEvidence is not null || line.Reference is not null)) == true))
            throw new AccountingValidationException("Los vínculos y efectos financieros requieren la versión financiera del comprobante.");
        if (request.VoucherId == Guid.Empty || request.BusinessId != user.BusinessId || request.MainPartyId == Guid.Empty ||
            request.Lines is null || request.Lines.Count is < 2 or > 500)
            throw new AccountingValidationException("El comprobante manual requiere al menos dos partidas.");
        ValidateText(request.ConceptCode, 40, "Concept code");
        ValidateText(request.Description, 500, "Descripción");
        ValidateDraftReference(request.Reference);
        foreach (var line in request.Lines)
        {
            if (line is null)
                throw new AccountingValidationException("El comprobante contiene una partida vacía.");
            ValidateText(line.Description, 500, "Descripción de la partida");
        }
        try
        {
            if (request.PayloadVersion == 4)
                AccountingJournal.ValidateIdentifiedSourceLines(request.Lines.Select(line => line.LineId));
            else ValidateManualLineIdentities(request.PayloadVersion, request.Lines.Select(line => line.LineId));
            // Acceptance and posting use the same per-line precision, side and
            // balance rules. Keep the original request for legacy replay hashes.
            AccountingJournal.Validate(request.Lines.Select(line => new JournalLine(
                line.AccountId, line.Debit, line.Credit, line.PartyId, line.CostCenterId, line.Description,
                line.LineId, BaseAmount: line.BaseAmount)));
        }
        catch (AccountingRuleException error)
        {
            throw new AccountingValidationException(
                "Las partidas deben tener un solo lado positivo, cuadrar a cuatro decimales por partida y respetar el importe máximo contable.", error);
        }
        ValidateFiscalCapture(user, request.PayloadVersion, request.VoucherTypeCode,
            request.Lines.Select(line => line.LineId), request.FiscalCapture, requireReview: requireFiscalReview);
    }

    private static void ValidateManualLineIdentities(int? payloadVersion, IEnumerable<Guid?> source)
    {
        try { AccountingJournal.ValidateSourceLineIdentities(payloadVersion, source); }
        catch (AccountingRuleException error) { throw new AccountingValidationException("La versión del comprobante y las identidades únicas de sus partidas no son válidas.", error); }
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
        if (result.Status == AccountingActivationStatuses.Configuring &&
            request.OpeningBalanceMode == "ImportedAndApproved")
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
            if (line.LineId == Guid.Empty || line.ExistingPosition is { } position &&
                (position.SubledgerId == Guid.Empty || position.ReversesTransactionId is not null || position.SubledgerKind is not ("Payable" or "Receivable")))
                throw new AccountingValidationException("La referencia de la obligación o la identidad de la partida no es válida.");
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

    public Task<AccountingAccountOptionPage> AccountOptionsAsync(
        AccountingUserIdentity user, AccountingAccountOptionQuery query, CancellationToken cancellationToken = default)
    {
        if (query.IncludeStructural || query.IncludeInactive)
            Demand(user, AccountingPermissionCodes.Read);
        var canRead = user.Permissions.Contains(AccountingPermissionCodes.Read) ||
            user.Permissions.Contains(AccountingPermissionCodes.ManualCreate);
        if (!canRead && !(query.ExpenseOnly && (user.Permissions.Contains(ExpensePermissionCodes.Create) || user.Permissions.Contains(ExpensePermissionCodes.Configure))) &&
            !(query.LiabilityOnly && user.Permissions.Contains(Auraly.Commerce.Taxation.Contracts.TaxationPermissionCodes.ManageWithholdingRules)))
            throw new AccountingForbiddenException("No tienes permiso para consultar estas cuentas.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.Search?.Length > 160)
            throw new AccountingValidationException("La página o la búsqueda de cuentas no es válida.");
        return store.AccountOptionsAsync(user, query with { Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim() }, cancellationToken);
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
            throw new AccountingValidationException("La cuenta bancaria y el tipo de cuenta son obligatorios; si indicas un auxiliar, su identificador debe ser válido.");
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
        ValidateAccountSemantics(request.Semantics);
        var code = request.Code.Trim();
        if (code.Any(character => !char.IsAsciiDigit(character)) ||
            code.Length is 3 or 5 || code.Length < 1)
            throw new AccountingValidationException(
                "El código PUC debe tener 1 dígito para clase, 2 para grupo, 4 para cuenta, 6 para subcuenta o más de 6 para auxiliar.");
        if (request.AllowsPosting && code.Length < 6)
            throw new AccountingValidationException("Las clases, grupos y cuentas son niveles de agrupación y no permiten movimientos.");
        if (request.AccountType is not ("Asset" or "Liability" or "Equity" or "Revenue" or "Expense" or "ContraRevenue" or "OrderDebit" or "OrderCredit"))
            throw new AccountingValidationException("El tipo de cuenta no es válido.");
        var compatibleType = code[0] switch
        {
            '1' => request.AccountType == "Asset",
            '2' => request.AccountType == "Liability",
            '3' => request.AccountType == "Equity",
            '4' => request.AccountType is "Revenue" or "ContraRevenue",
            '5' or '6' or '7' => request.AccountType == "Expense",
            '8' => request.AccountType == "OrderDebit",
            '9' => request.AccountType == "OrderCredit",
            _ => false
        };
        if (!compatibleType)
            throw new AccountingValidationException(
                "La naturaleza no corresponde a la clase PUC indicada por el primer dígito.");
        return store.CreateAccountAsync(user, request with { Code = code, Name = request.Name.Trim() }, cancellationToken);
    }

    public Task<AccountingAccountView> UpdateAccountAsync(AccountingUserIdentity user, Guid accountId,
        UpdateAccountingAccountRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Configure);
        if (accountId == Guid.Empty)
            throw new AccountingValidationException("La cuenta es obligatoria.");
        ValidateText(request.Name, 200, "Nombre de cuenta");
        ValidateAccountSemantics(request.Semantics);
        return store.UpdateAccountAsync(user, accountId, request with { Name = request.Name.Trim() }, cancellationToken);
    }

    private static void ValidateAccountSemantics(AccountingAccountSemantics? value)
    {
        if (value is null) return;
        ValidateText(value.FinancialFunction, 64, "Función financiera");
        ValidateText(value.BalanceSide, 6, "Naturaleza del saldo");
        if (value.ParentAccountId == Guid.Empty || value.BusinessId == Guid.Empty ||
            value.BankAccountId == Guid.Empty || value.PaymentMethodOptionId == Guid.Empty)
            throw new AccountingValidationException("Las relaciones de la cuenta deben tener identificadores válidos.");
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

    public async Task<IReadOnlyList<AccountMovementRow>> GetAccountMovementsAsync(AccountingUserIdentity user, string accountCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default, Guid? costCenterId = null)
    {
        ValidateReport(user, from, to);
        ValidateText(accountCode, 32, "Código de cuenta");
        // Preserve the pre-existing array contract while the admin consumes the
        // bounded page contract. Both adapters use the same journal query owner.
        var page = await store.GetAccountMovementsPageAsync(user,
            new(from, to, 1, int.MaxValue, CostCenterId: costCenterId, AccountRootCode: accountCode.Trim()), cancellationToken);
        return page.Items;
    }

    public Task<AccountMovementPage> GetAccountMovementsPageAsync(AccountingUserIdentity user,
        AccountMovementQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAccountMovementQuery(user, query);
        if (query.Page < 1 || query.PageSize is < 1 or > 200 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new AccountingValidationException("La paginación o fecha final del auxiliar no es válida.");
        return store.GetAccountMovementsPageAsync(user, normalized, cancellationToken);
    }

    // A bounded full report shares the journal reader with the page. The SQL
    // owner rejects an oversized snapshot before enriching movement details.
    public Task<AccountMovementPage> GetAccountMovementReportAsync(AccountingUserIdentity user,
        AccountMovementQuery query, CancellationToken cancellationToken = default)
    {
        const int maximumRows = 5000;
        var normalized = NormalizeAccountMovementQuery(user, query) with { Page = 1, PageSize = maximumRows };
        return store.GetAccountMovementsPageAsync(user, normalized, cancellationToken, maximumRows);
    }

    private static AccountMovementQuery NormalizeAccountMovementQuery(AccountingUserIdentity user, AccountMovementQuery query)
    {
        ValidateReport(user, query.From, query.To);
        var from = Normalize(query.AccountFrom);
        var to = Normalize(query.AccountTo);
        var root = Normalize(query.AccountRootCode);
        if (from?.Length > 32 || to?.Length > 32 || root?.Length > 32 ||
            from is not null && to is not null && string.CompareOrdinal(from, to) > 0 ||
            query.PartyId == Guid.Empty || query.CostCenterId == Guid.Empty)
            throw new AccountingValidationException("El rango de cuentas o los filtros del auxiliar no son válidos.");
        return query with { AccountFrom = from, AccountTo = to, AccountRootCode = root };
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
        CancellationToken cancellationToken = default, Guid? partyId = null)
    {
        ValidateReport(user, from, to);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new AccountingValidationException("La paginación de documentos contables no es válida.");
        return store.ListDocumentsAsync(user, from, to, Normalize(documentType),
            Normalize(status), Normalize(search), page, pageSize, cancellationToken, partyId);
    }

    public Task<FinancialTraceabilityLinePage> ListFinancialTraceabilityLinesAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType,
        string? status, string? search, int page, int pageSize,
        CancellationToken cancellationToken = default, Guid? partyId = null)
    {
        ValidateReport(user, from, to);
        if (page < 1 || pageSize is < 1 or > 500)
            throw new AccountingValidationException("La paginación de la trazabilidad financiera no es válida.");
        return store.ListFinancialTraceabilityLinesAsync(user, from, to, Normalize(documentType),
            Normalize(status), Normalize(search), page, pageSize, cancellationToken, partyId);
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
public sealed class AccountingValidationException : Exception
{
    public AccountingValidationException(string message) : base(message) { }
    public AccountingValidationException(string message, Exception innerException) : base(message, innerException) { }
}
public sealed class AccountingConflictException(string message) : Exception(message);
