using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Returns;

namespace Auraly.Application.Returns;

public interface ISalesDebitNoteStore
{
    Task<SalesDebitNoteAcceptance> AcceptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesDebitNoteRequest request,
        CancellationToken cancellationToken);

    Task<SalesDebitNotePage> ListAsync(
        SalesReturnUserIdentity user,
        SalesDebitNoteQuery query,
        CancellationToken cancellationToken);

    Task<SalesDebitNoteDetail?> GetAsync(
        SalesReturnUserIdentity user,
        Guid debitNoteId,
        CancellationToken cancellationToken);
}

public sealed class SalesDebitNoteService(
    ISalesDebitNoteStore store,
    IDocumentProcessingSignalPublisher signals)
{
    public async Task<SalesDebitNoteAcceptance> ConfirmAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesDebitNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, SalesDebitNotePermissionCodes.Create);
        if (request.BusinessId != user.BusinessId)
            throw new SalesReturnForbiddenException("The debit note belongs to another business.");
        if (request.DebitNoteId == Guid.Empty || request.OriginalDocumentId == Guid.Empty)
            throw new SalesReturnValidationException("DebitNoteId and OriginalDocumentId are required.");
        if (request.IssuedAt == default || request.DueAt == default || request.DueAt < request.IssuedAt)
            throw new SalesReturnValidationException("A valid issue date and due date are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new SalesReturnValidationException("A valid Idempotency-Key is required.");
        if (!DianDebitNoteConcepts.All.ContainsKey(request.ConceptCode))
            throw new SalesReturnValidationException("The DIAN debit-note concept is invalid.");
        if (string.IsNullOrWhiteSpace(request.ReasonDescription) || request.ReasonDescription.Trim().Length > 300)
            throw new SalesReturnValidationException("A reason of at most 300 characters is required.");
        if (request.Notes?.Trim().Length > 1000)
            throw new SalesReturnValidationException("Notes cannot exceed 1000 characters.");
        if (request.Lines.Count is < 1 or > 100)
            throw new SalesReturnValidationException("The debit note requires between one and 100 lines.");
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Trim().Length > 300 ||
                line.Quantity <= 0 || line.UnitPrice <= 0 || line.TaxRate is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(line.TaxCode) || line.TaxCode.Trim().Length > 16)
                throw new SalesReturnValidationException("A debit-note line is invalid.");
        }

        var normalized = request with
        {
            ConceptCode = request.ConceptCode.Trim(),
            ReasonDescription = request.ReasonDescription.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Lines = request.Lines.Select(line => line with
            {
                Description = line.Description.Trim(),
                TaxCode = line.TaxCode.Trim().ToUpperInvariant()
            }).ToArray()
        };
        var accepted = await store.AcceptAsync(
            user, idempotencyKey.Trim(), normalized, cancellationToken);
        await signals.PublishAsync(new DocumentProcessingSignal(
            accepted.JobId, request.BusinessId, request.DebitNoteId,
            SalesDebitNoteDocumentTypes.SalesDebitNote), cancellationToken);
        return accepted;
    }

    public Task<SalesDebitNotePage> ListAsync(
        SalesReturnUserIdentity user,
        SalesDebitNoteQuery query,
        CancellationToken cancellationToken = default)
    {
        Require(user, SalesDebitNotePermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.From > query.To)
            throw new SalesReturnValidationException("The debit-note query is invalid.");
        return store.ListAsync(user, query, cancellationToken);
    }

    public Task<SalesDebitNoteDetail?> GetAsync(
        SalesReturnUserIdentity user,
        Guid debitNoteId,
        CancellationToken cancellationToken = default)
    {
        Require(user, SalesDebitNotePermissionCodes.Read);
        if (debitNoteId == Guid.Empty)
            throw new SalesReturnValidationException("DebitNoteId is required.");
        return store.GetAsync(user, debitNoteId, cancellationToken);
    }

    private static void Require(SalesReturnUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new SalesReturnForbiddenException($"Permission '{permission}' is required.");
    }
}
