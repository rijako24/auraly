using System.Text.Json;
using System.Data;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptStore
{
    private static async Task<IReadOnlyList<GoodsReceiptVoucherCostSnapshot>?> ResolveReferencedCostsAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        IReadOnlyList<CalculatedGoodsReceiptVoucherCost>? documents, DateTimeOffset recognizedAt,
        CancellationToken cancellationToken)
    {
        if (documents is not { Count: > 0 }) return null;
        // A source lock serializes all assignments of that voucher, including
        // independent receipts. Expense cancellation takes this same lock.
        await using var command = new SqlCommand("""
            DECLARE @Requested TABLE(EntryId uniqueidentifier PRIMARY KEY,Mode nvarchar(16),LinesJson nvarchar(max));
            INSERT @Requested SELECT EntryId,Mode,LinesJson FROM OPENJSON(@Documents) WITH(
              EntryId uniqueidentifier,Mode nvarchar(16),LinesJson nvarchar(max) '$.Lines' AS JSON);
            DECLARE @ScopedEntries TABLE(SourceDocumentId uniqueidentifier,SourceDocumentType nvarchar(64),
              EntryId uniqueidentifier,EntryNumber nvarchar(24),OccurredAt datetimeoffset(7),Mode nvarchar(16),LinesJson nvarchar(max),
              PRIMARY KEY(SourceDocumentId,SourceDocumentType));
            INSERT @ScopedEntries
            SELECT entry.SourceDocumentId,entry.SourceDocumentType,entry.EntryId,entry.EntryNumber,entry.OccurredAt,requested.Mode,requested.LinesJson
            FROM @Requested requested JOIN dbo.AccountingEntries entry ON entry.EntryId=requested.EntryId
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId;
            DECLARE @SelectedLines TABLE(EntryId uniqueidentifier,EntryLineNumber int,AllocationMethod nvarchar(16),
              PRIMARY KEY(EntryId,EntryLineNumber));
            INSERT @SelectedLines
            SELECT requested.EntryId,selected.EntryLineNumber,selected.AllocationMethod
            FROM @ScopedEntries requested CROSS APPLY OPENJSON(requested.LinesJson)
              WITH(EntryLineNumber int,AllocationMethod nvarchar(16)) selected;
            DECLARE @LockedExpense uniqueidentifier;
            SELECT @LockedExpense=expense.ExpenseId FROM @ScopedEntries requested
            JOIN dbo.Expenses expense WITH(UPDLOCK,HOLDLOCK) ON expense.ExpenseId=requested.SourceDocumentId
              AND requested.SourceDocumentType=N'Expense'
              AND expense.BusinessId=@BusinessId
            ORDER BY requested.SourceDocumentId,requested.SourceDocumentType OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT requested.EntryId,requested.SourceDocumentId,requested.SourceDocumentType,requested.EntryNumber,requested.OccurredAt,
              CONVERT(bit,CASE WHEN mode.OptionId IS NOT NULL AND mode.IsActive=1 THEN 1 ELSE 0 END)
            FROM @ScopedEntries requested
            JOIN dbo.AccountingSourceDocuments source WITH(UPDLOCK,HOLDLOCK)
              ON source.SourceDocumentId=requested.SourceDocumentId AND source.SourceDocumentType=requested.SourceDocumentType
              AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
            LEFT JOIN reference.Options mode ON mode.CatalogCode=N'purchase-cost-association-mode' AND mode.Code=requested.Mode
            ORDER BY requested.SourceDocumentId,requested.SourceDocumentType
            OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            SELECT line.EntryId,line.LineNumber,line.AccountId,line.PartyId,line.CostCenterId,
              line.EligibleFunctionalAmount,line.AssignedFunctionalAmount,
              CONVERT(bit,CASE WHEN method.OptionId IS NOT NULL AND method.IsActive=1 THEN 1 ELSE 0 END)
            FROM @ScopedEntries requested
            CROSS APPLY purchasing.ReceiptCostVoucherLines(requested.EntryId) line
            JOIN @SelectedLines selected ON selected.EntryId=requested.EntryId AND selected.EntryLineNumber=line.LineNumber
            LEFT JOIN reference.Options method ON method.CatalogCode=N'purchase-cost-allocation-method'
              AND method.Code=selected.AllocationMethod
            OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.Add("@Documents", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(documents.Select(item => item.Request));
        var headers = new Dictionary<Guid, (Guid DocumentId, string Type, string Number, DateOnly Date, bool ModeActive)>();
        var lines = new Dictionary<(Guid EntryId, int LineNumber),
            (Guid AccountId, Guid? PartyId, Guid? CostCenterId, decimal Eligible, decimal Assigned, bool MethodActive)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                headers.Add(reader.GetGuid(0), (reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    DateOnly.FromDateTime(reader.GetDateTimeOffset(4).Date), reader.GetBoolean(5)));
            if (headers.Count != documents.Count)
                throw new PurchasingValidationException("Un comprobante no está contabilizado o no pertenece a esta sede.");
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                lines.Add((reader.GetGuid(0), reader.GetInt32(1)), (reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetDecimal(5), reader.GetDecimal(6), reader.GetBoolean(7)));
        }
        var snapshots = new List<GoodsReceiptVoucherCostSnapshot>();
        var invalid = new List<string>();
        var conflicts = new List<string>();
        foreach (var document in documents)
        {
            var header = headers[document.Request.EntryId];
            if (!header.ModeActive)
                invalid.Add($"{header.Number}: el modo de asociación elegido ya no está activo.");
            if (document.Request.Mode == GoodsReceiptCostAssociationModes.Distribute &&
                header.Date > DateOnly.FromDateTime(recognizedAt.Date))
                invalid.Add($"{header.Number}: es posterior a la fecha contable de esta compra. Puedes asociarlo sin repartir; su costo requiere una revisión posterior a su reconocimiento.");
            var selectedLines = new List<GoodsReceiptVoucherCostLineSnapshot>();
            var allocations = document.Allocations.ToLookup(allocation => allocation.CostLineNumber);
            foreach (var selected in document.Request.Lines)
            {
                if (!lines.TryGetValue((document.Request.EntryId, selected.EntryLineNumber), out var original) ||
                    original.Eligible <= 0)
                {
                    invalid.Add($"{header.Number}, partida {selected.EntryLineNumber}: no es un costo distribuible. Selecciona una partida de gasto o de costos de compra por distribuir, con el tercero requerido por su cuenta, sin IVA descontable, retenciones, bancos ni obligaciones.");
                    continue;
                }
                if (!original.MethodActive)
                    invalid.Add($"{header.Number}, partida {selected.EntryLineNumber}: el método de reparto elegido ya no está activo.");
                if (selected.FunctionalAmount > original.Eligible - original.Assigned)
                    conflicts.Add($"{header.Number}, partida {selected.EntryLineNumber}: el importe supera el costo disponible. Otro reparto pudo consumirlo; revisa el disponible.");
                selectedLines.Add(new(selected.EntryLineNumber, original.AccountId,
                    original.PartyId, original.CostCenterId, selected.FunctionalAmount, selected.AllocationMethod,
                    allocations[selected.EntryLineNumber].Select(allocation => new GoodsReceiptVoucherCostAllocationSnapshot(
                        allocation.ReceiptLineNumber, allocation.Factor, allocation.FunctionalAmount)).ToArray()));
            }
            snapshots.Add(new(document.Request.AssociationId, document.Request.EntryId,
                header.DocumentId, header.Type, header.Number, document.Request.Mode, selectedLines));
        }
        if (invalid.Count > 0)
            throw new PurchasingValidationException(string.Join("\n", invalid.Concat(conflicts)));
        if (conflicts.Count > 0)
            throw new PurchasingConflictException(string.Join("\n", conflicts));
        return snapshots;
    }
}
