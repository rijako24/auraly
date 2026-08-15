using System.Data;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
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
    string DocumentNumber,
    string? FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    IReadOnlyCollection<PosReceiptLine> Lines,
    IReadOnlyCollection<OfflineSalePayment> Payments,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string? Cufe,
    string? QrPayload,
    int PaperWidthMillimeters,
    string DocumentType = PosSaleDocumentTypes.Invoice);

public interface IPosReceiptPrinter
{
    Task PrintAsync(PosReceipt receipt, CancellationToken cancellationToken = default);
}

public sealed record CompletePosSaleCommand(
    UserId UserId,
    SalesExecutionContext Context,
    DateTimeOffset IssuedAt,
    string? SupplierTaxId,
    string CustomerIdentification,
    FiscalTechnicalKey? TechnicalKey,
    FiscalEnvironment? Environment,
    string? QrValidationUrl,
    IReadOnlyCollection<OfflineSalePayment> Payments,
    int PaperWidthMillimeters = 80,
    PosSaleUblSnapshotContract? UblSnapshot = null,
    string DocumentType = PosSaleDocumentTypes.Invoice);

public sealed record CompletePosSaleResult(
    PosEdgeIssueResult IssuedSale,
    PosDraft NextDraft,
    PosDocumentNumberPreview NextDocumentNumber,
    PosFiscalNumberPreview? NextFiscalNumber);

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
            WHERE DraftId=@DraftId AND Status='Active' AND IssuedAt IS NOT NULL;
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

    public async Task MarkIssuedAsync(
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
            SET IssuedAt=COALESCE(IssuedAt,@Now),UpdatedAt=@Now
            WHERE DraftId=@DraftId AND Status='Active'
              AND EXISTS(
                SELECT 1 FROM PosDraftDocuments
                WHERE DraftId=@DraftId AND DocumentId=@DocumentId);
            """;
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("@DraftId", draftId.Value.ToString("D"));
        command.Parameters.AddWithValue("@DocumentId", documentId.Value.ToString("D"));
        var affected = await command.ExecuteNonQueryAsync(ct);
        if (affected != 1)
            throw new InvalidOperationException("The issued sale could not lock its source draft.");
        await transaction.CommitAsync(ct);
    }
}

public sealed class PosSaleCompletionService(
    PosDraftStore drafts,
    PosDraftIssuanceStore issuance,
    PosEdgeSaleStore sales,
    IPosReceiptPrinter printer,
    TimeProvider? timeProvider = null)
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
            ExclusiveFromPublished(line.UnitPrice, line.TaxRate),
            ExclusiveFromPublished(line.Discount, line.TaxRate),
            line.Tax)).ToArray();
        var issued = await sales.IssueAsync(
            new PosEdgeIssueCommand(
                command.UserId,
                identity.DocumentId,
                command.Context,
                command.IssuedAt,
                command.SupplierTaxId,
                command.CustomerIdentification,
                command.TechnicalKey,
                command.Environment,
                command.QrValidationUrl,
                lines,
                command.Payments,
                command.UblSnapshot,
                draft.CustomerId,
                draft.SourceOrderId,
                command.DocumentType),
            ct);
        await issuance.MarkIssuedAsync(draftId, issued.DocumentId, ct);
        var immutable = issued.Upload;
        var payload = new PosReceipt(
            identity.PrintJobId,
            issued.DocumentId,
            issued.DocumentNumber,
            issued.FiscalNumber,
            immutable.CommercialSnapshot.IssuedAt,
            immutable.CommercialSnapshot.CustomerIdentification,
            immutable.Lines.Select(line => new PosReceiptLine(
                draft.Lines.First(source => source.ProductId.Value == line.ProductId).ProductCode,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.TaxAmount,
                line.LineTotal)).ToArray(),
            immutable.Payments.Select(payment => new OfflineSalePayment(
                payment.MethodCode,
                payment.Amount,
                payment.Reference)).ToArray(),
            immutable.CommercialSnapshot.UntaxedAmount,
            immutable.CommercialSnapshot.TaxAmount,
            immutable.CommercialSnapshot.PayableAmount,
            issued.Cufe,
            issued.QrPayload,
            command.PaperWidthMillimeters,
            immutable.CommercialSnapshot.DocumentType);

        await printer.PrintAsync(payload, ct);
        await issuance.CompleteAsync(draftId, issued.DocumentId, ct);
        var nextDraft = await drafts.GetOrCreateActiveAsync(draft.Scope, ct);
        var nextDocumentNumber = await sales.PreviewNextDocumentNumberAsync(
            command.Context.DeviceId ?? throw new InvalidOperationException("An Edge sale requires DeviceId."),
            command.DocumentType,
            ct);
        var nextFiscalNumber = PosSaleDocumentTypes.IsFiscal(command.DocumentType)
            ? await sales.PreviewNextFiscalNumberAsync(
                command.Context.DeviceId ?? throw new InvalidOperationException("An Edge sale requires DeviceId."),
                command.IssuedAt,
                ct)
            : null;
        return new CompletePosSaleResult(
            issued,
            nextDraft,
            nextDocumentNumber,
            nextFiscalNumber);
    }
    private static decimal ExclusiveFromPublished(decimal amount, decimal taxRate) =>
        taxRate == 0m
            ? amount
            : decimal.Round(amount / (1m + taxRate / 100m), 6,
                MidpointRounding.AwayFromZero);


    public async Task ReprintAsync(
        DocumentId documentId,
        UserId userId,
        int paperWidthMillimeters,
        CancellationToken ct = default)
    {
        if (paperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(
                nameof(paperWidthMillimeters), "Receipt width must be 58 or 80 mm.");
        var immutable = await sales.GetIssuedUploadAsync(documentId, ct)
            ?? throw new KeyNotFoundException("The issued sale does not exist locally.");
        var metadata = immutable.UblSnapshot?.Lines.ToDictionary(line => line.LineNumber);
        var payload = new PosReceipt(
            Guid.NewGuid(),
            documentId,
            immutable.DocumentNumber.FullNumber,
            immutable.FiscalSnapshot?.FiscalNumber,
            immutable.CommercialSnapshot.IssuedAt,
            immutable.CommercialSnapshot.CustomerIdentification,
            immutable.Lines.Select(line => new PosReceiptLine(
                metadata is not null && metadata.TryGetValue(line.LineNumber, out var item)
                    ? item.ProductCode
                    : string.Empty,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.TaxAmount,
                line.LineTotal)).ToArray(),
            immutable.Payments.Select(payment => new OfflineSalePayment(
                payment.MethodCode,
                payment.Amount,
                payment.Reference)).ToArray(),
            immutable.CommercialSnapshot.UntaxedAmount,
            immutable.CommercialSnapshot.TaxAmount,
            immutable.CommercialSnapshot.PayableAmount,
            immutable.FiscalSnapshot?.Cufe,
            immutable.FiscalSnapshot?.QrPayload,
            paperWidthMillimeters,
            immutable.CommercialSnapshot.DocumentType);

        await printer.PrintAsync(payload, ct);
        await sales.RecordReprintAsync(
            documentId,
            userId,
            timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow,
            ct);
    }
}
