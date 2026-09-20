using System.Net;

namespace Auraly.Pos.Printing;

public static class OrderContactPresentation
{
    public static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Sin registrar" : value;

    public static string Html(string? address, string? phone) =>
        $"<div class=\"pair\" style=\"grid-column:1/-1;min-width:0;display:block\"><span>Dirección</span><strong style=\"display:block;overflow-wrap:anywhere;white-space:pre-line;text-align:left\">{WebUtility.HtmlEncode(Value(address))}</strong></div>" +
        $"<div class=\"pair\"><span>Teléfono</span><strong>{WebUtility.HtmlEncode(Value(phone))}</strong></div>";
}
