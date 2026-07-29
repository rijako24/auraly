using System.Security.Cryptography;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record PosDeviceIdentity(
    Guid DeviceId,
    Guid TenantId,
    Guid BusinessId,
    Guid LocationId,
    Guid WarehouseId,
    Guid RegisterId,
    IReadOnlySet<string> Permissions);

public sealed record PosSaleContextValidation(bool IsValid, string? Reason)
{
    public static PosSaleContextValidation Valid() => new(true, null);
    public static PosSaleContextValidation Invalid(string reason) => new(false, reason);
}

public sealed record StoredPosSale(
    Guid DocumentId,
    Guid TenantId,
    string IdempotencyKey,
    byte[] PayloadHash,
    string FiscalStatus,
    string ProcessingStatus,
    string CufeReceived,
    string? CufeCalculated,
    Guid? ReceiptId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? Detail);

public sealed record StorePosSaleReceptionCommand(
    PosSaleUploadRequest Request,
    string IdempotencyKey,
    string SnapshotJson,
    byte[] PayloadHash,
    FiscalSnapshotVerificationResult Verification,
    DateTimeOffset ReceivedAt);

public interface IPosSaleServerStore
{
    Task<PosSaleContextValidation> ValidateContextAsync(
        PosSaleUploadRequest request,
        CancellationToken cancellationToken);

    Task<StoredPosSale?> FindAsync(
        Guid businessId,
        Guid documentId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StoredPosSale> StoreReceptionAsync(
        StorePosSaleReceptionCommand command,
        CancellationToken cancellationToken);
}

public interface IPosSaleCustomerResolver
{
    Task<Guid?> ResolveForBusinessAsync(
        Guid tenantId,
        Guid businessId,
        Guid sourceCustomerId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct);
}


public sealed class PosSaleForbiddenException(string message) : Exception(message);

public sealed class PosSaleInvalidException(string message) : Exception(message);

public sealed class PosSaleIdempotencyConflictException(string message) : Exception(message);

public sealed class PosSaleProcessingBusyException(string message) : Exception(message);

public sealed class ReceivePosSaleService(
    IPosSaleServerStore store,
    IPosSaleCustomerResolver customers,
    IFiscalSnapshotVerifier fiscalVerifier,
    DocumentProcessingEngine processingEngine,
    TimeProvider timeProvider)
{
    public async Task<PosSaleUploadResponse> ReceiveAsync(
        PosDeviceIdentity device,
        string idempotencyKey,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new PosSaleInvalidException("A valid Idempotency-Key header is required.");
        }

        DemandDeviceContext(device, request);
        if (request.CustomerId is Guid sourceCustomerId)
        {
            request = request with
            {
                CustomerId = await customers.ResolveForBusinessAsync(
                    request.TenantId,
                    request.BusinessId,
                    sourceCustomerId,
                    device.DeviceId,
                    timeProvider.GetUtcNow(),
                    cancellationToken)
            };
        }
        ValidateDocumentNumber(request);
        var snapshotJson = PosSaleContractSerializer.Serialize(request);
        var payloadHash = PosSaleContractSerializer.Hash(request);
        var existing = await store.FindAsync(
            request.BusinessId,
            request.DocumentId,
            idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            EnsureSameRequest(existing, request.DocumentId, idempotencyKey, payloadHash);
            if (existing.ProcessingStatus is "Completed" or "Blocked")
            {
                return ToResponse(existing, isDuplicate: true);
            }
        }

        var context = await store.ValidateContextAsync(request, cancellationToken);
        var verification = await fiscalVerifier.VerifyAsync(request, cancellationToken);
        if (!context.IsValid && verification.IsVerified)
        {
            verification = new FiscalSnapshotVerificationResult(
                false,
                request.FiscalSnapshot.Cufe,
                verification.CufeCalculated,
                context.Reason ?? "The fiscal series or authorization differs from server configuration.");
        }

        var receivedAt = timeProvider.GetUtcNow();
        var stored = await store.StoreReceptionAsync(
            new StorePosSaleReceptionCommand(
                request,
                idempotencyKey.Trim(),
                snapshotJson,
                payloadHash,
                verification,
                receivedAt),
            cancellationToken);
        EnsureSameRequest(stored, request.DocumentId, idempotencyKey, payloadHash);

        if (!verification.IsVerified || stored.ProcessingStatus == "Blocked")
        {
            return ToResponse(stored, isDuplicate: existing is not null);
        }

        var result = await processingEngine.ProcessAsync(
            new ConfirmedDocument(
                new TenantId(request.TenantId),
                new BusinessId(request.BusinessId),
                new DocumentId(request.DocumentId),
                PosSaleDocumentTypes.Invoice,
                snapshotJson,
                receivedAt),
            cancellationToken);
        if (result == DocumentProcessingResult.Busy)
        {
            throw new PosSaleProcessingBusyException("The document is currently being processed.");
        }

        var completed = await store.FindAsync(
                request.BusinessId,
                request.DocumentId,
                idempotencyKey,
                cancellationToken)
            ?? throw new InvalidOperationException("The processed sale could not be read back.");
        EnsureSameRequest(completed, request.DocumentId, idempotencyKey, payloadHash);
        return ToResponse(
            completed,
            isDuplicate: existing is not null || result == DocumentProcessingResult.AlreadyProcessed);
    }

