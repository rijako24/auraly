"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Loader2, Printer } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TenantBrand } from "@/components/brand/tenant-brand";
import { receivablesApi, type ReceivablesReportFilters, type ReceivablesReportItem, type ReceivablesReportPage } from "@/services/api/receivables";
import { payablesApi, type PayablesReportFilters, type PayablesReportItem, type PayablesReportPage } from "@/services/api/payables";
import { tenantsApi } from "@/services/api/tenants";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { formatCurrency, formatDate } from "@/lib/utils";

type Direction = "receivable" | "payable";
type ReportMode = "detail" | "summary";
type Row = ReceivablesReportItem | PayablesReportItem;
type AgingAmounts = Pick<Row, "notDueAmount" | "overdue1To30Amount" | "overdue31To60Amount" | "overdue61To90Amount" | "overdueOver90Amount">;
const ageBands = [
  { key: "notDueAmount", label: "Al día" },
  { key: "overdue1To30Amount", label: "1–30 días" },
  { key: "overdue31To60Amount", label: "31–60 días" },
  { key: "overdue61To90Amount", label: "61–90 días" },
  { key: "overdueOver90Amount", label: "Más de 90 días" },
] as const;
function ageLabel(amounts: AgingAmounts) {
  return ageBands.find(band => amounts[band.key] > 0)?.label ?? "Sin saldo";
}
type Props = {
  direction: Direction;
  onClose: () => void;
  initialPartyId?: string;
  initialPartySiteId?: string;
  initialPartyLabel?: string;
  initialFrom?: string;
  initialTo?: string;
  initialStatus?: string;
  initialOverdue?: boolean;
  initialSearch?: string;
  initialConceptId?: string;
  initialConceptLabel?: string;
};

const today = () => {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
};

