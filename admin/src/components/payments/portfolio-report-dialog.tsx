"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, ArrowDown, ArrowUp, FileText, Layers3, Loader2, Printer } from "lucide-react";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { partiesApi, type PartySiteRoleOption } from "@/services/api/parties";
import { receivablesApi, type ReceivablesReportFilters, type ReceivablesReportItem, type ReceivablesReportPage } from "@/services/api/receivables";
import { payablesApi, type PayablesReportFilters, type PayablesReportItem, type PayablesReportPage } from "@/services/api/payables";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDate } from "@/lib/utils";

type Direction = "receivable" | "payable";
type ReportMode = "detail" | "summary";
type Row = ReceivablesReportItem | PayablesReportItem;
type Props = {
  direction: Direction;
  onClose: () => void;
  initialPartyId?: string;
  initialPartySiteId?: string;
  initialPartyOption?: PagedEntityOption | null;
  initialFrom?: string;
  initialTo?: string;
  initialStatus?: string;
  initialOverdue?: boolean;
};

const today = () => {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
};

export function PortfolioReportDialog({ direction, onClose, initialPartyId,
  initialPartySiteId, initialPartyOption, initialFrom, initialTo,
  initialStatus, initialOverdue }: Props) {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [mode, setMode] = useState<ReportMode | null>(null);
  const [page, setPage] = useState(1);
  const [cutoff, setCutoff] = useState(today);
  const [from, setFrom] = useState(initialFrom ?? "");
  const [to, setTo] = useState(initialTo ?? "");
  const [status, setStatus] = useState(initialStatus ?? "all");
  const [outstandingOnly, setOutstandingOnly] = useState(false);
  const [overdueOnly, setOverdueOnly] = useState(initialOverdue ?? false);
  const [partyId, setPartyId] = useState(initialPartyId);
  const [siteId, setSiteId] = useState(initialPartySiteId);
  const [partyOption, setPartyOption] = useState<PagedEntityOption | null>(initialPartyOption ?? null);
  const [sortBy, setSortBy] = useState<string | undefined>();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("asc");
  const [printing, setPrinting] = useState(false);
  const [printError, setPrintError] = useState<string | null>(null);
  const invalidDates = Boolean(!cutoff || (from && from > cutoff) ||
    (to && to > cutoff) || (from && to && from > to));
  const filters = { page, pageSize: 50, consolidated: mode === "summary", cutoff,
    ...(direction === "receivable" ? { customerId: partyId } : { supplierId: partyId }),
    partySiteId: siteId, from: from || undefined, to: to || undefined,
    status: status === "all" ? undefined : status,
    outstandingOnly, overdueOnly, sortBy, sortDirection };
  const report = useQuery<ReceivablesReportPage | PayablesReportPage>({
    queryKey: ["portfolio-report", direction, businessId, filters],
    queryFn: () => direction === "receivable"
      ? receivablesApi.report(filters as ReceivablesReportFilters)
      : payablesApi.report(filters as PayablesReportFilters),
    enabled: Boolean(mode && businessId && !invalidDates),
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
      otherImpact: report.data.totalOtherImpact }] : [];
  const hasOtherImpact = currencyTotals.some(total => total.otherImpact !== 0) ||
    items.some(item => item.otherImpact !== 0);
  const partyLabel = direction === "receivable" ? "cliente" : "proveedor";
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
    if (!mode || printing || invalidDates) return;
    const preview = window.open("", "_blank");
    if (!preview) {
      setPrintError("El navegador bloqueó la vista de impresión. Permite abrir ventanas para este sitio.");
      return;
    }
    preview.opener = null;
    preview.document.write("<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><title>Preparando informe</title><body>Preparando informe…</body></html>");
    preview.document.close();
    setPrinting(true); setPrintError(null);
    try {
      const html = direction === "receivable"
        ? await receivablesApi.printReport(filters as ReceivablesReportFilters)
        : await payablesApi.printReport(filters as PayablesReportFilters);
      if (preview.closed) return;
      preview.document.open(); preview.document.write(html); preview.document.close();
      preview.focus();
      preview.requestAnimationFrame(() => preview.requestAnimationFrame(() => preview.print()));
    } catch (error) {
      if (!preview.closed) preview.close();
      setPrintError(error instanceof Error ? error.message : "No se pudo preparar el informe para imprimir.");
    } finally { setPrinting(false); }
  }

  return <Dialog open onOpenChange={open => !open && onClose()}>
    <DialogContent className="flex max-h-[96dvh] max-w-6xl flex-col overflow-hidden p-0">
      <DialogHeader className="shrink-0 border-b px-5 py-4 text-left">
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>Consulta la cartera al corte seleccionado, con sede y pagos aplicados.</DialogDescription>
      </DialogHeader>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-5">
        {!mode ? <div className="grid gap-3 sm:grid-cols-2">
          <button type="button" className="rounded-2xl border p-5 text-left transition hover:border-teal-600 hover:bg-teal-50" onClick={() => chooseMode("detail")}>
            <FileText className="mb-3 h-6 w-6 text-teal-700" /><strong className="block text-lg">Detallado</strong>
            <span className="mt-1 block text-sm text-muted-foreground">Cada factura o gasto, sus abonos o pagos y el saldo.</span>
          </button>
          <button type="button" className="rounded-2xl border p-5 text-left transition hover:border-teal-600 hover:bg-teal-50" onClick={() => chooseMode("summary")}>
            <Layers3 className="mb-3 h-6 w-6 text-teal-700" /><strong className="block text-lg">Consolidado</strong>
            <span className="mt-1 block text-sm text-muted-foreground">Totales por {partyLabel}, sede{direction === "payable" ? " y moneda" : ""}.</span>
          </button>
        </div> : <>
          <div className="flex items-center justify-between gap-3"><Button variant="ghost" size="sm" onClick={() => setMode(null)}><ArrowLeft className="mr-1 h-4 w-4" />Elegir informe</Button><span className="text-sm font-medium">{mode === "detail" ? "Detallado" : "Consolidado"}</span></div>
          <div className="grid gap-3 rounded-xl border bg-muted/20 p-4 sm:grid-cols-2 lg:grid-cols-4">
            <label className="space-y-1 text-sm">{partyLabel === "cliente" ? "Cliente y sede" : "Proveedor y sede"}
              <PagedEntitySelect<PartySiteRoleOption>
                queryKey={["portfolio-report-party", direction, businessId]}
                value={siteId ?? ""} selectedOption={partyOption}
                placeholder={`Todos los ${partyLabel}s`}
                ariaLabel={`Filtrar por ${partyLabel} y sede`}
                loadPage={(term, next, pageSize) => partiesApi.portfolioSiteOptions({role: direction === "receivable" ? "Customer" : "Supplier", search: term || undefined, page: next, pageSize})}
                getOption={item => ({value:item.partySiteId,label:`${item.displayName} · ${item.siteName}`,description:item.identification})}
                onChange={(_, option, item) => { if (!item) return; setPartyId(item.roleId); setSiteId(item.partySiteId); setPartyOption(option); setPage(1); }}
                onClear={() => { setPartyId(undefined); setSiteId(undefined); setPartyOption(null); setPage(1); }} />
            </label>
            <label className="space-y-1 text-sm">Emisión desde<DatePicker value={from} max={to || cutoff} onChange={value => { setFrom(value); setPage(1); }} /></label>
            <label className="space-y-1 text-sm">Emisión hasta<DatePicker value={to} min={from || undefined} max={cutoff} onChange={value => { setTo(value); setPage(1); }} /></label>
            <label className="space-y-1 text-sm">Corte<DatePicker value={cutoff} min={from || undefined} onChange={value => { setCutoff(value); setPage(1); }} /></label>
            <label className="space-y-1 text-sm">Estado
              <Select value={status} onValueChange={value => { setStatus(value); setPage(1); }}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
                <SelectItem value="all">Todos</SelectItem><SelectItem value="Open">Pendientes</SelectItem><SelectItem value="PartiallyPaid">Pago parcial</SelectItem><SelectItem value="Paid">Pagadas</SelectItem><SelectItem value="Cancelled">Canceladas</SelectItem>
              </SelectContent></Select>
            </label>
            <label className="flex items-center gap-2 self-end rounded-xl border bg-background px-3 py-2.5 text-sm"><Checkbox checked={outstandingOnly} onCheckedChange={value => { setOutstandingOnly(value === true); setPage(1); }} />Solo con saldo</label>
            <label className="flex items-center gap-2 self-end rounded-xl border bg-background px-3 py-2.5 text-sm"><Checkbox checked={overdueOnly} onCheckedChange={value => { setOverdueOnly(value === true); setPage(1); }} />Solo vencidas</label>
          </div>
          {invalidDates && <p className="text-sm text-destructive">Revisa las fechas: la emisión debe quedar dentro del corte.</p>}
          {report.isLoading && !invalidDates && <p className="flex items-center gap-2 py-8 text-sm"><Loader2 className="h-4 w-4 animate-spin" />Cargando informe…</p>}
          {report.isError && <p role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">No se pudo cargar el informe. <Button variant="link" onClick={() => void report.refetch()}>Reintentar</Button></p>}
          {report.data && !invalidDates && <>
            <div className="flex flex-wrap gap-3">{currencyTotals.map(total => <div key={total.currencyCode} className="min-w-56 flex-1 rounded-xl border bg-white p-3 text-sm"><strong>{total.currencyCode} · {total.invoiceCount} documentos</strong><p className="mt-1 text-muted-foreground">Original {formatCurrency(total.originalAmount,total.currencyCode)} · Pagado {formatCurrency(total.paidAmount,total.currencyCode)}{total.otherImpact !== 0 ? ` · Notas y ajustes ${formatCurrency(total.otherImpact,total.currencyCode)}` : ""}</p><p className="font-semibold">Saldo {formatCurrency(total.outstandingAmount,total.currencyCode)} · Vencido {formatCurrency(total.overdueAmount,total.currencyCode)}</p></div>)}</div>
            <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[850px] text-sm"><thead className="bg-muted/60 text-left"><tr>
              <th className="p-3">{sortButton("name", direction === "receivable" ? "Cliente / sede" : "Proveedor / sede")}</th>
              {mode === "detail" ? <><th className="p-3">{sortButton("documentNumber", "Documento")}</th><th className="p-3">{sortButton("issuedAt", "Emisión")}</th><th className="p-3">{sortButton("dueDate", "Vence")}</th></> : <th className="p-3">{sortButton("invoiceCount", "Documentos")}</th>}
              <th className="p-3">{direction === "payable" ? sortButton("currency", "Moneda") : "Moneda"}</th><th className="p-3 text-right">{sortButton("originalAmount", "Original")}</th>
              <th className="p-3 text-right">{sortButton("paidAmount", "Pagado")}</th>{hasOtherImpact && <th className="p-3 text-right">Notas y ajustes</th>}<th className="p-3 text-right">{sortButton("outstandingAmount", "Saldo")}</th>
            </tr></thead><tbody>{items.map((item, index) => <tr key={("receivableId" in item ? item.receivableId : item.payableId) ?? `${"customerId" in item ? item.customerId : item.supplierId}-${item.partySiteId}-${item.currencyCode}-${index}`} className="border-t align-top">
              <td className="p-3"><strong>{"customerName" in item ? item.customerName : item.supplierName}</strong><span className="block text-xs text-muted-foreground">{item.identification} · {item.partySiteName ?? "Sede principal"}</span></td>
              {mode === "detail" ? <><td className="p-3"><strong>{item.documentNumber}</strong>{item.applications?.length ? <ul className="mt-2 space-y-1 text-xs text-muted-foreground">{item.applications.map((application, line) => <li key={`${application.documentNumber}-${line}`}>{direction === "receivable" ? "Abono" : "Pago"} {application.documentNumber} · {formatDate(application.appliedAt)} · {formatCurrency(application.amount,item.currencyCode)}</li>)}</ul> : null}</td><td className="p-3">{item.issuedAt ? formatDate(item.issuedAt) : "—"}</td><td className="p-3">{item.dueDate ? formatDate(item.dueDate) : "—"}</td></> : <td className="p-3">{item.invoiceCount}</td>}
              <td className="p-3">{item.currencyCode}</td><td className="p-3 text-right tabular-nums">{formatCurrency(item.originalAmount,item.currencyCode)}</td><td className="p-3 text-right tabular-nums">{formatCurrency(item.paidAmount,item.currencyCode)}</td>{hasOtherImpact && <td className="p-3 text-right tabular-nums">{formatCurrency(item.otherImpact,item.currencyCode)}</td>}<td className="p-3 text-right font-semibold tabular-nums">{formatCurrency(item.outstandingAmount,item.currencyCode)}</td>
            </tr>)}</tbody></table>{items.length === 0 && <p className="p-8 text-center text-sm text-muted-foreground">No hay documentos para estos filtros.</p>}</div>
            <div className="flex items-center justify-between gap-3 text-sm"><span>{totalCount} {mode === "detail" ? "documentos" : "grupos"} · Página {page} de {Math.max(totalPages,1)}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={page <= 1 || report.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page >= totalPages || report.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div>
          </>}
        </>}
      </div>
      <DialogFooter className="shrink-0 border-t p-4 sm:justify-between">
        <div className="flex items-center gap-3"><Button variant="outline" disabled={!mode || invalidDates || printing || report.isLoading || report.isError} onClick={() => void printReport()}><Printer className="mr-2 h-4 w-4" />{printing ? "Preparando informe…" : "Imprimir informe"}</Button>{printError && <span role="alert" className="max-w-md text-sm text-destructive">{printError}</span>}</div>
        <Button variant="outline" onClick={onClose}>Cerrar</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}
