using System.Globalization;
using System.Text.Json;
using Auraly.Platform.Application.Commerce;

namespace Auraly.Platform.Infrastructure.Commerce;

internal sealed class XionCustomerDto
{
    public int? ClienteId { get; init; }
    public string? NoIdentificacion { get; init; }
    public string? NombreCompleto { get; init; }
    public string? Telefono { get; init; }
}

internal sealed class XionProductSummaryDto
{
    public int IdProducto { get; init; }
    public string? DescripcionLarga { get; init; }
    public decimal Existencias { get; init; }
    public decimal PrecioPublico1 { get; init; }
    public string? NombreProveedor { get; init; }
}

internal sealed class XionProductDto
{
    public int IdProducto { get; init; }
    public int? IdFamilia1 { get; init; }
    public int? IdFamilia2 { get; init; }
    public int? IdFamilia3 { get; init; }
    public int? IdFamilia4 { get; init; }
    public int? IdFamilia5 { get; init; }
    public int? ProveedorId { get; init; }
    public int? MarcaId { get; init; }
    public int? CasaComercialId { get; init; }
    public string? DescripcionLarga { get; init; }
    public string? DescripcionCorta { get; init; }
    public decimal Existencias { get; init; }
    public decimal PrecioCosto { get; init; }
    public decimal PrecioCostoPromedio { get; init; }
    public decimal PrecioPublico1 { get; init; }
    public decimal PrecioPublicoReal { get; init; }
    public decimal Embalaje { get; init; }
    public decimal ImpoConsumo { get; init; }
    public int? IvaCompraId { get; init; }
    public int? IvaVentaId { get; init; }
    public decimal Dc1 { get; init; }
    public decimal Dc2 { get; init; }
    public decimal Dc3 { get; init; }
    public decimal Dc4 { get; init; }
    public decimal Dc5 { get; init; }
    public decimal Df1 { get; init; }
    public decimal Df2 { get; init; }
    public decimal Df3 { get; init; }
    public decimal Df4 { get; init; }
    public decimal Df5 { get; init; }
    public bool EsCombo { get; init; }
    public bool VenderXPeso { get; init; }
    public bool VenderXFraccion { get; init; }
    public bool NoManejaInventario { get; init; }
    public bool TieneLote { get; init; }
    public bool TieneSerial { get; init; }
    public bool EsServicio { get; init; }
    public bool EsProduccion { get; init; }
    public bool EsConcesion { get; init; }
    public bool EsObsequio { get; init; }
    public bool PerteneceAsociacion { get; init; }
    public bool ProductoWeb { get; init; }
    public bool EsBolsa { get; init; }
    public bool EsAlterno { get; init; }
    public bool EsAncheta { get; init; }
    public bool Interno { get; init; }
    public string? NombreProveedor { get; init; }
    public XionSaleInfoDto? InformacionVenta { get; init; }
}

internal sealed class XionSaleInfoDto
{
    public decimal ImpoConsumo { get; init; }
    public decimal MargenProducto { get; init; }
    public decimal MargenLiquidacion { get; init; }
    public decimal MargenVenta { get; init; }
    public decimal DescuentoProducto { get; init; }
    public decimal PrecioCosto { get; init; }
    public decimal PrecioCostoPromedio { get; init; }
    public decimal PrecioPublicoReal { get; init; }
    public decimal PrecioVenta { get; init; }
    public decimal CantidadDpc { get; init; }
    public int CanalId { get; init; }
    public int ListaId { get; init; }
    public int EventoId { get; init; }
    public int IvaVentaId { get; init; }
    public decimal IvaVentaValor { get; init; }
    public string? IvaVenta { get; init; }
    public int IvaCompraId { get; init; }
    public decimal IvaCompraValor { get; init; }
    public string? IvaCompra { get; init; }
    public bool AplicaPuntos { get; init; }
}

internal sealed class XionUnavailableProductDto
{
    public int ProductoId { get; init; }
    public string? Descripcion { get; init; }
}

internal sealed class XionOrderDto
{
    public string? PedidoId { get; init; }
    public int? IdCliente { get; init; }
    public string? NombreCliente { get; init; }
    public DateTime FechaPedido { get; init; }
    public List<XionOrderItemDto> PedidoDetalle { get; init; } = [];
}

internal sealed class XionOrderItemDto
{
    public int IdProducto { get; init; }
    public string? Descripcion { get; init; }
    public string? DescripcionCorta { get; init; }
    public decimal Cantidad { get; init; }
    public decimal Precio { get; init; }
}

internal static class XionMapper
{
    public static ProductReference? ToProductReference(XionProductDto product, string currency)
    {
        var name = Clean(product.DescripcionLarga) ?? Clean(product.DescripcionCorta);
        if (product.IdProducto <= 0 || name is null)
            return null;
        var price = product.InformacionVenta?.PrecioVenta > 0
            ? product.InformacionVenta.PrecioVenta
            : product.PrecioPublico1;
        return new ProductReference(
            null,
            product.IdProducto.ToString(CultureInfo.InvariantCulture),
            product.IdProducto.ToString(CultureInfo.InvariantCulture),
            name,
            Clean(product.DescripcionCorta),
            null,
            price,
            currency,
            product.NoManejaInventario ? null : product.Existencias,
            RawPayloadJson: JsonSerializer.Serialize(product, CommerceJson.Options),
            ExternalCategoryId: product.IdFamilia1 > 0 ? product.IdFamilia1.Value.ToString(CultureInfo.InvariantCulture) : null);
    }

    public static CommerceOrderHistoryRecord? ToOrderHistory(XionOrderDto order)
    {
        var orderId = Clean(order.PedidoId);
        if (orderId is null || order.IdCliente is null or <= 0)
            return null;
        var items = order.PedidoDetalle
            .Where(item => item.IdProducto > 0)
            .Select(item => new CommerceOrderHistoryItem(
                item.IdProducto.ToString(CultureInfo.InvariantCulture),
                Clean(item.Descripcion) ?? Clean(item.DescripcionCorta) ?? item.IdProducto.ToString(CultureInfo.InvariantCulture),
                Clean(item.DescripcionCorta), item.Cantidad, item.Precio))
            .ToList();
        return new CommerceOrderHistoryRecord(
            orderId,
            order.IdCliente.Value.ToString(CultureInfo.InvariantCulture),
            Clean(order.NombreCliente),
            DateOnly.FromDateTime(order.FechaPedido),
            items);
    }

    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0057", StringComparison.Ordinal))
            digits = digits[4..];
        else if (digits.StartsWith("57", StringComparison.Ordinal) && digits.Length == 12)
            digits = digits[2..];
        return digits.Length >= 7 ? digits : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
