using System.Net;

namespace Auraly.Pos.Printing;

public static class OrderContactPresentation
{
    public static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Sin registrar" : value;

    public static string Html(string? address, string? phone) =>
        $"<div class=\"pair\" style=\"grid-column:1/-1;min-width:0;display:block\"><span>Dirección</span><strong style=\"display:block;overflow-wrap:anywhere;white-space:pre-line;text-align:left\">{WebUtility.HtmlEncode(Value(address))}</strong></div>" +
        $"<div class=\"pair\"><span>Teléfono</span><strong>{WebUtility.HtmlEncode(Value(phone))}</strong></div>";

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
}
