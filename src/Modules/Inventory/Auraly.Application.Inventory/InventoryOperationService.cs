using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Inventory;

namespace Auraly.Application.Inventory;

public interface IInventoryOperationStore
{
    Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmTransferAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmWarehouseTransferRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken);
}

public sealed class InventoryOperationService(
    IInventoryOperationStore store,
    IDocumentProcessingSignalPublisher signals)
{
    public Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Count);
        Required(request.DocumentId, nameof(request.DocumentId));
        Required(request.WarehouseId, nameof(request.WarehouseId));
        Required(request.OccurredAt, nameof(request.OccurredAt));
        ValidateReason(request.ReasonCode);
        if (request.ProductIds.Count == 0 || request.ProductIds.Any(id => id == Guid.Empty) || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            throw new InventoryValidationException("A stock count requires distinct products.");
        return store.StartCountAsync(user, request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) }, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Count);
        Required(documentId, nameof(documentId));
        ValidateKey(idempotencyKey);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.CountedQuantity < 0))
            throw new InventoryValidationException("Counted quantities cannot be negative.");
        return await PublishAsync(await store.ConfirmCountAsync(user, documentId, idempotencyKey.Trim(), request, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Adjust);
        Required(request.DocumentId, nameof(request.DocumentId));
        Required(request.WarehouseId, nameof(request.WarehouseId));
        Required(request.OccurredAt, nameof(request.OccurredAt));
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.QuantityChange == 0 || line.ExplicitUnitCost < 0 || line.QuantityChange < 0 && line.ExplicitUnitCost is not null))
            throw new InventoryValidationException("Adjustment quantities must be non-zero and explicit costs apply only to inbound adjustments.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ConfirmAdjustmentAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmTransferAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmWarehouseTransferRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Transfer);
        Required(request.DocumentId, nameof(request.DocumentId));
        Required(request.SourceWarehouseId, nameof(request.SourceWarehouseId));
        Required(request.DestinationWarehouseId, nameof(request.DestinationWarehouseId));
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
            throw new InventoryValidationException("Transfer warehouses must be different.");
        Required(request.OccurredAt, nameof(request.OccurredAt));
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.Quantity <= 0))
            throw new InventoryValidationException("Transfer quantities must be positive.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ConfirmTransferAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Damage);
        Required(request.DocumentId, nameof(request.DocumentId));
        Required(request.WarehouseId, nameof(request.WarehouseId));
        Required(request.OccurredAt, nameof(request.OccurredAt));
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.Quantity <= 0))
            throw new InventoryValidationException("Damage quantities must be positive.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ConfirmDamageAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Convert);
        Required(request.DocumentId, nameof(request.DocumentId));
        Required(request.WarehouseId, nameof(request.WarehouseId));
        Required(request.OccurredAt, nameof(request.OccurredAt));
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)), allowProductOnBothDirections: true);
        var directions = request.Lines.Select(line => line.Direction.Trim().ToUpperInvariant()).ToArray();
        if (directions.Any(direction => direction is not ("INPUT" or "OUTPUT")) || !directions.Contains("INPUT") || !directions.Contains("OUTPUT"))
            throw new InventoryValidationException("A conversion requires input and output lines.");
        if (request.Lines.Any(line => line.Quantity <= 0 || line.AllocationWeight <= 0))
            throw new InventoryValidationException("Conversion quantities and allocation weights must be positive.");
        var inputCount = directions.Count(direction => direction == "INPUT");
        var outputCount = directions.Count(direction => direction == "OUTPUT");
        var conversionType = request.ConversionType.Trim().ToUpperInvariant();
        if (conversionType == "SPLIT" && inputCount != 1 || conversionType == "MERGE" && outputCount != 1 || conversionType is not ("SPLIT" or "MERGE"))
            throw new InventoryValidationException("ConversionType must be Split (one input) or Merge (one output).");
        var weights = request.Lines.Where((_, index) => directions[index] == "OUTPUT").Select(line => line.AllocationWeight).ToArray();
        if (weights.Any(weight => weight is not null) && (weights.Any(weight => weight is null) || decimal.Round(weights.Sum(weight => weight!.Value), 6) != 100m))
            throw new InventoryValidationException("Output allocation weights must all be omitted or total 100 percent.");
        var lines = request.Lines.Select((line, index) => line with { Direction = directions[index] }).ToArray();
        var normalized = request with { ConversionType = conversionType, ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes), Lines = lines };
        return await PublishAsync(await store.ConfirmConversionAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    private async Task<InventoryOperationAcceptance> PublishAsync(InventoryOperationAcceptance acceptance, Guid businessId, CancellationToken cancellationToken)
    {
        await signals.PublishAsync(new DocumentProcessingSignal(acceptance.MovementId, businessId, acceptance.DocumentId, acceptance.DocumentType), cancellationToken);
        return acceptance;
    }

    private static void ValidateIdentity(InventoryUserIdentity user, Guid businessId, string permission)
    {
        if (user.BusinessId != businessId) throw new InventoryForbiddenException("The document belongs to another business.");
        if (!user.Permissions.Contains(permission)) throw new InventoryForbiddenException($"Permission '{permission}' is required.");
    }

    private static void ValidateLines(IEnumerable<(int LineNumber, Guid ProductId)> source, bool allowProductOnBothDirections = false)
    {
        var lines = source.ToArray();
        if (lines.Length == 0 || lines.Any(line => line.LineNumber <= 0 || line.ProductId == Guid.Empty) || lines.Select(line => line.LineNumber).Distinct().Count() != lines.Length)
            throw new InventoryValidationException("The document requires valid, unique line numbers and products.");
        if (!allowProductOnBothDirections && lines.Select(line => line.ProductId).Distinct().Count() != lines.Length)
            throw new InventoryValidationException("A product cannot be repeated in this document.");
    }

    private static void ValidateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 40) throw new InventoryValidationException("ReasonCode is required and limited to 40 characters.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 160) throw new InventoryValidationException("A valid Idempotency-Key is required.");
    }

    private static void Required(Guid value, string name) { if (value == Guid.Empty) throw new InventoryValidationException($"{name} is required."); }
    private static void Required(DateTimeOffset value, string name) { if (value == default) throw new InventoryValidationException($"{name} is required."); }
    private static string? Notes(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1000 ? value.Trim() : throw new InventoryValidationException("Notes are limited to 1000 characters.");
}

public sealed class InventoryForbiddenException(string message) : Exception(message);
public sealed class InventoryValidationException(string message) : Exception(message);
public sealed class InventoryConflictException(string message) : Exception(message);
