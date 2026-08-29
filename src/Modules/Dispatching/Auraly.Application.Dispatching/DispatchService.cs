using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Dispatching;

namespace Auraly.Application.Dispatching;

public interface IDispatchStore
{
    Task<DispatchPage> PageAsync(DispatchActorIdentity actor, DispatchQuery query, CancellationToken ct);
    Task<DispatchOptions> OptionsAsync(DispatchActorIdentity actor, CancellationToken ct);
    Task<DispatchDriverPage> DriversAsync(DispatchActorIdentity actor, int page, int pageSize, string? search, Guid? userId, CancellationToken ct);
    Task<DispatchCandidatePage> CandidatesAsync(DispatchActorIdentity actor, DispatchCandidateQuery query, CancellationToken ct);
    Task<DispatchDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct);
    Task<DispatchMutationResult> CreateAsync(DispatchActorIdentity actor, Guid dispatchId, string dispatchNumber, CreateDispatchRequest request, string driverName, string? vehiclePlate, string? notes, DateTimeOffset now, CancellationToken ct);
    Task<DispatchMutationResult> AddDocumentsAsync(DispatchActorIdentity actor, Guid dispatchId, IReadOnlyCollection<Guid> documentIds, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<DispatchMutationResult> RemoveDocumentAsync(DispatchActorIdentity actor, Guid dispatchId, Guid sourceDocumentId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<DispatchMutationResult> TransitionAsync(DispatchActorIdentity actor, Guid dispatchId, string targetStatus, byte[] rowVersion, string idempotencyKey, DateTimeOffset now, CancellationToken ct);
    Task<DispatchMutationResult> VerifyQuantityAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchVerificationRequest request, DateTimeOffset now, CancellationToken ct);
    Task<DispatchMutationResult> DeclareShortageAsync(DispatchActorIdentity actor, Guid dispatchId, DeclareDispatchShortageRequest request, byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<DispatchReport> ReportAsync(DispatchActorIdentity actor, Guid dispatchId, bool includePrices, CancellationToken ct);
}

public sealed class DispatchService(IDispatchStore store, IAuralyIdGenerator ids, TimeProvider time)
{
    public Task<DispatchPage> PageAsync(DispatchActorIdentity actor, DispatchQuery query, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Read);
        Page(query.Page, query.PageSize);
        Status(query.Status);
        if (query.From.HasValue && query.To < query.From) throw new DispatchValidationException("The date range is invalid.");
        return store.PageAsync(actor, query with { Search = Text(query.Search, 160) }, ct);
    }

    public Task<DispatchOptions> OptionsAsync(DispatchActorIdentity actor, CancellationToken ct)
    {
        RequireAny(actor, DispatchPermissionCodes.Read, DispatchPermissionCodes.Create);
        return store.OptionsAsync(actor, ct);
    }

    public Task<DispatchDriverPage> DriversAsync(DispatchActorIdentity actor, int page,
        int pageSize, string? search, Guid? userId, CancellationToken ct)
    {
        RequireAny(actor, DispatchPermissionCodes.Read, DispatchPermissionCodes.Create);
        Page(page, pageSize);
        return store.DriversAsync(actor, page, pageSize, Text(search, 160), userId, ct);
    }

    public Task<DispatchCandidatePage> CandidatesAsync(DispatchActorIdentity actor, DispatchCandidateQuery query, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.AttachDocuments);
        Page(query.Page, query.PageSize);
        if (query.DocumentType is not null && query.DocumentType is not ("SalesInvoice" or "SalesReceipt"))
            throw new DispatchValidationException("Only sales invoices and sales receipts can be dispatched.");
        return store.CandidatesAsync(actor, query with { Search = Text(query.Search, 160) }, ct);
    }

    public async Task<DispatchDetail> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Read); Required(dispatchId, "DispatchId");
        return await store.GetAsync(actor, dispatchId, ct) ?? throw new DispatchNotFoundException("The dispatch does not exist in the authenticated business.");
    }

    public Task<DispatchMutationResult> CreateAsync(DispatchActorIdentity actor, CreateDispatchRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Create); Scope(actor, request.BusinessId);
        Required(request.WarehouseId, "WarehouseId");
        var driver = RequiredText(request.DriverName, 160, "DriverName");
        var plate = Text(request.VehiclePlate, 24)?.ToUpperInvariant();
        var notes = Text(request.Notes, 500);
        UniqueDocuments(request.SourceDocumentIds);
        var id = ids.NewId();
        var number = $"DSP-{request.ScheduledDate:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}";
        return store.CreateAsync(actor, id, number, request, driver, plate, notes, time.GetUtcNow(), ct);
    }

    public Task<DispatchMutationResult> AddDocumentsAsync(DispatchActorIdentity actor, Guid dispatchId, AddDispatchDocumentsRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.AttachDocuments); Required(dispatchId, "DispatchId"); UniqueDocuments(request.SourceDocumentIds);
        return store.AddDocumentsAsync(actor, dispatchId, request.SourceDocumentIds, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<DispatchMutationResult> RemoveDocumentAsync(DispatchActorIdentity actor, Guid dispatchId, Guid sourceDocumentId, string rowVersion, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.AttachDocuments); Required(dispatchId, "DispatchId"); Required(sourceDocumentId, "SourceDocumentId");
        return store.RemoveDocumentAsync(actor, dispatchId, sourceDocumentId, RowVersion(rowVersion), time.GetUtcNow(), ct);
    }

    public Task<DispatchMutationResult> PrepareAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.Prepared, DispatchPermissionCodes.Prepare, request, ct);
    public Task<DispatchMutationResult> StartVerificationAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.InVerification, DispatchPermissionCodes.Verify, request, ct);
    public Task<DispatchMutationResult> CompleteVerificationAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.Verified, DispatchPermissionCodes.Verify, request, ct);
    public Task<DispatchMutationResult> ReleaseAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.Released, DispatchPermissionCodes.Release, request, ct);
    public Task<DispatchMutationResult> CancelAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.Cancelled, DispatchPermissionCodes.Cancel, request, ct);
    public Task<DispatchMutationResult> ReopenAsync(DispatchActorIdentity actor, Guid id, DispatchTransitionRequest request, CancellationToken ct) => Transition(actor, id, DispatchStatuses.InVerification, DispatchPermissionCodes.Reopen, request, ct);

    public Task<DispatchMutationResult> VerifyQuantityAsync(DispatchActorIdentity actor, Guid dispatchId, DispatchVerificationRequest request, CancellationToken ct)
    {
        Require(actor, request.QuantityDelta < 0 ? DispatchPermissionCodes.CorrectVerification : DispatchPermissionCodes.Verify);
        Required(dispatchId, "DispatchId"); Required(request.DispatchLineId, "DispatchLineId");
        if (request.QuantityDelta == 0) throw new DispatchValidationException("QuantityDelta cannot be zero.");
        Idempotency(request.IdempotencyKey);
        return store.VerifyQuantityAsync(actor, dispatchId, request with { Barcode = Text(request.Barcode, 64) }, time.GetUtcNow(), ct);
    }

    public Task<DispatchMutationResult> DeclareShortageAsync(DispatchActorIdentity actor, Guid dispatchId, DeclareDispatchShortageRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.DeclareShortage); Required(dispatchId, "DispatchId"); Required(request.DispatchLineId, "DispatchLineId");
        if (request.Quantity <= 0) throw new DispatchValidationException("Shortage quantity must be positive.");
        Idempotency(request.IdempotencyKey);
        var normalized = request with { Reason = RequiredText(request.Reason, 120, "Reason"), Notes = Text(request.Notes, 500) };
        return store.DeclareShortageAsync(actor, dispatchId, normalized, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
    }

    public Task<DispatchReport> ReportAsync(DispatchActorIdentity actor, Guid dispatchId, bool includePrices, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Reports); Required(dispatchId, "DispatchId");
        if (includePrices) Require(actor, DispatchPermissionCodes.ViewPrices);
        return store.ReportAsync(actor, dispatchId, includePrices, ct);
    }

    private Task<DispatchMutationResult> Transition(DispatchActorIdentity actor, Guid id, string status, string permission, DispatchTransitionRequest request, CancellationToken ct)
    { Require(actor, permission); Required(id, "DispatchId"); Idempotency(request.IdempotencyKey); return store.TransitionAsync(actor, id, status, RowVersion(request.RowVersion), request.IdempotencyKey.Trim(), time.GetUtcNow(), ct); }
    private static void Page(int page, int size) { if (page < 1 || size is < 1 or > 100) throw new DispatchValidationException("Page and PageSize are outside the allowed range."); }
    private static void Status(string? value) { if (value is not null && value is not (DispatchStatuses.Draft or DispatchStatuses.Prepared or DispatchStatuses.InVerification or DispatchStatuses.Verified or DispatchStatuses.Released or DispatchStatuses.Cancelled)) throw new DispatchValidationException("Dispatch status is invalid."); }
    private static void UniqueDocuments(IReadOnlyCollection<Guid> values) { if (values.Count == 0) throw new DispatchValidationException("Select at least one sales document."); if (values.Any(id => id == Guid.Empty) || values.Count != values.Distinct().Count()) throw new DispatchValidationException("Sales documents must be unique and valid."); }
    private static void Scope(DispatchActorIdentity actor, Guid businessId) { if (actor.BusinessId != businessId) throw new DispatchForbiddenException("The dispatch business does not match the authenticated context."); }
    private static void Required(Guid value, string field) { if (value == Guid.Empty) throw new DispatchValidationException($"{field} is required."); }
    private static string RequiredText(string? value, int max, string field) => Text(value, max) ?? throw new DispatchValidationException($"{field} is required.");
    private static string? Text(string? value, int max) { var result = value?.Trim(); if (string.IsNullOrEmpty(result)) return null; if (result.Length > max) throw new DispatchValidationException($"Text cannot exceed {max} characters."); return result; }
    private static void Idempotency(string value) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128) throw new DispatchValidationException("IdempotencyKey is required and limited to 128 characters."); }
    private static byte[] RowVersion(string value) { try { var bytes = Convert.FromBase64String(value); if (bytes.Length != 8) throw new FormatException(); return bytes; } catch (FormatException) { throw new DispatchValidationException("RowVersion is invalid."); } }
    private static void Require(DispatchActorIdentity actor, string permission) { if (!actor.Permissions.Contains(permission)) throw new DispatchForbiddenException($"Permission '{permission}' is required."); }
    private static void RequireAny(DispatchActorIdentity actor, params string[] permissions) { if (!permissions.Any(actor.Permissions.Contains)) throw new DispatchForbiddenException("A dispatch permission is required."); }
}
