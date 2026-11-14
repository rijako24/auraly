"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Loader2, Printer } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { partiesApi } from "@/services/api/parties";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TenantBrand } from "@/components/brand/tenant-brand";
import { receivablesApi, type ReceivablesReportFilters, type ReceivablesReportItem, type ReceivablesReportPage } from "@/services/api/receivables";
import { payablesApi, type PayablesReportFilters, type PayablesReportItem, type PayablesReportPage } from "@/services/api/payables";
import { tenantsApi } from "@/services/api/tenants";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { formatCurrency } from "@/lib/utils";
import { buildPortfolioReportExport, portfolioReportDate as formatDate } from "@/lib/portfolio-report-export";
import { proposedAccountingDate } from "@/lib/accounting-document-date";

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

type CustomerReportCriteria = {
  partyId: string; partyLabel: string; siteId: string; search: string; status: string;
  from: string; to: string; documentNumber: string; issuedFrom: string; issuedTo: string;
  minimumAgeDays: string; maximumAgeDays: string; minimumDaysPastDue: string; maximumDaysPastDue: string;
  overdueOnly: boolean; outstandingOnly: boolean;
};
const emptyCustomerCriteria: CustomerReportCriteria = {
  partyId: "", partyLabel: "", siteId: "", search: "", status: "", from: "", to: "",
  documentNumber: "", issuedFrom: "", issuedTo: "", minimumAgeDays: "", maximumAgeDays: "",
  minimumDaysPastDue: "", maximumDaysPastDue: "", overdueOnly: false, outstandingOnly: false,
};

