using System.Globalization;
using System.Net;
using System.Text;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;

namespace Auraly.Api;

internal static class PortfolioReportPrint
{
    private const int MaxRows = 5_000;
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public static async Task<string> ReceivablesAsync(ReceivablesService service,
        ReceivablesUserIdentity user, ReceivablesReportQuery query, CancellationToken token)
    {
        var result = await service.PrintReportAsync(user, query, token);
        return Build("Cuentas por cobrar", "Abono", query.Consolidated, query.From, query.To, query.Cutoff,
            new PrintPage(result.TotalCount,
                [new("COP", result.TotalInvoiceCount, result.TotalOriginal, result.TotalPaid,
                    result.TotalOtherImpact, result.TotalOutstanding, result.TotalOverdue)],
                result.Items.Select(item => new PrintRow(item.CustomerName, item.Identification,
                    item.PartySiteName, item.CurrencyCode, item.InvoiceCount, item.DocumentNumber,
                    item.IssuedAt, item.DueDate, item.OriginalAmount, item.PaidAmount,
                    item.OtherImpact, item.OutstandingAmount, item.Applications?
                        .Select(application => new PrintApplication(application.DocumentNumber,
                            application.AppliedAt, application.Amount)).ToArray() ?? [])).ToArray()), token);
    }

    public static async Task<string> PayablesAsync(PayablesService service,
        PayablesUserIdentity user, PayablesReportQuery query, CancellationToken token)
    {
        var result = await service.PrintReportAsync(user, query, token);
        return Build("Cuentas por pagar", "Pago", query.Consolidated, query.From, query.To, query.Cutoff,
            new PrintPage(result.TotalCount,
                result.CurrencyTotals.Select(total => new PrintTotal(total.CurrencyCode,
                    total.InvoiceCount, total.OriginalAmount, total.PaidAmount,
                    total.OtherImpact, total.OutstandingAmount, total.OverdueAmount)).ToArray(),
                result.Items.Select(item => new PrintRow(item.SupplierName, item.Identification,
                    item.PartySiteName, item.CurrencyCode, item.InvoiceCount, item.DocumentNumber,
                    item.IssuedAt, item.DueDate, item.OriginalAmount, item.PaidAmount,
                    item.OtherImpact, item.OutstandingAmount, item.Applications?
                        .Select(application => new PrintApplication(application.DocumentNumber,
                            application.AppliedAt, application.Amount)).ToArray() ?? [])).ToArray()), token);
    }