export function PortfolioReportDialog({ direction, onClose, initialPartyId,
  initialPartySiteId, initialPartyLabel, initialFrom, initialTo, initialStatus, initialOverdue,
  initialSearch, initialConceptId, initialConceptLabel }: Props) {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const tenantName = useTenantContextStore(state => state.tenants.find(item => item.tenantId === state.selectedTenantId)?.name ?? "Organización");
  const branding = useQuery({ queryKey: ["tenant-branding", tenantId], queryFn: tenantsApi.getBranding,
    enabled: Boolean(tenantId), staleTime: 10 * 60 * 1000 });
  const brandName = branding.data?.legalName ?? branding.data?.displayName ?? tenantName;
  const [mode, setMode] = useState<ReportMode>("detail");
  const [page, setPage] = useState(1);
  const [cutoff, setCutoff] = useState(today);
  const [sortBy, setSortBy] = useState<string | undefined>();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("asc");
  const [printing, setPrinting] = useState(false);
  const [printHtml, setPrintHtml] = useState<string | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);
  const invalidDates = Boolean(!cutoff || (initialFrom && initialFrom > cutoff) ||
    (initialTo && initialTo > cutoff) || (initialFrom && initialTo && initialFrom > initialTo));
  const filters = { page, pageSize: 50, consolidated: mode === "summary", cutoff,
    ...(direction === "receivable" ? { customerId: initialPartyId } : { supplierId: initialPartyId, conceptId: initialConceptId }),
    partySiteId: initialPartySiteId, from: initialFrom || undefined, to: initialTo || undefined,
    search: initialSearch || undefined, status: initialStatus,
    overdueOnly: initialOverdue === true, sortBy,
    sortDirection: sortBy ? sortDirection : undefined };
  const report = useQuery<ReceivablesReportPage | PayablesReportPage>({
    queryKey: ["portfolio-report", direction, businessId, filters],
    queryFn: () => direction === "receivable"
      ? receivablesApi.report(filters as ReceivablesReportFilters)
      : payablesApi.report(filters as PayablesReportFilters),
    enabled: Boolean(businessId && !invalidDates),
    staleTime: 30_000,
  });
  const items: Row[] = report.data?.items ?? [];
  const totalPages = report.data?.totalPages ?? 0;
  const totalCount = report.data?.totalCount ?? 0;
  const currencyTotals = report.data && "currencyTotals" in report.data
    ? report.data.currencyTotals
    : report.data ? [{ currencyCode: "COP", invoiceCount: report.data.totalInvoiceCount,
      originalAmount: report.data.totalOriginal, paidAmount: report.data.totalPaid,
      outstandingAmount: report.data.totalOutstanding,
      overdueAmount: report.data.totalOverdue,
      otherImpact: report.data.totalOtherImpact,
      notDueAmount: report.data.totalNotDue,
      overdue1To30Amount: report.data.totalOverdue1To30,
      overdue31To60Amount: report.data.totalOverdue31To60,
      overdue61To90Amount: report.data.totalOverdue61To90,
      overdueOver90Amount: report.data.totalOverdueOver90 }] : [];
  const hasOtherImpact = currencyTotals.some(total => total.otherImpact !== 0) ||
    items.some(item => item.otherImpact !== 0);
  const title = direction === "receivable" ? "Informe de cuentas por cobrar" : "Informe de cuentas por pagar";

  function order(column: string) {
    setSortDirection(current => sortBy === column && current === "asc" ? "desc" : "asc");
    setSortBy(column); setPage(1);
  }
  function chooseMode(next: ReportMode) {
    setMode(next);
    setPage(1);
    setSortBy(undefined);
    setSortDirection("asc");
  }
  function sortButton(column: string, label: string) {
    return <button type="button" onClick={() => order(column)} className="inline-flex items-center gap-1 text-left font-semibold hover:text-teal-700">
      {label}{sortBy === column && (sortDirection === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />)}
    </button>;
  }

  async function printReport() {
    if (printing || invalidDates) return;
    setPrinting(true); setPrintError(null); setPrintHtml(null);
    try {
      const html = direction === "receivable"
        ? await receivablesApi.printReport(filters as ReceivablesReportFilters)
        : await payablesApi.printReport(filters as PayablesReportFilters);
      setPrintHtml(html);
    } catch (error) {
      setPrintError(error instanceof Error ? error.message : "No se pudo preparar el informe para imprimir.");
    } finally { setPrinting(false); }
  }

  const filterSummary = [initialSearch && `Búsqueda: ${initialSearch}`,
    initialPartyId && (initialPartyLabel ?? "Tercero y sede seleccionados"), initialStatus && `Estado: ${initialStatus}`,
    initialOverdue && "Solo vencidas", initialFrom && `Desde ${initialFrom}`,
    initialTo && `Hasta ${initialTo}`, initialConceptId && (initialConceptLabel ?? "Concepto seleccionado")]
    .filter(Boolean).join(" · ") || "Todos los documentos";

  const grouped = new Map<string, Row[]>();
  if (mode === "detail") for (const item of items) {
    const partyId = "customerId" in item ? item.customerId : item.supplierId;
    const key = `${partyId}:${item.partySiteId}:${item.currencyCode}`;
    const group = grouped.get(key) ?? [];
    group.push(item);
    grouped.set(key, group);
  }

  return <Dialog open onOpenChange={open => !open && onClose()}>
    <DialogContent className="flex h-[96dvh] max-h-[96dvh] w-[98vw] max-w-[1500px] flex-col overflow-hidden p-0">
      <DialogHeader className="shrink-0 border-b px-5 py-4 text-left">
        <div className="flex items-start gap-4">
          <TenantBrand displayName={brandName} logoUrl={branding.data?.logoUrl}
            className="w-24 shrink-0 flex-col gap-1 text-center text-xs [&>span:last-child]:max-w-full [&>span:last-child]:whitespace-normal [&>span:last-child]:break-words" />
          <div className="min-w-0 pr-5 pt-1"><DialogTitle className="leading-snug">{title}</DialogTitle>
            <DialogDescription className="mt-1">Informe al corte · {filterSummary}</DialogDescription></div>
        </div>
      </DialogHeader>
      <div className="flex flex-wrap items-center gap-3 border-b bg-muted/30 px-5 py-3">
        <div className="inline-flex rounded-lg border bg-background p-1" role="group" aria-label="Tipo de informe">
          <Button size="sm" variant={mode === "detail" ? "default" : "ghost"} onClick={() => chooseMode("detail")}>Detallado</Button>
          <Button size="sm" variant={mode === "summary" ? "default" : "ghost"} onClick={() => chooseMode("summary")}>Consolidado</Button>
        </div>
        <label className="flex items-center gap-2 text-sm">Corte <span className="w-40"><DatePicker value={cutoff} min={initialFrom || undefined}
          onChange={value => { setCutoff(value); setPage(1); }} /></span></label>
        <span className="ml-auto text-sm text-muted-foreground">{totalCount.toLocaleString("es-CO")} {mode === "detail" ? "documentos" : "grupos"}</span>
      </div>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto bg-white p-5 text-slate-950">
        {invalidDates && <p role="alert" className="text-sm text-destructive">Revisa las fechas: la emisión debe quedar dentro del corte.</p>}
        {report.isLoading && !invalidDates && <p className="flex items-center gap-2 py-8 text-sm"><Loader2 className="h-4 w-4 animate-spin" />Cargando informe…</p>}
        {report.isError && <p role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">No se pudo cargar el informe. <Button variant="link" onClick={() => void report.refetch()}>Reintentar</Button></p>}
        {report.data && !invalidDates && <>
          <div className="flex flex-wrap gap-3">{currencyTotals.map(total => <div key={total.currencyCode} className="min-w-56 flex-1 rounded-xl border bg-slate-50 p-3 text-sm"><strong>{total.currencyCode} · {total.invoiceCount} documentos</strong><p className="mt-1 text-slate-600">Original {formatCurrency(total.originalAmount,total.currencyCode)} · Pagado {formatCurrency(total.paidAmount,total.currencyCode)}{total.otherImpact !== 0 ? ` · Notas y ajustes ${formatCurrency(total.otherImpact,total.currencyCode)}` : ""}</p><p className="font-semibold">Saldo {formatCurrency(total.outstandingAmount,total.currencyCode)} · Vencido {formatCurrency(total.overdueAmount,total.currencyCode)}</p></div>)}</div>
          <section className="rounded-xl border border-teal-100 bg-teal-50/50 p-4" aria-label="Edades de cartera">
            <div className="mb-3 flex flex-wrap items-baseline justify-between gap-1"><h3 className="font-semibold text-teal-950">Edades de cartera</h3><p className="text-xs text-slate-600">Saldo pendiente a la fecha de corte · todos los documentos filtrados</p></div>
            {currencyTotals.map(total => <div key={total.currencyCode} className="mb-2 last:mb-0"><p className="mb-1 text-xs font-semibold text-teal-800">{total.currencyCode}</p><div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
              {ageBands.map(band => <div key={band.key} className="rounded-lg border bg-white p-2"><span className="block text-xs text-slate-600">{band.label}</span><strong className="text-sm tabular-nums">{formatCurrency(total[band.key],total.currencyCode)}</strong></div>)}
            </div></div>)}
          </section>
          {mode === "detail" ? <div className="space-y-5">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="font-medium text-slate-700">Ordenar documentos por</span>
              <Select value={sortBy ?? "default"} onValueChange={value => { setSortBy(value === "default" ? undefined : value); setSortDirection("asc"); setPage(1); }}>
                <SelectTrigger aria-label="Ordenar documentos por" className="w-48 bg-white"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="default">Orden predeterminado</SelectItem>
                  <SelectItem value="documentNumber">Número de documento</SelectItem>
                  <SelectItem value="issuedAt">Fecha de emisión</SelectItem>
                  <SelectItem value="dueDate">Fecha de vencimiento</SelectItem>
                  <SelectItem value="originalAmount">Valor original</SelectItem>
                  <SelectItem value="paidAmount">{direction === "receivable" ? "Valor abonado" : "Valor pagado"}</SelectItem>
                  <SelectItem value="outstandingAmount">Saldo pendiente</SelectItem>
                </SelectContent>
              </Select>
              {sortBy && <Button type="button" size="sm" variant="outline" onClick={() => { setSortDirection(value => value === "asc" ? "desc" : "asc"); setPage(1); }}>
                {sortDirection === "asc" ? <ArrowUp className="mr-1 h-4 w-4" /> : <ArrowDown className="mr-1 h-4 w-4" />}
                {sortDirection === "asc" ? "Ascendente" : "Descendente"}
              </Button>}
            </div>
            {Array.from(grouped.entries()).map(([key, group]) => {
              const first = group[0];
              const name = "customerName" in first ? first.customerName : first.supplierName;
              return <section key={key} className="overflow-hidden rounded-xl border">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-teal-50 px-4 py-3">
                  <div><p className="text-[11px] font-semibold uppercase tracking-wide text-teal-700">{direction === "receivable" ? "Cliente" : "Proveedor"} · {first.partySiteName ?? "Sede principal"}</p><h3 className="text-base font-semibold text-teal-950">{name}</h3><p className="text-xs text-slate-600">Identificación {first.identification}</p></div>
                  <span className="text-xs font-medium text-teal-800">{group.length} {group.length === 1 ? "documento" : "documentos"} en esta página · {first.currencyCode}</span>
                </div>
                <div className="divide-y">{group.map(item => <article key={("receivableId" in item ? item.receivableId : item.payableId) ?? item.documentNumber} className="p-4">
                  <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
                    <div><p className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{direction === "receivable" ? "Factura" : "Factura o gasto"}</p><div className="flex flex-wrap items-center gap-2"><p className="font-semibold text-slate-950">{item.documentNumber ?? "Sin número"}</p>
                      <span className="rounded-full border border-teal-200 bg-teal-50 px-2 py-0.5 text-xs font-medium text-teal-800">{ageLabel(item)}</span></div>
                      <p className="mt-1 text-xs text-slate-600">Emisión: {item.issuedAt ? formatDate(item.issuedAt) : "—"} <span className="mx-1">·</span> Vence: {item.dueDate ? formatDate(item.dueDate) : "—"}</p></div>
                    <div className="grid w-full grid-cols-2 gap-x-4 gap-y-2 text-sm sm:w-auto sm:grid-cols-3 sm:gap-x-8">
                      <div><span className="block text-xs text-slate-500">Original</span><strong className="tabular-nums">{formatCurrency(item.originalAmount,item.currencyCode)}</strong></div>
                      <div><span className="block text-xs text-slate-500">{direction === "receivable" ? "Abonado" : "Pagado"}</span><strong className="tabular-nums">{formatCurrency(item.paidAmount,item.currencyCode)}</strong></div>
                      <div className="col-span-2 rounded-md bg-teal-50 px-2 py-1 sm:col-span-1"><span className="block text-xs text-teal-800">Saldo pendiente</span><strong className="tabular-nums text-teal-950">{formatCurrency(item.outstandingAmount,item.currencyCode)}</strong></div>
                    </div>
                  </div>
                  {item.otherImpact !== 0 && <p className="mt-2 text-xs text-slate-600">Notas y ajustes: <strong>{formatCurrency(item.otherImpact,item.currencyCode)}</strong></p>}
                  <div className="mt-3 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm">
                    <p className="font-semibold text-slate-700">{direction === "receivable" ? "Abonos de esta factura" : "Pagos de esta obligación"}</p>
                    {item.applications?.length ? <ul className="mt-2 divide-y divide-slate-200">{item.applications.map((application, line) => <li key={`${application.documentNumber}-${line}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2 first:pt-0 last:pb-0"><span><strong>{application.documentNumber}</strong><span className="ml-2 text-xs text-slate-500">{formatDate(application.appliedAt)}</span></span><strong className="tabular-nums">{formatCurrency(application.amount,item.currencyCode)}</strong></li>)}</ul>
                      : <p className="mt-1 text-xs text-slate-500">Sin {direction === "receivable" ? "abonos" : "pagos"} aplicados al corte.</p>}
                  </div>
                </article>)}</div>
              </section>;
            })}
          </div> : <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[760px] text-sm"><thead className="bg-slate-100 text-left"><tr>
            <th className="p-3">{sortButton("name", direction === "receivable" ? "Cliente / sede" : "Proveedor / sede")}</th><th className="p-3">{sortButton("invoiceCount", "Documentos")}</th><th className="p-3">Moneda</th>
            <th className="p-3 text-right">{sortButton("originalAmount", "Original")}</th><th className="p-3 text-right">{sortButton("paidAmount", "Pagado")}</th>
            {hasOtherImpact && <th className="p-3 text-right">Notas y ajustes</th>}<th className="p-3 text-right">{sortButton("outstandingAmount", "Saldo")}</th>
          </tr></thead><tbody>{items.map((item, index) => <tr key={`${"customerId" in item ? item.customerId : item.supplierId}-${item.partySiteId}-${item.currencyCode}-${index}`} className="border-t">
            <td className="p-3"><strong>{"customerName" in item ? item.customerName : item.supplierName}</strong><span className="block text-xs text-slate-500">{item.identification} · {item.partySiteName ?? "Sede principal"}</span>
              <div className="mt-2 space-y-0.5 text-xs text-slate-600">{ageBands.filter(band => item[band.key] !== 0).map(band => <div key={band.key}>{band.label}: <strong>{formatCurrency(item[band.key],item.currencyCode)}</strong></div>)}</div></td>
            <td className="p-3">{item.invoiceCount}</td><td className="p-3">{item.currencyCode}</td><td className="p-3 text-right tabular-nums">{formatCurrency(item.originalAmount,item.currencyCode)}</td>
            <td className="p-3 text-right tabular-nums">{formatCurrency(item.paidAmount,item.currencyCode)}</td>{hasOtherImpact && <td className="p-3 text-right tabular-nums">{formatCurrency(item.otherImpact,item.currencyCode)}</td>}
            <td className="p-3 text-right font-semibold tabular-nums">{formatCurrency(item.outstandingAmount,item.currencyCode)}</td>
          </tr>)}</tbody></table></div>}
          {items.length === 0 && <p className="rounded-xl border p-8 text-center text-sm text-slate-500">No hay documentos para estos filtros.</p>}
          <div className="flex items-center justify-between gap-3 text-sm"><span>Página {page} de {Math.max(totalPages,1)}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={page <= 1 || report.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page >= totalPages || report.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div>
        </>}
      </div>
      <DialogFooter className="shrink-0 border-t p-4 sm:justify-between">
        <div className="flex items-center gap-3"><Button variant="outline" disabled={invalidDates || printing || report.isLoading || report.isError || !report.data} onClick={() => void printReport()}><Printer className="mr-2 h-4 w-4" />{printing ? "Preparando informe…" : "Imprimir informe completo"}</Button>{printError && <span role="alert" className="max-w-md text-sm text-destructive">{printError}</span>}</div>
        <Button variant="outline" onClick={onClose}>Cerrar</Button>
      </DialogFooter>
      {printHtml && <iframe title="Impresión del informe" className="absolute h-0 w-0 border-0" srcDoc={printHtml}
        onLoad={event => { event.currentTarget.contentWindow?.focus(); event.currentTarget.contentWindow?.print(); }} />}
    </DialogContent>
  </Dialog>;
}
