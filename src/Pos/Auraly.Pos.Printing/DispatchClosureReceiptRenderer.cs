using System.Globalization;
using System.Net;
using Auraly.Contracts.Dispatching;
using Auraly.BuildingBlocks.Domain.Time;

namespace Auraly.Pos.Printing;

public static class DispatchClosureReceiptRenderer
{
    public static string RenderHtml(DispatchExecutionDetail dispatch, TimeZoneInfo timeZone,
        string? companyName = null, int paperWidthMillimeters = 80)
    {
        if (paperWidthMillimeters is not (58 or 80)) throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        var closure = dispatch.Settlement ?? throw new InvalidOperationException("El despacho todavía no tiene una declaración guardada.");
        var at = closure.TransporterClosedAt ?? throw new InvalidOperationException("El cierre no conserva su fecha de declaración.");
        var template = PosPrintTemplateCatalog.DispatchClosure;
        var date = BusinessCalendar.InZone(at,timeZone).ToString("dd/MM/yyyy HH:mm zzz", CultureInfo.InvariantCulture);
        // The driver's receipt deliberately contains declared amounts only. Reprinting
        // never runs closure, reconciliation, inventory or accounting commands.
        return $$"""
            <!doctype html><html lang="es" data-auraly-report="{{template.Code}}" data-auraly-report-version="{{template.Version}}"><head><meta charset="utf-8"><title>Cierre de despacho</title>
            <style>@page{size:{{paperWidthMillimeters}}mm auto;margin:3mm}*{box-sizing:border-box}body{width:{{paperWidthMillimeters-6}}mm;margin:auto;font:12px/1.4 ui-monospace,Consolas,monospace;color:#111}h1{font:800 17px/1.2 Arial,sans-serif}h2{font-size:14px}header{text-align:center;border-bottom:1px dashed;padding-bottom:8px}p{overflow-wrap:anywhere}table{width:100%;border-collapse:collapse}td{padding:8px 0;border-bottom:1px dashed #999}td:last-child{text-align:right;font-weight:bold}.note{border:1px solid;padding:8px;margin-top:12px}</style></head><body>
            <header>{{(string.IsNullOrWhiteSpace(companyName)?"":$"<h1>{Encode(companyName)}</h1>")}}<h2>Cierre de despacho</h2><strong>{{Encode(dispatch.DispatchNumber)}}</strong></header>
            <p>Transportador: {{Encode(dispatch.DriverName)}}<br>Usuario: {{closure.TransporterClosedBy}}<br>Fecha: {{Encode(date)}}<br>Zona: {{Encode(timeZone.Id)}}</p>
            <h2>Valores declarados</h2><table><tbody><tr><td>Efectivo</td><td>{{Money(closure.DeclaredCash)}}</td></tr><tr><td>Tarjetas</td><td>{{Declared(closure.DeclaredCard)}}</td></tr><tr><td>Transferencias</td><td>{{Declared(closure.DeclaredTransfer)}}</td></tr></tbody></table>
            <p class="note">Declaración guardada. La recepción y validación administrativa se consultan en Cierres de caja.</p></body></html>
            """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Money(decimal value) => Encode(value.ToString("C0", CultureInfo.GetCultureInfo("es-CO")));
    private static string Declared(decimal? value) => value.HasValue ? Money(value.Value) : "No declarado";
}