    private static string Build(string title, string applicationLabel, bool consolidated,
        DateOnly? from, DateOnly? to, DateOnly cutoff,
        PrintPage first, CancellationToken token)
    {
        if (first.TotalCount > MaxRows)
            throw new PortfolioReportPrintException(
                $"El informe tiene más de {MaxRows.ToString("N0", Culture)} filas. Ajusta los filtros antes de imprimirlo.", 422);
        var html = new StringBuilder(Math.Max(4_096, first.TotalCount * 340));
        html.Append("""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Informe de cartera</title>
            <style>@page{size:landscape;margin:10mm}*{box-sizing:border-box}body{margin:0;color:#17212b;font:12px/1.4 Arial,sans-serif}
            header{border-bottom:2px solid #0c7772;padding:0 0 12px;margin-bottom:14px}h1{margin:0 0 4px;font-size:22px}
            .eyebrow{color:#0c7772;font-weight:700;letter-spacing:.15em;text-transform:uppercase;font-size:10px}
            .meta{color:#526478}.totals{display:flex;flex-wrap:wrap;gap:8px;margin:12px 0}.total{border:1px solid #d9e4e5;border-radius:8px;padding:7px 10px}
            table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #d9e4e5;padding:6px;text-align:left;vertical-align:top}
            th{background:#eaf4f3;color:#174d4a}td.num,th.num{text-align:right;white-space:nowrap}.sub{font-size:10px;color:#536271}
            tr{break-inside:avoid}thead{display:table-header-group}.empty{text-align:center;padding:24px}.footer{margin-top:14px;border-top:1px solid #d9e4e5;padding-top:7px;color:#526478;font-size:10px}
            </style></head><body><header><div class="eyebrow">Auraly · reporte corporativo</div>
            """);
        html.Append("<h1>").Append(Encode(title)).Append(consolidated ? " · consolidado" : " · detallado")
            .Append("</h1><div class=\"meta\">Corte: ").Append(cutoff.ToString("dd/MM/yyyy", Culture));
        if (from is not null || to is not null)
            html.Append(" · Emisión ").Append(from?.ToString("dd/MM/yyyy", Culture) ?? "inicio")
                .Append(" a ").Append(to?.ToString("dd/MM/yyyy", Culture) ?? "corte");
        html.Append(" · ").Append(first.TotalCount.ToString("N0", Culture)).Append(" filas</div></header><div class=\"totals\">");
        foreach (var total in first.Totals)
        {
            html.Append("<div class=\"total\"><strong>").Append(Encode(total.Currency))
                .Append(" · ").Append(total.InvoiceCount.ToString("N0", Culture))
                .Append(" documentos</strong><br>Original ").Append(Money(total.Original, total.Currency))
                .Append(" · Pagado ").Append(Money(total.Paid, total.Currency));
            if (total.OtherImpact != 0)
                html.Append(" · Notas y ajustes ").Append(Money(total.OtherImpact, total.Currency));
            html.Append("<br><strong>Saldo ").Append(Money(total.Outstanding, total.Currency))
                .Append(" · Vencido ").Append(Money(total.Overdue, total.Currency))
                .Append("</strong></div>");
        }
        html.Append("</div><table><thead><tr><th>Tercero y sede</th>");
        if (consolidated) html.Append("<th>Documentos</th>");
        else html.Append("<th>Documento y aplicaciones</th><th>Emisión</th><th>Vence</th>");
        html.Append("<th>Moneda</th><th class=\"num\">Original</th><th class=\"num\">Pagado</th><th class=\"num\">Notas y ajustes</th><th class=\"num\">Saldo</th></tr></thead><tbody>");
        foreach (var row in first.Rows)
        {
            token.ThrowIfCancellationRequested();
                html.Append("<tr><td><strong>").Append(Encode(row.Name)).Append("</strong><br><span class=\"sub\">")
                    .Append(Encode(row.Identification)).Append(" · ").Append(Encode(row.Site ?? "Sede principal"))
                    .Append("</span></td>");
                if (consolidated) html.Append("<td>").Append(row.InvoiceCount.ToString("N0", Culture)).Append("</td>");
                else
                {
                    html.Append("<td><strong>").Append(Encode(row.DocumentNumber ?? "—")).Append("</strong>");
                    foreach (var application in row.Applications)
                        html.Append("<div class=\"sub\">").Append(applicationLabel).Append(' ')
                            .Append(Encode(application.DocumentNumber))
                            .Append(" · ").Append(Date(application.AppliedAt)).Append(" · ")
                            .Append(Money(application.Amount, row.Currency)).Append("</div>");
                    html.Append("</td><td>").Append(Date(row.IssuedAt)).Append("</td><td>")
                        .Append(Date(row.DueDate)).Append("</td>");
                }
                html.Append("<td>").Append(Encode(row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Original, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Paid, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.OtherImpact, row.Currency)).Append("</td><td class=\"num\"><strong>")
                    .Append(Money(row.Outstanding, row.Currency)).Append("</strong></td></tr>");
        }
        if (first.TotalCount == 0) html.Append($"<tr><td class=\"empty\" colspan=\"{(consolidated ? 7 : 9)}\">No hay documentos para estos filtros.</td></tr>");
        html.Append("</tbody></table><div class=\"footer\">Auraly · generado ")
            .Append(DateTimeOffset.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", Culture))
            .Append(" · ").Append(first.TotalCount.ToString("N0", Culture))
            .Append(" filas</div></body></html>");
        return html.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Money(decimal amount, string currency) =>
        WebUtility.HtmlEncode($"{amount.ToString(amount == decimal.Round(amount, 2) ? "N2" : "N4", Culture)} {currency}");
    private static string Date(DateTimeOffset? value) => value?.ToString("dd/MM/yyyy", Culture) ?? "—";

    private sealed record PrintApplication(string DocumentNumber, DateTimeOffset AppliedAt, decimal Amount);
    private sealed record PrintRow(string Name, string Identification, string? Site, string Currency,
        int InvoiceCount, string? DocumentNumber, DateTimeOffset? IssuedAt, DateTimeOffset? DueDate,
        decimal Original, decimal Paid, decimal OtherImpact, decimal Outstanding,
        IReadOnlyList<PrintApplication> Applications);
    private sealed record PrintTotal(string Currency, int InvoiceCount, decimal Original, decimal Paid,
        decimal OtherImpact, decimal Outstanding, decimal Overdue);
    private sealed record PrintPage(int TotalCount, IReadOnlyList<PrintTotal> Totals,
        IReadOnlyList<PrintRow> Rows);
}

internal sealed class PortfolioReportPrintException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
