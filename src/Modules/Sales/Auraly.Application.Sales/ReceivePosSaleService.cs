using System.Security.Cryptography;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record PosDeviceIdentity(
    Guid DeviceId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public sealed record PosSaleContextValidation(bool IsValid, string? Reason)
{
    public static PosSaleContextValidation Valid() => new(true, null);
    public static PosSaleContextValidation Invalid(string reason) => new(false, reason);
}

public sealed record StoredPosSale(
    Guid DocumentId,
    Guid? MovementId,
    Guid TenantId,
    string IdempotencyKey,
    byte[] PayloadHash,
    string? FiscalStatus,
    string ProcessingStatus,
    string? CufeReceived,
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
    IDocumentProcessingSignalPublisher signalPublisher,
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
        ValidateIdempotencyKey(idempotencyKey);
        if (!string.Equals(
                request.SourceMode,
                SaleSourceModes.PosEdge,
                StringComparison.Ordinal))
            throw new PosSaleInvalidException(
                "The POS upload endpoint only accepts PosEdge documents.");
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
        return await ReceiveCoreAsync(
            idempotencyKey, request, validateDeviceContext: true, cancellationToken);
    }

    public async Task<PosSaleUploadResponse> ReceiveOnlineAsync(
        OnlineSalesUserIdentity user,
        string idempotencyKey,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(idempotencyKey);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate) ||
            request.TenantId != user.TenantId ||
            request.SoldByUserId != user.UserId ||
            request.DeviceId != Guid.Empty ||
            !string.Equals(
                request.SourceMode,
                SaleSourceModes.Online,
                StringComparison.Ordinal))
            throw new PosSaleForbiddenException(
                "The prepared online sale differs from the authenticated user context.");
        return await ReceiveCoreAsync(
            idempotencyKey, request, validateDeviceContext: false, cancellationToken);
    }

    private async Task<PosSaleUploadResponse> ReceiveCoreAsync(
        string idempotencyKey,
        PosSaleUploadRequest request,
        bool validateDeviceContext,
        CancellationToken cancellationToken)
    {
        ValidateDocumentNumber(request);
        ValidateSettlement(request);
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
                return ToResponse(existing, isDuplicate: true);
        }

        var context = validateDeviceContext
            ? await store.ValidateContextAsync(request, cancellationToken)
            : PosSaleContextValidation.Valid();
        var verification = request.FiscalSnapshot is null
            ? new FiscalSnapshotVerificationResult(true, string.Empty, null, null)
            : await fiscalVerifier.VerifyAsync(request, cancellationToken);
        if (!context.IsValid && verification.IsVerified)
        {
            verification = new FiscalSnapshotVerificationResult(
                false,
                request.FiscalSnapshot?.Cufe ?? string.Empty,
                verification.CufeCalculated,
                context.Reason ??
                "The fiscal series or authorization differs from server configuration.");
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
            return ToResponse(stored, isDuplicate: existing is not null);

        await signalPublisher.PublishAsync(
            new DocumentProcessingSignal(
                stored.MovementId ?? throw new InvalidOperationException(
                    "The accepted sale has no durable processing movement."),
                request.BusinessId,
                request.DocumentId,
                request.CommercialSnapshot.DocumentType,
                EconomicEffectsEnabled: !request.FiscalHabilitationOnly),
            cancellationToken);

        var completed = await store.FindAsync(
                request.BusinessId,
                request.DocumentId,
                idempotencyKey,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The processed sale could not be read back.");
        EnsureSameRequest(completed, request.DocumentId, idempotencyKey, payloadHash);
        return ToResponse(
            completed,
            isDuplicate: existing is not null);
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new PosSaleInvalidException(
                "A valid Idempotency-Key header is required.");
    }

    private static void DemandDeviceContext(
        PosDeviceIdentity device,
        PosSaleUploadRequest request)
    {
        if (!device.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new PosSaleForbiddenException(
                "The device cannot register POS sales.");

        if (device.DeviceId != request.DeviceId ||
            device.TenantId != request.TenantId)
            throw new PosSaleForbiddenException(
                "The uploaded tenant or device differs from the authenticated context.");
    }

    private static void ValidateSettlement(PosSaleUploadRequest request)
    {
        var paid = request.Payments.Sum(payment => payment.Amount);
        var credit = request.Credit?.Amount ?? 0m;
        if (request.Payments.Any(payment => payment.Amount <= 0) ||
            request.Payments.Select(payment => payment.PaymentNumber).Distinct().Count() != request.Payments.Count ||
            paid + credit != request.Lines.Sum(line => line.LineTotal))
            throw new PosSaleInvalidException(
                "Actual payments plus financed balance must equal the invoice total.");
        if (request.Credit is null) return;
        if (request.Credit.CustomerId == Guid.Empty || request.CustomerId != request.Credit.CustomerId)
            throw new PosSaleInvalidException(
                "A credit sale requires the selected customer as debtor.");
        if (request.Credit.Amount <= 0 || request.Credit.DueDate < request.CommercialSnapshot.IssuedAt)
            throw new PosSaleInvalidException("The financed balance and due date are invalid.");
        if (request.FiscalSnapshot is not null &&
            (request.UblSnapshot is null || request.UblSnapshot.PaymentFormCode != "2" ||
             request.UblSnapshot.DueDate != DateOnly.FromDateTime(request.Credit.DueDate.Date)))
            throw new PosSaleInvalidException(
                "The immutable UBL snapshot must identify the credit terms.");
    }

    private static void ValidateDocumentNumber(PosSaleUploadRequest request)
    {
        var number = request.DocumentNumber;
        if (!string.Equals(number.DocumentType, request.CommercialSnapshot.DocumentType,
                StringComparison.Ordinal) ||
            (request.FiscalSnapshot is not null && !string.Equals(
                number.DocumentType, request.FiscalSnapshot.DocumentType,
                StringComparison.Ordinal)))
            throw new PosSaleInvalidException(
                "The Auraly document type and fiscal document type must match.");

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

        if (!string.Equals(
                expected.FullNumber,
                number.FullNumber,
                StringComparison.Ordinal))
            throw new PosSaleInvalidException(
                "The Auraly document number does not match its components.");
    }

    private static void EnsureSameRequest(
        StoredPosSale existing,
        Guid documentId,
        string idempotencyKey,
        byte[] payloadHash)
    {
        if (existing.DocumentId != documentId ||
            !string.Equals(
                existing.IdempotencyKey,
                idempotencyKey.Trim(),
                StringComparison.Ordinal) ||
            existing.PayloadHash.Length != payloadHash.Length ||
            !CryptographicOperations.FixedTimeEquals(
                existing.PayloadHash,
                payloadHash))
            throw new PosSaleIdempotencyConflictException(
                "The document ID or idempotency key was already used with different content.");
    }

    private static PosSaleUploadResponse ToResponse(
        StoredPosSale sale,
        bool isDuplicate) =>
        new(
            sale.ReceiptId ?? Guid.Empty,
            sale.DocumentId,
            sale.FiscalStatus is null
                ? isDuplicate ? PosSaleRemoteStatuses.AlreadyProcessed : PosSaleRemoteStatuses.CommercialAccepted
                : sale.FiscalStatus == PosSaleRemoteStatuses.FiscalIntegrityConflict
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
