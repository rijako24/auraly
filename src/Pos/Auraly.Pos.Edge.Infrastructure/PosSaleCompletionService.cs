using System.Data;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosReceiptLine(
    string ProductCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    decimal Total);

public sealed record PosReceipt(
    Guid PrintJobId,
    DocumentId DocumentId,
    string FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    IReadOnlyCollection<PosReceiptLine> Lines,
    IReadOnlyCollection<OfflineSalePayment> Payments,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string Cufe,
    string QrPayload,
    int PaperWidthMillimeters);

public interface IPosReceiptPrinter
{
    Task PrintAsync(PosReceipt receipt, CancellationToken cancellationToken = default);
}

public sealed record CompletePosSaleCommand(
    UserId UserId,
    RegisterContext Register,
    DateTimeOffset IssuedAt,
    string SupplierTaxId,
    string CustomerIdentification,
    FiscalTechnicalKey TechnicalKey,
    FiscalEnvironment Environment,
    string QrValidationUrl,
    IReadOnlyCollection<OfflineSalePayment> Payments,
    Guid DeviceId,
    int PaperWidthMillimeters = 80);

public sealed record CompletePosSaleResult(
    PosEdgeIssueResult IssuedSale,
    PosDraft NextDraft,
    PosFiscalNumberPreview NextFiscalNumber);

public sealed class PosDraftIssuanceStore(
    string connectionString,
    IAuralyIdGenerator idGenerator,
    TimeProvider timeProvider)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS PosDraftDocuments(
              DraftId TEXT PRIMARY KEY,
              DocumentId TEXT NOT NULL UNIQUE,
              PrintJobId TEXT NOT NULL UNIQUE,
              CreatedAt TEXT NOT NULL,
              CompletedAt TEXT NULL,
              FOREIGN KEY(DraftId) REFERENCES PosDrafts(DraftId));
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<(DocumentId DocumentId, Guid PrintJobId)> GetOrCreateAsync(
        DraftId draftId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText =
            "SELECT DocumentId,PrintJobId FROM PosDraftDocuments WHERE DraftId=@DraftId;";
        read.Parameters.AddWithValue("@DraftId", draftId.Value.ToString("D"));
        await using var reader = await read.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var value = (
                new DocumentId(Guid.Parse(reader.GetString(0))),
                Guid.Parse(reader.GetString(1)));
            await reader.DisposeAsync();
            await transaction.CommitAsync(ct);
            return value;
        }
        await reader.DisposeAsync();
        var documentId = new DocumentId(idGenerator.NewId());
        var printJobId = idGenerator.NewId();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO PosDraftDocuments(DraftId,DocumentId,PrintJobId,CreatedAt)
            VALUES(@DraftId,@DocumentId,@PrintJobId,@CreatedAt);
            """;
        insert.Parameters.AddWithValue("@DraftId", draftId.Value.ToString("D"));
        insert.Parameters.AddWithValue("@DocumentId", documentId.Value.ToString("D"));
        insert.Parameters.AddWithValue("@PrintJobId", printJobId.ToString("D"));
        insert.Parameters.AddWithValue("@CreatedAt", timeProvider.GetUtcNow().ToString("O"));
        await insert.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return (documentId, printJobId);
    }

    public async Task CompleteAsync(
        DraftId draftId,
        DocumentId documentId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE PosDrafts
            SET Status='Consumed',ConsumedAt=@Now,UpdatedAt=@Now
            WHERE DraftId=@DraftId AND Status='Active';
            UPDATE PosDraftDocuments SET CompletedAt=@Now
            WHERE DraftId=@DraftId AND DocumentId=@DocumentId;
            INSERT INTO PosDraftAudit(AuditId,DraftId,Action,RelatedDraftId,OccurredAt)
            SELECT @AuditId,@DraftId,'IssuedAndPrinted',@DocumentId,@Now
            WHERE changes()>0;
            """;
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("@DraftId", draftId.Value.ToString("D"));
        command.Parameters.AddWithValue("@DocumentId", documentId.Value.ToString("D"));
        command.Parameters.AddWithValue("@AuditId", idGenerator.NewId().ToString("D"));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

public sealed class PosSaleCompletionService(
    PosDraftStore drafts,
    PosDraftIssuanceStore issuance,
    PosEdgeSaleStore sales,
    IPosReceiptPrinter printer)
{
    public async Task<CompletePosSaleResult> CompleteAsync(
        DraftId draftId,
        CompletePosSaleCommand command,
        CancellationToken ct = default)
    {
        if (command.PaperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(nameof(command), "Receipt width must be 58 or 80 mm.");
        var draft = await drafts.GetAsync(draftId, ct)
            ?? throw new KeyNotFoundException("The active sale does not exist.");
        if (draft.Status != PosDraftStatus.Active || draft.Lines.Count == 0)
            throw new InvalidOperationException("Only a non-empty active sale can be completed.");
        if (command.Payments.Count == 0 ||
            command.Payments.Sum(payment => payment.Amount) != draft.PayableAmount)
            throw new InvalidOperationException("Payments must equal the payable amount.");

        var identity = await issuance.GetOrCreateAsync(draftId, ct);
        var lines = draft.Lines.Select(line => new OfflineSaleLine(
            new PosCatalogProduct(
                line.ProductId,
                line.ProductCode,
                line.Description,
                [],
                true,
                false,
                line.TaxCode,
                line.TaxRate),
            line.Quantity,
            line.UnitPrice,
            line.Discount,
            line.Tax)).ToArray();
        var issued = await sales.IssueAsync(
            new PosEdgeIssueCommand(
                command.UserId,
                identity.DocumentId,
                command.Register,
                command.IssuedAt,
                command.SupplierTaxId,
                command.CustomerIdentification,
                command.TechnicalKey,
                command.Environment,
                command.QrValidationUrl,
                lines,
                command.DeviceId,
                command.Payments),
            ct);
        var payload = new PosReceipt(
            identity.PrintJobId,
            issued.DocumentId,
            issued.FiscalNumber,
            command.IssuedAt,
            command.CustomerIdentification,
            draft.Lines.Select(line => new PosReceiptLine(
                line.ProductCode,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.Discount,
                line.Tax,
                line.Total)).ToArray(),
            command.Payments,
            draft.UntaxedAmount,
            draft.TaxAmount,
            draft.PayableAmount,
            issued.Cufe,
            issued.QrPayload,
            command.PaperWidthMillimeters);

        await printer.PrintAsync(payload, ct);
        await issuance.CompleteAsync(draftId, issued.DocumentId, ct);
        var nextDraft = await drafts.GetOrCreateActiveAsync(draft.Scope, ct);
        var nextFiscalNumber = await sales.PreviewNextFiscalNumberAsync(
            command.Register.RegisterId,
            command.IssuedAt,
            ct);
        return new CompletePosSaleResult(issued, nextDraft, nextFiscalNumber);
    }
}
