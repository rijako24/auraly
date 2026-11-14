using System.Globalization;
using System.Net;
using System.Text;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api;

internal static class PortfolioReportPrint
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");

    public static async Task<string> ReceivablesAsync(ReceivablesService service,
        ReceivablesUserIdentity user, ReceivablesReportQuery query, ITenantService tenants, CancellationToken token)
    {
        var result = await service.PrintReportAsync(user, query, token);
        var brand = await tenants.GetPrintBrandingAsync(user.TenantId, token);
        return Build("Cuentas por cobrar", "Abono", query.Consolidated, query.IncludeApplications, query.From, query.To, query.Cutoff,
            new PrintPage(result.TotalCount,
                [new PrintTotal("COP", result.TotalInvoiceCount, result.TotalOriginal, result.TotalPaid,
                    result.TotalOtherImpact, result.TotalOutstanding, result.TotalOverdue)
                {
                    NotDue = result.TotalNotDue, Overdue1To30 = result.TotalOverdue1To30,
                    Overdue31To60 = result.TotalOverdue31To60, Overdue61To90 = result.TotalOverdue61To90,
                    OverdueOver90 = result.TotalOverdueOver90,
                    AvailableAdvances = result.TotalAvailableAdvances, AvailableCredits = result.TotalAvailableCredits,
                    NetAmount = result.TotalNetAmount
                }],
                result.Items.Select(item => new PrintRow(item.CustomerId ?? item.PartyId ??
                    throw new InvalidOperationException("La cuenta por cobrar no tiene deudor."), item.PartySiteId,
                    item.CustomerName, item.Identification, item.PartySiteName,
                    item.CurrencyCode, item.InvoiceCount, item.DocumentNumber,
                    item.IssuedAt, item.DueDate, item.OriginalAmount, item.PaidAmount,
                    item.OtherImpact, item.OutstandingAmount, item.Applications?
                        .Select(application => new PrintApplication(application.DocumentNumber,
                            application.AppliedAt, application.Amount)).ToArray() ?? [])
                {
                    NotDue = item.NotDueAmount, Overdue1To30 = item.Overdue1To30Amount,
                    Overdue31To60 = item.Overdue31To60Amount, Overdue61To90 = item.Overdue61To90Amount,
                    OverdueOver90 = item.OverdueOver90Amount,
                    TermDays = item.TermDays, AgeDays = item.AgeDays, DaysPastDue = item.DaysPastDue,
                    Contact = string.Join(" · ", new[] { item.LegalName, item.Address, item.City, item.Phone, item.Email }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    CreditLimit = item.CreditLimit, GroupTotals = item.GroupTotals,
                    AvailableAdvances = item.AvailableAdvances, AvailableCredits = item.AvailableCredits, NetAmount = item.NetAmount,
                    IsCreditBalance = item.IsCreditBalance
                }).ToArray()), brand, token, query.TemplateVersion,
                string.Join(" · ", new[] {
                    query.DocumentNumber is null ? null : $"Factura: {query.DocumentNumber}",
                    query.Search is null ? null : $"Búsqueda: {query.Search}",
                    query.IssuedFrom is null ? null : $"Emisión desde {query.IssuedFrom:dd/MM/yyyy}",
                    query.IssuedTo is null ? null : $"Emisión hasta {query.IssuedTo:dd/MM/yyyy}",
                    query.MinimumAgeDays is null ? null : $"Edad desde {query.MinimumAgeDays} días",
                    query.MaximumAgeDays is null ? null : $"Edad hasta {query.MaximumAgeDays} días",
                    query.MinimumDaysPastDue is null ? null : $"Mora desde {query.MinimumDaysPastDue} días",
                    query.MaximumDaysPastDue is null ? null : $"Mora hasta {query.MaximumDaysPastDue} días",
                    query.OverdueOnly ? "Solo vencidas" : null, query.OutstandingOnly ? "Solo con saldo" : null,
                    query.Status is null ? null : $"Estado: {query.Status}"
                }.Where(value => value is not null)));
    }

    public static async Task<string> PayablesAsync(PayablesService service,
        PayablesUserIdentity user, PayablesReportQuery query, ITenantService tenants, CancellationToken token)
    {
        var result = await service.PrintReportAsync(user, query, token);
        var brand = await tenants.GetPrintBrandingAsync(user.TenantId, token);
        return Build("Cuentas por pagar", "Pago", query.Consolidated, query.IncludeApplications, query.From, query.To, query.Cutoff,
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
                }).ToArray()), brand, token);
    }

    private static string Build(string title, string applicationLabel, bool consolidated, bool includeApplications,
        DateOnly? from, DateOnly? to, DateOnly cutoff,
        PrintPage first, TenantBrandingDto brand, CancellationToken token, int templateVersion = 2,
        string? filterSummary = null)
    {
        var html = new StringBuilder(Math.Max(4_096, first.TotalCount * 700));
        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"auraly-report-template\" content=\"portfolio-v")
            .Append(templateVersion).Append("\"><title>Informe de cartera</title>");
        if (templateVersion >= 3) html.Append("<style>table{font-size:10px}tfoot{font-weight:bold;background:#eaf4f3}</style>");
        html.Append("""
            <style>@page{size:landscape;margin:10mm}*{box-sizing:border-box}body{margin:0;color:#17212b;font:12px/1.4 Arial,sans-serif}
            header{border-bottom:2px solid #0c7772;padding:0 0 12px;margin-bottom:14px;display:flex;gap:20px;align-items:center}h1{margin:0 0 4px;font-size:22px}
            .brand{width:150px;text-align:center;flex-shrink:0}.brand img{display:block;max-width:120px;max-height:65px;margin:0 auto 5px}
            .eyebrow{color:#0c7772;font-weight:700;letter-spacing:.15em;text-transform:uppercase;font-size:10px}
            .meta{color:#526478}.totals{display:flex;flex-wrap:wrap;gap:8px;margin:12px 0}.total{border:1px solid #d9e4e5;border-radius:8px;padding:7px 10px}
            table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #d9e4e5;padding:6px;text-align:left;vertical-align:top}
            th{background:#eaf4f3;color:#174d4a}td.num,th.num{text-align:right;white-space:nowrap}.sub{font-size:10px;color:#536271}
            tr{break-inside:avoid}thead{display:table-header-group}.empty{text-align:center;padding:24px}.footer{margin-top:14px;border-top:1px solid #d9e4e5;padding-top:7px;color:#526478;font-size:10px}
            .party{margin:14px 0}.party-head{background:#eaf4f3;border:1px solid #d9e4e5;padding:8px 10px;font-weight:700;color:#174d4a;break-after:avoid}
            .applications{background:#f5f8f8;margin-top:8px;padding:6px 8px}
            .applications div{display:flex;justify-content:space-between;gap:12px;border-top:1px solid #d9e4e5;padding:3px 0}.applications div:first-of-type{border:0}
            .aging{border:1px solid #c7e1df;background:#f2faf9;border-radius:8px;padding:8px 10px;margin:10px 0}.aging-items{display:flex;flex-wrap:wrap;gap:14px}.aging-items div{min-width:100px}.aging-items span{display:block;color:#536271;font-size:10px}
            </style></head><body><header><div class="brand">
            """);
        if (brand.LogoUrl is { Length: > 0 } logo)
            html.Append("<img alt=\"Logo de la empresa\" src=\"").Append(Encode(logo)).Append("\">");
        html.Append("<strong>").Append(Encode(brand.LegalName ?? brand.DisplayName)).Append("</strong>");
        if (brand.Nit is { Length: > 0 } nit)
            html.Append("<div class=\"sub\">NIT ").Append(Encode(nit))
                .Append(brand.VerificationDigit is { Length: > 0 } digit ? $"-{Encode(digit)}" : "").Append("</div>");
        html.Append("</div><div><div class=\"eyebrow\">Auraly · reporte corporativo</div><h1>").Append(Encode(title)).Append(consolidated ? " · consolidado" : " · detallado")
            .Append("</h1><div class=\"meta\">Corte: ").Append(cutoff.ToString("dd/MM/yyyy", Culture));
        if (from is not null || to is not null)
            html.Append(" · Registro ").Append(from?.ToString("dd/MM/yyyy", Culture) ?? "inicio")
                .Append(" a ").Append(to?.ToString("dd/MM/yyyy", Culture) ?? "corte");
        html.Append(" · ").Append(first.TotalCount.ToString("N0", Culture)).Append(" filas</div>");
        if (templateVersion >= 3)
            html.Append("<div class=\"sub\">").Append(Encode(filterSummary ?? "Todos los documentos"))
                .Append("</div><div class=\"sub\">Edad: días desde emisión. Mora: días desde vencimiento; negativa = sin vencer. Contacto y cupo vigentes.</div>");
        html.Append("</div></header><div class=\"totals\">");
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
                .Append("</strong>");
            if (templateVersion >= 4)
                AppendCredits(html, total.AvailableAdvances, total.AvailableCredits, total.NetAmount, total.Currency);
            html.Append("</div>");
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
                    .Append(" · ").Append(Encode(row.Site ?? (templateVersion >= 4 ? "Sin sede identificada" : "Sede principal")))
                    .Append("</span><br><span class=\"sub\">Edades: ")
                    .Append(AgeInline(row)).Append("</span></td><td>")
                    .Append(row.InvoiceCount.ToString("N0", Culture)).Append("</td>");
                html.Append("<td>").Append(Encode(row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Original, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.Paid, row.Currency)).Append("</td><td class=\"num\">")
                    .Append(Money(row.OtherImpact, row.Currency)).Append("</td><td class=\"num\"><strong>")
                    .Append(Money(row.Outstanding, row.Currency)).Append("</strong>");
                if (templateVersion >= 4)
                    AppendCredits(html, row.AvailableAdvances, row.AvailableCredits, row.NetAmount, row.Currency);
                html.Append("</td></tr>");
            }
            if (first.TotalCount == 0) html.Append("<tr><td class=\"empty\" colspan=\"7\">No hay documentos para estos filtros.</td></tr>");
            html.Append("</tbody></table>");
        }
        else
        {
            AppendDetail(html, applicationLabel, includeApplications, first.Rows, token, templateVersion >= 3, templateVersion >= 4);
            if (first.TotalCount == 0) html.Append("<p class=\"empty\">No hay documentos para estos filtros.</p>");
        }
        html.Append("<div class=\"footer\">Auraly · generado ")
            .Append(DateTimeOffset.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", Culture))
            .Append(" · ").Append(first.TotalCount.ToString("N0", Culture))
            .Append(" filas</div></body></html>");
        return html.ToString();
    }

    private static void AppendDetail(StringBuilder html, string applicationLabel, bool includeApplications,
        IReadOnlyList<PrintRow> rows, CancellationToken token, bool expanded, bool withCredits)
    {
        string? previousGroup = null;
        PrintRow? previous = null;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var group = string.Join('\u001f', row.PartyId.ToString("N"),
                row.PartySiteId?.ToString("N") ?? "", row.Currency);
            if (group != previousGroup)
            {
                if (previousGroup is not null) CloseDetailGroup(html, previous!, expanded, withCredits);
                html.Append("<section class=\"party\"><div class=\"party-head\">")
                    .Append(Encode(row.Name)).Append(" · ").Append(Encode(row.Identification))
                    .Append(" · ").Append(Encode(row.Site ?? (withCredits ? "Sin sede identificada" : "Sede principal")))
                    .Append(" · ").Append(Encode(row.Currency));
                if (expanded) html.Append("<div class=\"sub\">").Append(Encode(row.Contact ?? ""))
                    .Append(" · Cupo vigente: ").Append(row.CreditLimit is { } limit ? Money(limit, row.Currency) : "Sin cupo definido").Append("</div>");
                html.Append("</div><table><thead><tr><th>Documento</th><th>Emisión</th><th>Vencimiento</th>");
                if (expanded) html.Append("<th class=\"num\">Plazo (días)</th><th class=\"num\">Edad (días)</th><th class=\"num\">Mora (días)</th>");
                html.Append("<th class=\"num\">Original</th><th class=\"num\">")
                    .Append(applicationLabel == "Abono" ? "Abonado" : "Pagado")
                    .Append("</th><th class=\"num\">Notas y ajustes</th><th class=\"num\">Saldo pendiente</th><th>")
                    .Append(expanded ? "Tramo" : "Edad").Append("</th></tr></thead><tbody>");
                previousGroup = group;
            }
            html.Append("<tr><td><strong>")
                .Append(Encode(row.DocumentNumber ?? "Sin número"))
                .Append("</strong>");
            if (withCredits && row.IsCreditBalance)
                AppendCredits(html,row.AvailableAdvances,row.AvailableCredits,row.NetAmount,row.Currency);
            html.Append("</td><td>").Append(Date(row.IssuedAt))
                .Append("</td><td>").Append(Date(row.DueDate)).Append("</td>");
            if (expanded) html.Append("<td class=\"num\">").Append(row.TermDays?.ToString(Culture) ?? "—")
                .Append("</td><td class=\"num\">").Append(row.AgeDays?.ToString(Culture) ?? "—")
                .Append("</td><td class=\"num\">").Append(row.DaysPastDue?.ToString(Culture) ?? "—").Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(Money(row.Original, row.Currency)).Append("</td><td class=\"num\">")
                .Append(Money(row.Paid, row.Currency)).Append("</td><td class=\"num\">")
                .Append(Money(row.OtherImpact, row.Currency)).Append("</td><td class=\"num\"><strong>")
                .Append(Money(row.Outstanding, row.Currency)).Append("</strong></td><td>")
                .Append(AgeLabel(row)).Append("</td></tr>");
            previous = row;
            if (!includeApplications || row.IsCreditBalance) continue;
            html.Append("<tr><td colspan=\"").Append(expanded ? 11 : 8).Append("\"><div class=\"applications\"><strong>").Append(applicationLabel)
                .Append("s aplicados</strong>");
            foreach (var application in row.Applications)
                html.Append("<div><span>").Append(Encode(application.DocumentNumber))
                    .Append(" · ").Append(Date(application.AppliedAt)).Append("</span><strong>")
                    .Append(Money(application.Amount, row.Currency)).Append("</strong></div>");
            if (row.Applications.Count == 0)
                html.Append("<p class=\"sub\">Sin ").Append(applicationLabel.ToLowerInvariant())
                    .Append("s aplicados al corte.</p>");
            html.Append("</div></td></tr>");
        }
        if (previousGroup is not null) CloseDetailGroup(html, previous!, expanded, withCredits);
    }

    private static void CloseDetailGroup(StringBuilder html, PrintRow row, bool expanded, bool withCredits)
    {
        html.Append("</tbody>");
        if (expanded && row.GroupTotals is { } total)
        {
            html.Append("<tfoot><tr><td colspan=\"6\">Subtotal cliente / sede · ").Append(total.InvoiceCount)
                .Append(" documentos filtrados</td><td class=\"num\">").Append(Money(total.OriginalAmount,row.Currency))
                .Append("</td><td class=\"num\">").Append(Money(total.PaidAmount,row.Currency))
                .Append("</td><td class=\"num\">").Append(Money(total.OtherImpact,row.Currency))
                .Append("</td><td class=\"num\">").Append(Money(total.OutstandingAmount,row.Currency))
                .Append("</td><td></td></tr><tr><td colspan=\"11\"><div class=\"aging-items\">")
                .Append(AgeCell("Al día",total.NotDueAmount,row.Currency))
                .Append(AgeCell("1–30 días",total.Overdue1To30Amount,row.Currency))
                .Append(AgeCell("31–60 días",total.Overdue31To60Amount,row.Currency))
                .Append(AgeCell("61–90 días",total.Overdue61To90Amount,row.Currency))
                .Append(AgeCell("Más de 90 días",total.OverdueOver90Amount,row.Currency))
                .Append("</div>");
            if (withCredits) AppendCredits(html, total.AvailableAdvances, total.AvailableCredits, total.NetAmount, row.Currency);
            html.Append("</td></tr></tfoot>");
        }
        html.Append("</table></section>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static void AppendCredits(StringBuilder html, decimal advances, decimal credits, decimal net, string currency) =>
        html.Append("<div class=\"sub\">Anticipos disponibles: ").Append(Money(advances,currency))
            .Append(" · Otros saldos a favor: ").Append(Money(credits,currency))
            .Append(" · <strong>Neto comercial: ").Append(Money(net,currency)).Append("</strong></div>");
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
        public decimal AvailableAdvances { get; init; }
        public decimal AvailableCredits { get; init; }
        public decimal NetAmount { get; init; }
        public bool IsCreditBalance { get; init; }
        public int? TermDays { get; init; }
        public int? AgeDays { get; init; }
        public int? DaysPastDue { get; init; }
        public string? Contact { get; init; }
        public decimal? CreditLimit { get; init; }
        public ReceivablesReportGroupTotals? GroupTotals { get; init; }
        public decimal NotDue { get; init; }
        public decimal Overdue1To30 { get; init; }
        public decimal Overdue31To60 { get; init; }
        public decimal Overdue61To90 { get; init; }
        public decimal OverdueOver90 { get; init; }
    }
    private sealed record PrintTotal(string Currency, int InvoiceCount, decimal Original, decimal Paid,
        decimal OtherImpact, decimal Outstanding, decimal Overdue)
    {
        public decimal AvailableAdvances { get; init; }
        public decimal AvailableCredits { get; init; }
        public decimal NetAmount { get; init; }
        public decimal NotDue { get; init; }
        public decimal Overdue1To30 { get; init; }
        public decimal Overdue31To60 { get; init; }
        public decimal Overdue61To90 { get; init; }
        public decimal OverdueOver90 { get; init; }
    }
    private sealed record PrintPage(int TotalCount, IReadOnlyList<PrintTotal> Totals,
        IReadOnlyList<PrintRow> Rows);
}
