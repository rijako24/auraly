using System.Net;

namespace Auraly.Pos.Printing;

public static class OrderContactPresentation
{
    public static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Sin registrar" : value;

    public static string Html(string? name, string? identification, string? address, string? phone) =>
        Field("Cliente", name) +
        Field("Identificación", identification) +
        Field("Dirección", address) +
        Field("Teléfono", phone);

    public static string OptionalHtml(string? address, string? phone)
    {
        var addressHtml = string.IsNullOrWhiteSpace(address)
            ? string.Empty
            : $"<div class=\"pair\" style=\"grid-column:1/-1;min-width:0;display:block\"><span>Dirección</span><strong style=\"display:block;overflow-wrap:anywhere;white-space:pre-line;text-align:left\">{WebUtility.HtmlEncode(address)}</strong></div>";
        var phoneHtml = string.IsNullOrWhiteSpace(phone)
            ? string.Empty
            : $"<div class=\"pair\"><span>Teléfono</span><strong>{WebUtility.HtmlEncode(phone)}</strong></div>";
        return addressHtml + phoneHtml;
    }

    private static string Field(string label, string? value) =>
        $"<div class=\"pair order-customer-field\" style=\"grid-column:1/-1;min-width:0\"><span>{label}</span><strong style=\"min-width:0;overflow-wrap:anywhere;white-space:pre-line;text-align:right\">{WebUtility.HtmlEncode(Value(value))}</strong></div>";
}
