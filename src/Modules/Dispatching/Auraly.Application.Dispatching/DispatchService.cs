using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Dispatching;

namespace Auraly.Application.Dispatching;

public interface IDispatchStore
{
    Task<DispatchPage> PageAsync(DispatchActorIdentity actor, DispatchQuery query, CancellationToken ct);
    Task<DispatchCandidatePage> CandidatesAsync(DispatchActorIdentity actor, DispatchCandidateQuery query, CancellationToken ct);
    Task<DispatchDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct, DispatchDetailQuery? query = null);
    Task<DispatchMutationResult> CreateAsync(DispatchActorIdentity actor, Guid dispatchId, CreateDispatchRequest request, string? vehiclePlate, string? notes, DateTimeOffset now, CancellationToken ct);
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
        RequireAny(actor, DispatchPermissionCodes.Read, DispatchPermissionCodes.ExecuteDeliveries);
        Page(query.Page, query.PageSize);
        Status(query.Status);
        if (query.From.HasValue && query.To < query.From) throw new DispatchValidationException("El rango de fechas no es válido.");
        return store.PageAsync(actor, query with { Search = Text(query.Search, 160) }, ct);
    }

    public Task<DispatchCandidatePage> CandidatesAsync(DispatchActorIdentity actor, DispatchCandidateQuery query, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.AttachDocuments);
        Page(query.Page, query.PageSize);
        if (query.DocumentType is not null && query.DocumentType is not ("SalesInvoice" or "SalesReceipt"))
            throw new DispatchValidationException("Solo se pueden despachar facturas y comprobantes de venta.");
        if (query.DateRangeVersion is not (1 or 2) || query.To < query.From ||
            query.DateRangeVersion == 2 && (!query.From.HasValue || !query.To.HasValue))
            throw new DispatchValidationException("Selecciona un rango de fechas válido para los documentos.");
        return store.CandidatesAsync(actor, query with { Search = Text(query.Search, 160) }, ct);
    }

    public async Task<DispatchDetail> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct, DispatchDetailQuery? query = null)
    {
        Require(actor, DispatchPermissionCodes.Read); Required(dispatchId, "DispatchId");
        query ??= new(); Page(query.DocumentPage, query.DocumentPageSize); Page(query.LinePage, query.LinePageSize);
        return await store.GetAsync(actor, dispatchId, ct, query) ?? throw new DispatchNotFoundException("El despacho no existe en la empresa autorizada.");
    }

    public Task<DispatchMutationResult> CreateAsync(DispatchActorIdentity actor, CreateDispatchRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.Create); Scope(actor, request.BusinessId);
        var plate = Text(request.VehiclePlate, 24)?.ToUpperInvariant();
        var notes = Text(request.Notes, 500);
        UniqueDocuments(request.SourceDocumentIds);
        var id = ids.NewId();
        if (request.DriverCarrierId is not Guid carrier || carrier == Guid.Empty)
            throw new DispatchValidationException("Selecciona un tercero con rol Transportador.");
        return store.CreateAsync(actor, id, request, plate, notes, time.GetUtcNow(), ct);
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
        if (request.QuantityDelta == 0) throw new DispatchValidationException("La variación de cantidad debe ser distinta de cero.");
        Idempotency(request.IdempotencyKey);
        return store.VerifyQuantityAsync(actor, dispatchId, request with { Barcode = Text(request.Barcode, 64) }, time.GetUtcNow(), ct);
    }

    public Task<DispatchMutationResult> DeclareShortageAsync(DispatchActorIdentity actor, Guid dispatchId, DeclareDispatchShortageRequest request, CancellationToken ct)
    {
        Require(actor, DispatchPermissionCodes.DeclareShortage); Required(dispatchId, "DispatchId"); Required(request.DispatchLineId, "DispatchLineId");
        if (request.Quantity <= 0) throw new DispatchValidationException("La cantidad faltante debe ser mayor que cero.");
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
    private static void Page(int page, int size) { if (page < 1 || size is < 1 or > 100 || ((long)page - 1) * size > int.MaxValue) throw new DispatchValidationException("La página o la cantidad de registros supera el rango permitido."); }
    private static void Status(string? value)
    {
        if (value is not null && value is not (DispatchStatuses.Draft or DispatchStatuses.Prepared or
            DispatchStatuses.InVerification or DispatchStatuses.Verified or DispatchStatuses.Released or
            DispatchStatuses.InDelivery or DispatchStatuses.PendingSettlement or DispatchStatuses.SettlementProcessing or
            DispatchStatuses.SettlementAttention or DispatchStatuses.Closed or DispatchStatuses.Cancelled))
            throw new DispatchValidationException("El estado del despacho no es válido.");
    }
    private static void UniqueDocuments(IReadOnlyCollection<Guid> values) { if (values.Count is 0 or > DispatchLimits.MaximumDocuments) throw new DispatchValidationException("Selecciona entre 1 y 500 documentos de venta."); if (values.Any(id => id == Guid.Empty) || values.Count != values.Distinct().Count()) throw new DispatchValidationException("Los documentos de venta deben ser válidos y no pueden repetirse."); }
    private static void Scope(DispatchActorIdentity actor, Guid businessId) { if (actor.BusinessId != businessId) throw new DispatchForbiddenException("La empresa del despacho no corresponde al contexto autorizado."); }
    private static void Required(Guid value, string field) { if (value == Guid.Empty) throw new DispatchValidationException($"El campo {field} es obligatorio."); }
    private static string RequiredText(string? value, int max, string field) => Text(value, max) ?? throw new DispatchValidationException($"El campo {field} es obligatorio.");
    private static string? Text(string? value, int max) { var result = value?.Trim(); if (string.IsNullOrEmpty(result)) return null; if (result.Length > max) throw new DispatchValidationException($"El texto admite máximo {max} caracteres."); return result; }
    private static void Idempotency(string value) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128) throw new DispatchValidationException("La identificación de la operación es obligatoria y admite máximo 128 caracteres."); }
    private static byte[] RowVersion(string value) { try { var bytes = Convert.FromBase64String(value); if (bytes.Length != 8) throw new FormatException(); return bytes; } catch (FormatException) { throw new DispatchValidationException("La versión del documento no es válida. Vuelve a abrirlo antes de continuar."); } }
    private static void Require(DispatchActorIdentity actor, string permission) { if (!actor.Permissions.Contains(permission)) throw new DispatchForbiddenException($"No tienes permiso para realizar esta acción."); }
    private static void RequireAny(DispatchActorIdentity actor, params string[] permissions) { if (!permissions.Any(actor.Permissions.Contains)) throw new DispatchForbiddenException("No tienes permiso para consultar o administrar despachos."); }
}
