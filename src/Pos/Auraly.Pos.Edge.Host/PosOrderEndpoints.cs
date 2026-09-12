using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Orders;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosOrderRecoveryService(
    PosOrderServerClient server,
    PosCatalogStore catalog,
    PosDraftStore drafts,
    PosEdgeRuntimeContext runtime)
{
    public async Task<PosDraft> RecoverAsync(
        PosLocalUserSession session,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await server.ClaimAsync(session, orderId, cancellationToken);
        try
        {
            var order = await server.GetAsync(session, orderId, cancellationToken);
            var productIds = order.Lines
                .Select(line => line.ProductId ?? throw new InvalidOperationException(
                    $"El producto '{line.ProductName}' del pedido no está vinculado al catálogo."))
                .Distinct()
                .ToArray();
            var products = await catalog.GetByProductIdsAsync(productIds, cancellationToken);
            var lines = new List<PosDraftLineInput>(order.Lines.Count);
            foreach (var orderLine in order.Lines)
            {
                if (!products.TryGetValue(orderLine.ProductId!.Value, out var product))
                    throw new InvalidOperationException(
                        $"El producto '{orderLine.ProductName}' no está disponible en el catálogo local.");
                lines.Add(new PosDraftLineInput(
                    new ProductId(product.ProductId),
                    product.ProductCode,
                    product.Name,
                    product.BaseUnitCode,
                    product.TaxCode,
                    product.TaxRate,
                    orderLine.Quantity,
                    orderLine.UnitPrice,
                    orderLine.UnitPrice,
                    order.Currency,
                    string.IsNullOrWhiteSpace(orderLine.PriceSource)
                        ? "Order"
                        : orderLine.PriceSource,
                    Discount: orderLine.DiscountAmount,
                    Note: $"Pedido {order.OrderNumber}",
                    AllowsFractionalSale: product.AllowsFractionalSale,
                    DocumentUnitCost: product.UnitCost,
                    AllowsDocumentCostOverride: !product.ManagesStock));
            }

            return await drafts.ImportOrderAsync(
                runtime.ScopeFor(session),
                order.OrderId,
                order.CustomerId,
                lines,
                cancellationToken);
        }
        catch
        {
            try
            {
                await server.ReleaseAsync(session, orderId, CancellationToken.None);
            }
            catch (Exception releaseError)
                when (releaseError is HttpRequestException or PosOrderServerException)
            {
                // The server lease expires durably; never hide the original recovery error.
            }
            throw;
        }
    }
}

public static class PosOrderEndpoints
{
    public static RouteGroupBuilder MapPosOrders(this RouteGroupBuilder edge)
    {
        edge.MapGet("/orders", async (
            HttpRequest request,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var query = request.QueryString.HasValue
                ? request.QueryString.Value![1..]
                : "page=1&pageSize=50";
            return Results.Ok(await server.PageAsync(
                sessions.Required(), query, ct));
        });

        edge.MapGet("/orders/{orderId:guid}", async (
            Guid orderId,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await server.GetAsync(
                sessions.Required(), orderId, ct)));

        edge.MapPost("/orders/{orderId:guid}/recover", async (
            Guid orderId,
            PosOrderRecoveryService recovery,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await recovery.RecoverAsync(
                sessions.Required(), orderId, ct)));

        edge.MapPost("/orders/{orderId:guid}/claim", async (
            Guid orderId,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await server.ClaimAsync(sessions.Required(), orderId, ct)));

        edge.MapPost("/orders/{orderId:guid}/claim/release", async (
            Guid orderId,
            PosOrderServerClient server,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            await server.ReleaseAsync(sessions.Required(), orderId, ct);
            return Results.Ok(new { released = true });
        });

        edge.MapPost("/orders/print", async (
            PrintPosOrdersRequest request,
            PosOrderServerClient server,
            ConfigurablePosReceiptPrinter printer,
            PosPrinterConfigurationStore printerSettings,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var documents = await server.PrintBatchAsync(
                sessions.Required(), request.OrderIds, ct);
            var width = printerSettings.Load().OrderReceiptPaperWidthMillimeters;
            var receipts = documents.Select(document => new PosReceipt(
                Guid.NewGuid(),
                new DocumentId(document.OrderId),
                document.OrderNumber,
                null,
                document.CreatedAt,
                document.CustomerIdentification ?? string.Empty,
                document.Lines.Select(line => new PosReceiptLine(
                    line.ProductCode ?? string.Empty,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    0,
                    line.LineTotal)).ToArray(),
                [],
                document.Total,
                0,
                document.Total,
                null,
                null,
                width,
                "Order",
                CustomerName: document.CustomerName ?? "Cliente")).ToArray();
            await printer.PrintOrdersAsync(receipts, ct);
            return Results.Ok(new { printedCount = receipts.Length });
        });

        edge.MapPost("/orders/invoice", async (
            InvoicePosOrdersRequest request,
            PosOrderServerClient server,
            ConfigurablePosReceiptPrinter receiptPrinter,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var session = sessions.Required();
            var response = await server.InvoiceAsync(
                session,
                request.OrderIds,
                request.PaymentMethodCode,
                request.PaymentReference,
                request.BankAccountId,
                request.PaymentNotes,
                request.DocumentType,
                request.IdempotencyKey,
                ct);
            try
            {
                var receipts = response.Results
                    .Where(result => result.Error is null && result.Receipt is not null)
                    .Select(result => result.Receipt!)
                    .ToArray();
                await receiptPrinter.PrintSalesDocumentsAsync(receipts, ct);
                return Results.Ok(response with
                {
                    PrintStatus = receipts.Length == 0 ? "NotRequired" : "Sent"
                });
            }
            catch (Exception error) when (error is IOException or
                                          InvalidOperationException or
                                          PlatformNotSupportedException or
                                          System.ComponentModel.Win32Exception)
            {
                return Results.Ok(response with
                {
                    PrintStatus = "Failed",
                    PrintError = "Los pedidos se facturaron, pero no fue posible imprimir: " +
                                 (string.IsNullOrWhiteSpace(error.Message)
                                     ? "la impresora configurada no respondió. Revisa la impresora de Facturas en Periféricos."
                                     : error.Message)
                });
            }
        });

        return edge;
    }
}

public sealed record InvoicePosOrdersRequest(
    IReadOnlyCollection<Guid> OrderIds,
    string PaymentMethodCode,
    string? PaymentReference,
    Guid? BankAccountId,
    string? PaymentNotes,
    string IdempotencyKey,
    string DocumentType = "SalesInvoice");

public sealed record PrintPosOrdersRequest(IReadOnlyCollection<Guid> OrderIds);