    private static void DemandDeviceContext(PosDeviceIdentity device, PosSaleUploadRequest request)
    {
        if (!device.Permissions.Contains(CommercePermissionCodes.SalesCreate))
        {
            throw new PosSaleForbiddenException("The device cannot register POS sales.");
        }

        if (device.DeviceId != request.DeviceId ||
            device.TenantId != request.TenantId ||
            device.BusinessId != request.BusinessId ||
            device.LocationId != request.LocationId ||
            device.WarehouseId != request.WarehouseId ||
            device.RegisterId != request.RegisterId)
        {
            throw new PosSaleForbiddenException(
                "The uploaded tenant, business, location, warehouse, register or device differs from the authenticated context.");
        }
    }

    private static void ValidateDocumentNumber(PosSaleUploadRequest request)
    {
        var number = request.DocumentNumber;
        if (!string.Equals(number.DocumentType, request.FiscalSnapshot.DocumentType, StringComparison.Ordinal))
        {
            throw new PosSaleInvalidException(
                "The Auraly document type and fiscal document type must match.");
        }

        AuralyDocumentNumberAssignment expected;
        try
        {
            expected = AuralyDocumentNumberAssignment.Create(
                number.SeriesId,
                number.DocumentType,
                number.Prefix,
                number.SeriesCode,
                number.Consecutive,
                number.Padding);
        }
        catch (ArgumentException exception)
        {
            throw new PosSaleInvalidException(exception.Message);
        }

        if (!string.Equals(expected.FullNumber, number.FullNumber, StringComparison.Ordinal))
        {
            throw new PosSaleInvalidException("The Auraly document number does not match its components.");
        }
    }

    private static void EnsureSameRequest(
        StoredPosSale existing,
        Guid documentId,
        string idempotencyKey,
        byte[] payloadHash)
    {
        if (existing.DocumentId != documentId ||
            !string.Equals(existing.IdempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal) ||
            existing.PayloadHash.Length != payloadHash.Length ||
            !CryptographicOperations.FixedTimeEquals(existing.PayloadHash, payloadHash))
        {
            throw new PosSaleIdempotencyConflictException(
                "The document ID or idempotency key was already used with different content.");
        }
    }

    private static PosSaleUploadResponse ToResponse(StoredPosSale sale, bool isDuplicate) =>
        new(
            sale.ReceiptId ?? Guid.Empty,
            sale.DocumentId,
            sale.FiscalStatus == PosSaleRemoteStatuses.FiscalIntegrityConflict
                ? PosSaleRemoteStatuses.FiscalIntegrityConflict
                : isDuplicate
                    ? PosSaleRemoteStatuses.AlreadyProcessed
                    : PosSaleRemoteStatuses.FiscalVerified,
            sale.CufeReceived,
            sale.CufeCalculated,
            isDuplicate,
            sale.ReceivedAt,
            sale.ProcessedAt,
            sale.Detail);
}