export function PortfolioReportDialog({ direction, onClose, initialPartyId,
  initialPartySiteId, initialPartyLabel, initialFrom, initialTo, initialStatus, initialOverdue,
  initialSearch, initialConceptId, initialConceptLabel }: Props) {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const timeZone = useBusinessContextStore(state => state.businesses.find(item => item.businessId === businessId)?.timeZone);
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const tenantName = useTenantContextStore(state => state.tenants.find(item => item.tenantId === state.selectedTenantId)?.name ?? "Organización");
  const branding = useQuery({ queryKey: ["tenant-print-branding", tenantId], queryFn: async () => {
      await tenantsApi.getPrintBranding();
      const prepared = tenantsApi.readyLocalPrintBranding();
      if (!prepared) throw new Error("No se pudo preparar la marca de la empresa.");
      return prepared;
    },
    enabled: Boolean(tenantId), staleTime: 10 * 60 * 1000 });
  const brandName = branding.data?.legalName ?? branding.data?.displayName ?? tenantName;
  const printBrandName = branding.data?.nit
    ? `${brandName} · NIT ${branding.data.nit}${branding.data.verificationDigit ? `-${branding.data.verificationDigit}` : ""}` : brandName;
  const [mode, setMode] = useState<ReportMode>("detail");
  const [includeApplications, setIncludeApplications] = useState(false);
  const [page, setPage] = useState(1);
  const localToday = timeZone ? proposedAccountingDate(timeZone).slice(0,10) : undefined;
  const [cutoff, setCutoff] = useState(() => timeZone ? proposedAccountingDate(timeZone).slice(0,10) : "");
  const [sortBy, setSortBy] = useState<string | undefined>();
  const [sortDirection, setSortDirection] = useState<"asc" | "desc">("asc");
  const [printing, setPrinting] = useState(false);
  const [exporting, setExporting] = useState<"loading" | "pdf" | "xlsx" | null>(null);
  const [printHtml, setPrintHtml] = useState<string | null>(null);
  const [printError, setPrintError] = useState<string | null>(null);
  const [criteria, setCriteria] = useState<CustomerReportCriteria>(() => ({ ...emptyCustomerCriteria,
    partyId: initialPartyId ?? "", partyLabel: initialPartyLabel ?? "", siteId: initialPartySiteId ?? "",
    search: initialSearch ?? "", status: initialStatus ?? "", from: initialFrom ?? "", to: initialTo ?? "",
    overdueOnly: initialOverdue === true }));
  const [draft, setDraft] = useState(criteria);
  const [filterError, setFilterError] = useState<string | null>(null);
  const invalidDates = Boolean(!cutoff || (criteria.from && criteria.from > cutoff) ||
    (criteria.to && criteria.to > cutoff) || (criteria.from && criteria.to && criteria.from > criteria.to) ||
    (criteria.issuedFrom && criteria.issuedFrom > cutoff) || (criteria.issuedTo && criteria.issuedTo > cutoff));
  const filters = { page, pageSize: 50, consolidated: mode === "summary", cutoff,
    includeApplications: mode === "detail" && includeApplications,
    ...(direction === "receivable" ? { customerId: criteria.partyId || undefined,
      documentNumber: criteria.documentNumber.trim() || undefined,
      issuedFrom: criteria.issuedFrom || undefined, issuedTo: criteria.issuedTo || undefined,
      minimumAgeDays: criteria.minimumAgeDays === "" ? undefined : Number(criteria.minimumAgeDays),
      maximumAgeDays: criteria.maximumAgeDays === "" ? undefined : Number(criteria.maximumAgeDays),
      minimumDaysPastDue: criteria.minimumDaysPastDue === "" ? undefined : Number(criteria.minimumDaysPastDue),
      maximumDaysPastDue: criteria.maximumDaysPastDue === "" ? undefined : Number(criteria.maximumDaysPastDue),
      outstandingOnly: criteria.outstandingOnly }
      : { supplierId: initialPartyId, conceptId: initialConceptId }),
    partySiteId: criteria.siteId || undefined, from: criteria.from || undefined, to: criteria.to || undefined,
    search: criteria.search || undefined, status: criteria.status || undefined,
    overdueOnly: criteria.overdueOnly, sortBy,
    sortDirection: sortBy ? sortDirection : undefined };
  const report = useQuery<ReceivablesReportPage | PayablesReportPage>({
    queryKey: ["portfolio-report", direction, tenantId, businessId, filters, timeZone],
    queryFn: () => direction === "receivable"
      ? receivablesApi.report(filters as ReceivablesReportFilters)
      : payablesApi.report(filters as PayablesReportFilters),
    enabled: Boolean(tenantId && businessId && !invalidDates),
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
  const exportUnavailable = invalidDates || printing || !!exporting || report.isLoading || report.isError || !report.data || !branding.isSuccess;

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
    if (printing || exporting || invalidDates || !branding.isSuccess) return;
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

  async function exportReport(format: "pdf" | "xlsx") {
    if (printing || exporting || invalidDates || !branding.isSuccess) return;
    setExporting("loading"); setPrintError(null);
    try {
      const data = direction === "receivable"
        ? await receivablesApi.exportReport(filters as ReceivablesReportFilters)
        : await payablesApi.exportReport(filters as PayablesReportFilters);
      setExporting(format);
      const { exportReportPdf, exportReportExcel } = await import("@/lib/report-export");
      await (format === "pdf" ? exportReportPdf : exportReportExcel)(buildPortfolioReportExport(data,
        { title, brandName: printBrandName, cutoff, filterSummary, consolidated: mode === "summary", includeApplications, logoSource: branding.data?.logoUrl, format }));
    } catch (error) {
      setPrintError(error instanceof Error ? error.message : "No se pudo exportar el informe.");
    } finally { setExporting(null); }
  }

  const filterSummary = [criteria.search && `Búsqueda: ${criteria.search}`,
    criteria.partyId && (criteria.partyLabel || "Tercero seleccionado"), criteria.status && `Estado: ${criteria.status}`,
    criteria.overdueOnly && "Solo vencidas", criteria.outstandingOnly && "Solo con saldo",
    criteria.from && `Registro desde ${criteria.from}`, criteria.to && `Registro hasta ${criteria.to}`,
    criteria.documentNumber && `Factura: ${criteria.documentNumber}`,
    criteria.issuedFrom && `Emisión desde ${criteria.issuedFrom}`, criteria.issuedTo && `Emisión hasta ${criteria.issuedTo}`,
    criteria.minimumAgeDays !== "" && `Edad desde ${criteria.minimumAgeDays} días`,
    criteria.maximumAgeDays !== "" && `Edad hasta ${criteria.maximumAgeDays} días`,
    criteria.minimumDaysPastDue !== "" && `Mora desde ${criteria.minimumDaysPastDue} días`,
    criteria.maximumDaysPastDue !== "" && `Mora hasta ${criteria.maximumDaysPastDue} días`,
    initialConceptId && (initialConceptLabel ?? "Concepto seleccionado")]
    .filter(Boolean).join(" · ") || "Todos los documentos";

  function applyCriteria() {
    if ((draft.issuedFrom && draft.issuedTo && draft.issuedFrom > draft.issuedTo) ||
      (draft.from && draft.to && draft.from > draft.to) ||
      [draft.from, draft.to, draft.issuedFrom, draft.issuedTo].some(value => value && value > cutoff) ||
      (draft.minimumAgeDays !== "" && draft.maximumAgeDays !== "" && Number(draft.minimumAgeDays) > Number(draft.maximumAgeDays)) ||
      (draft.minimumDaysPastDue !== "" && draft.maximumDaysPastDue !== "" && Number(draft.minimumDaysPastDue) > Number(draft.maximumDaysPastDue))) {
      setFilterError("Revisa los rangos: desde no puede superar hasta ni la fecha de corte."); return;
    }
    setCriteria(draft); setPage(1); setFilterError(null); setPrintHtml(null);
  }

  const grouped = new Map<string, Row[]>();
  if (mode === "detail") for (const item of items) {
    const partyId = "customerId" in item ? item.customerId ?? item.partyId : item.supplierId;
    const key = `${partyId}:${item.partySiteId}:${item.currencyCode}`;
    const group = grouped.get(key) ?? [];
    group.push(item);
    grouped.set(key, group);
  }

  return <Dialog open onOpenChange={open => !open && onClose()}>
    <DialogContent className="flex h-[96dvh] max-h-[96dvh] w-[98vw] max-w-[1500px] flex-col overflow-hidden p-0">
      <DialogHeader data-testid="portfolio-report-header" className="shrink-0 border-b px-5 py-4 text-left">
        <div className="flex items-start gap-4">
          <TenantBrand displayName={brandName} logoUrl={branding.data?.logoUrl} showName={false} className="shrink-0" />
          <div className="min-w-0 pr-5 pt-1"><DialogTitle className="leading-snug">{title}</DialogTitle>
            <DialogDescription className="mt-1">Informe al corte · {filterSummary}</DialogDescription></div>
        </div>
        <p className="break-words text-xs font-semibold leading-relaxed">{brandName}</p>
      </DialogHeader>
      <div className="flex flex-wrap items-center gap-3 border-b bg-muted/30 px-5 py-3">
        <div className="inline-flex rounded-lg border bg-background p-1" role="group" aria-label="Tipo de informe">
          <Button size="sm" variant={mode === "detail" ? "default" : "ghost"} onClick={() => chooseMode("detail")}>Detallado</Button>
          <Button size="sm" variant={mode === "summary" ? "default" : "ghost"} onClick={() => chooseMode("summary")}>Consolidado</Button>
        </div>
        {mode === "detail" && <label className="flex items-center gap-2 text-sm"><Checkbox
          checked={includeApplications} onCheckedChange={checked => { setIncludeApplications(checked === true); setPage(1); }} />
          Mostrar detalle de {direction === "receivable" ? "abonos" : "pagos"}</label>}
        <label className="flex items-center gap-2 text-sm">Corte <span className="w-56"><DatePicker today={localToday} value={cutoff}
          onChange={value => { setCutoff(value); setPage(1); }} /></span></label>
        <span className="ml-auto text-sm text-muted-foreground">{totalCount.toLocaleString("es-CO")} {mode === "detail" ? totalCount===1 ? "registro" : "registros" : totalCount===1 ? "grupo" : "grupos"}</span>
      </div>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto bg-white p-5 text-slate-950">
        {direction === "receivable" && <details className="rounded-xl border p-3">
          <summary className="cursor-pointer text-sm font-semibold">Filtros del informe</summary>
          <form className="mt-3 space-y-3" onSubmit={event => { event.preventDefault(); applyCriteria(); }}>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <div className="space-y-1 text-xs"><span>Cliente · todas sus sedes</span><PartyRoleSelect role="Customer"
                value={draft.partyId} selectedOption={draft.partyId && draft.partyLabel ? {value:draft.partyId,label:draft.partyLabel} : null}
                sourceKey="portfolio" loadPage={(search,page,pageSize) => partiesApi.portfolioRoleOptions({role:"Customer",search:search || undefined,page,pageSize})}
                onChange={(partyId, party) => setDraft(value => ({...value,partyId,partyLabel:party?.displayName ?? "",siteId:""}))}
                ariaLabel="Cliente del informe" placeholder="Todos los clientes" /></div>
              <label className="space-y-1 text-xs">Factura exacta<Input aria-label="Factura exacta" maxLength={64} value={draft.documentNumber}
                onChange={event => setDraft({...draft,documentNumber:event.target.value})} /></label>
              <label className="space-y-1 text-xs">Emisión desde<DatePicker today={localToday} value={draft.issuedFrom} max={cutoff}
                onChange={value => setDraft({...draft,issuedFrom:value})} /></label>
              <label className="space-y-1 text-xs">Emisión hasta<DatePicker today={localToday} value={draft.issuedTo} max={cutoff}
                onChange={value => setDraft({...draft,issuedTo:value})} /></label>
              {([['minimumAgeDays','Edad desde (días)',0],['maximumAgeDays','Edad hasta (días)',0],
                ['minimumDaysPastDue','Mora desde (días)',undefined],['maximumDaysPastDue','Mora hasta (días)',undefined]] as const).map(([key,label,min]) =>
                <label key={key} className="space-y-1 text-xs">{label}<Input aria-label={label} type="number" step="1" min={min} value={draft[key]}
                  onChange={event => setDraft({...draft,[key]:event.target.value})} /></label>)}
            </div>
            <label className="block max-w-lg space-y-1 text-xs">Búsqueda general<Input aria-label="Búsqueda general del informe" maxLength={120}
              value={draft.search} onChange={event => setDraft({...draft,search:event.target.value})} placeholder="Documento, identificación o sede" /></label>
            <p className="text-xs text-muted-foreground">Edad: días desde la emisión al corte. Mora: días desde el vencimiento al corte; un valor negativo indica días pendientes para vencer.</p>
            <div className="flex flex-wrap items-center gap-4 text-sm">
              <label className="flex items-center gap-2"><Checkbox checked={draft.outstandingOnly} onCheckedChange={checked => setDraft({...draft,outstandingOnly:checked === true})} />Solo con saldo</label>
              <label className="flex items-center gap-2"><Checkbox checked={draft.overdueOnly} onCheckedChange={checked => setDraft({...draft,overdueOnly:checked === true})} />Solo vencidas</label>
              <Button type="submit" size="sm">Aplicar filtros</Button>
              <Button type="button" size="sm" variant="outline" onClick={() => { setDraft({...emptyCustomerCriteria}); setCriteria({...emptyCustomerCriteria}); setPage(1); setFilterError(null); setPrintHtml(null); }}>Ver todos los clientes</Button>
            </div>
            {(draft.siteId || draft.search || draft.status || draft.from || draft.to) && <p className="text-xs text-muted-foreground">Se conservan los filtros de la cartera al abrir el informe. «Ver todos los clientes» limpia también búsqueda, sede, registro y estado heredados.</p>}
            {filterError && <p role="alert" className="text-sm text-destructive">{filterError}</p>}
          </form>
        </details>}
        {invalidDates && <p role="alert" className="text-sm text-destructive">{!cutoff ? "Selecciona la fecha de corte del informe." : "Revisa las fechas: emisión y registro deben quedar dentro del corte."}</p>}
        {branding.isError && <p role="alert" className="text-sm text-destructive">No se pudo preparar la marca para imprimir o exportar. <Button variant="link" onClick={() => void branding.refetch()}>Reintentar marca</Button></p>}
        {report.isLoading && !invalidDates && <p className="flex items-center gap-2 py-8 text-sm"><Loader2 className="h-4 w-4 animate-spin" />Cargando informe…</p>}
        {report.isError && <p role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">{report.error instanceof Error ? report.error.message : "No se pudo cargar el informe."} <Button variant="link" onClick={() => void report.refetch()}>Reintentar</Button></p>}
        {report.data && !invalidDates && <>
          <div className="flex flex-wrap gap-3">{currencyTotals.map(total => <div key={total.currencyCode} className="min-w-56 flex-1 rounded-xl border bg-slate-50 p-3 text-sm"><strong>{total.currencyCode} · {total.invoiceCount} documentos</strong><p className="mt-1 text-slate-600">Original {formatCurrency(total.originalAmount,total.currencyCode)} · Pagado {formatCurrency(total.paidAmount,total.currencyCode)}{total.otherImpact !== 0 ? ` · Notas y ajustes ${formatCurrency(total.otherImpact,total.currencyCode)}` : ""}</p><p className="font-semibold">Saldo {formatCurrency(total.outstandingAmount,total.currencyCode)} · Vencido {formatCurrency(total.overdueAmount,total.currencyCode)}</p></div>)}</div>
          {!("currencyTotals" in report.data) && <CreditSummary currency="COP" availableAdvances={report.data.totalAvailableAdvances}
            availableCredits={report.data.totalAvailableCredits} netAmount={report.data.totalNetAmount}/>}
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
                  {direction === "receivable" && <><SelectItem value="ageDays">Edad en días</SelectItem><SelectItem value="daysPastDue">Mora en días</SelectItem></>}
                  <SelectItem value="originalAmount">Valor original</SelectItem>
                  <SelectItem value="paidAmount">{direction === "receivable" ? "Valor abonado" : "Valor pagado"}</SelectItem>
                  <SelectItem value="outstandingAmount">Saldo pendiente</SelectItem>
                  {direction === "receivable" && <SelectItem value="netAmount">Neto comercial</SelectItem>}
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
              const employee = "customerId" in first && !first.customerId && !!first.partyId;
              return <section key={key} className="overflow-hidden rounded-xl border">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-teal-50 px-4 py-3">
                  <div><p className="text-[11px] font-semibold uppercase tracking-wide text-teal-700">{employee ? "Tercero" : direction === "receivable" ? "Cliente" : "Proveedor"}{!employee && ` · ${first.partySiteName ?? "Sin sede identificada"}`}</p><h3 className="text-base font-semibold text-teal-950">{name}</h3><p className="text-xs text-slate-600">Identificación {first.identification}</p></div>
                  <span className="text-xs font-medium text-teal-800">{group.length} {group.length === 1 ? "registro" : "registros"} en esta página · {first.currencyCode}</span>
                  {"customerId" in first && <div className="w-full text-xs text-slate-600">
                    {first.legalName && first.legalName !== name && <p>Razón social: {first.legalName}</p>}
                    {[first.address,first.city,first.phone && `Tel. ${first.phone}`,first.email].filter(Boolean).join(" · ")}
                    <p>Cupo vigente: {first.creditLimit == null ? "Sin cupo definido" : formatCurrency(first.creditLimit,first.currencyCode)}</p>
                  </div>}
                </div>
                <div className="overflow-x-auto"><table className={`w-full ${direction === "receivable" ? "min-w-[1320px]" : "min-w-[1020px]"} text-sm`}>
                  <thead className="bg-slate-50 text-left"><tr>
                    <th className="p-3">Documento</th><th className="p-3">Emisión</th><th className="p-3">Vencimiento</th>
                    {direction === "receivable" && <><th className="p-3 text-right">Plazo (días)</th><th className="p-3 text-right">Edad (días)</th><th className="p-3 text-right">Mora (días)</th></>}
                    <th className="p-3 text-right">Original</th><th className="p-3 text-right">{direction === "receivable" ? "Abonado" : "Pagado"}</th>
                    <th className="p-3 text-right">Notas y ajustes</th><th className="p-3 text-right">Saldo pendiente</th><th className="p-3">Tramo</th>
                  </tr></thead>
                  {group.map(item => <tbody key={("receivableId" in item ? item.receivableId : item.payableId) ?? item.documentNumber}>
                    <tr className="border-t">
                      <td className="p-3 font-semibold">{item.documentNumber ?? "Sin número"}
                        {"customerId" in item && item.isCreditBalance && <CreditSummary {...item} currency={item.currencyCode}/>}</td>
                      <td className="p-3 whitespace-nowrap">{item.issuedAt ? formatDate(item.issuedAt) : "—"}</td>
                      <td className="p-3 whitespace-nowrap">{item.dueDate ? formatDate(item.dueDate) : "—"}</td>
                      {"customerId" in item && <><td className="p-3 text-right tabular-nums">{item.termDays ?? "—"}</td><td className="p-3 text-right tabular-nums">{item.ageDays ?? "—"}</td><td className={`p-3 text-right tabular-nums ${(item.daysPastDue ?? 0) > 0 && item.outstandingAmount > 0 ? "font-semibold text-red-700" : ""}`}>{item.daysPastDue ?? "—"}</td></>}
                      <td className="p-3 text-right tabular-nums">{formatCurrency(item.originalAmount,item.currencyCode,4)}</td>
                      <td className="p-3 text-right tabular-nums">{formatCurrency(item.paidAmount,item.currencyCode,4)}</td>
                      <td className="p-3 text-right tabular-nums">{formatCurrency(item.otherImpact,item.currencyCode,4)}</td>
                      <td className="p-3 text-right font-semibold tabular-nums">{formatCurrency(item.outstandingAmount,item.currencyCode,4)}</td>
                      <td className="p-3 whitespace-nowrap">{ageLabel(item)}</td>
                    </tr>
                    {includeApplications && !("customerId" in item && item.isCreditBalance) && <tr><td colSpan={direction === "receivable" ? 11 : 8} className="border-t bg-slate-50 px-6 py-3">
                      <p className="font-semibold text-slate-700">{employee ? "Recuperaciones de esta obligación" : direction === "receivable" ? "Abonos de esta factura" : "Pagos de esta obligación"}</p>
                      {item.applications?.length ? <ul className="mt-1 divide-y">{item.applications.map((application,line) =>
                        <li key={`${application.documentNumber}-${line}`} className="flex items-center justify-between gap-4 py-1">
                          <span>{application.documentNumber} · {formatDate(application.appliedAt)}</span>
                          <strong className="tabular-nums">{formatCurrency(application.amount,item.currencyCode,4)}</strong>
                        </li>)}</ul> : <p className="mt-1 text-xs text-slate-500">Sin {direction === "receivable" ? "abonos" : "pagos"} aplicados al corte.</p>}
                    </td></tr>}
                  </tbody>)}
                  {"customerId" in first && first.groupTotals && <tfoot className="border-t bg-teal-50 font-semibold"><tr>
                    <td colSpan={6} className="p-3">Subtotal cliente / sede · {first.groupTotals.invoiceCount} documentos filtrados</td>
                    <td className="p-3 text-right tabular-nums">{formatCurrency(first.groupTotals.originalAmount,first.currencyCode,4)}</td>
                    <td className="p-3 text-right tabular-nums">{formatCurrency(first.groupTotals.paidAmount,first.currencyCode,4)}</td>
                    <td className="p-3 text-right tabular-nums">{formatCurrency(first.groupTotals.otherImpact,first.currencyCode,4)}</td>
                    <td className="p-3 text-right tabular-nums">{formatCurrency(first.groupTotals.outstandingAmount,first.currencyCode,4)}</td><td className="p-3" />
                  </tr><tr><td colSpan={11} className="px-3 pb-3 text-xs font-normal">{ageBands.map(band => `${band.label}: ${formatCurrency(first.groupTotals![band.key],first.currencyCode)}`).join(" · ")}
                    <CreditSummary {...first.groupTotals} currency={first.currencyCode}/></td></tr></tfoot>}
                </table></div>
              </section>;
            })}
          </div> : <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[760px] text-sm"><thead className="bg-slate-100 text-left"><tr>
            <th className="p-3">{sortButton("name", direction === "receivable" ? "Tercero / sede" : "Proveedor / sede")}</th><th className="p-3">{sortButton("invoiceCount", "Documentos")}</th><th className="p-3">Moneda</th>
            <th className="p-3 text-right">{sortButton("originalAmount", "Original")}</th><th className="p-3 text-right">{sortButton("paidAmount", "Pagado")}</th>
            {hasOtherImpact && <th className="p-3 text-right">Notas y ajustes</th>}<th className="p-3 text-right">{sortButton("outstandingAmount", "Saldo")}</th>
          </tr></thead><tbody>{items.map((item, index) => <tr key={`${"customerId" in item ? item.customerId ?? item.partyId : item.supplierId}-${item.partySiteId}-${item.currencyCode}-${index}`} className="border-t">
            <td className="p-3"><strong>{"customerName" in item ? item.customerName : item.supplierName}</strong><span className="block text-xs text-slate-500">{item.identification} · {item.partySiteName ?? "Sin sede identificada"}</span>
              <div className="mt-2 space-y-0.5 text-xs text-slate-600">{ageBands.filter(band => item[band.key] !== 0).map(band => <div key={band.key}>{band.label}: <strong>{formatCurrency(item[band.key],item.currencyCode)}</strong></div>)}</div></td>
            <td className="p-3">{item.invoiceCount}</td><td className="p-3">{item.currencyCode}</td><td className="p-3 text-right tabular-nums">{formatCurrency(item.originalAmount,item.currencyCode)}</td>
            <td className="p-3 text-right tabular-nums">{formatCurrency(item.paidAmount,item.currencyCode)}</td>{hasOtherImpact && <td className="p-3 text-right tabular-nums">{formatCurrency(item.otherImpact,item.currencyCode)}</td>}
            <td className="p-3 text-right font-semibold tabular-nums">{formatCurrency(item.outstandingAmount,item.currencyCode)}
              {"customerId" in item && <CreditSummary {...item} currency={item.currencyCode}/>}</td>
          </tr>)}</tbody></table></div>}
          {items.length === 0 && <p className="rounded-xl border p-8 text-center text-sm text-slate-500">No hay documentos para estos filtros.</p>}
          <div className="flex items-center justify-between gap-3 text-sm"><span>Página {page} de {Math.max(totalPages,1)}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={page <= 1 || report.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page >= totalPages || report.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div>
        </>}
      </div>
      <DialogFooter className="shrink-0 border-t p-4 sm:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="outline" disabled={exportUnavailable} onClick={() => void printReport()}><Printer className="mr-2 h-4 w-4" />{printing ? "Preparando informe…" : "Imprimir informe completo"}</Button>
          <Button variant="outline" disabled={exportUnavailable} onClick={() => void exportReport("pdf")}>Descargar PDF</Button>
          <Button variant="outline" disabled={exportUnavailable} onClick={() => void exportReport("xlsx")}>Exportar Excel</Button>
          {exporting && <p role="status" className="text-sm">{exporting === "loading" ? "Consultando informe completo…" : `Generando ${exporting === "pdf" ? "PDF" : "Excel"}…`}</p>}
          {printError && <span role="alert" className="max-w-md text-sm text-destructive">{printError}</span>}
        </div>
        <Button variant="outline" onClick={onClose}>Cerrar</Button>
      </DialogFooter>
      {printHtml && <iframe title="Impresión del informe" className="absolute h-0 w-0 border-0" srcDoc={printHtml}
        onLoad={event => { event.currentTarget.contentWindow?.focus(); event.currentTarget.contentWindow?.print(); }} />}
    </DialogContent>
  </Dialog>;
}

function CreditSummary({ availableAdvances, availableCredits, netAmount, currency }: {
  availableAdvances?:number; availableCredits?:number; netAmount?:number; currency:string;
}) {
  if (availableAdvances === undefined && availableCredits === undefined && netAmount === undefined) return null;
  if (availableAdvances === undefined || availableCredits === undefined || netAmount === undefined)
    return <p role="alert">El desglose de saldos a favor está incompleto.</p>;
  return <div className="space-y-1 rounded-lg border border-teal-100 bg-teal-50/60 p-3 text-xs font-normal">
    <p>Anticipos disponibles: <strong>{formatCurrency(availableAdvances,currency,4)}</strong></p>
    <p>Otros saldos a favor: <strong>{formatCurrency(availableCredits,currency,4)}</strong></p>
    <p className="font-semibold">Neto comercial: {formatCurrency(netAmount,currency,4)}</p>
  </div>;
}
