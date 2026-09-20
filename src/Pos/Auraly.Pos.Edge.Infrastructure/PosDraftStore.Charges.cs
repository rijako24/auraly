using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosDraftStore
{
    public async Task<PosDraft> SaveChargeAsync(PosDraftScope scope, DraftId draftId,
        InvoiceChargeDraftRequest request, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await RequireActiveAsync(connection, transaction, draftId, ct);
        var stored = (await ReadStoredDraftsAsync(connection, transaction, [draftId], ct)).Single();
        var draft = stored.Draft;
        if (draft.Scope != scope) throw new UnauthorizedAccessException("La factura no pertenece a esta sesión y sede.");
        if (draft.Lines.Count == 0) throw new InvoiceChargeValidationException("Agrega productos antes de agregar un cargo.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Payload FROM PosInvoiceCharges WHERE ChargeId=@ChargeId AND BusinessId=@BusinessId;";
        command.Parameters.AddRange([P("@ChargeId", request.ChargeId), P("@BusinessId", scope.BusinessId.Value)]);
        var payload = await command.ExecuteScalarAsync(ct) as string
            ?? throw new InvoiceChargeValidationException("El cargo no está habilitado en esta sede.");
        var definition = JsonSerializer.Deserialize<InvoiceChargeDefinition>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("La configuración local del cargo no es válida.");
        if (definition.Version != request.ChargeVersion)
            throw new InvoiceChargeConflictException("La configuración cambió. Vuelve a seleccionar el cargo.");
        var selection = new InvoiceChargeSelection(request.AppliedChargeId, definition, request.SupplierId, request.ManualAmount);
        var next = stored.Selections.Where(x => x.AppliedChargeId != request.AppliedChargeId).Append(selection).ToArray();
        var charges = InvoiceChargeApplication.Calculate(draft.Lines.Sum(x => x.Total), next);
        var now = Now();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO PosDraftCharges(DraftId,AppliedChargeId,ChargeId,SelectionJson)
            VALUES(@DraftId,@AppliedId,@ChargeId,@Json)
            ON CONFLICT(DraftId,AppliedChargeId) DO UPDATE SET
              ChargeId=excluded.ChargeId,SelectionJson=excluded.SelectionJson;
            UPDATE PosDrafts SET UpdatedAt=@Now WHERE DraftId=@DraftId;
            """, [P("@DraftId", draftId.Value), P("@AppliedId", request.AppliedChargeId),
                P("@ChargeId", request.ChargeId), P("@Json", JsonSerializer.Serialize(selection)), P("@Now", now)], ct);
        var result = draft with { Charges = charges, UpdatedAt = now };
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<PosDraft> RemoveChargeAsync(PosDraftScope scope, DraftId draftId,
        Guid appliedChargeId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await RequireActiveAsync(connection, transaction, draftId, ct);
        var stored = (await ReadStoredDraftsAsync(connection, transaction, [draftId], ct)).Single();
        var draft = stored.Draft;
        if (draft.Scope != scope) throw new UnauthorizedAccessException("La factura no pertenece a esta sesión y sede.");
        var now = Now();
        await ExecuteAsync(connection, transaction, """
            DELETE FROM PosDraftCharges WHERE DraftId=@DraftId AND AppliedChargeId=@AppliedId;
            UPDATE PosDrafts SET UpdatedAt=@Now WHERE DraftId=@DraftId;
            """, [P("@DraftId", draftId.Value), P("@AppliedId", appliedChargeId), P("@Now", now)], ct);
        await transaction.CommitAsync(ct);
        return draft with { Charges = draft.Charges?.Where(x => x.AppliedChargeId != appliedChargeId).ToArray(), UpdatedAt = now };
    }

    private static InvoiceChargeSelection ReadSelection(string json) =>
        JsonSerializer.Deserialize<InvoiceChargeSelection>(json)
            ?? throw new InvalidDataException("El cargo guardado no contiene una configuración válida.");

    private sealed record StoredDraft(PosDraft Draft, IReadOnlyList<InvoiceChargeSelection> Selections);

    private static async Task<IReadOnlyList<StoredDraft>> ReadStoredDraftsAsync(SqliteConnection connection,
        SqliteTransaction? transaction, IReadOnlyList<DraftId> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        if (ids.Count > 200) throw new ArgumentOutOfRangeException(nameof(ids));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT BusinessId,WarehouseId,DeviceId,WorkSessionId,UserId,CustomerId,SellerId,Status,
              Name,Reference,Observation,CreatedAt,UpdatedAt,SourceOrderId,CustomerPartySiteId,DraftId
            FROM PosDrafts WHERE DraftId IN (SELECT value FROM json_each(@Ids));
            SELECT LineId,ProductId,ProductCode,Description,UnitCode,TaxCode,TaxRate,Quantity,
              BaseUnitPrice,UnitPrice,CurrencyCode,PriceSource,PriceChannelId,Discount,Note,
              AllowsFractionalSale,DocumentUnitCost,AllowsDocumentCostOverride,Position,
              IsPriceOverridden,PromotionDiscount,PublicLineTotal,DraftId
            FROM PosDraftLines WHERE DraftId IN (SELECT value FROM json_each(@Ids)) ORDER BY Position,LineId;
            SELECT DraftId,SelectionJson FROM PosDraftCharges
            WHERE DraftId IN (SELECT value FROM json_each(@Ids)) ORDER BY AppliedChargeId;
            """;
        command.Parameters.Add(P("@Ids", JsonSerializer.Serialize(ids.Select(x => x.Value))));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var headers = new Dictionary<DraftId, PosDraft>();
        while (await reader.ReadAsync(ct))
        {
            var id = new DraftId(Guid.Parse(reader.GetString(15)));
            headers.Add(id, ReadHeader(id, reader));
        }
        var lines = headers.ToDictionary(x => x.Key, _ => new List<PosDraftLine>());
        var selections = headers.ToDictionary(x => x.Key, _ => new List<InvoiceChargeSelection>());
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) lines[new(Guid.Parse(reader.GetString(22)))].Add(ReadLine(reader));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) selections[new(Guid.Parse(reader.GetString(0)))].Add(ReadSelection(reader.GetString(1)));
        return ids.Where(headers.ContainsKey).Select(id => new StoredDraft(headers[id] with
        {
            Lines = lines[id],
            Charges = InvoiceChargeApplication.Calculate(lines[id].Sum(x => x.Total), selections[id])
        }, selections[id])).ToArray();
    }
}
