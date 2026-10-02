"use client";

import { Suspense, useRef, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FileSearch, FilePlus2, RefreshCw, Eye, Send, Loader2 } from "lucide-react";
import { PartySelect } from "@/components/parties/party-role-select";
import { accountingApi, type VoucherDraft } from "@/services/api/accounting";
import { AccountingDocumentDialog } from "@/components/accounting/accounting-document-dialog";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {fiscalStatusLabel} from "@/lib/accounting-labels";
import { useReferenceOptions } from "@/hooks/use-reference-options";

import { useRouter, useSearchParams } from "next/navigation";
import { useAuthStore } from "@/stores/auth-store";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";

const noPermissions: string[] = [];
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const year = new Date().getFullYear();

export default function FinancialTraceabilityPage() { return <Suspense fallback={<p>Cargando trazabilidad…</p>}><FinancialTraceabilityWorkspace /></Suspense>; }

function FinancialTraceabilityWorkspace() {
  const params = useSearchParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const draftId = params.get("document");
  const creating = params.get("new") === "1";
  const editing = creating || !!draftId;
  const statuses = useReferenceOptions("accounting-document-status");
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [from, setFrom] = useState(`${year}-01-01`);
  const [to, setTo] = useState(`${year}-12-31`);
  const [status, setStatus] = useState("all");
  const [documentType, setDocumentType] = useState("all");
  const [search, setSearch] = useState("");
  const [partyId, setPartyId] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string>();
  const [sendingId, setSendingId] = useState<string | null>(null);
  const sendingRef = useRef(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const documentTypes = useReferenceOptions("accounting-document-type", Boolean(tenantId && businessId));
  const draftKey = ["voucher-draft", tenantId, businessId, draftId];
  const draft = useQuery({ queryKey: draftKey, queryFn: () => accountingApi.voucherDraft(draftId!), enabled: !!draftId && !!tenantId && !!businessId, staleTime: Infinity, retry: false });
  const contextKey = `${tenantId ?? "none"}:${businessId ?? "none"}`;
  const filters = { from, to, documentType: documentType === "all" ? undefined : documentType, status: status === "all" ? undefined : status, search: search.trim() || undefined, partyId: partyId || undefined };
  const query = useQuery({
    queryKey: ["financial-traceability", contextKey, from, to, documentType, status, search, partyId, page],
    queryFn: () => accountingApi.documents({ ...filters, page, pageSize: 25 }),
    enabled: Boolean(tenantId && businessId) && !editing,
    staleTime: Infinity,
  });

  if (!tenantId || !businessId)
    return <Card><CardContent className="p-10 text-center text-muted-foreground">Selecciona una empresa y una sede para consultar su trazabilidad.</CardContent></Card>;

  function savedDraft(value: VoucherDraft) {
    queryClient.setQueryData(["voucher-draft", tenantId, businessId, value.documentId], value);
    queryClient.setQueryData(["financial-traceability", contextKey, from, to, documentType, status, search, partyId, page], (old: typeof query.data) => {
      if (!old) return old;
      const existed = old.items.some(row => row.sourceDocumentId === value.documentId);
      const matches = value.occurredAt.slice(0, 10) >= from && value.occurredAt.slice(0, 10) <= to &&
        (!partyId || (value.adjustment ? existed : value.lines.some(line => line.value.partyId === partyId))) &&
        (documentType === "all" || documentType === value.documentType) && (status === "all" || status === value.status) &&
        (!search.trim() || [value.reference, value.description, value.documentId, value.row.entryNumber].some(text => text?.toLowerCase().includes(search.trim().toLowerCase())));
      const items = old.items.filter(row => row.sourceDocumentId !== value.documentId);
      if (matches && (existed || creating && page === 1)) items.push(value.row);
      items.sort((a, b) => b.occurredAt.localeCompare(a.occurredAt) || a.sourceDocumentId.localeCompare(b.sourceDocumentId));
      const totalCount = old.totalCount + (creating && matches && !existed ? 1 : 0) - (existed && !matches ? 1 : 0);
      return { ...old, items: items.slice(0, old.pageSize), totalCount, totalPages: Math.ceil(totalCount / old.pageSize) };
    });
    if (creating) router.replace(`/dashboard/financial-traceability?document=${value.documentId}`, { scroll: false });
  }
  async function sendFromList(documentId: string) {
    if (sendingRef.current) return;
    sendingRef.current = true;
    setSendingId(documentId);
    setSendError(null);
    try {
      const current = await queryClient.fetchQuery({
        queryKey: ["voucher-draft", tenantId, businessId, documentId],
        queryFn: () => accountingApi.voucherDraft(documentId),
        staleTime: Infinity,
      });
      savedDraft(current.sentAt ? current : await accountingApi.sendVoucherDraft(documentId, current.rowVersion));
      queryClient.removeQueries({ queryKey: ["financial-traceability", contextKey], type: "inactive" });
    } catch (cause) {
      setSendError(cause instanceof Error ? cause.message : "No se pudo contabilizar el comprobante.");
    } finally {
      sendingRef.current = false;
      setSendingId(null);
    }
  }
  if (editing) {
    if (draft.isError) return <div role="alert">No se pudo abrir el comprobante. <Button onClick={() => void draft.refetch()}>Reintentar</Button><Button variant="outline" onClick={() => router.replace("/dashboard/financial-traceability")}>Volver</Button></div>;
    if (draftId && !draft.data) return <p>Cargando comprobante…</p>;
    return <VoucherDraftEditor key={[tenantId, businessId, draftId ?? "new"].join(":")} businessId={businessId} initial={draft.data} onSaved={savedDraft} onClose={() => router.replace("/dashboard/financial-traceability")} />;
  }

  return <div className="space-y-6">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-wrap items-center gap-4"><span className="rounded-2xl bg-white/10 p-3"><FileSearch className="h-7 w-7" /></span><div className="min-w-0 flex-1"><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">Documento → efecto → comprobante</p><h1 className="mt-1 text-3xl font-black">Trazabilidad financiera</h1><p className="mt-1 max-w-3xl text-sm text-cyan-50/80">Consulta en un solo lugar qué ocurrió fiscal y contablemente con ventas, compras, gastos, inventario, pagos, notas y devoluciones.</p></div>{permissions.includes("accounting.manual.create") && <Button variant="secondary" onClick={() => router.push("/dashboard/financial-traceability?new=1")}><FilePlus2 className="mr-2 h-4 w-4" />Nuevo comprobante</Button>}</div></header>
    <Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b"><div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_11rem_11rem_15rem_13rem]"><div><CardTitle>Documentos y comprobantes</CardTitle><p className="mt-1 text-sm text-muted-foreground">Abre cualquier fila para auditar cuentas, tercero, centro de costo, valores, estado DIAN y errores.</p></div><Field label="Desde"><DatePicker value={from} onChange={value => { setFrom(value); setPage(1); }} /></Field><Field label="Hasta"><DatePicker value={to} onChange={value => { setTo(value); setPage(1); }} /></Field><Field label="Tipo de operación"><Select value={documentType} onValueChange={value => { setDocumentType(value); setPage(1); }} disabled={documentTypes.isLoading || documentTypes.isError}><SelectTrigger aria-label="Tipo de operación"><SelectValue placeholder={documentTypes.isLoading ? "Cargando…" : "Todas las operaciones"} /></SelectTrigger><SelectContent><SelectItem value="all">Todas las operaciones</SelectItem>{(documentTypes.data ?? []).map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field><Field label="Estado"><Select value={status} onValueChange={value => { setStatus(value); setPage(1); }}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem>{statuses.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field></div><div className="mt-4 max-w-md"><Label>Tercero</Label><PartySelect value={partyId} onChange={value => { setPartyId(value); setPage(1); }} leadingOptions={[{ value: "", label: "Todos los terceros" }]} placeholder="Todos los terceros" /></div><ServerSearchInput className="mt-4" value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={query.isFetching} placeholder="Número del documento, comprobante o identificador" /></CardHeader>
      <CardContent className="p-0">{query.isError && <p role="alert" className="p-4 text-destructive">No se pudo cargar la trazabilidad.</p>}{sendError && <p role="alert" className="p-4 text-destructive">{sendError}</p>}<Button variant="ghost" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw className="mr-2 h-4 w-4" />Actualizar</Button><div className="overflow-x-auto"><table className="w-full min-w-[980px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Documento</th><th className="text-left">Fiscal</th><th className="text-left">Comprobante</th><th className="text-left">Estado</th><th className="text-right">Débito</th><th className="pr-3 text-left">Observación</th><th className="p-3 text-right">Acciones</th></tr></thead><tbody>{(query.data?.items ?? []).map(row => <tr onClick={() => row.hasManualDraft ? router.push(`/dashboard/financial-traceability?document=${row.sourceDocumentId}`) : setSelected(row.sourceDocumentId)} key={`${row.sourceDocumentType}-${row.sourceDocumentId}`} className="cursor-pointer border-t transition hover:bg-muted/40"><td className="p-3">{new Date(row.occurredAt).toLocaleDateString("es-CO")}</td><td><b>{row.sourceDocumentNumber ?? (documentTypes.data?.find(item => item.code === row.sourceDocumentType)?.label ?? row.sourceDocumentType)}</b><small className="block text-muted-foreground">{(documentTypes.data?.find(item => item.code === row.sourceDocumentType)?.label ?? row.sourceDocumentType)}</small></td><td>{row.dianNumber ?? "—"}<small className="block text-muted-foreground">{fiscalStatusLabel(row.fiscalStatus)}</small></td><td className="font-mono">{row.entryNumber ?? "—"}</td><td><Badge variant={row.status === "Posted" ? "secondary" : "outline"}>{statuses.data?.find(item => item.code === row.status)?.label ?? row.status}</Badge></td><td className="text-right">{row.debitTotal == null ? "—" : money.format(row.debitTotal)}</td><td className="pr-3 text-xs">{row.errorMessage ?? "—"}</td><td className="p-3 text-right whitespace-nowrap">{row.hasManualDraft && row.status === "Created" && permissions.includes("accounting.manual.send") && <Button variant="outline" size="sm" disabled={!!sendingId} onClick={event => { event.stopPropagation(); void sendFromList(row.sourceDocumentId); }}>{sendingId === row.sourceDocumentId ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Send className="mr-2 h-4 w-4" />}{sendingId === row.sourceDocumentId ? "Contabilizando…" : "Contabilizar"}</Button>}<Button variant="ghost" size="sm" aria-label="Ver documento" onClick={event => { event.stopPropagation(); if(row.hasManualDraft) router.push("/dashboard/financial-traceability?document=" + row.sourceDocumentId); else setSelected(row.sourceDocumentId); }}><Eye className="mr-2 h-4 w-4" />Ver</Button></td></tr>)}</tbody></table>{query.isLoading && <p className="p-8 text-center text-sm text-muted-foreground">Cargando documentos…</p>}{!query.isLoading && !query.data?.items.length && <p className="p-8 text-center text-sm text-muted-foreground">No hay documentos para estos filtros.</p>}</div><div className="flex items-center justify-between border-t p-4 text-sm"><span>{query.data?.totalCount ?? 0} documentos</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><span className="px-2 py-2">Página {page} de {Math.max(1, query.data?.totalPages ?? 1)}</span><Button size="sm" variant="outline" disabled={page >= (query.data?.totalPages ?? 0)} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div></CardContent></Card>
    <AccountingDocumentDialog documentId={selected} onClose={() => setSelected(undefined)} />
  </div>;
}

function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
