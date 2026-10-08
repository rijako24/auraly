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
                [new PrintTotal("COP", result.TotalInvoiceCount, result.TotalOriginal, result.TotalPaid,
                    result.TotalOtherImpact, result.TotalOutstanding, result.TotalOverdue)
                {
                    NotDue = result.TotalNotDue, Overdue1To30 = result.TotalOverdue1To30,
                    Overdue31To60 = result.TotalOverdue31To60, Overdue61To90 = result.TotalOverdue61To90,
                    OverdueOver90 = result.TotalOverdueOver90
                }],
                result.Items.Select(item => new PrintRow(item.CustomerId, item.PartySiteId,
                    item.CustomerName, item.Identification, item.PartySiteName,
                    item.CurrencyCode, item.InvoiceCount, item.DocumentNumber,
                    item.IssuedAt, item.DueDate, item.OriginalAmount, item.PaidAmount,
                    item.OtherImpact, item.OutstandingAmount, item.Applications?
                        .Select(application => new PrintApplication(application.DocumentNumber,
                            application.AppliedAt, application.Amount)).ToArray() ?? [])
                {
                    NotDue = item.NotDueAmount, Overdue1To30 = item.Overdue1To30Amount,
                    Overdue31To60 = item.Overdue31To60Amount, Overdue61To90 = item.Overdue61To90Amount,
                    OverdueOver90 = item.OverdueOver90Amount
                }).ToArray()), token);
    }

    public static async Task<string> PayablesAsync(PayablesService service,
        PayablesUserIdentity user, PayablesReportQuery query, CancellationToken token)
    {
        var result = await service.PrintReportAsync(user, query, token);
        return Build("Cuentas por pagar", "Pago", query.Consolidated, query.From, query.To, query.Cutoff,
            new PrintPage(result.TotalCount,
                result.CurrencyTotals.Select(total => new PrintTotal(total.CurrencyCode,
                    total.InvoiceCount, total.OriginalAmount, total.PaidAmount,
                    total.OtherImpact, total.OutstandingAmount, total.OverdueAmount)
                {
                    NotDue = total.NotDueAmount, Overdue1To30 = total.Overdue1To30Amount,
                    Overdue31To60 = total.Overdue31To60Amount, Overdue61To90 = total.Overdue61To90Amount,
                    OverdueOver90 = total.OverdueOver90Amount
                }).ToArray(),
                result.Items.Select(item => new PrintRow(item.SupplierId, item.PartySiteId,
                    item.SupplierName, item.Identification, item.PartySiteName,
                    item.CurrencyCode, item.InvoiceCount, item.DocumentNumber,
                    item.IssuedAt, item.DueDate, item.OriginalAmount, item.PaidAmount,
                    item.OtherImpact, item.OutstandingAmount, item.Applications?
                        .Select(application => new PrintApplication(application.DocumentNumber,
                            application.AppliedAt, application.Amount)).ToArray() ?? [])
                {
                    NotDue = item.NotDueAmount, Overdue1To30 = item.Overdue1To30Amount,
                    Overdue31To60 = item.Overdue31To60Amount, Overdue61To90 = item.Overdue61To90Amount,
                    OverdueOver90 = item.OverdueOver90Amount
                }).ToArray()), token);
    }

    private static string Build(string title, string applicationLabel, bool consolidated,
        DateOnly? from, DateOnly? to, DateOnly cutoff,
        PrintPage first, CancellationToken token)
    {
        if (first.TotalCount > MaxRows)
            throw new PortfolioReportPrintException(
                $"El informe tiene más de {MaxRows.ToString("N0", Culture)} filas. Ajusta los filtros antes de imprimirlo.", 422);
        var html = new StringBuilder(Math.Max(4_096, first.TotalCount * 700));
        html.Append("""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Informe de cartera</title>
            <style>@page{size:landscape;margin:10mm}*{box-sizing:border-box}body{margin:0;color:#17212b;font:12px/1.4 Arial,sans-serif}
            header{border-bottom:2px solid #0c7772;padding:0 0 12px;margin-bottom:14px}h1{margin:0 0 4px;font-size:22px}
            .eyebrow{color:#0c7772;font-weight:700;letter-spacing:.15em;text-transform:uppercase;font-size:10px}
            .meta{color:#526478}.totals{display:flex;flex-wrap:wrap;gap:8px;margin:12px 0}.total{border:1px solid #d9e4e5;border-radius:8px;padding:7px 10px}
            table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #d9e4e5;padding:6px;text-align:left;vertical-align:top}
            th{background:#eaf4f3;color:#174d4a}td.num,th.num{text-align:right;white-space:nowrap}.sub{font-size:10px;color:#536271}
            tr{break-inside:avoid}thead{display:table-header-group}.empty{text-align:center;padding:24px}.footer{margin-top:14px;border-top:1px solid #d9e4e5;padding-top:7px;color:#526478;font-size:10px}
            .party{margin:14px 0}.party-head{background:#eaf4f3;border:1px solid #d9e4e5;padding:8px 10px;font-weight:700;color:#174d4a;break-after:avoid}
            .document{border:1px solid #d9e4e5;border-top:0;padding:9px 10px;break-inside:avoid}.document-head{display:flex;justify-content:space-between;gap:12px}
            .amounts{display:flex;flex-wrap:wrap;gap:18px;margin-top:7px}.amounts span{display:block;color:#536271;font-size:10px}.amounts strong{font-size:12px}
            .balance{background:#eaf4f3;padding:3px 8px;border-radius:5px}.applications{background:#f5f8f8;margin-top:8px;padding:6px 8px}
            .applications div{display:flex;justify-content:space-between;gap:12px;border-top:1px solid #d9e4e5;padding:3px 0}.applications div:first-of-type{border:0}
            .aging{border:1px solid #c7e1df;background:#f2faf9;border-radius:8px;padding:8px 10px;margin:10px 0}.aging-items{display:flex;flex-wrap:wrap;gap:14px}.aging-items div{min-width:100px}.aging-items span{display:block;color:#536271;font-size:10px}
            </style></head><body><header><div class="eyebrow">Auraly · reporte corporativo</div>
            """);
        html.Append("<h1>").Append(Encode(title)).Append(consolidated ? " · consolidado" : " · detallado")
            .Append("</h1><div class=\"meta\">Corte: ").Append(cutoff.ToString("dd/MM/yyyy", Culture));
        if (from is not null || to is not null)
            html.Append(" · Registro ").Append(from?.ToString("dd/MM/yyyy", Culture) ?? "inicio")
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
        html.Append("</div>");
        foreach (var total in first.Totals)
            html.Append("<div class=\"aging\"><strong>Edades de cartera · ").Append(Encode(total.Currency))
                .Append("</strong><div class=\"aging-items\">")
                .Append(AgeCell("Al día", total.NotDue, total.Currency))
                .Append(AgeCell("1–30 días", total.Overdue1To30, total.Currency))
                .Append(AgeCell("31–60 días", total.Overdue31To60, total.Currency))
                .Append(AgeCell("61–90 días", total.Overdue61To90, total.Currency))
                .Append(AgeCell("Más de 90 días", total.OverdueOver90, total.Currency))
                .Append("</div></div>");
        if (consolidated)
        {
            html.Append("<table><thead><tr><th>Tercero y sede</th><th>Documentos</th><th>Moneda</th><th class=\"num\">Original</th><th class=\"num\">Pagado</th><th class=\"num\">Notas y ajustes</th><th class=\"num\">Saldo</th></tr></thead><tbody>");
            foreach (var row in first.Rows)
            {
                token.ThrowIfCancellationRequested();
                html.Append("<tr><td><strong>").Append(Encode(row.Name))
                    .Append("</strong><br><span class=\"sub\">").Append(Encode(row.Identification))
                    .Append(" · ").Append(Encode(row.Site ?? "Sede principal"))
                    .Append("</span><br><span class=\"sub\">Edades: ")
                    .Append(AgeInline(row)).Append("</span></td><td>")
                    .Append(row.InvoiceCount.ToString("N0", Culture)).Append("</td>");
                html.Append("<td>").Append(Encode(row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Original, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Paid, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.OtherImpact, row.Currency)).Append("</td><td class=\"num\"><strong>")
                    .Append(Money(row.Outstanding, row.Currency)).Append("</strong></td></tr>");
            }
            if (first.TotalCount == 0) html.Append("<tr><td class=\"empty\" colspan=\"7\">No hay documentos para estos filtros.</td></tr>");
            html.Append("</tbody></table>");
        }
        else
        {
            AppendDetail(html, applicationLabel, first.Rows, token);
            if (first.TotalCount == 0) html.Append("<p class=\"empty\">No hay documentos para estos filtros.</p>");
        }
        html.Append("<div class=\"footer\">Auraly · generado ")
            .Append(DateTimeOffset.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", Culture))
            .Append(" · ").Append(first.TotalCount.ToString("N0", Culture))
            .Append(" filas</div></body></html>");
        return html.ToString();
    }

    private static void AppendDetail(StringBuilder html, string applicationLabel,
        IReadOnlyList<PrintRow> rows, CancellationToken token)
    {
        string? previousGroup = null;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var group = string.Join('\u001f', row.PartyId.ToString("N"),
                row.PartySiteId?.ToString("N") ?? "", row.Currency);
            if (group != previousGroup)
            {
                if (previousGroup is not null) html.Append("</section>");
                html.Append("<section class=\"party\"><div class=\"party-head\">")
                    .Append(Encode(row.Name)).Append(" · ").Append(Encode(row.Identification))
                    .Append(" · ").Append(Encode(row.Site ?? "Sede principal"))
                    .Append(" · ").Append(Encode(row.Currency)).Append("</div>");
                previousGroup = group;
            }
            html.Append("<article class=\"document\"><div class=\"document-head\"><strong>")
                .Append(Encode(row.DocumentNumber ?? "Sin número"))
                .Append("</strong><span class=\"sub\">Emisión ").Append(Date(row.IssuedAt))
                .Append(" · Vence ").Append(Date(row.DueDate)).Append(" · ")
                .Append(AgeLabel(row)).Append("</span></div>")
                .Append("<div class=\"amounts\"><div><span>Original</span><strong>")
                .Append(Money(row.Original, row.Currency)).Append("</strong></div><div><span>")
                .Append(applicationLabel == "Abono" ? "Abonado" : "Pagado")
                .Append("</span><strong>").Append(Money(row.Paid, row.Currency))
                .Append("</strong></div><div class=\"balance\"><span>Saldo pendiente</span><strong>")
                .Append(Money(row.Outstanding, row.Currency)).Append("</strong></div></div>");
            if (row.OtherImpact != 0)
                html.Append("<div class=\"sub\">Notas y ajustes: ")
                    .Append(Money(row.OtherImpact, row.Currency)).Append("</div>");
            html.Append("<div class=\"applications\"><strong>").Append(applicationLabel)
                .Append("s aplicados</strong>");
            foreach (var application in row.Applications)
                html.Append("<div><span>").Append(Encode(application.DocumentNumber))
                    .Append(" · ").Append(Date(application.AppliedAt)).Append("</span><strong>")
                    .Append(Money(application.Amount, row.Currency)).Append("</strong></div>");
            if (row.Applications.Count == 0)
                html.Append("<p class=\"sub\">Sin ").Append(applicationLabel.ToLowerInvariant())
                    .Append("s aplicados al corte.</p>");
            html.Append("</div></article>");
        }
        if (previousGroup is not null) html.Append("</section>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string AgeCell(string label, decimal amount, string currency) =>
        $"<div><span>{label}</span><strong>{Money(amount, currency)}</strong></div>";
    private static string AgeInline(PrintRow row) => row.Outstanding <= 0 ? "Sin saldo" : string.Join(" · ", new[]
    {
        ("Al día", row.NotDue), ("1–30", row.Overdue1To30),
        ("31–60", row.Overdue31To60), ("61–90", row.Overdue61To90),
        (">90", row.OverdueOver90)
    }.Where(band => band.Item2 != 0).Select(band => $"{band.Item1}: {Money(band.Item2, row.Currency)}"));
    private static string AgeLabel(PrintRow row) => row.NotDue > 0 ? "Al día" :
        row.Overdue1To30 > 0 ? "1–30 días" : row.Overdue31To60 > 0 ? "31–60 días" :
        row.Overdue61To90 > 0 ? "61–90 días" : row.OverdueOver90 > 0 ? "Más de 90 días" : "Sin saldo";
    private static string Money(decimal amount, string currency) =>
        WebUtility.HtmlEncode($"{amount.ToString(amount == decimal.Round(amount, 2) ? "N2" : "N4", Culture)} {currency}");
    private static string Date(DateTimeOffset? value) => value?.ToString("dd/MM/yyyy", Culture) ?? "—";

    private sealed record PrintApplication(string DocumentNumber, DateTimeOffset AppliedAt, decimal Amount);
    private sealed record PrintRow(Guid PartyId, Guid? PartySiteId,
        string Name, string Identification, string? Site, string Currency,
        int InvoiceCount, string? DocumentNumber, DateTimeOffset? IssuedAt, DateTimeOffset? DueDate,
        decimal Original, decimal Paid, decimal OtherImpact, decimal Outstanding,
        IReadOnlyList<PrintApplication> Applications)
    {
        public decimal NotDue { get; init; }
        public decimal Overdue1To30 { get; init; }
        public decimal Overdue31To60 { get; init; }
        public decimal Overdue61To90 { get; init; }
        public decimal OverdueOver90 { get; init; }
    }
    private sealed record PrintTotal(string Currency, int InvoiceCount, decimal Original, decimal Paid,
        decimal OtherImpact, decimal Outstanding, decimal Overdue)
    {
        public decimal NotDue { get; init; }
        public decimal Overdue1To30 { get; init; }
        public decimal Overdue31To60 { get; init; }
        public decimal Overdue61To90 { get; init; }
        public decimal OverdueOver90 { get; init; }
    }
    private sealed record PrintPage(int TotalCount, IReadOnlyList<PrintTotal> Totals,
        IReadOnlyList<PrintRow> Rows);
}

internal sealed class PortfolioReportPrintException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
