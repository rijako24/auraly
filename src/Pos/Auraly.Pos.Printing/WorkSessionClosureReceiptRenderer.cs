using System.Globalization;
using System.Net;
using Auraly.Contracts.WorkSessions;

namespace Auraly.Pos.Printing;

public static class WorkSessionClosureReceiptRenderer
{
    public static string RenderHtml(
        WorkSessionClosureView value,
        string? companyName = null,
        string? companyLogoSource = null,
        int paperWidthMillimeters = 80)
    {
        if (paperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        return value.ReceiptTemplateVersion switch
        {
            1 => RenderV1(value, companyName, companyLogoSource, paperWidthMillimeters),
            2 => RenderV2(value, companyName, companyLogoSource, paperWidthMillimeters),
            3 or 4 or 5 => RenderMovementClosure(value, companyName, companyLogoSource, paperWidthMillimeters),
            _ => throw new InvalidOperationException(
                $"La versión {value.ReceiptTemplateVersion} de la tirilla de cierre no está disponible.")
        };
    }

    private static string RenderV1(
        WorkSessionClosureView value,
        string? companyName,
        string? companyLogoSource,
        int paperWidthMillimeters)
    {
        var payments = string.Join(string.Empty, value.PaymentTotals.Select(payment =>
        {
            var cashDetails = IsCash(payment.PaymentMethodCode)
                ? $"<div class=\"payment-details\"><span>Entradas <strong>{Money(Math.Max(0, payment.OtherAmount))}</strong></span><span>Salidas <strong>{Money(Math.Abs(Math.Min(0, payment.OtherAmount)))}</strong></span></div>"
                : string.Empty;
            var reconciliation = payment.CountedAmount is { } counted
                ? $"<div class=\"payment-details\"><span>Esperado <strong>{Money(payment.NetAmount)}</strong></span><span>Contado <strong>{Money(counted)}</strong></span><span>Diferencia <strong>{Money(Math.Abs(payment.Difference ?? counted - payment.NetAmount))}</strong></span></div>"
                : string.Empty;
            return $"<section class=\"payment\" data-payment-method=\"{Encode(payment.PaymentMethodCode)}\"><h3>{Encode(PaymentMethodName(payment.PaymentMethodCode))}</h3><div class=\"payment-details\"><span>Ventas <strong>{Money(payment.SalesAmount)}</strong></span><span>Devoluciones <strong>{Money(payment.RefundAmount)}</strong></span></div>{cashDetails}{reconciliation}</section>";
        }));
        var creditSales = string.Join(string.Empty, (value.CreditSales ?? []).Select(credit =>
            $"<tr><td>{Encode(credit.CustomerName)} · {Encode(credit.DocumentNumber)}</td><td>{Money(credit.Amount)}</td></tr>"));
        var note = string.IsNullOrWhiteSpace(value.Note)
            ? string.Empty
            : $"<p><strong>Nota:</strong> {Encode(value.Note)}</p>";
        var logo = Logo(companyLogoSource, companyName ?? value.BusinessName);
        var body = $$"""
<header>{{logo}}<h1>{{Encode(companyName ?? value.BusinessName)}}</h1><h2>Arqueo de caja · Cierre confirmado</h2><p class="scope">Sede: {{Encode(value.BusinessName)}}</p>
<p class="session-details"><strong>Usuario que trabajó:</strong> {{Encode(value.UserName)}}<br><strong>Apertura:</strong> {{Date(value.OpenedAt)}}<br><strong>Cierre:</strong> {{Date(value.ClosedAt)}}<br><strong>Duración:</strong> {{Duration(value.OpenedAt, value.ClosedAt)}}</p>
</header>
<h2 class="section-title">Actividad del turno</h2><table class="rows"><tbody><tr class="count-row"><td>Número de ventas</td><td>{{value.SalesCount}}</td></tr><tr class="count-row"><td>Ventas a cartera</td><td>{{value.CreditSalesCount}}</td></tr><tr class="count-row"><td>Devoluciones</td><td>{{value.ReturnCount}}</td></tr></tbody></table>
{{(creditSales.Length > 0 ? $"<h2 class=\"section-title\">Clientes a cartera</h2><table class=\"rows\"><tbody>{creditSales}</tbody></table>" : string.Empty)}}
<h2 class="section-title">Detalle por medio de pago</h2>{{payments}}
<h2 class="section-title">Totales del turno</h2><table class="rows"><tbody><tr><td>Ventas</td><td>{{Money(value.TotalSales)}}</td></tr><tr><td>Devoluciones</td><td>{{Money(value.TotalRefunds)}}</td></tr><tr><td>Valor a cartera</td><td>{{Money(value.CreditSalesAmount)}}</td></tr><tr><td>Entradas de caja</td><td>{{Money(CashEntries(value))}}</td></tr><tr><td>Salidas de caja</td><td>{{Money(CashExits(value))}}</td></tr><tr><td>Efectivo esperado</td><td>{{Money(value.ExpectedCash)}}</td></tr><tr><td>Efectivo contado</td><td>{{Money(value.CountedCash ?? 0)}}</td></tr></tbody></table>
<div class="difference"><strong>{{Encode(DifferenceLabel(value.CashDifference ?? 0))}}</strong><strong>{{Money(Math.Abs(value.CashDifference ?? 0))}}</strong></div>{{note}}
""";
        return DocumentV1(body, paperWidthMillimeters);
    }

    private static string RenderV2(
        WorkSessionClosureView value,
        string? companyName,
        string? companyLogoSource,
        int paperWidthMillimeters,
        int version = 2)
    {
        var creditSales = value.CreditSales ?? [];
        var creditDetailTotal = creditSales.Sum(item => item.Amount);
        if (creditSales.Count != value.CreditSalesCount ||
            creditDetailTotal != value.CreditSalesAmount)
            throw new InvalidDataException(
                "El detalle de ventas a cartera no coincide con el total congelado del cierre.");

        var payments = string.Join(string.Empty, value.PaymentTotals
            .OrderBy(payment => PaymentOrder(payment.PaymentMethodCode))
            .ThenBy(payment => payment.PaymentMethodCode, StringComparer.Ordinal)
            .Select(payment =>
            {
                var cashDetails = IsCash(payment.PaymentMethodCode)
                    ? $"<div class=\"payment-details\"><span>Entradas <strong>{Money(version >= 4 ? payment.CashEntryAmount : Math.Max(0, payment.OtherAmount))}</strong></span><span>Salidas <strong>{Money(version >= 4 ? payment.CashExitAmount : Math.Abs(Math.Min(0, payment.OtherAmount)))}</strong></span></div>"
                    : string.Empty;
                var reconciliation = payment.CountedAmount is not { } counted
                    ? string.Empty
                    : $"<div class=\"payment-details\"><span>{(IsCash(payment.PaymentMethodCode) ? "Efectivo esperado" : "Esperado")} <strong>{Money(payment.NetAmount)}</strong></span><span>{(IsCash(payment.PaymentMethodCode) ? "Efectivo contado" : "Contado")} <strong>{Money(counted)}</strong></span></div>{DifferenceBox(payment.Difference ?? counted - payment.NetAmount)}";
                return $"<section class=\"payment\" data-payment-method=\"{Encode(payment.PaymentMethodCode)}\"><h3>{Encode(PaymentMethodName(payment.PaymentMethodCode))}</h3><div class=\"payment-details\"><span>Ventas <strong>{Money(payment.SalesAmount)}</strong></span><span>Devoluciones <strong>{Money(payment.RefundAmount)}</strong></span></div>{cashDetails}{(version == 4 ? ChargePaymentRows(value.InvoiceCharges ?? [], payment.PaymentMethodCode) : string.Empty)}{reconciliation}</section>";
            }));
        var creditRows = string.Join(string.Empty, creditSales.Select(credit => version >= 4
            ? $"<tr><td>{Encode(credit.CustomerName)}</td><td>{Encode(credit.DocumentNumber)}</td><td>{Money(credit.Amount)}</td></tr>"
            : $"<tr><td><strong>{Encode(credit.CustomerName)}</strong><small>{Encode(credit.DocumentNumber)}</small></td><td>{Money(credit.Amount)}</td></tr>"));
        var creditColumnSpan = version >= 4 ? " colspan=\"2\"" : string.Empty;
        var chargeGroups = version >= 5
            ? (value.InvoiceCharges ?? []).GroupBy(charge => charge.ChargeId)
                .Select(group => (Name: group.First().Name, Count: group.Count(), Amount: group.Sum(charge => charge.Amount)))
                .ToArray()
            : [];
        var chargeCounts = string.Join(string.Empty, chargeGroups.Select(group =>
            $"<tr class=\"count-row\"><td>{Encode(group.Name)}</td><td>{group.Count}</td></tr>"));
        var chargeTotals = string.Join(string.Empty, chargeGroups.Select(group =>
            $"<tr><td>{Encode(group.Name)}</td><td>{Money(group.Amount)}</td></tr>"));
        var logo = Logo(companyLogoSource, companyName ?? value.BusinessName);
        var body = $$"""
<header>{{logo}}<h1>{{Encode(companyName ?? value.BusinessName)}}</h1><h2>Arqueo de caja · Cierre confirmado</h2><p class="scope">Sede: {{Encode(Location(value))}}</p>
<p class="session-details"><strong>Usuario que trabajó:</strong> {{Encode(value.UserName)}}<br><strong>Apertura:</strong> {{Date(value.OpenedAt)}}<br><strong>Cierre:</strong> {{Date(value.ClosedAt)}}<br><strong>Duración:</strong> {{Duration(value.OpenedAt, value.ClosedAt)}}</p>
</header>
<h2 class="section-title">Actividad del turno</h2><table class="rows"><tbody><tr class="count-row"><td>Número de ventas</td><td>{{value.SalesCount}}</td></tr><tr class="count-row"><td>Ventas a cartera</td><td>{{value.CreditSalesCount}}</td></tr><tr class="count-row"><td>Devoluciones</td><td>{{value.ReturnCount}}</td></tr>{{chargeCounts}}</tbody></table>
<h2 class="section-title">Totales del turno</h2><table class="rows"><tbody><tr><td>Ventas</td><td>{{Money(value.TotalSales)}}</td></tr><tr><td>Devoluciones</td><td>{{Money(value.TotalRefunds)}}</td></tr><tr><td>Valor a cartera</td><td>{{Money(value.CreditSalesAmount)}}</td></tr><tr><td>Entradas de caja</td><td>{{Money(CashEntries(value))}}</td></tr><tr><td>Salidas de caja</td><td>{{Money(CashExits(value))}}</td></tr>{{chargeTotals}}</tbody></table>
<h2 class="section-title">Ventas a cartera</h2><table class="rows credit-sales"><tbody>{{(creditRows.Length > 0 ? creditRows : $"<tr><td{creditColumnSpan}>Sin ventas a cartera</td><td>$ 0</td></tr>")}}</tbody><tfoot><tr><th{{creditColumnSpan}}>Total cartera</th><th>{{Money(value.CreditSalesAmount)}}</th></tr></tfoot></table>
{{(version == 4 ? ChargePaymentRows(value.InvoiceCharges ?? [], "Credit") : string.Empty)}}
<h2 class="section-title">Detalle por medio de pago</h2>{{payments}}
{{(version == 4 ? ChargeSection(value.InvoiceCharges ?? []) : string.Empty)}}
{{Note(value.Note)}}
""";
        return DocumentV2(body, paperWidthMillimeters);
    }

    private static string RenderMovementClosure(
        WorkSessionClosureView value,
        string? companyName,
        string? companyLogoSource,
        int paperWidthMillimeters)
    {
        var details = value.CashMovements ?? [];
        var entries = details
            .Where(item => item.Direction == CashMovementDirections.In)
            .ToArray();
        var exits = details
            .Where(item => item.Direction == CashMovementDirections.Out)
            .ToArray();

        var version = value.ReceiptTemplateVersion;
        var sections = PortfolioSection("Abonos a cartera",value.ReceivablePayments??[])
                       + PortfolioSection("Pagos a proveedores",value.PayablePayments??[])
                       + CashMovementSection("Entradas de dinero", entries,
                           entries.Sum(item => item.Amount), version)
                       + CashMovementSection("Salidas de dinero", exits,
                           exits.Sum(item => item.Amount), version);
        if (version >= 5)
            sections += ChargeSection(value.InvoiceCharges ?? []);
        var compactStyles = version >= 4
            ? ".invoice-charges small{display:block;color:#555;font-size:9px}.invoice-charges td:last-child{white-space:nowrap}.cash-movement small{white-space:pre-line;overflow-wrap:anywhere}.cash-movement td:last-child{white-space:nowrap;vertical-align:top}.credit-sales td:first-child,.credit-sales th:first-child{width:auto}.credit-sales td{vertical-align:top;overflow-wrap:anywhere}.credit-sales td:nth-child(2){white-space:nowrap;padding-left:4px;padding-right:4px}.credit-sales td:last-child{white-space:nowrap}"
            : string.Empty;
        return RenderV2(value, companyName, companyLogoSource, paperWidthMillimeters, version)
            .Replace(
                "<h2 class=\"section-title\">Detalle por medio de pago</h2>",
                sections + "<h2 class=\"section-title\">Detalle por medio de pago</h2>",
                StringComparison.Ordinal)
            .Replace(
                "</style>",
                ".cash-movement td:first-child{width:72%}.cash-movement small{display:block;color:#555;font-size:9px}.cash-empty{color:#666}" + compactStyles + "</style>",
                StringComparison.Ordinal)
            .Replace(
                $"data-auraly-report-version=\"{PosPrintTemplateCatalog.WorkSessionClosureV2.Version}\"",
                $"data-auraly-report-version=\"{version}\"",
                StringComparison.Ordinal);
    }

    private static string ChargePaymentRows(IReadOnlyList<WorkSessionInvoiceCharge> charges, string method)
    {
        var rows = charges.SelectMany(charge => charge.Payments
            .Where(payment => payment.PaymentMethodCode.Equals(method, StringComparison.OrdinalIgnoreCase))
            .Select(payment => $"<tr><td>{Encode(charge.Name)}<small>{Encode(charge.DocumentNumber)} · {Encode(charge.SupplierName)}</small></td><td>{Money(payment.Amount)}</td></tr>"));
        var html = string.Join(string.Empty, rows);
        return html.Length == 0 ? string.Empty : $"<table class=\"rows invoice-charges\"><tbody>{html}</tbody></table>";
    }

    private static string ChargeSection(IReadOnlyList<WorkSessionInvoiceCharge> charges)
    {
        var rows = string.Join(string.Empty, charges.Select(charge =>
            $"<tr><td>{Encode(charge.Name)}<small>{Encode(charge.DocumentNumber)} · {Encode(charge.SupplierName)} · {(charge.InvoicedAmount > 0 ? "En factura" : "Gasto")}</small></td><td>{Money(charge.Amount)}</td></tr>"));
        if (charges.Count == 0) rows = "<tr><td>Sin cargos</td><td>$ 0</td></tr>";
        return $"<h2 class=\"section-title\">Cargos de facturación</h2><table class=\"rows invoice-charges\"><tbody>{rows}</tbody><tfoot><tr><th>Incluido en facturas</th><th>{Money(charges.Sum(charge => charge.InvoicedAmount))}</th></tr><tr><th>Registrado como gasto</th><th>{Money(charges.Sum(charge => charge.ExpenseAmount))}</th></tr></tfoot></table>";
    }

    private static string CashMovementSection(
        string title,
        IReadOnlyList<WorkSessionCashMovementDetail> details,
        decimal total,
        int version)
    {
        var rows = details.Count == 0
            ? "<tr class=\"cash-empty\"><td>Sin movimientos</td><td>$ 0</td></tr>"
            : string.Join(string.Empty, details.Select(detail =>
            {
                if (version >= 4)
                {
                    var observation = string.IsNullOrWhiteSpace(detail.Notes)
                        ? string.Empty : $"<small>{Encode(detail.Notes)}</small>";
                    return $"<tr class=\"cash-movement\"><td><strong>{Encode(detail.ReasonName)}</strong>{observation}</td><td>{Money(detail.Amount)}</td></tr>";
                }
                var reference = string.IsNullOrWhiteSpace(detail.Reference)
                    ? string.Empty
                    : $" · {Encode(detail.Reference)}";
                return $"<tr class=\"cash-movement\"><td><strong>{Encode(detail.ReasonName)}</strong><small>{Encode(detail.DocumentNumber)} · {Date(detail.OccurredAt)} · {Encode(detail.ResponsibleName)}{reference}</small></td><td>{Money(detail.Amount)}</td></tr>";
            }));
        return $"<h2 class=\"section-title\">{Encode(title)}</h2><table class=\"rows cash-movements\"><tbody>{rows}</tbody><tfoot><tr><th>Total</th><th>{Money(total)}</th></tr></tfoot></table>";
    }

    private static string PortfolioSection(string title,IReadOnlyList<WorkSessionPortfolioPayment> payments)
    {
        if(payments.Count==0)return $"<h2 class=\"section-title\">{Encode(title)}</h2><table class=\"rows\"><tbody><tr><td>Sin movimientos</td><td>$ 0</td></tr></tbody></table>";
        var rows=string.Join(string.Empty,payments.SelectMany(payment=>payment.Applications.Select(application=>
            $"<tr><td><strong>{Encode(payment.PartyName)}</strong><small>{Encode(application.DocumentNumber)}</small></td><td>{Money(application.Amount)}</td></tr>")));
        return $"<h2 class=\"section-title\">{Encode(title)}</h2><table class=\"rows credit-sales\"><tbody>{rows}</tbody><tfoot><tr><th>Total</th><th>{Money(payments.Sum(payment=>payment.TotalAmount))}</th></tr></tfoot></table>";
    }

    private static string DocumentV1(string body, int paperWidthMillimeters) => $$"""
<!doctype html><html lang="es" data-auraly-report="{{PosPrintTemplateCatalog.WorkSessionClosureV1.Code}}" data-auraly-report-version="1"><head><meta charset="utf-8"><title>Cierre de sesión de venta</title>
<style>@page{size:{{paperWidthMillimeters}}mm auto;margin:3mm}*{box-sizing:border-box}body{width:{{paperWidthMillimeters}}mm;font:11px/1.35 ui-monospace,Consolas,monospace;color:#111;margin:0;padding:5mm 3mm 2mm 2mm}.brand-logo{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:9px}h1{font:800 19px/1.2 Arial,sans-serif;margin:3px 0;text-transform:uppercase}header h2{font-size:12px;margin:7px 0 4px;text-transform:uppercase}.scope{margin:3px 0}.section-title{font-size:11px;margin:13px 0 6px;padding-bottom:4px;border-bottom:1px dashed #777;text-transform:uppercase}.session-details{text-align:left;font-size:11px;line-height:1.5;margin-top:10px}.rows{width:100%;border-collapse:collapse;margin:0}.rows td{padding:3px 1px;text-align:right;font-size:11px;font-variant-numeric:tabular-nums}.rows td:first-child{width:64%;text-align:left}.count-row td{font-size:12px;font-weight:800}.payment{border-bottom:1px dashed #aaa;padding:5px 0}.payment h3{font-size:11px;margin:2px 0 4px;text-transform:uppercase}.payment-details span{display:flex;justify-content:space-between;gap:8px;width:100%;padding:2px 1px}.payment-details span>strong{font-variant-numeric:tabular-nums;text-align:right}.difference{display:flex;justify-content:space-between;font-size:16px;border:2px solid #111;padding:8px;margin-top:12px;font-weight:800}.note{margin-top:9px;padding:6px;border:1px dashed #777}.platform-footer{margin-top:10px;text-align:center;font:700 12px/1.4 Arial,sans-serif}</style></head><body>
{{body}}<footer class="platform-footer">www.auralyapp.co</footer></body></html>
""";

    private static string DocumentV2(string body, int paperWidthMillimeters) => $$"""
<!doctype html><html lang="es" data-auraly-report="{{PosPrintTemplateCatalog.WorkSessionClosureV2.Code}}" data-auraly-report-version="2"><head><meta charset="utf-8"><title>Cierre de sesión de venta</title>
<style>@page{size:{{paperWidthMillimeters}}mm auto;margin:3mm}*{box-sizing:border-box}body{width:{{paperWidthMillimeters}}mm;font:11px/1.35 ui-monospace,Consolas,monospace;color:#111;margin:0;padding:5mm 3mm 2mm 2mm}.brand-logo{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:9px}h1{font:800 19px/1.2 Arial,sans-serif;margin:3px 0;text-transform:uppercase}header h2{font-size:12px;margin:7px 0 4px;text-transform:uppercase}.scope{margin:3px 0}.section-title{font-size:11px;margin:13px 0 6px;padding-bottom:4px;border-bottom:1px dashed #777;text-transform:uppercase}.session-details{text-align:left;font-size:11px;line-height:1.5;margin-top:10px}.rows{width:100%;border-collapse:collapse;margin:0}.rows td,.rows th{padding:3px 1px;text-align:right;font-size:11px;font-variant-numeric:tabular-nums}.rows td:first-child,.rows th:first-child{width:64%;text-align:left}.rows tfoot th{border-top:1px dashed #777;padding-top:5px}.credit-sales small{display:block;font-weight:400}.count-row td{font-size:12px;font-weight:800}.payment{border-bottom:1px dashed #aaa;padding:7px 0}.payment h3{font-size:11px;margin:2px 0 4px;text-transform:uppercase}.payment-details span{display:flex;justify-content:space-between;gap:8px;width:100%;padding:2px 1px}.payment-details span>strong{font-variant-numeric:tabular-nums;text-align:right}.difference{display:flex;justify-content:space-between;font-size:15px;border:2px solid #111;padding:7px;margin-top:7px;font-weight:800}.note{margin-top:9px;padding:6px;border:1px dashed #777}.platform-footer{margin-top:10px;text-align:center;font:700 12px/1.4 Arial,sans-serif}</style></head><body>
{{body}}<footer class="platform-footer">www.auralyapp.co</footer></body></html>
""";

    private static string DifferenceBox(decimal difference) =>
        $"<div class=\"difference\"><strong>{DifferenceLabel(difference)}</strong><strong>{Money(Math.Abs(difference))}</strong></div>";
    private static string Note(string? note) => string.IsNullOrWhiteSpace(note)
        ? string.Empty
        : $"<p class=\"note\"><strong>Observación:</strong> {Encode(note)}</p>";
    private static string Logo(string? source, string companyName) =>
        string.IsNullOrWhiteSpace(source)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(source)}\" alt=\"Logo de {Encode(companyName)}\">";
    private static int PaymentOrder(string code) => code switch
    {
        "Card" or "DebitCard" or "CreditCard" => 0,
        "Transfer" => 1,
        "Cash" => 3,
        _ => 2
    };
    private static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
    private static string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        var duration = end - start;
        return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";
    }
    private static string Money(decimal value) =>
        "$ " + value.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));
    private static string DifferenceLabel(decimal value) => value > 0
        ? "SOBRANTE"
        : value < 0 ? "FALTANTE" : "CUADRA";
    private static string PaymentMethodName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "DebitCard" => "Tarjeta débito",
        "CreditCard" => "Tarjeta crédito",
        "Card" => "Tarjeta",
        "Transfer" => "Transferencia",
        "Credit" => "Crédito / cartera",
        "Voucher" => "Bono / vale",
        "Check" => "Cheque",
        "Withholding" => "Retención",
        _ => code
    };
    private static bool IsCash(string code) =>
        code.Equals("Cash", StringComparison.OrdinalIgnoreCase);
    private static decimal CashEntries(WorkSessionClosureView value) =>
        value.CashMovements is not null
            ? value.CashMovements
                .Where(movement => movement.Direction == CashMovementDirections.In)
                .Sum(movement => movement.Amount)
            : value.PaymentTotals.Sum(payment => Math.Max(0, payment.OtherAmount));
    private static decimal CashExits(WorkSessionClosureView value) =>
        value.CashMovements is not null
            ? value.CashMovements
                .Where(movement => movement.Direction == CashMovementDirections.Out)
                .Sum(movement => movement.Amount)
            : value.PaymentTotals.Sum(payment => Math.Abs(Math.Min(0, payment.OtherAmount)));
    private static string Location(WorkSessionClosureView value) => value.WarehouseName is null
        ? value.BusinessName
        : $"{value.BusinessName} · {value.WarehouseName}";
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
