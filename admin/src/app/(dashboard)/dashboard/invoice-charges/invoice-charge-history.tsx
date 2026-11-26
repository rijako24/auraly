"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, FileText, SlidersHorizontal } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "@/components/ui/accordion";
import { DocumentNumbers } from "@/components/documents/document-numbers";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { PartyRoleSelect, type PartyRoleSelection } from "@/components/parties/party-role-select";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ReportViewer } from "@/components/reports/report-viewer";
import { partiesApi } from "@/services/api/parties";
import { invoiceChargesApi, type InvoiceChargeHistoryPage } from "@/services/api/invoice-charges";
import { buildInvoiceChargeReport } from "@/lib/invoice-charge-report";
import { businessStatusLabel } from "@/lib/accounting-labels";
import { useBusinessDates } from "@/hooks/use-business-dates";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });
type HistoryItem = InvoiceChargeHistoryPage["items"][number];

export function InvoiceChargeHistory({ businessId }: { businessId: string }) {
  const dates = useBusinessDates(businessId);
  const [from, setFrom] = useState(dates.today);
  const [to, setTo] = useState(dates.today);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [createdBy, setCreatedBy] = useState<PartyRoleSelection | null>(null);
  const [customer, setCustomer] = useState<PartyRoleSelection | null>(null);
  const [reportMode, setReportMode] = useState("detail");
  const [report, setReport] = useState<ReturnType<typeof buildInvoiceChargeReport> | null>(null);
  const [reportBusy, setReportBusy] = useState(false);
  const [reportError, setReportError] = useState<string | null>(null);
  const reportRequest = useRef<AbortController | null>(null);
  useEffect(() => () => reportRequest.current?.abort(), []);
  const valid = Boolean(from && to && from <= to &&
    (new Date(to).valueOf() - new Date(from).valueOf()) / 86_400_000 <= 30);
  const filters = { from, to, search, createdByUserId: createdBy?.roleId, customerId: customer?.roleId };
  const query = useQuery({ queryKey: ["invoice-charge-history-by-invoice", businessId, filters, page, pageSize],
    queryFn: ({ signal }) => invoiceChargesApi.history({ ...filters, page, pageSize }, signal), enabled: valid && report === null,
    staleTime: Infinity, refetchOnWindowFocus: false });

  async function generateReport() {
    if (!valid || reportRequest.current) return;
    const request = new AbortController();
    reportRequest.current = request; setReportBusy(true); setReportError(null);
    const description = [`Del ${dates.formatDate(from)} al ${dates.formatDate(to)}`,
      createdBy ? `Generado por: ${createdBy.displayName}` : null,
      customer ? `Cliente: ${customer.displayName}` : null, search ? `Búsqueda: ${search}` : null].filter(Boolean).join(" · ");
    try {
      const data = await invoiceChargesApi.exportHistory(filters, request.signal);
      if (!request.signal.aborted) setReport(buildInvoiceChargeReport(data, {
        consolidated: reportMode === "summary", description, formatDateTime: dates.formatDateTime,
      }));
    } catch (failure) {
      if (!request.signal.aborted) setReportError(failure instanceof Error ? failure.message : "No fue posible generar el reporte.");
    } finally {
      if (reportRequest.current === request) reportRequest.current = null;
      if (!request.signal.aborted) setReportBusy(false);
    }
  }

  if (report) return <ReportViewer {...report} onClose={() => setReport(null)} documentDownloads searchable={false}/>;
  // SQL pages invoice identities first; each group contains its matching charges in full.
  const invoices = new Map<string, HistoryItem[]>();
  for (const item of query.data?.items ?? []) {
    const charges = invoices.get(item.documentId);
    if (charges) charges.push(item);
    else invoices.set(item.documentId, [item]);
  }
  return <div className="min-w-0 space-y-4">
    <p className="text-sm text-muted-foreground"><span>Cargos en el periodo</span>: <strong>{query.isSuccess ? query.data.totalCount : "—"}</strong>{query.isSuccess && <> · {query.data.totalDocuments} facturas</>}</p>
    <div className="grid gap-4 sm:grid-cols-3">{[
      ["Asumido por el cliente", query.isSuccess ? money.format(query.data.invoicedTotal) : "—"],
      ["Asumido por la empresa", query.isSuccess ? money.format(query.data.expenseTotal) : "—"],
      ["Gastos generados", query.isSuccess ? money.format(query.data.generatedExpenseTotal) : "—"],
    ].map(([label, value]) => <Card key={label}><CardContent className="p-5"><p className="text-sm text-muted-foreground">{label}</p><strong className="mt-2 block text-2xl tabular-nums">{value}</strong></CardContent></Card>)}</div>
    <Card><CardContent className="p-4"><fieldset disabled={reportBusy} className="space-y-4">
    <Collapsible open={filtersOpen} onOpenChange={setFiltersOpen} className="space-y-4"><div className="flex flex-wrap items-end gap-4">
      <div className="w-full space-y-2 sm:w-52"><Label htmlFor="charges-from">Desde</Label><DatePicker id="charges-from" value={from} onChange={value => { setFrom(value); setPage(1); }}/></div>
      <div className="w-full space-y-2 sm:w-52"><Label htmlFor="charges-to">Hasta</Label><DatePicker id="charges-to" value={to} onChange={value => { setTo(value); setPage(1); }}/></div>
      <div className="min-w-0 flex-1 basis-64"><ServerSearchInput placeholder="Cargo, proveedor o factura" value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={query.isFetching}/></div>
      <CollapsibleTrigger asChild><Button variant="outline"><SlidersHorizontal className="mr-2 h-4 w-4"/>Filtros{(createdBy || customer) && <span className="ml-2 rounded-full bg-muted px-2">{Number(Boolean(createdBy)) + Number(Boolean(customer))}</span>}<ChevronDown className={`ml-2 h-4 w-4 ${filtersOpen ? "rotate-180" : ""}`}/></Button></CollapsibleTrigger>
      <Button variant="outline" disabled={!valid || query.isFetching} onClick={() => void query.refetch()}>Actualizar</Button>
    </div><CollapsibleContent className="grid gap-4 border-t pt-4 sm:grid-cols-2">
      <div className="min-w-0 space-y-2"><Label>Generado por</Label><PartyRoleSelect role="User" sourceKey="invoice-charge-history" value={createdBy?.roleId ?? ""}
        selectedOption={createdBy ? { value: createdBy.roleId, label: createdBy.displayName, description: createdBy.identification } : null}
        ariaLabel="Filtrar por usuario que generó el gasto" placeholder="Todos los usuarios" loadPage={(search, page, pageSize) => partiesApi.roleOptions({ role: "User", includeInactive: true, search, page, pageSize })}
        onChange={(value, party) => { setCreatedBy(value ? party ?? null : null); setPage(1); }}/></div>
      <div className="min-w-0 space-y-2"><Label>Cliente</Label><PartyRoleSelect role="Customer" sourceKey="invoice-charge-history" value={customer?.roleId ?? ""}
        selectedOption={customer ? { value: customer.roleId, label: customer.displayName, description: customer.identification } : null}
        ariaLabel="Filtrar cargos por cliente" placeholder="Todos los clientes" loadPage={(search, page, pageSize) => partiesApi.roleOptions({ role: "Customer", includeInactive: true, search, page, pageSize })}
        onChange={(value, party) => { setCustomer(value ? party ?? null : null); setPage(1); }}/></div>
    </CollapsibleContent></Collapsible>
      {!valid && <p role="alert" className="w-full text-sm text-destructive">Selecciona un periodo de hasta 31 días.</p>}
      <div className="flex flex-wrap items-end gap-3 border-t pt-4"><div className="w-full space-y-2 sm:w-52"><Label htmlFor="charge-report-mode">Tipo de reporte</Label><Select value={reportMode} onValueChange={setReportMode}><SelectTrigger id="charge-report-mode"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="detail">Detallado por gasto</SelectItem><SelectItem value="summary">Consolidado por factura</SelectItem></SelectContent></Select></div><Button variant="outline" disabled={!valid || reportBusy} onClick={() => void generateReport()}><FileText className="mr-2 h-4 w-4"/>{reportBusy ? "Generando reporte…" : "Generar reporte"}</Button></div>
    </fieldset>{reportError && <p role="alert" className="mt-3 text-sm text-destructive">{reportError}</p>}</CardContent></Card>
    {query.isError && <p role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">No fue posible consultar los cargos. Usa Actualizar para reintentar.</p>}
    {valid && query.isPending && <p role="status" className="p-6 text-center text-muted-foreground">Consultando cargos…</p>}
    {valid && query.isSuccess && <Card><CardContent className="p-0">
      <Accordion type="multiple" className="min-w-0">
        {[...invoices].map(([documentId, items]) => {
          const invoice = items[0];
          const expenseCount = items.filter(item => item.expenseId).length;
          return <AccordionItem key={documentId} value={documentId}>
            <AccordionTrigger aria-label={`Ver cargos de ${invoice.documentNumber}`} className="gap-3 px-4 text-left hover:bg-muted/30 hover:no-underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring sm:px-5">
              <span className="grid min-w-0 flex-1 gap-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-center">
                <span className="min-w-0"><DocumentNumbers internalNumber={invoice.documentNumber} fiscalNumber={invoice.fiscalNumber}/><span className="mt-2 block text-xs font-normal text-muted-foreground">Total factura <strong className="ml-1 text-sm font-semibold tabular-nums text-foreground">{money.format(invoice.invoiceTotal)}</strong></span></span>
                <span className="min-w-0 break-words font-normal"><span className="block">{invoice.customerName}</span><span className="mt-1 block text-xs text-muted-foreground">Aplicado {dates.formatDateTime(invoice.issuedAt)}</span></span>
                <span className="text-xs font-normal text-muted-foreground">{items.length} {items.length === 1 ? "cargo" : "cargos"}<span className="ml-2 sm:ml-0 sm:mt-1 sm:block">{expenseCount} {expenseCount === 1 ? "gasto generado" : "gastos generados"}</span></span>
              </span>
            </AccordionTrigger>
            <AccordionContent className="px-4 pb-4 pt-1 text-foreground sm:px-5">
              <div className="max-w-full overflow-x-auto rounded-lg border" role="region" aria-label={`Gastos de ${invoice.documentNumber}`} tabIndex={0}>
                <table className="w-full min-w-[60rem] text-left text-sm">
                  <thead className="bg-muted/50 text-xs text-muted-foreground"><tr>
                    {["Gasto / cargo", "Proveedor", "Fecha / usuario", "Asumido por el cliente", "Asumido por la empresa", "Gasto generado", "Estado"].map((label, index) => <th key={label} scope="col" className={`px-3 py-3 font-medium ${index >= 3 && index <= 5 ? "text-right" : ""}`}>{label}</th>)}
                  </tr></thead>
                  <tbody>{items.map(item => <tr key={item.charge.appliedChargeId} aria-label={`Gasto ${item.charge.name}`} className="border-t align-top hover:bg-muted/20">
                    <td className="px-3 py-3"><span className="block max-w-48 break-words font-medium">{item.charge.name}</span>{item.expenseId && item.expenseDocumentNumber ? <DocumentNumbers className="mt-1" internalNumber={item.expenseDocumentNumber} fiscalNumber={item.expenseFiscalNumber}/> : <span className="mt-1 block text-xs text-muted-foreground">Sin gasto vinculado</span>}</td>
                    <td className="px-3 py-3"><span className="block max-w-48 break-words">{item.charge.supplier.name}</span></td>
                    <td className="px-3 py-3"><span className="block max-w-48">{item.expenseAcceptedAt ? dates.formatDateTime(item.expenseAcceptedAt) : "—"}</span><span className="mt-1 block max-w-48 break-words text-xs text-muted-foreground">{item.expenseCreatedByName || (item.expenseId ? "Usuario no informado" : "—")}</span></td>
                    <td data-testid="charge-customer-amount" className="whitespace-nowrap px-3 py-3 text-right font-medium tabular-nums">{money.format(item.charge.invoicedAmount)}</td>
                    <td data-testid="charge-company-amount" className="whitespace-nowrap px-3 py-3 text-right font-medium tabular-nums">{money.format(item.charge.expenseAmount)}</td>
                    <td className="whitespace-nowrap px-3 py-3 text-right tabular-nums"><strong>{item.expenseGrossAmount == null ? "—" : money.format(item.expenseGrossAmount)}</strong>{item.expenseId && <span className="mt-1 block text-xs text-muted-foreground">Por pagar: {item.payableBalance === null ? "Sin cuenta registrada" : money.format(item.payableBalance)}</span>}</td>
                    <td className="px-3 py-3"><span className="inline-block rounded-md bg-muted px-2 py-1 text-xs">{item.expenseStatus ? businessStatusLabel(item.expenseStatus) : "Sin gasto"}</span></td>
                  </tr>)}</tbody>
                </table>
              </div>
            </AccordionContent>
          </AccordionItem>;
        })}
      </Accordion>
      {query.data.items.length === 0 && <p className="p-10 text-center text-muted-foreground">No hay cargos en este periodo.</p>}
      {query.data.totalDocuments > 0 && <div className="px-4"><DataTablePagination pageIndex={page - 1} pageSize={pageSize} pageCount={Math.ceil(query.data.totalDocuments / pageSize)} totalItems={query.data.totalDocuments}
        onPageChange={index => setPage(index + 1)} onPageSizeChange={size => { setPageSize(size); setPage(1); }}/></div>}
    </CardContent></Card>}
  </div>;
}
