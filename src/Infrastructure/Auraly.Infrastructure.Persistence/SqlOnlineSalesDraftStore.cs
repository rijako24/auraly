using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Application.Inventory;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Money;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Authorization;
using Auraly.Application.Orders;
using Auraly.Contracts.Orders;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time,
    SqlInventoryOperationStore inventoryOperations) : IOnlineSalesDraftStore, IOnlineSalesCheckoutStore,
    IOnlineSalesHistoryStore, IOrderCancellationStore
{
    public async Task<OnlineSalesDraft> GetOrCreateActiveAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext requested,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

        var context = await ResolveOnlineContextAsync(
            connection, transaction, user, requested, cancellationToken);

        var draftId = await FindActiveAsync(
            connection, transaction, context.BusinessId,
            context.WorkSessionId, user.UserId, cancellationToken);
        if (draftId is null)
        {
            draftId = ids.NewId();
            var now = time.GetUtcNow();
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDrafts(
                  SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
                  Status,Version,CreatedAt,UpdatedAt)
                VALUES(
                  @DraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
                  N'Active',1,@Now,@Now);
                """,
                [
                    P("@DraftId", draftId.Value),
                    P("@BusinessId", context.BusinessId),
                    P("@WarehouseId", context.WarehouseId),
                    P("@WorkSessionId", context.WorkSessionId),
                    P("@UserId", user.UserId),
                    P("@Now", now)
                ],
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDrafts
                SET WarehouseId=@WarehouseId,Version=Version+1,UpdatedAt=@Now
                WHERE SalesDraftId=@DraftId AND WarehouseId<>@WarehouseId;
                """,
                [
                    P("@DraftId", draftId.Value),
                    P("@WarehouseId", context.WarehouseId),
                    P("@Now", time.GetUtcNow())
                ],
                cancellationToken);
        }

        var result = await ReadDraftAsync(
            connection, transaction, draftId.Value, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> AddProductAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        string selector,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "AddProduct";
        var hash = Hash($"{operation}|{draftId:D}|{selector}|{Invariant(quantity)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var productId = await ResolveProductIdAsync(
            connection, transaction, state.BusinessId,
            selector, cancellationToken);
        var product = await ReadProductAsync(
            connection, transaction, state.BusinessId, state.WarehouseId,
            productId, cancellationToken);
        DemandAllowedQuantity(product.AllowsFractionalSale, quantity);
        await DemandInventoryAsync(
            connection, transaction, state, draftId, productId, null,
            quantity, cancellationToken);
        await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDraftLines(
                  SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
                  UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,PublicUnitPrice,PublicLineTotal,DocumentUnitCost,
                  CurrencyCode,PriceSource,PriceChannelId,IsGenericProductSnapshot,
                  DiscountAmount,PromotionDiscountAmount,Position)
                SELECT
                  @LineId,@DraftId,@ProductId,@ProductCode,@Description,
                  @UnitCode,@TaxCode,@TaxRate,@Quantity,@BaseUnitPrice,@UnitPrice,@PublicUnitPrice,@PublicLineTotal,@DocumentUnitCost,
                  @CurrencyCode,@PriceSource,@PriceChannelId,@IsGenericProduct,
                  0,0,COALESCE(MAX(Position),0)+1
                FROM dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;
                """,
                [
                    P("@LineId", ids.NewId()), P("@DraftId", draftId),
                    P("@ProductId", productId), P("@ProductCode", product.Code),
                    P("@Description", product.Name), P("@UnitCode", product.UnitCode),
                    P("@TaxCode", product.TaxCode), P("@TaxRate", product.TaxRate),
                    P("@Quantity", quantity), P("@BaseUnitPrice", product.IsGenericProduct ? 0m : product.UnitPrice),
                    P("@UnitPrice", MonetaryRounding.CeilingLineUnitPrice(TaxExclusive(
                        MonetaryRounding.CeilingLineUnitPrice(product.IsGenericProduct ? 0m : product.UnitPrice), product.TaxRate))), P("@CurrencyCode", product.CurrencyCode),
                    P("@PublicUnitPrice", MonetaryRounding.CeilingLineUnitPrice(product.IsGenericProduct ? 0m : product.UnitPrice)),
                    P("@PublicLineTotal", MonetaryRounding.RoundLineAmount(
                        MonetaryRounding.CeilingLineUnitPrice(product.IsGenericProduct ? 0m : product.UnitPrice) * quantity)),
                    P("@DocumentUnitCost", product.IsGenericProduct ? 0m : product.UnitCost),
                    P("@PriceSource", product.IsGenericProduct ? "Manual" : "Base"),
                    P("@PriceChannelId", null),
                    P("@IsGenericProduct", product.IsGenericProduct)
                ],
                cancellationToken);
        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> ChangeQuantityAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "ChangeQuantity";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}|{Invariant(quantity)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var line = await ReadLineProductAsync(
            connection, transaction, draftId, lineId, cancellationToken);
        var product = await ReadProductAsync(
            connection, transaction, state.BusinessId, state.WarehouseId, line.ProductId, cancellationToken);
        DemandAllowedQuantity(product.AllowsFractionalSale, quantity);
        await DemandInventoryAsync(
            connection, transaction, state, draftId, line.ProductId, lineId,
            quantity, cancellationToken);
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines
            SET Quantity=@Quantity,
                PublicLineTotal=CASE WHEN PriceSource=N'Manual'
                    THEN ROUND(PublicUnitPrice*@Quantity-DiscountAmount-PromotionDiscountAmount,2)
                    ELSE PublicLineTotal END
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [
                P("@Quantity", quantity),
                P("@DraftId", draftId), P("@LineId", lineId)
            ],
            cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");

        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);

        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> SetDiscountAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal discount,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "SetDiscount";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}|{Invariant(discount)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines
            SET DiscountAmount=@Discount,
                PublicLineTotal=ROUND(PublicUnitPrice*Quantity-@Discount-PromotionDiscountAmount,2),
                PriceSource=N'Manual',PriceChannelId=NULL
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId
              AND @Discount<=Quantity*PublicUnitPrice-PromotionDiscountAmount;
            """,
            [P("@Discount", discount), P("@DraftId", draftId), P("@LineId", lineId)],
            cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "El descuento supera el valor de la línea o la línea no existe.");
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> UpdateLinesAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        IReadOnlyList<UpdateSalesDraftLineRequest> lines,
        bool includesProratedDiscount,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "UpdateLines";
        var payload = string.Join('|', lines
            .OrderBy(line => line.LineId)
            .Select(line => $"{line.LineId:D}:{line.Description.Trim()}:{Invariant(line.PublicUnitPrice)}:{Invariant(line.Discount)}:{Invariant(line.DocumentUnitCost)}"));
        var hash = Hash($"{operation}|{draftId:D}|{includesProratedDiscount}|{payload}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var currentDraft = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        var activeLines = await ReadLineProductsAsync(
            connection, transaction, draftId, cancellationToken);
        if (lines.Count != activeLines.Count ||
            activeLines.Any(current => lines.All(line => line.LineId != current.LineId)))
            throw new OnlineSalesDraftValidationException(
                "Debes enviar exactamente todas las líneas de la venta activa.");
        var currentDraftLines = currentDraft.Lines.ToDictionary(line => line.LineId);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesChangeDescription) &&
            lines.Any(line => !string.Equals(
                line.Description.Trim(),
                currentDraftLines[line.LineId].Description,
                StringComparison.Ordinal)))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesChangeDescription}' is required.");
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesChangePrice) &&
            lines.Any(line =>
                !currentDraftLines[line.LineId].AllowsDocumentCostOverride &&
                (line.PublicUnitPrice != currentDraftLines[line.LineId].PublicUnitPrice ||
                 line.Discount != currentDraftLines[line.LineId].Discount ||
                 line.DocumentUnitCost != currentDraftLines[line.LineId].DocumentUnitCost)))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesChangePrice}' is required.");
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesReadCostAndMargin) &&
            lines.Any(line =>
                !currentDraftLines[line.LineId].AllowsDocumentCostOverride &&
                line.DocumentUnitCost != currentDraftLines[line.LineId].DocumentUnitCost))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesReadCostAndMargin}' is required.");
        if (includesProratedDiscount &&
            !user.Permissions.Contains(CommercePermissionCodes.SalesProratedDiscount))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesProratedDiscount}' is required.");

        var activeByLine = activeLines.ToDictionary(line => line.LineId);
        var updates = lines.Select(line =>
        {
            var current = activeByLine[line.LineId];
            var currentDraftLine = currentDraftLines[line.LineId];
            var evaluation = SaleLineMonetaryPolicy.EvaluateDocumentUpdate(
                new(current.Quantity, currentDraftLine.PublicUnitPrice,
                    currentDraftLine.PromotionDiscount, current.DocumentUnitCost,
                    currentDraftLine.TaxRate, currentDraftLine.AllowsDocumentCostOverride),
                new(line.Description, line.PublicUnitPrice, line.Discount,
                    line.DocumentUnitCost));
            if (!evaluation.IsValid)
                throw new OnlineSalesDraftValidationException(evaluation.Failure!.Message);
            var value = evaluation.Update!;
            return new
            {
                line.LineId,
                value.Description,
                value.PublicUnitPrice,
                UnitPrice = value.UntaxedUnitPrice,
                value.PublicLineTotal,
                value.DocumentUnitCost,
                value.Discount,
                value.PriceChanged
            };
        }).ToArray();
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE target
            SET Description=input.Description,BaseUnitPrice=input.UnitPrice,UnitPrice=input.UnitPrice,
                PublicUnitPrice=input.PublicUnitPrice,PublicLineTotal=input.PublicLineTotal,
                DocumentUnitCost=input.DocumentUnitCost,DiscountAmount=input.Discount,
                PriceSource=CASE WHEN input.PriceChanged=1 THEN N'Manual' ELSE target.PriceSource END,
                PriceChannelId=CASE WHEN input.PriceChanged=1 THEN NULL ELSE target.PriceChannelId END
            FROM dbo.SalesDraftLines target
            JOIN OPENJSON(@UpdatesJson) WITH(
              LineId uniqueidentifier '$.LineId',Description nvarchar(500) '$.Description',
              UnitPrice decimal(18,2) '$.UnitPrice',PublicUnitPrice decimal(18,2) '$.PublicUnitPrice',
              DocumentUnitCost decimal(19,6) '$.DocumentUnitCost',
              PublicLineTotal decimal(18,2) '$.PublicLineTotal',
              Discount decimal(19,4) '$.Discount',
              PriceChanged bit '$.PriceChanged') input
              ON input.LineId=target.SalesDraftLineId
            WHERE target.SalesDraftId=@DraftId;
            """,
            [P("@UpdatesJson", JsonSerializer.Serialize(updates)), P("@DraftId", draftId)],
            cancellationToken);
        if (affected != lines.Count)
            throw new OnlineSalesDraftValidationException(
                "Una línea ya no pertenece a la venta activa.");

        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> RemoveLineAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "RemoveLine";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        await ReadLineProductAsync(
            connection, transaction, draftId, lineId, cancellationToken);
        var affected = await ExecuteAsync(connection, transaction, """
            DELETE dbo.SalesDraftLines
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [P("@DraftId", draftId), P("@LineId", lineId)], cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");
        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid? customerId,
        Guid? partySiteId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (customerId.HasValue != partySiteId.HasValue)
            throw new OnlineSalesDraftValidationException(
                "Cliente y sede deben seleccionarse juntos.");
        const string operation = "SelectCustomer";
        var hash = Hash($"{operation}|{draftId:D}|{customerId?.ToString("D") ?? "Final"}|{partySiteId?.ToString("D") ?? "None"}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            var replayedCustomer = await ReadCustomerAsync(
                connection, transaction, state.BusinessId,
                replay.CustomerId, replay.CustomerPartySiteId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(replay, replayedCustomer);
        }
        DemandActiveVersion(state, expectedVersion);
        var customer = await ReadCustomerAsync(
            connection, transaction, state.BusinessId,
            customerId, partySiteId, cancellationToken);
        if (customerId is not null && customer is null)
            throw new OnlineSalesDraftValidationException(
                "El cliente no está disponible para este negocio.");

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts SET CustomerId=@CustomerId,CustomerPartySiteId=@PartySiteId
            WHERE SalesDraftId=@DraftId;
            """,
            [P("@CustomerId", customerId), P("@PartySiteId", customer?.PartySiteId), P("@DraftId", draftId)], cancellationToken);
        await RepriceDraftAsync(
            connection, transaction, state, draftId, customerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(result, customer);
    }

    public async Task<OnlineSalesDraft> ResetAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        long expectedVersion,
        bool cancelSourceOrder,
        string idempotencyKey,
        CancellationToken cancellationToken)
        => await ResetCoreAsync(
            user, draftId, expectedVersion, idempotencyKey,
            completedOrderId: null, cancelSourceOrder, cancellationToken);

    public async Task<OnlineSalesDraft> ResetAfterOrderAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid orderId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
        => await ResetCoreAsync(
            user, draftId, expectedVersion, idempotencyKey,
            orderId, cancelSourceOrder: false, cancellationToken);

    private async Task<OnlineSalesDraft> ResetCoreAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        long expectedVersion,
        string idempotencyKey,
        Guid? completedOrderId,
        bool cancelSourceOrder,
        CancellationToken cancellationToken)
    {
        var operation = completedOrderId.HasValue
            ? "ResetAfterOrder"
            : cancelSourceOrder
                ? "ResetAndCancelOrder"
                : "ResetAfterFailedOrder";
        var hash = Hash(
            $"{operation}|{draftId:D}|{expectedVersion}|{completedOrderId:D}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            _ = cancelSourceOrder && state.SourceOrderId is Guid replaySourceOrderId
                ? await CancelOrderCoreAsync(
                    connection,
                    transaction,
                    new OrderActor(
                        user.UserId,
                        user.TenantId,
                        state.BusinessId,
                        state.WorkSessionId,
                        null,
                        user.Permissions),
                    replaySourceOrderId,
                    state.WorkSessionId,
                    "Venta reiniciada desde el punto de venta.",
                    idempotencyKey,
                    cancellationToken)
                : null;
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        StoredOrderCancellation? cancellation = null;
        if (state.SourceOrderId is Guid sourceOrderId && !completedOrderId.HasValue)
        {
            if (cancelSourceOrder)
            {
                cancellation = await CancelOrderCoreAsync(
                    connection,
                    transaction,
                    new OrderActor(
                        user.UserId,
                        user.TenantId,
                        state.BusinessId,
                        state.WorkSessionId,
                        null,
                        user.Permissions),
                    sourceOrderId,
                    state.WorkSessionId,
                    "Venta reiniciada desde el punto de venta.",
                    idempotencyKey,
                    cancellationToken);
            }
        }
        if (completedOrderId.HasValue)
        {
            await using var proof = connection.CreateCommand();
            proof.Transaction = transaction;
            proof.CommandText = """
                SELECT COUNT_BIG(*)
                FROM dbo.Orders completedOrder
                JOIN dbo.SalesDrafts draft ON draft.SalesDraftId=@DraftId
                WHERE completedOrder.OrderId=@OrderId
                  AND completedOrder.BusinessId=draft.BusinessId
                  AND completedOrder.CustomerId=draft.CustomerId
                  AND (
                    draft.SourceOrderId=completedOrder.OrderId
                    OR (
                      draft.SourceOrderId IS NULL
                      AND completedOrder.CapturedByUserId=@UserId
                      AND LOWER(completedOrder.IdempotencyKey)=LOWER(
                        CONCAT(N'pos-order-',CONVERT(nvarchar(36),@DraftId),N'-',@ExpectedVersion))
                    )
                  );
                """;
            proof.Parameters.AddRange([
                P("@DraftId", draftId), P("@OrderId", completedOrderId.Value),
                P("@UserId", user.UserId), P("@ExpectedVersion", expectedVersion)
            ]);
            if (Convert.ToInt64(await proof.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new OnlineSalesDraftValidationException(
                    "La venta solo se puede limpiar automáticamente después de guardar su pedido.");
        }
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            UPDATE claim
            SET ReleasedAt=@Now
            FROM dbo.OrderClaims claim
            JOIN dbo.SalesDrafts draft ON draft.SourceOrderId=claim.OrderId
            WHERE draft.SalesDraftId=@DraftId AND claim.ReleasedAt IS NULL;

            UPDATE dbo.SalesDrafts
            SET Status=N'Deleted',
                SourceOrderId=CASE WHEN @PreserveSourceOrder=1 THEN SourceOrderId ELSE NULL END,
                DeletedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@DraftId AND Version=@ExpectedVersion;
            """,
            [P("@Now", now), P("@DraftId", draftId), P("@ExpectedVersion", expectedVersion),
             P("@PreserveSourceOrder", cancelSourceOrder)],
            cancellationToken);
        var nextId = ids.NewId();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@NextId", nextId), P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId), P("@WorkSessionId", state.WorkSessionId), P("@UserId", user.UserId),
                P("@Now", now)
            ],
            cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, nextId,
            idempotencyKey, operation, hash, 1, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, nextId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<OnlineSalesDraft?> ReplayAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string key,
        string operation,
        string hash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId,Operation,RequestHash
            FROM dbo.SalesDraftMutationReceipts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key;
            """;
        command.Parameters.AddRange([P("@BusinessId", businessId), P("@Key", key)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var resultDraftId = reader.GetGuid(0);
        var storedOperation = reader.GetString(1);
        var storedHash = reader.GetString(2);
        await reader.DisposeAsync();
        if (!string.Equals(operation, storedOperation, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(storedHash)))
            throw new OnlineSalesDraftIdempotencyException(
                "La clave idempotente ya fue usada por otra mutación.");
        return await ReadDraftAsync(connection, transaction, resultDraftId, ct);
    }

    private async Task SaveReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid resultDraftId,
        string key,
        string operation,
        string hash,
        long version,
        CancellationToken ct) =>
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDraftMutationReceipts(
              SalesDraftMutationReceiptId,BusinessId,SalesDraftId,IdempotencyKey,
              Operation,RequestHash,ResultVersion,CreatedAt)
            VALUES(@Id,@BusinessId,@DraftId,@Key,@Operation,@Hash,@Version,@Now);
            """,
            [
                P("@Id", ids.NewId()), P("@BusinessId", businessId),
                P("@DraftId", resultDraftId), P("@Key", key),
                P("@Operation", operation), P("@Hash", hash),
                P("@Version", version), P("@Now", time.GetUtcNow())
            ],
            ct);

    private static async Task<DraftState> LockDraftAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.BusinessId,d.WarehouseId,d.WorkSessionId,d.Version,d.Status,
                   d.CustomerId,w.AllowNegativeStockSales,d.SourceOrderId,d.CustomerPartySiteId
            FROM dbo.SalesDrafts d WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
            JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
            WHERE d.SalesDraftId=@DraftId AND d.UserId=@UserId
              AND b.TenantId=@TenantId;
            """;
        command.Parameters.AddRange([
            P("@DraftId", draftId), P("@UserId", user.UserId),
            P("@TenantId", user.TenantId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "El borrador no pertenece al usuario autenticado.");
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetInt64(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetBoolean(6),reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8));
    }

    private static void DemandActiveVersion(DraftState state, long expectedVersion)
    {
        if (!string.Equals(state.Status, "Active", StringComparison.Ordinal))
            throw new OnlineSalesDraftValidationException(
                "El borrador ya no está activo.");
        if (state.Version != expectedVersion)
            throw new OnlineSalesDraftConcurrencyException(
                $"El borrador cambió. Versión actual: {state.Version}.");
    }

    private async Task<long> AdvanceVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        long expectedVersion,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.SalesDrafts
            SET Version=Version+1,UpdatedAt=@Now
            OUTPUT inserted.Version
            WHERE SalesDraftId=@DraftId AND Version=@ExpectedVersion AND Status=N'Active';
            """;
        command.Parameters.AddRange([
            P("@Now", now), P("@DraftId", draftId), P("@ExpectedVersion", expectedVersion)
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is long version
            ? version
            : throw new OnlineSalesDraftConcurrencyException(
                "El borrador fue modificado por otra ventana.");
    }

    private static async Task<ProductSnapshot> ReadProductAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid warehouseId,
        Guid productId,
        CancellationToken ct)
    {
        var products = await ReadProductsAsync(
            connection, transaction, businessId, warehouseId, [productId], ct);
        return products.TryGetValue(productId, out var product)
            ? product
            : throw new OnlineSalesDraftValidationException(
                "El producto no está disponible para este negocio.");
    }

    private static async Task<IReadOnlyDictionary<Guid, ProductSnapshot>> ReadProductsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid warehouseId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct)
    {
        var requested = productIds.Where(value => value != Guid.Empty).Distinct().ToArray();
        if (requested.Length == 0)
            return new Dictionary<Guid, ProductSnapshot>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ;WITH RequestedProducts AS
            (
              SELECT TRY_CONVERT(uniqueidentifier,[value]) ProductId
              FROM OPENJSON(@ProductIdsJson)
              WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
            ),
            ProductCategoryAncestors AS
            (
              SELECT scopedProduct.ProductId RootProductId,
                     category.ProductCategoryId,category.ParentProductCategoryId
              FROM dbo.Products scopedProduct
              JOIN RequestedProducts requested ON requested.ProductId=scopedProduct.ProductId
              JOIN dbo.ProductCategories category ON category.ProductCategoryId=scopedProduct.ProductCategoryId
              WHERE category.BusinessId=@BusinessId
              UNION ALL
              SELECT child.RootProductId,parent.ProductCategoryId,parent.ParentProductCategoryId
              FROM dbo.ProductCategories parent
              JOIN ProductCategoryAncestors child ON child.ParentProductCategoryId=parent.ProductCategoryId
              WHERE parent.BusinessId=@BusinessId
            )
            SELECT p.ProductId,
                   COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
                   p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
                   COALESCE(t.DianTaxCode,N'01'),COALESCE(t.Rate,0),
                   price.Amount,
                   price.CurrencyCode,p.AllowsFractionalSale,
                    COALESCE(price.CostBasisAmount,0),
                    CAST(CASE WHEN p.ManageStock=1 OR EXISTS(
                      SELECT 1 FROM dbo.ProductLinks inventoryLink
                      WHERE inventoryLink.BusinessId=@BusinessId
                        AND inventoryLink.ChildProductId=p.ProductId
                        AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1)
                      THEN 1 ELSE 0 END AS bit),
                    p.CategoryName,p.ProductCategoryId,p.ProductBrandId,
                    COALESCE((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),ancestor.ProductCategoryId),N',')
                              FROM ProductCategoryAncestors ancestor
                              WHERE ancestor.RootProductId=p.ProductId),N''),
                    latestCost.Amount,
                    COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent),
                    p.IsGenericProduct
            FROM dbo.Products p
            JOIN RequestedProducts requested ON requested.ProductId=p.ProductId
            LEFT JOIN dbo.TaxProfiles t
              ON t.TaxProfileId=p.TaxProfileId AND t.IsActive=1
            CROSS APPLY (
              SELECT TOP(1) pp.Amount,pp.CurrencyCode,pp.CostBasisAmount,
                     pp.TargetMarginPercent,pp.EffectiveMarginPercent
              FROM dbo.ProductPrices pp
              WHERE pp.BusinessId=@BusinessId AND pp.ProductId=p.ProductId
                AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET()
                AND (pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY pp.ValidFrom DESC,pp.ProductPriceId
            ) price
            LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
              AND balance.ProductId=p.ProductId AND balance.WarehouseId=@WarehouseId
            OUTER APPLY
            (
              SELECT COALESCE(
                (SELECT TOP (1) latest.LatestUnitCost
                 FROM dbo.SupplierProductLatestCosts latest
                 WHERE latest.BusinessId=@BusinessId AND latest.ProductId=p.ProductId
                 ORDER BY latest.ObservedAt DESC,latest.SupplierId),
                price.CostBasisAmount,NULLIF(balance.AverageUnitCost,0),0) Amount
            ) latestCost
            WHERE p.IsActive=1
              AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@WarehouseId", warehouseId),
            P("@ProductIdsJson", System.Text.Json.JsonSerializer.Serialize(requested))
        ]);
        var products = new Dictionary<Guid, ProductSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            products.Add(reader.GetGuid(0), new(
                reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetString(7), reader.GetBoolean(8),
                reader.GetDecimal(9), reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.GetString(14).Split(',',StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray(),
                reader.GetDecimal(15),reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                reader.GetBoolean(17)));
        return products;
    }

    private static async Task<ResolvedSalesExecutionContext> ResolveOnlineContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext requested,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.BusinessId,w.WarehouseId,s.WorkSessionId,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.Warehouses w
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=b.BusinessId
             AND w.IsActive=1 AND w.UseForSales=1
            JOIN dbo.WorkSessions s
             ON s.WorkSessionId=@WorkSessionId
             AND s.BusinessId=b.BusinessId
             AND s.TenantId=@TenantId
             AND s.UserId=@UserId
             AND (@DeviceId IS NULL OR s.DeviceId=@DeviceId)
             AND s.Status=N'Open'
            WHERE b.TenantId=@TenantId
              AND b.BusinessId=@BusinessId
              AND b.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId),
            P("@BusinessId", requested.BusinessId),
            P("@WarehouseId", requested.WarehouseId),
            P("@WorkSessionId", requested.WorkSessionId),
            P("@UserId", user.UserId),
            P("@DeviceId", user.DeviceId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "La sesión de trabajo no pertenece al usuario y negocio autenticados.");
        return new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetBoolean(3));
    }

    private static async Task<Guid?> FindActiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid workSessionId,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId FROM dbo.SalesDrafts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WorkSessionId=@WorkSessionId
              AND UserId=@UserId AND Status=N'Active';
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@WorkSessionId", workSessionId), P("@UserId", userId)
        ]);
        return await command.ExecuteScalarAsync(ct) is Guid value ? value : null;
    }

    private static async Task<DraftLineMatch?> FindProductLineAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid productId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,Quantity FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND ProductId=@ProductId;
            """;
        command.Parameters.AddRange([P("@DraftId", draftId), P("@ProductId", productId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetDecimal(1)) : null;
    }

    private static async Task<Guid> ResolveProductIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string selector,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP(2) p.ProductId,
              CASE
                WHEN p.ProductId=TRY_CONVERT(uniqueidentifier,@Selector) THEN 0
                WHEN EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=@BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Selector) THEN 1
                WHEN p.ProductCode=@Selector THEN 2
                WHEN p.Sku=@Selector THEN 3
                WHEN p.Reference=@Selector THEN 4
                WHEN EXISTS (
                  SELECT 1 FROM dbo.ProductIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.BusinessId=@BusinessId
                    AND i.IsActive=1 AND i.Value=@Selector) THEN 5
                ELSE 6
              END AS MatchRank
            FROM dbo.Products p
            WHERE (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId)) AND p.IsActive=1
              AND (
                p.ProductId=TRY_CONVERT(uniqueidentifier,@Selector) OR
                p.ProductCode=@Selector OR p.Sku=@Selector OR
                p.Reference=@Selector OR p.Name=@Selector OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=@BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Selector) OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.BusinessId=@BusinessId
                    AND i.IsActive=1 AND i.Value=@Selector))
            ORDER BY MatchRank,p.ProductId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@Selector", selector)
        ]);
        var matches = new List<(Guid ProductId, int Rank)>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            matches.Add((reader.GetGuid(0), reader.GetInt32(1)));
        if (matches.Count == 0)
            throw new OnlineSalesDraftValidationException(
                "No se encontró un producto vendible con ese identificador.");
        if (matches.Count > 1 && matches[0].Rank == matches[1].Rank)
            throw new OnlineSalesDraftValidationException(
                "El identificador coincide con más de un producto; selecciónalo en el buscador.");
        return matches[0].ProductId;
    }

    private static async Task<DraftLineProduct> ReadLineProductAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid lineId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode,TaxRate,DocumentUnitCost,
                   UnitPrice,PriceSource
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """;
        command.Parameters.AddRange([P("@DraftId", draftId), P("@LineId", lineId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
            reader.GetDecimal(7), reader.GetString(8));
    }

    private static async Task<IReadOnlyList<DraftLineProduct>> ReadLineProductsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode,TaxRate,DocumentUnitCost,
                   UnitPrice,PriceSource
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId ORDER BY Position,SalesDraftLineId;
            """;
        command.Parameters.Add(P("@DraftId", draftId));
        var result = new List<DraftLineProduct>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetString(8)));
        return result;
    }

    public async Task<OnlineSalesDraft> DiscardUnpricedGenericLineAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "DiscardUnpricedGenericLine";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        var affected = await ExecuteAsync(connection, transaction, """
            DELETE dbo.SalesDraftLines
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId
              AND IsGenericProductSnapshot=1 AND PublicUnitPrice=0
              AND DocumentUnitCost=0 AND DiscountAmount=0 AND PromotionDiscountAmount=0;
            """, [P("@DraftId", draftId), P("@LineId", lineId)], cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "Solo se puede descartar sin autorización un producto genérico que aún no tiene valor.");
        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<OnlineSalesCustomer?> ReadCustomerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        Guid? partySiteId,
        CancellationToken ct)
    {
        if (customerId is null) return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.CustomerId,COALESCE(p.Identification,N''),
                   COALESCE(p.DisplayName,p.LegalName,
                            CONCAT(p.FirstName,N' ',p.LastName),N'Sin nombre'),
                   CASE WHEN s.ValidFrom<=SYSDATETIMEOFFSET()
                          AND(s.ValidUntil IS NULL OR s.ValidUntil>SYSDATETIMEOFFSET())
                        THEN s.PriceChannelId END,c.RequiresElectronicInvoice,
                   CAST(COALESCE(cp.IsCreditEnabled,0) AS bit),
                   CASE WHEN cp.CreditLimit IS NULL THEN NULL
                        ELSE CASE WHEN cp.CreditLimit-COALESCE(balance.Outstanding,0)<0 THEN 0
                                  ELSE cp.CreditLimit-COALESCE(balance.Outstanding,0) END END,
                   site.PartySiteId,site.Name,site.AddressLine
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            CROSS APPLY(
                SELECT candidate.PartySiteId,candidate.Name,candidate.AddressLine
                FROM dbo.PartySites candidate
                WHERE candidate.PartyId=p.PartyId AND candidate.IsActive=1
                  AND candidate.PartySiteId=@PartySiteId
            ) site
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            LEFT JOIN dbo.CustomerCreditProfiles cp ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            OUTER APPLY(SELECT SUM(r.OutstandingAmount) Outstanding FROM dbo.Receivables r
                        WHERE r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
                          AND r.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId
              AND c.IsActive=1 AND p.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@CustomerId", customerId), P("@BusinessId", businessId), P("@PartySiteId", partySiteId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2).Trim(),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetBoolean(4), reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9))
            : null;
    }

    private static async Task DemandInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid draftId,
        Guid productId,
        Guid? excludedLineId,
        decimal selectedQuantity,
        CancellationToken ct)
    {
        if (state.WarehouseAllowsNegativeStock) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @InventoryProductId UNIQUEIDENTIFIER;
            DECLARE @SelectedFactor DECIMAL(19,6);
            DECLARE @ManagesStock BIT;
            SELECT @InventoryProductId=COALESCE(link.ParentProductId,p.ProductId),
                   @SelectedFactor=COALESCE(NULLIF(link.InventoryFactor,0),1),
                   @ManagesStock=CAST(CASE WHEN p.ManageStock=1 OR link.ProductLinkId IS NOT NULL
                                           THEN 1 ELSE 0 END AS BIT)
            FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=@BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            WHERE p.ProductId=@ProductId
              AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));

            DECLARE @Available DECIMAL(19,6)=COALESCE((
              SELECT balance.QuantityOnHand
              FROM dbo.InventoryBalances balance WITH (UPDLOCK,HOLDLOCK)
              WHERE balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
                AND balance.ProductId=@InventoryProductId),0);
            SELECT @InventoryProductId,@SelectedFactor,@ManagesStock,@Available;

            SELECT line.SalesDraftLineId,line.ProductId,line.Quantity,
                   COALESCE(lineLink.ParentProductId,line.ProductId),
                   COALESCE(NULLIF(lineLink.InventoryFactor,0),1),
                   CAST(CASE WHEN lineProduct.ManageStock=1 OR lineLink.ProductLinkId IS NOT NULL
                             THEN 1 ELSE 0 END AS BIT)
            FROM dbo.SalesDraftLines line WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Products lineProduct ON lineProduct.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks lineLink
              ON lineLink.BusinessId=@BusinessId AND lineLink.ChildProductId=line.ProductId
             AND lineLink.SharesInventory=1 AND lineLink.IsActive=1
            WHERE line.SalesDraftId=@DraftId
              AND COALESCE(lineLink.ParentProductId,line.ProductId)=@InventoryProductId
              AND (@ExcludedLineId IS NULL OR line.SalesDraftLineId<>@ExcludedLineId);
            """;
        command.Parameters.AddRange([
            P("@BusinessId", state.BusinessId), P("@WarehouseId", state.WarehouseId),
            P("@DraftId", draftId), P("@ProductId", productId),
            P("@ExcludedLineId", excludedLineId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El producto ya no está disponible.");
        var inventoryProductId = reader.GetGuid(0);
        var selectedFactor = reader.GetDecimal(1);
        var managesStock = reader.GetBoolean(2);
        var available = reader.GetDecimal(3);
        var demandLines = new List<InventoryDemandLine>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            demandLines.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(3),
                reader.GetDecimal(4), reader.GetDecimal(2), reader.GetBoolean(5)));
        demandLines.Add(new(
            Guid.Empty, productId, inventoryProductId, selectedFactor,
            selectedQuantity, managesStock));
        var demand = InventoryDemandResolver.Resolve(demandLines).SingleOrDefault();
        if (demand is not null && available < demand.RequiredInventoryQuantity)
            throw new OnlineSalesDraftValidationException(
                $"Inventario insuficiente. Disponible: {Invariant(InventoryDemandResolver.InProductUnits(available, selectedFactor))}.");
    }

    private static async Task<OnlineSalesDraft> ReadDraftAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        var drafts = await ReadDraftsAsync(connection, transaction, [draftId], ct);
        if (!drafts.TryGetValue(draftId, out var draft))
            throw new OnlineSalesDraftValidationException("El borrador no existe.");
        return draft;
    }

    private static async Task<IReadOnlyDictionary<Guid, OnlineSalesDraft>> ReadDraftsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyCollection<Guid> draftIds,
        CancellationToken ct)
    {
        if (draftIds.Count == 0)
            return new Dictionary<Guid, OnlineSalesDraft>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT draft.SalesDraftId,draft.BusinessId,draft.WarehouseId,draft.WorkSessionId,draft.UserId,
                   draft.CustomerId,draft.SellerId,draft.Status,draft.Name,draft.Reference,draft.Observation,
                   draft.Version,draft.UpdatedAt,draft.SourceOrderId,draft.CustomerPartySiteId
            FROM dbo.SalesDrafts draft
            JOIN OPENJSON(@DraftIdsJson) WITH(DraftId uniqueidentifier '$') input
              ON input.DraftId=draft.SalesDraftId;

            SELECT line.SalesDraftId,line.SalesDraftLineId,line.ProductId,line.ProductCode,line.Description,line.UnitCode,
                   line.TaxCode,line.TaxRate,line.Quantity,line.BaseUnitPrice,line.UnitPrice,line.CurrencyCode,
                   line.PriceSource,line.DiscountAmount,line.DocumentUnitCost,
                   line.IsGenericProductSnapshot,
                   product.AllowsFractionalSale,
                   line.PromotionDiscountAmount,line.PublicUnitPrice,line.PublicLineTotal
            FROM dbo.SalesDraftLines line
            JOIN OPENJSON(@DraftIdsJson) WITH(DraftId uniqueidentifier '$') input
              ON input.DraftId=line.SalesDraftId
            JOIN dbo.SalesDrafts draft ON draft.SalesDraftId=line.SalesDraftId
            JOIN dbo.Products product ON product.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.BusinessId=draft.BusinessId
             AND inventoryLink.ChildProductId=line.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            ORDER BY line.SalesDraftId,line.Position,line.SalesDraftLineId;

            SELECT charge.DraftId,charge.SelectionJson
            FROM sales.InvoiceChargeDraftSelections charge
            JOIN OPENJSON(@DraftIdsJson) WITH(DraftId uniqueidentifier '$') input ON input.DraftId=charge.DraftId
            ORDER BY charge.DraftId,charge.AppliedChargeId;
            """;
        command.Parameters.Add(P("@DraftIdsJson", JsonSerializer.Serialize(draftIds)));
        var headers = new Dictionary<Guid, DraftSnapshotHeader>();
        var lines = new Dictionary<Guid, List<OnlineSalesDraftLine>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var draftId = reader.GetGuid(0);
            headers[draftId] = new(
                draftId, reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt64(11),
                reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14));
            lines[draftId] = [];
        }
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var draftId = reader.GetGuid(0);
            if (!lines.TryGetValue(draftId, out var draftLines)) continue;
            var quantity = reader.GetDecimal(8);
            var price = reader.GetDecimal(10);
            var discount = reader.GetDecimal(13);
            var promotionDiscount = reader.GetDecimal(17);
            var taxRate = reader.GetDecimal(7);
            var publicUnitPrice = reader.GetDecimal(18);
            var total = reader.GetDecimal(19);
            var net = MonetaryRounding.RoundLineAmount(TaxExclusive(total, taxRate));
            var tax = total - net;
            draftLines.Add(new(
                reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                taxRate, quantity, reader.GetDecimal(9), price,
                reader.GetString(11), reader.GetString(12), discount,
                reader.GetDecimal(14), reader.GetBoolean(15), reader.GetBoolean(16),
                net, tax, total, promotionDiscount, publicUnitPrice, total));
        }
        var selections = headers.Keys.ToDictionary(id => id, _ => new List<InvoiceChargeSelection>());
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            selections[reader.GetGuid(0)].Add(JsonSerializer.Deserialize<InvoiceChargeSelection>(reader.GetString(1))
                ?? throw new InvalidDataException("El cargo del borrador no contiene un snapshot válido."));
        return headers.ToDictionary(pair => pair.Key, pair =>
        {
            var header = pair.Value;
            var draftLines = lines[pair.Key];
            var charges = InvoiceChargeApplication.Calculate(draftLines.Sum(line => line.Total), selections[pair.Key]);
            return new OnlineSalesDraft(
                header.DraftId, header.BusinessId, header.WarehouseId, header.WorkSessionId,
                header.UserId, header.CustomerId, header.SellerId, header.Status,
                header.Name, header.Reference, header.Observation, header.Version,
                header.UpdatedAt, draftLines, draftLines.Sum(line => line.Net) + charges.Sum(x => x.InvoicedUntaxedAmount),
                draftLines.Sum(line => line.Tax) + charges.Sum(x => x.InvoicedTaxAmount),
                draftLines.Sum(line => line.Total) + charges.Sum(x => x.InvoicedAmount),
                header.SourceOrderId,header.CustomerPartySiteId,charges);
        });
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    private static string Invariant(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static decimal TaxExclusive(decimal grossPrice, decimal taxRate) =>
        decimal.Round(grossPrice / (1m + taxRate / 100m), 6,
            MidpointRounding.AwayFromZero);

    private static FiscalLineAmounts Fiscalize(OnlineSalesDraftLine line)
    {
        var amounts = SaleLineMonetaryPolicy.FromClosedPublishedAmounts(
            line.Quantity,
            line.Net,
            line.Discount,
            line.PromotionDiscount,
            line.TaxRate);
        return new(
            amounts.UnitPrice,
            amounts.DiscountAmount,
            amounts.PromotionDiscountAmount);
    }

    private static void DemandValidPreparedFiscalSnapshot(
        PosSaleUploadRequest request,
        FiscalVerificationMaterial material)
    {
        var result = FiscalSnapshotValidator.Verify(request, material);
        if (!result.IsVerified)
            throw new OnlineSalesDraftValidationException(
                $"La factura no superó la validación fiscal interna: {result.ConflictReason}");
    }

    private sealed record DraftState(
        Guid BusinessId,
        Guid WarehouseId,
        Guid WorkSessionId,
        long Version,
        string Status,
        Guid? CustomerId,
        bool WarehouseAllowsNegativeStock,
        Guid? SourceOrderId,
        Guid? CustomerPartySiteId);
    private sealed record DraftLineMatch(Guid LineId, decimal Quantity);
    private sealed record FiscalLineAmounts(
        decimal UnitPrice,
        decimal Discount,
        decimal PromotionDiscount);
    private sealed record DraftLineProduct(
        Guid LineId,
        Guid ProductId,
        decimal Quantity,
        decimal BaseUnitPrice,
        string CurrencyCode,
        decimal TaxRate,
        decimal DocumentUnitCost,
        decimal UnitPrice,
        string PriceSource);
    private sealed record ProductSnapshot(
        string Code,
        string Name,
        string UnitCode,
        string TaxCode,
        decimal TaxRate,
        decimal UnitPrice,
        string CurrencyCode,
        bool AllowsFractionalSale,
        decimal UnitCost,
        bool ManagesStock,
        string? CategoryName,
        Guid? ProductCategoryId,
        Guid? ProductBrandId,
        IReadOnlyCollection<Guid> ProductCategoryAncestorIds,
        decimal LatestUnitCost,
        decimal? TargetMarginPercent,
        bool IsGenericProduct = false);
    private sealed record DraftSnapshotHeader(
        Guid DraftId,
        Guid BusinessId,
        Guid WarehouseId,
        Guid WorkSessionId,
        Guid UserId,
        Guid? CustomerId,
        Guid? SellerId,
        string Status,
        string? Name,
        string? Reference,
        string? Observation,
        long Version,
        DateTimeOffset UpdatedAt,
        Guid? SourceOrderId,
        Guid? CustomerPartySiteId);

    private static void DemandAllowedQuantity(bool allowsFractionalSale, decimal quantity)
    {
        if (!allowsFractionalSale && quantity != decimal.Truncate(quantity))
            throw new OnlineSalesDraftValidationException(
                "Este producto solo se vende en unidades completas.");
    }
    private sealed record ResolvedSalesExecutionContext(
        Guid BusinessId,
        Guid WarehouseId,
        Guid WorkSessionId,
        bool WarehouseAllowsNegativeStockSales);
}
