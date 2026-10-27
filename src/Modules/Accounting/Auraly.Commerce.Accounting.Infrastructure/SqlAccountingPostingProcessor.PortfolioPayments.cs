using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private async Task<IReadOnlyList<Guid>> ApplyManualVoucherFinancialEffectsAsync(SqlConnection connection,
        SqlTransaction transaction, SourceEnvelope source, CancellationToken token)
    {
        var request = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(source.PayloadJson)
            ?? throw new InvalidOperationException("The manual voucher payload is invalid.");
        if (request.PayloadVersion != 4) return [];
        if (request.VoucherId != source.DocumentId || request.BusinessId != source.BusinessId || request.OccurredAt != source.OccurredAt ||
            request.CurrencyCode != "COP" || request.FinancialEffects is not { } effects || effects.AcceptedByUserId == Guid.Empty ||
            effects.Payments is null || effects.Positions is null || effects.CreditApplications is null || effects.DirectMovements is null)
            throw new InvalidOperationException("The financial voucher does not match its supported accepted effects.");
        AccountingJournal.ValidateIdentifiedSourceLines(request.Lines.Select(line => line.LineId));
        var raw = request.Lines.ToDictionary(line => line.LineId!.Value);
        var claimed = new HashSet<Guid>();
        var unapplications = effects.Unapplications ?? [];
        var cancellation = request.VoucherTypeCode == "SupportDocumentCancellation";
        if (request.VoucherTypeCode == "SupportDocument" && (request.FiscalCapture is null || request.MainPartyId is null ||
            effects.Positions.Count != 1 || effects.Positions[0].SubledgerKind != AccountingSubledgerKinds.Payable ||
            effects.Positions[0].PartyId != request.MainPartyId || effects.Payments.Count != 0 ||
            effects.CreditApplications.Count != 0 || effects.DirectMovements.Count != 0 || unapplications.Count != 0))
            throw new InvalidOperationException("The accepted support document must recognize its supplier obligation without money or settlement effects.");
        if (cancellation && (request.FiscalCapture is not { Acquisitions.Count: 0, CalculationHash: not null } ||
            effects.Positions.Count != 1 || effects.Payments.Count != 0 || effects.CreditApplications.Count != 0 ||
            effects.DirectMovements.Count != 0 || unapplications.Count != 0 || request.WorkSessionId is not null))
            throw new InvalidOperationException("The support cancellation must reverse its original obligation without money or other effects.");
        ValidateAcceptedCreditApplications(request, effects, raw, claimed);
        ValidateAcceptedUnapplications(request, effects, raw, claimed);
        foreach (var movement in effects.DirectMovements)
        {
            if (movement.MovementId == Guid.Empty || movement.GroupId == Guid.Empty ||
                !claimed.Add(movement.SourceLineId) || !raw.TryGetValue(movement.SourceLineId, out var line) ||
                line.AccountId != movement.AccountId || line.ExistingPosition is not null || movement.Amount == 0 ||
                movement.Amount != line.Debit - line.Credit || movement.Reference != line.Reference ||
                line.FinancialGroupId is { } group && group != movement.GroupId)
                throw new InvalidOperationException("An accepted treasury movement does not match its immutable source line.");
        }
        foreach (var opening in effects.Positions)
        {
            if (cancellation)
            {
                if (opening.EffectKind != "Cancellation" || opening.SubledgerKind != AccountingSubledgerKinds.Payable ||
                    opening.EffectId == Guid.Empty || opening.PositionId == Guid.Empty || opening.CounterpartyRoleId is null ||
                    opening.PartySiteId is null || opening.PartyId != request.MainPartyId || opening.Amount <= 0 ||
                    opening.GroupId != opening.SourceLineId || !claimed.Add(opening.SourceLineId) ||
                    !raw.TryGetValue(opening.SourceLineId, out var reversed) || reversed.AccountId != opening.AccountId ||
                    reversed.PartyId != opening.PartyId || reversed.PartySiteId != opening.PartySiteId ||
                    reversed.Debit != opening.Amount || reversed.Credit != 0 ||
                    reversed.ExistingPosition is not { SubledgerKind: "Payable", ReversesTransactionId: null } target ||
                    target.SubledgerId != opening.PositionId || reversed.PaymentEvidence is not null ||
                    reversed.FinancialGroupId is not null || reversed.DueDate is not null || opening.DueDate is not null)
                    throw new InvalidOperationException("The accepted support cancellation differs from its original payable binding.");
                continue;
            }
            var payable = opening.SubledgerKind == AccountingSubledgerKinds.Payable;
            var advance = opening.SubledgerKind is "SupplierCredit" or "CustomerCredit";
            var creditSide = payable || opening.SubledgerKind == "CustomerCredit";
            if (opening.EffectKind != "Open" || opening.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable or "SupplierCredit" or "CustomerCredit") ||
                !advance && request.VoucherTypeCode is not ("Accounting" or "SupportDocument") || opening.EffectId == Guid.Empty || opening.PositionId == Guid.Empty ||
                opening.SubledgerKind is (AccountingSubledgerKinds.Payable or "SupplierCredit") && opening.CounterpartyRoleId is null ||
                opening.CounterpartyRoleId is not null && opening.PartySiteId is null ||
                opening.CounterpartyRoleId is null && opening.PartySiteId is not null || opening.PartyId == Guid.Empty ||
                !claimed.Add(opening.SourceLineId) || !raw.TryGetValue(opening.SourceLineId, out var line) ||
                line.ExistingPosition is not null || line.AccountId != opening.AccountId || line.PartyId != opening.PartyId ||
                line.PartySiteId is { } site && site != opening.PartySiteId || line.PaymentEvidence is not null ||
                (!advance || line.FinancialGroupId is not null) && opening.GroupId != (line.FinancialGroupId ?? opening.SourceLineId) || opening.Amount <= 0 ||
                opening.Amount != (creditSide ? line.Credit : line.Debit) || (creditSide ? line.Debit : line.Credit) != 0 ||
                (!advance && (opening.DueDate is not { } due || due != line.DueDate || due == default ||
                    string.IsNullOrWhiteSpace(opening.DocumentReference) || opening.DocumentReference.Length > (payable ? 80 : 64) ||
                    opening.DocumentReference != (line.Reference ?? request.Reference))) ||
                advance && (opening.DueDate is not null || line.DueDate is not null || opening.DocumentReference is not null ||
                    !HasAcceptedAdvanceFunding(opening, effects)))
                throw new InvalidOperationException("An accepted obligation opening does not match its immutable source line.");
        }
        foreach (var group in effects.Payments)
        {
            if (group.PaymentId == Guid.Empty || group.GroupId == Guid.Empty ||
                group.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                group.Allocations is null || group.Tenders is null ||
                (group.AdvancePositionId is null ? group.AdvanceAmount != 0 : group.AdvanceAmount <= 0 ||
                    !effects.Positions.Any(item => item.PositionId == group.AdvancePositionId && item.GroupId == group.GroupId &&
                        item.SubledgerKind == (group.SubledgerKind == AccountingSubledgerKinds.Payable ? "SupplierCredit" : "CustomerCredit"))) ||
                group.Allocations.Count == 0 && group.AdvancePositionId is null)
                throw new InvalidOperationException("An accepted payment group is invalid.");
            var payable = group.SubledgerKind == AccountingSubledgerKinds.Payable;
            foreach (var item in group.Allocations)
            {
                if (!claimed.Add(item.SourceLineId) || !raw.TryGetValue(item.SourceLineId, out var line) ||
                    line.AccountId != item.AccountId || line.PartyId != group.PartyId ||
                    line.ExistingPosition is not { } target || target.SubledgerKind != group.SubledgerKind || target.SubledgerId != item.SubledgerId ||
                    item.Amount <= 0 || item.Amount + item.FinancialDiscountAdjustment != (payable ? line.Debit : line.Credit) ||
                    item.FinancialDiscountEarned != line.FinancialDiscountEarned ||
                    (!payable && (item.FinancialDiscountEarned is not null || item.FinancialDiscountAdjustment != 0m)) ||
                    (payable ? line.Credit : line.Debit) != 0 ||
                    line.FinancialGroupId is { } capturedGroup && capturedGroup != group.GroupId)
                    throw new InvalidOperationException("An accepted allocation does not match its immutable source line.");
            }
            foreach (var item in group.Tenders)
            {
                if (!claimed.Add(item.SourceLineId) || !raw.TryGetValue(item.SourceLineId, out var line) ||
                    line.AccountId != item.AccountId || line.ExistingPosition is not null ||
                    item.Amount <= 0 || item.Amount != (payable ? line.Credit : line.Debit) || (payable ? line.Debit : line.Credit) != 0 ||
                    item.Reference != line.Reference || item.PartyId != line.PartyId ||
                    line.FinancialGroupId is { } capturedGroup && capturedGroup != group.GroupId)
                    throw new InvalidOperationException("An accepted tender does not match its immutable source line.");
            }
        }
        if (request.Lines.Any(line => line.ExistingPosition is not null && !claimed.Contains(line.LineId!.Value)) ||
            effects.DirectMovements.Select(item => item.MovementId).Distinct().Count() != effects.DirectMovements.Count ||
            effects.DirectMovements.Any(item => effects.Payments.Any(group => group.GroupId == item.GroupId)) ||
            effects.Payments.Select(group => group.GroupId).Distinct().Count() != effects.Payments.Count ||
            effects.Payments.Select(group => group.PaymentId).Distinct().Count() != effects.Payments.Count ||
            effects.Positions.Select(item => item.PositionId).Distinct().Count() != effects.Positions.Count ||
            effects.Positions.Select(item => item.EffectId).Concat(effects.CreditApplications.Select(item => item.EffectId))
                .Concat(unapplications.Select(item => item.EffectId)).Distinct().Count()
                != effects.Positions.Count + effects.CreditApplications.Count + unapplications.Count)
            throw new InvalidOperationException("The financial voucher has unclaimed or repeated effects.");
        if (effects.CreditApplications.Count > 0 || unapplications.Count > 0 || cancellation)
            await LockManualFinancialPositionsAsync(connection, transaction, source, token);
        if (cancellation)
            await ApplyManualSupportCancellationAsync(connection, transaction, source, effects.Positions.Single(), token);
        var payableOpenings = effects.Positions.Where(item => item.EffectKind == "Open" && item.SubledgerKind == AccountingSubledgerKinds.Payable).ToArray();
        if (payableOpenings.Length > 0)
            await OpenPayablesAsync(connection, transaction, source.BusinessId, payableOpenings.Select(opening =>
                new PayableOpening(opening.PositionId, opening.EffectId, opening.CounterpartyRoleId!.Value,
                    opening.PartySiteId, source.DocumentId, source.DocumentType, opening.SourceLineId,
                    opening.DocumentReference!, request.CurrencyCode, opening.Amount, opening.Amount, 1m,
                    null, opening.DueDate!.Value, source.OccurredAt)).ToArray(), token);
        var receivableOpenings = effects.Positions.Where(item => item.SubledgerKind == AccountingSubledgerKinds.Receivable).ToArray();
        if (receivableOpenings.Length > 0)
            await OpenManualReceivablesAsync(connection, transaction, source, receivableOpenings, token);
        var creditOpenings = effects.Positions.Where(item => item.SubledgerKind is "SupplierCredit" or "CustomerCredit").ToArray();
        if (creditOpenings.Length > 0)
            await OpenManualCreditsAsync(connection, transaction, source, creditOpenings, token);
        await ApplyDirectionAsync(AccountingSubledgerKinds.Payable);
        await ApplyDirectionAsync(AccountingSubledgerKinds.Receivable);
        var discountedPayments = effects.Payments.Where(group => group.SubledgerKind == AccountingSubledgerKinds.Payable &&
            group.Allocations.Any(item => item.FinancialDiscountEarned is not null)).Select(group => group.PaymentId).ToArray();
        await PrepareSupplierDiscountPricesAsync(connection, transaction, source.BusinessId,
            effects.AcceptedByUserId, discountedPayments, token);
        var creditCustomers = await ApplyCreditApplicationsAsync(connection, transaction, source, effects.CreditApplications, raw, token);
        var unapplicationCustomers = await ApplyUnapplicationsAsync(connection, transaction, source, unapplications, token);
        if (request.WorkSessionId is { } sessionId && effects.DirectMovements.Count > 0)
            await InsertSessionMovementsAsync(connection, transaction, effects.DirectMovements.Select(item =>
                new SessionMovementWrite(item.MovementId, sessionId, source.DocumentId,
                    item.Amount > 0 ? "CashIn" : "CashOut", item.MethodCode, item.Amount,
                    item.Reference ?? request.Reference, source.OccurredAt, effects.AcceptedByUserId,
                    $"manual-treasury:{source.DocumentId:N}:{item.SourceLineId:N}")).ToArray(), token);
        return effects.Payments.Where(group => group.SubledgerKind == AccountingSubledgerKinds.Receivable && group.CounterpartyRoleId.HasValue)
            .Select(group => group.CounterpartyRoleId!.Value)
            .Concat(receivableOpenings.Where(item => item.CounterpartyRoleId.HasValue).Select(item => item.CounterpartyRoleId!.Value))
            .Concat(creditOpenings.Where(item => item.SubledgerKind == "CustomerCredit" && item.CounterpartyRoleId.HasValue)
                .Select(item => item.CounterpartyRoleId!.Value))
            .Concat(creditCustomers)
            .Concat(unapplicationCustomers)
            .Distinct().ToArray();

        async Task ApplyDirectionAsync(string kind)
        {
            var groups = effects.Payments.Where(group => group.SubledgerKind == kind).ToArray();
            if (groups.Length == 0) return;
            await ApplyPortfolioPaymentsAsync(connection, transaction, source.BusinessId, kind,
                groups.Select(group => new PortfolioPaymentApplication(group.PaymentId, group.CounterpartyRoleId,
                    kind == AccountingSubledgerKinds.Receivable && group.CounterpartyRoleId is null ? group.PartyId : null,
                    request.WorkSessionId, request.OccurredAt, effects.AcceptedByUserId, request.CurrencyCode,
                    group.Allocations.Select((item, index) => new PortfolioAllocation(index + 1, item.SubledgerId, item.Amount,
                        item.FinancialDiscountEarned is null ? null : item.FinancialDiscountAdjustment)).ToArray(),
                    group.Tenders.Select((item, index) => new PortfolioTender(index + 1, item.MethodCode, item.Amount, item.Reference)).ToArray(),
                    source.DocumentId, source.DocumentType, group.GroupId, group.AdvanceAmount)).ToArray(), token);
        }
    }

    private async Task ApplyManualSupportCancellationAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, VoucherFinancialPositionEffect cancellation, CancellationToken token)
    {
        await using var command = new SqlCommand(DeclarePortfolioBalanceChangesSql + """
            INSERT @PortfolioChanges
            SELECT payable.PayableId,-@Amount FROM dbo.Payables payable
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=payable.SupplierId AND supplier.TenantId=@TenantId
              AND supplier.SupplierId=@SupplierId AND supplier.PartyId=@PartyId
            JOIN dbo.AccountingEntryLines opening ON opening.EntryId=payable.OpeningEntryId
              AND opening.LineNumber=payable.OpeningEntryLineNumber AND opening.AccountId=@AccountId
              AND opening.SourceLineId=@SourceLineId AND opening.PartyId=@PartyId
            WHERE payable.PayableId=@PayableId AND payable.BusinessId=@BusinessId
              AND payable.SourceDocumentType=N'ManualAccountingVoucher' AND payable.CurrencyCode=N'COP'
              AND payable.PartySiteId=@PartySiteId AND payable.Status=N'Open'
              AND payable.OriginalAmount=@Amount AND payable.OutstandingAmount=@Amount;
            IF @@ROWCOUNT<>1 THROW 51850,'The original support obligation changed before cancellation.',1;
            """ + PortfolioBalanceChangesSql("Payables", "PayableId", PortfolioBalanceChangeKind.Cancellation) + """
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
            VALUES(@EffectId,@PayableId,N'Credit',@Amount,@DocumentId,@DocumentType,@SourceLineId,@OccurredAt,@Now);
            IF @@ROWCOUNT<>1 THROW 51850,'The support cancellation history is incomplete.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", source.TenantId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@OccurredAt", source.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@EffectId", cancellation.EffectId);
        command.Parameters.AddWithValue("@PayableId", cancellation.PositionId);
        command.Parameters.AddWithValue("@SupplierId", cancellation.CounterpartyRoleId!.Value);
        command.Parameters.AddWithValue("@PartyId", cancellation.PartyId);
        command.Parameters.AddWithValue("@PartySiteId", cancellation.PartySiteId!.Value);
        command.Parameters.AddWithValue("@AccountId", cancellation.AccountId);
        command.Parameters.AddWithValue("@SourceLineId", cancellation.SourceLineId);
        AddMoney(command, "@Amount", cancellation.Amount);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task OpenManualCreditsAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, IReadOnlyList<VoucherFinancialPositionEffect> openings, CancellationToken token)
    {
        await using var command = new SqlCommand(DeclareSupplierCreditOpeningsSql + DeclareCustomerCreditOpeningsSql + """
            DECLARE @CreditInputs TABLE(PositionId uniqueidentifier,EffectId uniqueidentifier,SubledgerKind nvarchar(16),
              CounterpartyRoleId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier,
              SourceLineId uniqueidentifier,Amount decimal(19,4));
            INSERT @CreditInputs SELECT * FROM OPENJSON(@Rows) WITH(PositionId uniqueidentifier,EffectId uniqueidentifier,
              SubledgerKind nvarchar(16),CounterpartyRoleId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier,
              SourceLineId uniqueidentifier,Amount decimal(19,4));
            IF (SELECT COUNT(*) FROM @CreditInputs)<>@Count OR EXISTS(SELECT 1 FROM @CreditInputs
              WHERE SubledgerKind NOT IN(N'SupplierCredit',N'CustomerCredit'))
              THROW 51846,'The manual credit opening batch is incomplete.',1;
            INSERT @SupplierCreditOpenings(CreditId,TransactionId,SupplierId,PartySiteId,DocumentId,DocumentType,
              SourceLineId,Nature,Currency,Amount)
            SELECT PositionId,EffectId,CounterpartyRoleId,PartySiteId,@CreditParentDocumentId,@CreditParentDocumentType,
              SourceLineId,N'Advance',N'COP',Amount FROM @CreditInputs WHERE SubledgerKind=N'SupplierCredit';
            INSERT @CustomerCreditOpenings(CreditId,TransactionId,CustomerId,PartyId,PartySiteId,DocumentId,DocumentType,
              SourceLineId,Nature,Currency,Amount)
            SELECT PositionId,EffectId,CounterpartyRoleId,CASE WHEN CounterpartyRoleId IS NULL THEN PartyId END,
              PartySiteId,@CreditParentDocumentId,@CreditParentDocumentType,SourceLineId,N'Advance',N'COP',Amount
            FROM @CreditInputs WHERE SubledgerKind=N'CustomerCredit';
            """ + OpenSupplierCreditsSql + OpenCustomerCreditsSql, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(openings);
        command.Parameters.AddWithValue("@Count", openings.Count);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@CreditParentDocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@CreditParentDocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@CreditAt", source.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task OpenManualReceivablesAsync(SqlConnection connection, SqlTransaction transaction,
        SourceEnvelope source, IReadOnlyList<VoucherFinancialPositionEffect> openings, CancellationToken token)
    {
        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + """
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartyId,PartySiteId,
              DocumentId,DocumentType,SourceLineId,Number,Amount,DueDate,OccurredAt)
            SELECT PositionId,EffectId,CounterpartyRoleId,CASE WHEN CounterpartyRoleId IS NULL THEN PartyId END,
              PartySiteId,@DocumentId,@DocumentType,SourceLineId,DocumentReference,Amount,DueDate,@At
            FROM OPENJSON(@Rows) WITH(PositionId uniqueidentifier,EffectId uniqueidentifier,CounterpartyRoleId uniqueidentifier,
              PartyId uniqueidentifier,PartySiteId uniqueidentifier,SourceLineId uniqueidentifier,DocumentReference nvarchar(64),
              Amount decimal(19,4),DueDate datetimeoffset(7));
            IF @@ROWCOUNT<>@Count THROW 51845,'The manual receivable opening batch is incomplete.',1;
            """ + OpenReceivablesSql, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(openings);
        command.Parameters.AddWithValue("@Count", openings.Count);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@At", source.OccurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<FinancialFactsResult> LoadManualVoucherFactsAsync(SqlConnection connection,
        SqlTransaction transaction, SourceEnvelope source, CancellationToken token)
    {
        var facts = LoadManualVoucherFacts(source);
        using var document = JsonDocument.Parse(source.PayloadJson);
        if (!document.RootElement.TryGetProperty("PayloadVersion", out var version) || version.ValueKind == JsonValueKind.Null ||
            version.GetInt32() != 4) return FinancialFactsResult.Ready(facts);
        var acceptedEffects = document.RootElement.GetProperty("FinancialEffects");
        if (!acceptedEffects.GetProperty("Payments").EnumerateArray().Any(group => group.GetProperty("Allocations").GetArrayLength() > 0)
            && acceptedEffects.GetProperty("CreditApplications").GetArrayLength() == 0
            && !acceptedEffects.GetProperty("Positions").EnumerateArray().Any(position =>
                position.GetProperty("EffectKind").GetString() == "Cancellation")
            && (!acceptedEffects.TryGetProperty("Unapplications", out var reversalsCapture) ||
                reversalsCapture.ValueKind == JsonValueKind.Null || reversalsCapture.GetArrayLength() == 0))
            return FinancialFactsResult.Ready(facts);
        await using var command = new SqlCommand("""
            DECLARE @Positions TABLE(Kind nvarchar(16),PositionId uniqueidentifier,AccountId uniqueidentifier,PartyId uniqueidentifier,
              PRIMARY KEY(Kind,PositionId,AccountId,PartyId));
            INSERT @Positions SELECT payment.SubledgerKind,input.SubledgerId,input.AccountId,payment.PartyId
              FROM OPENJSON(@Payload,'$.FinancialEffects.Payments') WITH(SubledgerKind nvarchar(16),PartyId uniqueidentifier,Allocations nvarchar(max) AS JSON) payment
              CROSS APPLY OPENJSON(payment.Allocations) WITH(SubledgerId uniqueidentifier,AccountId uniqueidentifier) input
              UNION SELECT target.SubledgerKind,target.SubledgerId,line.AccountId,line.PartyId
              FROM accounting.ManualVoucherCreditTargets(@Payload) target
              JOIN OPENJSON(@Payload,'$.Lines') WITH(LineId uniqueidentifier,AccountId uniqueidentifier,PartyId uniqueidentifier) line
                ON line.LineId=target.SourceLineId;
            SELECT TOP(1) Issue FROM(
              SELECT COALESCE(opening.Issue,CASE WHEN opening.AccountId<>input.AccountId OR party.PartyId<>input.PartyId
                  THEN N'La aplicación ya no corresponde al auxiliar original de la obligación.' END) Issue
              FROM @Positions input LEFT JOIN dbo.Payables p ON p.PayableId=input.PositionId AND p.BusinessId=@BusinessId
              LEFT JOIN dbo.Suppliers party ON party.SupplierId=p.SupplierId AND party.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'Payable',p.OpeningEntryId,p.OpeningEntryLineNumber,party.PartyId) opening
              WHERE input.Kind=N'Payable'
              UNION ALL
              SELECT COALESCE(opening.Issue,CASE WHEN opening.AccountId<>input.AccountId OR COALESCE(p.PartyId,party.PartyId)<>input.PartyId
                  THEN N'La aplicación ya no corresponde al auxiliar original de la obligación.' END)
              FROM @Positions input LEFT JOIN dbo.Receivables p ON p.ReceivableId=input.PositionId AND p.BusinessId=@BusinessId
              LEFT JOIN dbo.Customers party ON party.CustomerId=p.CustomerId AND party.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'Receivable',p.OpeningEntryId,p.OpeningEntryLineNumber,COALESCE(p.PartyId,party.PartyId)) opening
              WHERE input.Kind=N'Receivable'
              UNION ALL
              SELECT COALESCE(opening.Issue,CASE WHEN opening.AccountId<>input.AccountId OR party.PartyId<>input.PartyId
                  OR credit.CurrencyCode IS NULL OR credit.CurrencyCode<>N'COP' OR credit.CreditNature IS NULL
                  THEN N'El saldo a favor ya no corresponde a su cuenta, tercero, moneda o naturaleza conciliada.' END)
              FROM @Positions input LEFT JOIN dbo.SupplierCredits credit ON credit.SupplierCreditId=input.PositionId AND credit.BusinessId=@BusinessId
              LEFT JOIN dbo.Suppliers party ON party.SupplierId=credit.SupplierId AND party.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'SupplierCredit',credit.OpeningEntryId,credit.OpeningEntryLineNumber,party.PartyId) opening
              WHERE input.Kind=N'SupplierCredit'
              UNION ALL
              SELECT COALESCE(opening.Issue,CASE WHEN opening.AccountId<>input.AccountId OR COALESCE(credit.PartyId,party.PartyId)<>input.PartyId
                  OR credit.CurrencyCode IS NULL OR credit.CurrencyCode<>N'COP' OR credit.CreditNature IS NULL
                  THEN N'El saldo a favor ya no corresponde a su cuenta, tercero, moneda o naturaleza conciliada.' END)
              FROM @Positions input LEFT JOIN dbo.CustomerCredits credit ON credit.CustomerCreditId=input.PositionId AND credit.BusinessId=@BusinessId
              LEFT JOIN dbo.Customers party ON party.CustomerId=credit.CustomerId AND party.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'CustomerCredit',credit.OpeningEntryId,credit.OpeningEntryLineNumber,COALESCE(credit.PartyId,party.PartyId)) opening
              WHERE input.Kind=N'CustomerCredit'
            ) issues WHERE Issue IS NOT NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", source.TenantId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = source.PayloadJson;
        return await command.ExecuteScalarAsync(token) is string issue
            ? FinancialFactsResult.Pending("OriginalPositionAccountMissing", $"No se aplicó el pago/recaudo/cruce. {issue}")
            : FinancialFactsResult.Ready(facts);
    }

    private sealed record PortfolioPaymentApplication(
        Guid PaymentId, Guid? RoleId, Guid? PartyId, Guid? WorkSessionId,
        DateTimeOffset PaidAt, Guid ConfirmedByUserId, string CurrencyCode,
        IReadOnlyList<PortfolioAllocation> Allocations, IReadOnlyList<PortfolioTender> Tenders,
        Guid? ParentSourceDocumentId = null, string? ParentSourceDocumentType = null, Guid? SourceGroupId = null,
        decimal AdvanceAmount = 0m);

    private sealed record PortfolioAllocation(int LineNumber, Guid PositionId, decimal Amount,
        decimal? ExpectedFinancialDiscountAdjustment = null);
    private sealed record PortfolioTender(int LineNumber, string MethodCode, decimal Amount, string? Reference);

    // Both native payments and manual-source groups use this participant in the
    // processor's existing transaction. A position shared by several groups is
    // locked and updated once; applications, history and session move together.
    private async Task ApplyPortfolioPaymentsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, string subledgerKind,
        IReadOnlyList<PortfolioPaymentApplication> payments, CancellationToken token)
    {
        if (subledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
            payments.Count is < 1 or > 250 || payments.Select(item => item.PaymentId).Distinct().Count() != payments.Count)
            throw new InvalidOperationException("The accepted portfolio payment batch is invalid.");
        var receivable = subledgerKind == AccountingSubledgerKinds.Receivable;

        // Only these closed identifiers enter SQL interpolation. No caller or
        // document can supply table names, predicates or an operation selector.
        var positionTable = receivable ? "Receivables" : "Payables";
        var positionId = receivable ? "ReceivableId" : "PayableId";
        var paymentTable = receivable ? "CustomerPayments" : "SupplierPayments";
        var applicationTable = receivable ? "CustomerPaymentApplications" : "SupplierPaymentApplications";
        var tenderTable = receivable ? "CustomerPaymentTenders" : "SupplierPaymentTenders";
        var transactionTable = receivable ? "ReceivableTransactions" : "PayableTransactions";
        var transactionId = receivable ? "ReceivableTransactionId" : "PayableTransactionId";
        var roleMatch = receivable
            ? "(balance.CustomerId=payment.RoleId OR balance.CustomerId IS NULL AND payment.RoleId IS NULL AND balance.PartyId=payment.PartyId)"
            : "balance.SupplierId=payment.RoleId";
        var headerPartyMatch = receivable
            ? "(header.CustomerId=input.RoleId AND header.PartyId IS NULL OR header.CustomerId IS NULL AND input.RoleId IS NULL AND header.PartyId=input.PartyId)"
            : "header.SupplierId=input.RoleId AND input.PartyId IS NULL";
        var discountColumn = receivable ? "CONVERT(decimal(19,4),NULL)" : "application.FinancialDiscountAdjustment";
        var balanceReduction = receivable ? PortfolioBalanceReductionSql(positionTable, positionId)
            : DeclarePortfolioBalanceChangesSql + """
              INSERT @PortfolioChanges SELECT PositionId,-SUM(Amount+COALESCE(FinancialDiscountAdjustment,0))
              FROM @Ready GROUP BY PositionId;
              """ + PortfolioBalanceChangesSql(positionTable, positionId, PortfolioBalanceChangeKind.Settlement);
        var discountTransactions = receivable ? string.Empty : """
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
            SELECT NEWID(),input.PositionId,N'Adjustment',-input.FinancialDiscountAdjustment,
              COALESCE(payment.ParentSourceDocumentId,input.PaymentId),
              COALESCE(payment.ParentSourceDocumentType,N'PayablePayment'),
                CASE WHEN payment.ParentSourceDocumentType=N'ManualAccountingVoucher'
                  THEN payment.SourceGroupId END,payment.PaidAt,@Now
            FROM @Ready input JOIN @Payments payment ON payment.PaymentId=input.PaymentId
            WHERE input.FinancialDiscountAdjustment IS NOT NULL AND input.FinancialDiscountAdjustment<>0;
            """;

        await using var command = new SqlCommand($"""
            DECLARE @Payments TABLE(PaymentId uniqueidentifier PRIMARY KEY,RoleId uniqueidentifier,PartyId uniqueidentifier,
              WorkSessionId uniqueidentifier,PaidAt datetimeoffset(7),ConfirmedByUserId uniqueidentifier,CurrencyCode char(3),
              ParentSourceDocumentId uniqueidentifier,ParentSourceDocumentType nvarchar(64),SourceGroupId uniqueidentifier,
              AdvanceAmount decimal(19,4),
              Allocations nvarchar(max),Tenders nvarchar(max));
            INSERT @Payments SELECT * FROM OPENJSON(@Rows) WITH(
              PaymentId uniqueidentifier,RoleId uniqueidentifier,PartyId uniqueidentifier,WorkSessionId uniqueidentifier,
              PaidAt datetimeoffset(7),ConfirmedByUserId uniqueidentifier,CurrencyCode char(3),
              ParentSourceDocumentId uniqueidentifier,ParentSourceDocumentType nvarchar(64),SourceGroupId uniqueidentifier,
              AdvanceAmount decimal(19,4),
              Allocations nvarchar(max) AS JSON,Tenders nvarchar(max) AS JSON);
            IF (SELECT COUNT(*) FROM @Payments)<>@PaymentCount
              THROW 51607,'The accepted payment batch is incomplete.',1;
            DECLARE @Positions TABLE(PositionId uniqueidentifier PRIMARY KEY);
            INSERT @Positions SELECT DISTINCT input.PositionId FROM @Payments payment
              CROSS APPLY OPENJSON(payment.Allocations) WITH(PositionId uniqueidentifier) input;
            DECLARE @LockedPosition uniqueidentifier;
            SELECT @LockedPosition=balance.[{positionId}] FROM @Positions input
              JOIN dbo.[{positionTable}] balance WITH(UPDLOCK,HOLDLOCK)
                ON balance.[{positionId}]=input.PositionId AND balance.BusinessId=@BusinessId
              ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            IF EXISTS(SELECT 1 FROM @Payments input LEFT JOIN dbo.[{paymentTable}] header WITH(UPDLOCK,HOLDLOCK)
              ON header.PaymentId=input.PaymentId AND header.BusinessId=@BusinessId AND header.Status=N'Accepted'
                AND header.CurrencyCode=input.CurrencyCode AND header.PaidAt=input.PaidAt
                AND header.ConfirmedByUserId=input.ConfirmedByUserId AND ({headerPartyMatch})
                AND (header.WorkSessionId=input.WorkSessionId OR header.WorkSessionId IS NULL AND input.WorkSessionId IS NULL)
                AND (header.ParentSourceDocumentId=input.ParentSourceDocumentId AND header.ParentSourceDocumentType=input.ParentSourceDocumentType
                  AND header.SourceGroupId=input.SourceGroupId OR header.ParentSourceDocumentId IS NULL AND input.ParentSourceDocumentId IS NULL
                  AND input.ParentSourceDocumentType IS NULL AND input.SourceGroupId IS NULL)
              WHERE header.PaymentId IS NULL)
              THROW 51607,'The accepted payment identity or parent source does not match.',1;

            DECLARE @Allocations TABLE(PaymentId uniqueidentifier,LineNumber int,PositionId uniqueidentifier,
              Amount decimal(19,4),TransactionId uniqueidentifier,ExpectedFinancialDiscountAdjustment decimal(19,4),
              PRIMARY KEY(PaymentId,LineNumber),UNIQUE(PaymentId,PositionId));
            INSERT @Allocations SELECT payment.PaymentId,input.LineNumber,input.PositionId,input.Amount,input.TransactionId,
              input.ExpectedFinancialDiscountAdjustment
            FROM @Payments payment CROSS APPLY OPENJSON(payment.Allocations) WITH(
              LineNumber int,PositionId uniqueidentifier,Amount decimal(19,4),TransactionId uniqueidentifier,
              ExpectedFinancialDiscountAdjustment decimal(19,4)) input;
            IF EXISTS(SELECT 1 FROM @Payments payment WHERE
              (SELECT COUNT(*) FROM @Allocations input WHERE input.PaymentId=payment.PaymentId) NOT BETWEEN
                CASE WHEN payment.AdvanceAmount>0 AND payment.ParentSourceDocumentId IS NOT NULL THEN 0 ELSE 1 END AND 100
                OR payment.AdvanceAmount IS NULL OR payment.AdvanceAmount<0
                OR payment.AdvanceAmount>0 AND payment.ParentSourceDocumentId IS NULL)
              OR EXISTS(SELECT 1 FROM @Allocations WHERE LineNumber<=0 OR Amount<=0 OR TransactionId IS NULL)
              THROW 51607,'The accepted payment allocations are invalid.',1;
            DECLARE @Ready TABLE(PaymentId uniqueidentifier,LineNumber int,PositionId uniqueidentifier,
              Amount decimal(19,4),TransactionId uniqueidentifier,FinancialDiscountAdjustment decimal(19,4),
              PRIMARY KEY(PaymentId,LineNumber));
            INSERT @Ready SELECT input.PaymentId,input.LineNumber,input.PositionId,input.Amount,input.TransactionId,
              {discountColumn}
            FROM @Allocations input JOIN @Payments payment ON payment.PaymentId=input.PaymentId
            JOIN dbo.[{positionTable}] balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.[{positionId}]=input.PositionId AND balance.BusinessId=@BusinessId
                AND {roleMatch} AND balance.CurrencyCode=payment.CurrencyCode AND balance.Status IN(N'Open',N'PartiallyPaid')
            JOIN dbo.[{applicationTable}] application WITH(UPDLOCK,HOLDLOCK)
              ON application.[{positionId}]=input.PositionId AND application.PaymentId=input.PaymentId
                AND application.LineNumber=input.LineNumber AND application.Amount=input.Amount AND application.AppliedAt IS NULL;
            IF (SELECT COUNT(*) FROM @Ready)<>(SELECT COUNT(*) FROM @Allocations)
              THROW 51607,'The payment allocations are no longer valid.',1;
            IF EXISTS(SELECT 1 FROM @Allocations input JOIN @Ready ready
              ON ready.PaymentId=input.PaymentId AND ready.LineNumber=input.LineNumber
              WHERE input.ExpectedFinancialDiscountAdjustment IS NOT NULL
                AND input.ExpectedFinancialDiscountAdjustment<>COALESCE(ready.FinancialDiscountAdjustment,0))
              THROW 51607,'The accepted discount decision changed after payment acceptance.',1;
            IF EXISTS(SELECT 1 FROM @Payments payment JOIN dbo.[{paymentTable}] header ON header.PaymentId=payment.PaymentId
              WHERE header.TotalAmount<>payment.AdvanceAmount+COALESCE((SELECT SUM(input.Amount) FROM @Ready input WHERE input.PaymentId=payment.PaymentId),0)
                OR (SELECT COUNT(*) FROM dbo.[{applicationTable}] application WHERE application.PaymentId=payment.PaymentId)
                  <>(SELECT COUNT(*) FROM @Ready input WHERE input.PaymentId=payment.PaymentId))
              THROW 51607,'The payment does not match all its accepted applications.',1;

            """ + balanceReduction + discountTransactions + $"""
            UPDATE application SET AppliedAt=@Now FROM dbo.[{applicationTable}] application
              JOIN @Ready ready ON ready.PaymentId=application.PaymentId AND ready.LineNumber=application.LineNumber
                AND ready.PositionId=application.[{positionId}] WHERE application.AppliedAt IS NULL;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Ready)
              THROW 51607,'The payment applications were not updated atomically.',1;
            INSERT dbo.[{transactionTable}]([{transactionId}],[{positionId}],TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT input.TransactionId,input.PositionId,N'Payment',input.Amount,input.PaymentId,payment.PaidAt,@Now
            FROM @Ready input JOIN @Payments payment ON payment.PaymentId=input.PaymentId;

            DECLARE @Tenders TABLE(PaymentId uniqueidentifier,LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
              Reference nvarchar(160),MovementId uniqueidentifier,SourceKey nvarchar(160),PRIMARY KEY(PaymentId,LineNumber));
            INSERT @Tenders SELECT payment.PaymentId,input.LineNumber,input.MethodCode,input.Amount,input.Reference,input.MovementId,input.SourceKey
            FROM @Payments payment CROSS APPLY OPENJSON(payment.Tenders) WITH(LineNumber int,MethodCode nvarchar(32),
              Amount decimal(19,4),Reference nvarchar(160),MovementId uniqueidentifier,SourceKey nvarchar(160)) input;
            IF EXISTS(SELECT 1 FROM @Tenders input LEFT JOIN dbo.[{tenderTable}] tender
              ON tender.PaymentId=input.PaymentId AND tender.LineNumber=input.LineNumber
                AND tender.MethodCode=input.MethodCode AND tender.Amount=input.Amount
                AND (tender.Reference=input.Reference OR tender.Reference IS NULL AND input.Reference IS NULL)
              WHERE tender.PaymentId IS NULL)
              OR EXISTS(SELECT 1 FROM @Payments payment JOIN dbo.[{paymentTable}] header ON header.PaymentId=payment.PaymentId
                WHERE header.TotalAmount<>(SELECT SUM(input.Amount) FROM @Tenders input WHERE input.PaymentId=payment.PaymentId)
                  OR (SELECT COUNT(*) FROM @Tenders input WHERE input.PaymentId=payment.PaymentId) NOT BETWEEN 1 AND 10
                  OR (SELECT COUNT(*) FROM dbo.[{tenderTable}] tender WHERE tender.PaymentId=payment.PaymentId)
                    <>(SELECT COUNT(*) FROM @Tenders input WHERE input.PaymentId=payment.PaymentId))
              THROW 51607,'The payment does not match its accepted tender breakdown.',1;

            UPDATE header SET Status=N'Processed',ProcessedAt=@Now FROM dbo.[{paymentTable}] header
              JOIN @Payments payment ON payment.PaymentId=header.PaymentId
              WHERE header.BusinessId=@BusinessId AND header.Status=N'Accepted';
            IF @@ROWCOUNT<>@PaymentCount THROW 51607,'The payments could not be completed atomically.',1;
            INSERT dbo.WorkSessionMovements(WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
              MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            SELECT input.MovementId,payment.WorkSessionId,input.PaymentId,NULL,CONVERT(date,payment.PaidAt),@MovementType,
              input.MethodCode,@Sign*input.Amount,input.Reference,input.SourceKey,payment.PaidAt,payment.ConfirmedByUserId
            FROM @Tenders input JOIN @Payments payment ON payment.PaymentId=input.PaymentId WHERE payment.WorkSessionId IS NOT NULL;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Tenders input JOIN @Payments payment ON payment.PaymentId=input.PaymentId
                WHERE payment.WorkSessionId IS NOT NULL)
              THROW 51608,'The work-session payment breakdown was not recorded atomically.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@PaymentCount", payments.Count);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@MovementType", receivable ? "ReceivablePayment" : "PayablePayment");
        command.Parameters.AddWithValue("@Sign", receivable ? 1 : -1);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(payments.Select(payment => new
        {
            payment.PaymentId, payment.RoleId, payment.PartyId, payment.WorkSessionId, payment.PaidAt,
            payment.ConfirmedByUserId, payment.CurrencyCode, payment.ParentSourceDocumentId,
            payment.ParentSourceDocumentType, payment.SourceGroupId,
            payment.AdvanceAmount,
            Allocations = payment.Allocations.Select(item => new { item.LineNumber, item.PositionId, item.Amount,
                item.ExpectedFinancialDiscountAdjustment, TransactionId = ids.NewId() }),
            Tenders = payment.Tenders.Select(item => new
            {
                item.LineNumber, item.MethodCode, item.Amount, item.Reference, MovementId = ids.NewId(),
                SourceKey = $"{(receivable ? "receivable-payment" : "payable-payment")}:{payment.PaymentId:N}:{item.LineNumber}"
            })
        }));
        await command.ExecuteNonQueryAsync(token);
    }
}
