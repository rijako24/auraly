using System.Globalization;
using Auraly.Platform.Application.Commerce;

namespace Auraly.Platform.Infrastructure.Commerce;

internal sealed class MantisOrderHistoryResponse
{
    public string? ErrorKey { get; init; }
    public List<MantisOrderHistoryDto> SDTConsultarPedidoCasalins { get; init; } = [];
}

internal sealed class MantisOrderHistoryDto
{
    public string? FechaPedido { get; init; }
    public string? IdentificacionCliente { get; init; }
    public string? NombreCliente { get; init; }
    public string? NumeroPedido { get; init; }
    public List<MantisOrderHistoryItemDto> SDTConsultarPedidoCasalinsDetalle { get; init; } = [];
}

internal sealed class MantisOrderHistoryItemDto
{
    public string? CodigoProducto { get; init; }
    public string? NombrePresentacion { get; init; }
    public string? NombreProducto { get; init; }
    public string? Precio { get; init; }
    public string? Unidades { get; init; }
}

internal static class MantisOrderHistoryMapper
{
    public static CommerceOrderHistoryRecord? ToRecord(MantisOrderHistoryDto source)
    {
        var orderId = Clean(source.NumeroPedido);
        var customerId = Clean(source.IdentificacionCliente);
        if (orderId is null || customerId is null)
            return null;

        var items = (source.SDTConsultarPedidoCasalinsDetalle ?? [])
            .Select(ToItem)
            .OfType<CommerceOrderHistoryItem>()
            .ToList();
        return new CommerceOrderHistoryRecord(
            orderId,
            customerId,
            Clean(source.NombreCliente),
            ParseDate(source.FechaPedido),
            items);
    }

    private static CommerceOrderHistoryItem? ToItem(MantisOrderHistoryItemDto source)
    {
        var productId = Clean(source.CodigoProducto);
        var productName = Clean(source.NombreProducto);
        if (productId is null || productName is null)
            return null;

        return new CommerceOrderHistoryItem(
            productId,
            productName,
            Clean(source.NombrePresentacion),
            ParseDecimal(source.Unidades) ?? 0m,
            ParseDecimal(source.Precio) ?? 0m);
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            Clean(value),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(
            Clean(value),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
