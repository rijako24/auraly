"use client";

import { useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { FileSearch, FileText, Search } from "lucide-react";
import { toast } from "sonner";
import { accountingApi, type AccountingDocumentRow } from "@/services/api/accounting";
import { AccountingDocumentDialog } from "@/components/accounting/accounting-document-dialog";
import { ReportViewer } from "@/components/reports/report-viewer";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {accountingDocumentTypeLabel,accountingStatusLabel,fiscalStatusLabel} from "@/lib/accounting-labels";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const year = new Date().getFullYear();

export default function FinancialTraceabilityPage() {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [from, setFrom] = useState(`${year}-01-01`);
  const [to, setTo] = useState(`${year}-12-31`);
  const [status, setStatus] = useState("all");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string>();
  const [reportRows, setReportRows] = useState<AccountingDocumentRow[] | null>(null);
  const [loadingReport, setLoadingReport] = useState(false);
  const contextKey = `${tenantId ?? "none"}:${businessId ?? "none"}`;
  const filters = { from, to, status: status === "all" ? undefined : status, search: search.trim() || undefined };
  const query = useQuery({
    queryKey: ["financial-traceability", contextKey, from, to, status, search, page],
    queryFn: () => accountingApi.documents({ ...filters, page, pageSize: 25 }),
    enabled: Boolean(tenantId && businessId),
  });

  async function openReport() {
    setLoadingReport(true);
    try {
      const rows: AccountingDocumentRow[] = [];
      let nextPage = 1;
      let totalPages = 1;
      do {
        const result = await accountingApi.documents({ ...filters, page: nextPage, pageSize: 100 });
        rows.push(...result.items);
        totalPages = result.totalPages;
        nextPage += 1;
      } while (nextPage <= totalPages);
      setReportRows(rows);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible generar el reporte.");
    } finally {
      setLoadingReport(false);
    }
  }

  if (!tenantId || !businessId)
    return <Card><CardContent className="p-10 text-center text-muted-foreground">Selecciona una empresa y una sede para consultar su trazabilidad.</CardContent></Card>;

  if (reportRows) return <ReportViewer
    onClose={() => setReportRows(null)}
    title="Trazabilidad financiera"
    description={`Documentos, estado fiscal y efecto contable · ${from} a ${to}`}
    fileName={`trazabilidad-financiera-${from}-${to}`}
    rows={reportRows.map(row => ({
      id: row.sourceDocumentId,
      fecha: new Date(row.occurredAt).toLocaleDateString("es-CO"), tipo: accountingDocumentTypeLabel(row.sourceDocumentType),
      documento: row.sourceDocumentNumber ?? row.sourceDocumentId,
      documentoFiscal: row.fiscalDocumentType ?? "", numeroDian: row.dianNumber ?? "",
      estadoFiscal: row.fiscalStatus ? fiscalStatusLabel(row.fiscalStatus) : "Sin envío fiscal", comprobante: row.entryNumber ?? "",
      estadoContable: accountingStatusLabel(row.status), debito: row.debitTotal ?? 0,
      credito: row.creditTotal ?? 0, observacion: row.errorMessage ?? "",
    }))}
    columns={[
      { key: "fecha", label: "Fecha" }, { key: "tipo", label: "Tipo" },
      { key: "documento", label: "Documento" }, { key: "documentoFiscal", label: "Documento fiscal" },
      { key: "numeroDian", label: "Número DIAN" }, { key: "estadoFiscal", label: "Estado DIAN" },
      { key: "comprobante", label: "Comprobante" }, { key: "estadoContable", label: "Estado contable" },
      { key: "debito", label: "Débito", align: "right", format: value => money.format(Number(value ?? 0)) },
      { key: "credito", label: "Crédito", align: "right", format: value => money.format(Number(value ?? 0)) },
      { key: "observacion", label: "Observación" },
    ]}
  />;

  return <div className="space-y-6">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-wrap items-center gap-4"><span className="rounded-2xl bg-white/10 p-3"><FileSearch className="h-7 w-7" /></span><div className="min-w-0 flex-1"><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">Documento → efecto → comprobante</p><h1 className="mt-1 text-3xl font-black">Trazabilidad financiera</h1><p className="mt-1 max-w-3xl text-sm text-cyan-50/80">Consulta en un solo lugar qué ocurrió fiscal y contablemente con ventas, compras, gastos, inventario, pagos, notas y devoluciones.</p></div><Button variant="secondary" disabled={loadingReport} onClick={() => void openReport()}><FileText className="mr-2 h-4 w-4" />{loadingReport ? "Generando…" : "Generar reporte"}</Button></div></header>
    <Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b"><div className="grid gap-3 lg:grid-cols-[1fr_11rem_11rem_13rem]"><div><CardTitle>Documentos y comprobantes</CardTitle><p className="mt-1 text-sm text-muted-foreground">Abre cualquier fila para auditar cuentas, tercero, centro de costo, valores, estado DIAN y errores. El reporte usa el visor estándar del sistema.</p></div><Field label="Desde"><DatePicker value={from} onChange={value => { setFrom(value); setPage(1); }} /></Field><Field label="Hasta"><DatePicker value={to} onChange={value => { setTo(value); setPage(1); }} /></Field><Field label="Estado"><Select value={status} onValueChange={value => { setStatus(value); setPage(1); }}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem><SelectItem value="Posted">Contabilizados</SelectItem><SelectItem value="Pending">Pendientes</SelectItem><SelectItem value="CommercialEffectsApplied">Efecto comercial sin asiento</SelectItem><SelectItem value="AccountingPendingConfiguration">Requieren configuración</SelectItem><SelectItem value="MissingAccountingJob">Ausencia contable</SelectItem></SelectContent></Select></Field></div><div className="relative mt-4"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} placeholder="Número del documento, comprobante o identificador" /></div></CardHeader>
      <CardContent className="p-0"><div className="overflow-x-auto"><table className="w-full min-w-[980px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Documento</th><th className="text-left">Fiscal</th><th className="text-left">Comprobante</th><th className="text-left">Estado</th><th className="text-right">Débito</th><th className="pr-3 text-left">Observación</th></tr></thead><tbody>{(query.data?.items ?? []).map(row => <tr onClick={() => setSelected(row.sourceDocumentId)} key={`${row.sourceDocumentType}-${row.sourceDocumentId}`} className="cursor-pointer border-t transition hover:bg-muted/40"><td className="p-3">{new Date(row.occurredAt).toLocaleDateString("es-CO")}</td><td><b>{row.sourceDocumentNumber ?? accountingDocumentTypeLabel(row.sourceDocumentType)}</b><small className="block text-muted-foreground">{accountingDocumentTypeLabel(row.sourceDocumentType)}</small></td><td>{row.dianNumber ?? "—"}<small className="block text-muted-foreground">{fiscalStatusLabel(row.fiscalStatus)}</small></td><td className="font-mono">{row.entryNumber ?? "—"}</td><td><Status value={row.status} /></td><td className="text-right">{row.debitTotal == null ? "—" : money.format(row.debitTotal)}</td><td className="pr-3 text-xs">{row.errorMessage ?? "—"}</td></tr>)}</tbody></table>{query.isLoading && <p className="p-8 text-center text-sm text-muted-foreground">Cargando documentos…</p>}{!query.isLoading && !query.data?.items.length && <p className="p-8 text-center text-sm text-muted-foreground">No hay documentos para estos filtros.</p>}</div><div className="flex items-center justify-between border-t p-4 text-sm"><span>{query.data?.totalCount ?? 0} documentos</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><span className="px-2 py-2">Página {page} de {Math.max(1, query.data?.totalPages ?? 1)}</span><Button size="sm" variant="outline" disabled={page >= (query.data?.totalPages ?? 0)} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div></CardContent></Card>
    <AccountingDocumentDialog documentId={selected} onClose={() => setSelected(undefined)} />
  </div>;
}

function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
function Status({ value }: { value: string }) { return <Badge variant={value === "Posted" ? "secondary" : value === "Pending" ? "outline" : "destructive"}>{accountingStatusLabel(value)}</Badge>; }
