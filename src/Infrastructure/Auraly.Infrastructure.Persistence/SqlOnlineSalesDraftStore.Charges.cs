using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesDraft> SaveChargeAsync(OnlineSalesUserIdentity user, Guid draftId,
        InvoiceChargeDraftRequest request, string idempotencyKey, CancellationToken ct)
    {
        const string operation = "SaveInvoiceCharge";
        var hash = Hash(JsonSerializer.Serialize(new { operation, draftId, request }));
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var state = await LockDraftAsync(connection, tx, user, draftId, ct);
        var replay = await ReplayAsync(connection, tx, state.BusinessId, idempotencyKey, operation, hash, ct);
        if (replay is not null) { await tx.CommitAsync(ct); return replay; }
        DemandActiveVersion(state, request.ExpectedVersion);
        var definition = await SqlInvoiceChargeStore.ReadOneAsync(connection, tx, user.TenantId,
            state.BusinessId, request.ChargeId, ct)
            ?? throw new OnlineSalesDraftValidationException("El cargo no está activo en esta sede.");
        if (definition.Version != request.ChargeVersion)
            throw new OnlineSalesDraftConcurrencyException("La configuración del cargo cambió. Vuelve a seleccionarlo.");
        var draft = await ReadDraftAsync(connection, tx, draftId, ct);
        var selection = new InvoiceChargeSelection(request.AppliedChargeId, definition, request.SupplierId, request.ManualAmount);
        if (draft.Lines.Count == 0) throw new OnlineSalesDraftValidationException("Agrega productos antes de agregar un cargo.");
        var previousCharges = draft.Charges ?? [];
        if (previousCharges.Count >= InvoiceChargeApplication.MaximumChargesPerInvoice &&
            previousCharges.All(x => x.AppliedChargeId != request.AppliedChargeId))
            throw new OnlineSalesDraftValidationException("Se admiten hasta diez cargos por factura.");
        try { _ = InvoiceChargeApplication.Calculate(draft.Lines.Sum(x => x.Total), selection); }
        catch (InvoiceChargeValidationException error) { throw new OnlineSalesDraftValidationException(error.Message); }
        await ExecuteAsync(connection, tx, """
            UPDATE sales.InvoiceChargeDraftSelections SET ChargeId=@ChargeId,Version=@Version,SelectionJson=@Json
              WHERE DraftId=@DraftId AND AppliedChargeId=@AppliedId;
            IF @@ROWCOUNT=0 INSERT sales.InvoiceChargeDraftSelections(DraftId,AppliedChargeId,ChargeId,Version,SelectionJson)
              VALUES(@DraftId,@AppliedId,@ChargeId,@Version,@Json);
            """, [P("@DraftId", draftId), P("@AppliedId", request.AppliedChargeId), P("@ChargeId", request.ChargeId),
                P("@Version", definition.Version), P("@Json", JsonSerializer.Serialize(selection))], ct);
        var version = await AdvanceVersionAsync(connection, tx, draftId, request.ExpectedVersion, ct);
        await SaveReceiptAsync(connection, tx, state.BusinessId, draftId, idempotencyKey, operation, hash, version, ct);
        var result = await ReadDraftAsync(connection, tx, draftId, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<OnlineSalesDraft> RemoveChargeAsync(OnlineSalesUserIdentity user, Guid draftId,
        Guid appliedChargeId, long expectedVersion, string idempotencyKey, CancellationToken ct)
    {
        const string operation = "RemoveInvoiceCharge";
        var hash = Hash($"{operation}|{draftId:D}|{appliedChargeId:D}");
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var state = await LockDraftAsync(connection, tx, user, draftId, ct);
        var replay = await ReplayAsync(connection, tx, state.BusinessId, idempotencyKey, operation, hash, ct);
        if (replay is not null) { await tx.CommitAsync(ct); return replay; }
        DemandActiveVersion(state, expectedVersion);
        if (await ExecuteAsync(connection, tx,
            "DELETE FROM sales.InvoiceChargeDraftSelections WHERE DraftId=@DraftId AND AppliedChargeId=@Id;",
            [P("@DraftId", draftId), P("@Id", appliedChargeId)], ct) != 1)
            throw new OnlineSalesDraftValidationException("El cargo no pertenece a esta factura.");
        var version = await AdvanceVersionAsync(connection, tx, draftId, expectedVersion, ct);
        await SaveReceiptAsync(connection, tx, state.BusinessId, draftId, idempotencyKey, operation, hash, version, ct);
        var result = await ReadDraftAsync(connection, tx, draftId, ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
