using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Inventory;

namespace Auraly.Application.Inventory;

public interface IInventoryOperationStore
{
    Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> DispatchTransferAsync(InventoryUserIdentity user, string idempotencyKey, DispatchWarehouseTransferRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ReceiveTransferAsync(InventoryUserIdentity user, Guid transferId, string idempotencyKey, ReceiveWarehouseTransferRequest request, byte[] rowVersion, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmProductionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductionRequest request, CancellationToken cancellationToken);
    Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken);
}

public sealed class InventoryOperationService(
    IInventoryOperationStore store,
    IDocumentProcessingSignalPublisher signals)
{
    public Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Count);
        Required(request.DocumentId, "documento");
        Required(request.WarehouseId, "bodega");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateReason(request.ReasonCode);
        if (request.Lines.Count == 0 || request.Lines.Any(line => line.ProductId == Guid.Empty || line.PreCountQuantity < 0) || request.Lines.Select(line => line.ProductId).Distinct().Count() != request.Lines.Count)
            throw new InventoryValidationException("El conteo requiere productos válidos, sin repetirlos.");
        return store.StartCountAsync(user, request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) }, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Count);
        Required(documentId, "documento");
        ValidateKey(idempotencyKey);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.CountedQuantity < 0))
            throw new InventoryValidationException("Las cantidades contadas no pueden ser negativas.");
        return await PublishAsync(await store.ConfirmCountAsync(user, documentId, idempotencyKey.Trim(), request, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ApplyCountAsync(InventoryUserIdentity user, string idempotencyKey, ApplyStockCountRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0 || request.Lines.Any(line => line.InitialQuantity < 0 || line.CountedQuantity < 0))
            throw new InventoryValidationException("Las cantidades iniciales y finales del conteo no pueden ser negativas.");
        var started = new StartStockCountRequest(
            request.DocumentId, request.BusinessId, request.WarehouseId, request.OccurredAt,
            request.ReasonCode, request.Notes,
            request.Lines.Select(line => new StartStockCountLineRequest(line.ProductId, line.InitialQuantity)).ToArray());
        try
        {
            await StartCountAsync(user, started, cancellationToken);
        }
        catch (InventoryConflictException)
        {
            // A retry after a lost response can find the deterministic document already started.
        }
        var confirmation = new ConfirmStockCountRequest(request.BusinessId,
            request.Lines.Select((line, index) => new StockCountLineRequest(index + 1, line.ProductId, line.CountedQuantity)).ToArray());
        return await ConfirmCountAsync(user, request.DocumentId, idempotencyKey, confirmation, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Adjust);
        Required(request.DocumentId, "documento");
        Required(request.WarehouseId, "bodega");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.QuantityChange == 0 || line.ExplicitUnitCost < 0 || line.QuantityChange < 0 && line.ExplicitUnitCost is not null))
            throw new InventoryValidationException("Las cantidades del movimiento deben ser distintas de cero; el costo explícito solo aplica a las entradas.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ConfirmAdjustmentAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> DispatchTransferAsync(InventoryUserIdentity user, string idempotencyKey, DispatchWarehouseTransferRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.DispatchTransfer);
        Required(request.DocumentId, "documento");
        Required(request.SourceWarehouseId, "bodega de origen");
        Required(request.DestinationWarehouseId, "bodega de destino");
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
            throw new InventoryValidationException("Las bodegas de origen y destino deben ser diferentes.");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.DispatchedQuantity <= 0))
            throw new InventoryValidationException("Las cantidades del traslado deben ser mayores que cero.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.DispatchTransferAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ReceiveTransferAsync(InventoryUserIdentity user, Guid transferId, string idempotencyKey, ReceiveWarehouseTransferRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.ReceiveTransfer);
        Required(transferId, "traslado");
        Required(request.ReceiptId, "recepción");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.ReceivedQuantity < 0) ||
            request.Lines.All(line => line.ReceivedQuantity == 0) && string.IsNullOrWhiteSpace(request.DifferenceReasonCode))
            throw new InventoryValidationException("La recepción requiere una cantidad mayor que cero o un motivo para registrar la pérdida de todo el envío.");
        var version = RowVersion(request.RowVersion);
        var normalizedReason = string.IsNullOrWhiteSpace(request.DifferenceReasonCode) ? null : request.DifferenceReasonCode.Trim().ToUpperInvariant();
        if (normalizedReason?.Length > 40)
            throw new InventoryValidationException("El código del motivo de la diferencia admite máximo 40 caracteres.");
        var normalized = request with { DifferenceReasonCode = normalizedReason, Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ReceiveTransferAsync(user, transferId, idempotencyKey.Trim(), normalized, version, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Damage);
        Required(request.DocumentId, "documento");
        Required(request.WarehouseId, "bodega");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        if (request.Lines.Any(line => line.Quantity <= 0))
            throw new InventoryValidationException("Las cantidades de la avería deben ser mayores que cero.");
        if (request.Disposition is not null and not "Irreversible")
            throw new InventoryValidationException("El tratamiento de la avería no es válido.");
        var normalized = request with { ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes) };
        return await PublishAsync(await store.ConfirmDamageAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Convert);
        Required(request.DocumentId, "documento");
        Required(request.WarehouseId, "bodega");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        ValidateReason(request.ReasonCode);
        ValidateLines(request.Lines.Select(line => (line.LineNumber, line.ProductId)));
        var directions = request.Lines.Select(line => line.Direction.Trim().ToUpperInvariant()).ToArray();
        if (directions.Any(direction => direction is not ("INPUT" or "OUTPUT")) || !directions.Contains("INPUT") || !directions.Contains("OUTPUT"))
            throw new InventoryValidationException("La conversión requiere productos de entrada y de salida.");
        if (request.Lines.Any(line => line.Quantity <= 0 || line.AllocationWeight <= 0))
            throw new InventoryValidationException("Las cantidades y los porcentajes de distribución de la conversión deben ser mayores que cero.");
        var inputCount = directions.Count(direction => direction == "INPUT");
        var outputCount = directions.Count(direction => direction == "OUTPUT");
        var conversionType = request.ConversionType?.Trim().ToUpperInvariant() ?? string.Empty;
        if (conversionType == "SPLIT" && inputCount != 1 || conversionType == "MERGE" && outputCount != 1 || conversionType is not ("SPLIT" or "MERGE"))
            throw new InventoryValidationException("La conversión debe ser una división con una entrada o una combinación con una salida.");
        var weights = request.Lines.Where((_, index) => directions[index] == "OUTPUT").Select(line => line.AllocationWeight).ToArray();
        if (weights.Any(weight => weight is not null) && (weights.Any(weight => weight is null) || decimal.Round(weights.Sum(weight => weight!.Value), 6) != 100m))
            throw new InventoryValidationException("Indica todos los porcentajes de distribución de las salidas hasta sumar 100 %, o deja todos vacíos.");
        var lines = request.Lines.Select((line, index) => line with { Direction = directions[index] }).ToArray();
        var normalized = request with { ConversionType = conversionType, ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(), Notes = Notes(request.Notes), Lines = lines };
        return await PublishAsync(await store.ConfirmConversionAsync(user, idempotencyKey.Trim(), normalized, cancellationToken), request.BusinessId, cancellationToken);
    }

    public async Task<InventoryOperationAcceptance> ConfirmProductionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(user, request.BusinessId, InventoryPermissionCodes.Produce);
        Required(request.DocumentId, "documento");
        Required(request.WarehouseId, "bodega");
        Required(request.ProductId, "producto");
        Required(request.OccurredAt, "fecha de la operación");
        ValidateKey(idempotencyKey);
        if (request.ExpectedRecipeVersion < 1 || request.Quantity <= 0 || request.Quantity > 9999999999999.999999m)
            throw new InventoryValidationException("La cantidad y la versión de receta deben ser válidas.");
        return await PublishAsync(await store.ConfirmProductionAsync(user, idempotencyKey.Trim(),
            request with { Notes = Notes(request.Notes) }, cancellationToken), request.BusinessId, cancellationToken);
    }

    private async Task<InventoryOperationAcceptance> PublishAsync(InventoryOperationAcceptance acceptance, Guid businessId, CancellationToken cancellationToken)
    {
        await signals.PublishAsync(new DocumentProcessingSignal(acceptance.MovementId, businessId, acceptance.DocumentId, acceptance.DocumentType), cancellationToken);
        return acceptance;
    }

    private static void ValidateIdentity(InventoryUserIdentity user, Guid businessId, string permission)
    {
        if (user.BusinessId != businessId) throw new InventoryForbiddenException("El documento pertenece a otra sede.");
        if (!user.Permissions.Contains(permission)) throw new InventoryForbiddenException("No tienes permiso para realizar esta operación de inventario.");
    }

    private static void ValidateLines(IEnumerable<(int LineNumber, Guid ProductId)> source)
    {
        var lines = source.ToArray();
        if (lines.Length == 0 || lines.Any(line => line.LineNumber <= 0 || line.ProductId == Guid.Empty) || lines.Select(line => line.LineNumber).Distinct().Count() != lines.Length)
            throw new InventoryValidationException("El documento requiere números de línea y productos válidos, sin repetirlos.");
        if (lines.Select(line => line.ProductId).Distinct().Count() != lines.Length)
            throw new InventoryValidationException("Un producto no puede repetirse en este documento.");
    }

    private static void ValidateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 40) throw new InventoryValidationException("Selecciona un motivo válido cuyo código no supere 40 caracteres.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 160) throw new InventoryValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");
    }

    private static void Required(Guid value, string name) { if (value == Guid.Empty) throw new InventoryValidationException($"El campo {name} es obligatorio."); }
    private static void Required(DateTimeOffset value, string name) { if (value == default) throw new InventoryValidationException($"El campo {name} es obligatorio."); }
    private static string? Notes(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1000 ? value.Trim() : throw new InventoryValidationException("Las observaciones admiten máximo 1.000 caracteres.");
    private static byte[] RowVersion(string value)
    {
        try { var bytes = Convert.FromBase64String(value); return bytes.Length == 8 ? bytes : throw new FormatException(); }
        catch (FormatException) { throw new InventoryValidationException("La versión del documento no es válida. Vuelve a abrirlo antes de continuar."); }
    }
}

public sealed class InventoryForbiddenException(string message) : Exception(message);
public sealed class InventoryValidationException(string message) : Exception(message);
public sealed class InventoryConflictException(string message) : Exception(message);
