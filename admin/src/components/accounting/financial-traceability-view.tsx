"use client";

import { Suspense, useRef, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FileSearch, FilePlus2, RefreshCw, Eye, SlidersHorizontal, ChevronDown } from "lucide-react";
import { PartySelect } from "@/components/parties/party-role-select";
import { accountingApi, type AccountingDocumentPage, type VoucherDraft, type VoucherDraftPreparation } from "@/services/api/accounting";
import { createVoucherPositionLine } from "@/lib/voucher-payment-preparation";
import { AccountingDocumentDialog } from "@/components/accounting/accounting-document-dialog";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { DocumentProcessingActions } from "./document-processing-actions";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {accountingReversalStatusLabel,fiscalStatusLabel} from "@/lib/accounting-labels";
import { useReferenceOptions } from "@/hooks/use-reference-options";

import { useRouter, useSearchParams } from "next/navigation";
import { useAuthStore } from "@/stores/auth-store";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { requireBusinessTimeZone } from "@/lib/accounting-document-date";
import { FinancialConceptSelect } from "./financial-concept-select";
import type { FinancialConcept } from "@/services/api/financial-concepts";

const noPermissions: string[] = [];
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });

type TraceabilityScope = "CashReceipt";

export function FinancialTraceabilityView({ scope }: { scope?: TraceabilityScope }) {
  return <Suspense fallback={<p>Cargando documentos…</p>}><ScopedFinancialTraceability scope={scope} /></Suspense>;
}

function ScopedFinancialTraceability({ scope }: { scope?: TraceabilityScope }){
  const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
  if(!tenant||!business)return <Card><CardContent className="p-10 text-center text-muted-foreground">Selecciona una empresa y una sede para consultar su trazabilidad.</CardContent></Card>;
  return <FinancialTraceabilityWorkspace key={`${tenant}/${business}/${scope ?? "all"}`} scope={scope} businessId={business}/>;
}

function FinancialTraceabilityWorkspace({ scope, businessId }: { scope?: TraceabilityScope; businessId: string }) {
  const params = useSearchParams();
  const router = useRouter();
  const basePath = scope === "CashReceipt" ? "/dashboard/cash-receipts" : "/dashboard/financial-traceability";
  const queryClient = useQueryClient();
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const draftId = params.get("document");
  const creating = params.get("new") === "1";
  const payableId = !scope && creating && !draftId ? params.get("payable") : null;
  const editing = creating || !!draftId;
  const statuses = useReferenceOptions("accounting-document-status",!editing);
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const { today, timeZone } = useBusinessDates(businessId);
  const localToday=today(),year=localToday.slice(0,4);
  const [from, setFrom] = useState(`${year}-01-01`);
  const [to, setTo] = useState(`${year}-12-31`);
  const [status, setStatus] = useState("all");
  const [documentType, setDocumentType] = useState("all");
  const [voucherTypeCode,setVoucherTypeCode]=useState<string>(scope ?? "all");
  const [search, setSearch] = useState("");
  const [partyId, setPartyId] = useState("");
  const [conceptFilter,setConceptFilter]=useState<FinancialConcept|null>(null);
  const [page, setPage] = useState(1);
  const [filtersOpen,setFiltersOpen]=useState(false);
  const filterCount=[from!==`${year}-01-01`,to!==`${year}-12-31`,status!=="all",documentType!=="all",voucherTypeCode!==(scope??"all"),!!partyId,!!conceptFilter].filter(Boolean).length;
  const [selected, setSelected] = useState<string>();
  const [sendingId, setSendingId] = useState<string | null>(null);
  const sendingRef = useRef(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const documentTypes = useReferenceOptions("accounting-document-type", !editing && Boolean(tenantId && businessId),{from,to,dateRangeVersion:2});
  const voucherTypes=useReferenceOptions("accounting-voucher-type",!editing && Boolean(tenantId && businessId));
  const draftKey = ["voucher-draft", tenantId, businessId, draftId];
  const draft = useQuery({ queryKey: draftKey, queryFn: () => accountingApi.voucherDraft(draftId!), enabled: !!draftId && !!tenantId && !!businessId, staleTime: Infinity, retry: false });
  const canPreparePayment = permissions.includes("accounting.read") && permissions.includes("accounting.manual.create");
  const paymentPreparation = useQuery<VoucherDraftPreparation>({
    queryKey: ["voucher-payable-preparation", tenantId, businessId, payableId, localToday],
    enabled: !!payableId && canPreparePayment,
    retry: false, staleTime: Infinity, refetchOnWindowFocus: false, refetchOnReconnect: false,
    queryFn: async () => {
      const page = await accountingApi.openPositions({ subledgerKind: "Payable", subledgerId: payableId!,
        page: 1, pageSize: 1, effectiveOn: localToday });
      const position = page.items[0];
      if (page.totalCount !== 1 || !position || position.subledgerId !== payableId)
        throw new Error("La cuenta por pagar ya no está disponible para este egreso.");
      if (position.openingLinkIssue || !position.accountId)
        throw new Error(position.openingLinkIssue || "La cuenta por pagar requiere conciliación de su apertura antes de pagar.");
      if (position.currencyCode !== "COP" || position.availableAmount <= 0)
        throw new Error("La obligación no tiene saldo disponible en COP. Revisa sus pagos pendientes.");
      return { description: `Pago ${position.documentNumber}`, reference: position.documentNumber,
        mainPartyId: position.partyId, mainPartyName: position.partyName,
        mainPartyIdentification: position.partyIdentification,
        lines: [createVoucherPositionLine(position, position.availableAmount, null)] };
    },
  });
  const contextKey = `${tenantId ?? "none"}:${businessId ?? "none"}`;
  const filters = { from, to, dateRangeVersion:2, voucherTypeCode:voucherTypeCode==="all"?undefined:voucherTypeCode, documentType: documentType === "all" ? undefined : documentType, status: status === "all" ? undefined : status, search: search.trim() || undefined, partyId: partyId || undefined,financialConceptId:conceptFilter?.financialConceptId };
  const listKey = ["financial-traceability", contextKey, from, to, documentType, voucherTypeCode, status, search, partyId, conceptFilter?.financialConceptId, page, timeZone];
  const query = useQuery({
    queryKey: listKey,
    queryFn: () => accountingApi.documents({ ...filters, page, pageSize: 25 }),
    enabled: Boolean(tenantId && businessId) && !editing,
    staleTime: Infinity,
  });

  function savedDraft(value: VoucherDraft, isNew = creating) {
    queryClient.setQueryData(["voucher-draft", tenantId, businessId, value.documentId], value);
    // The command returns this document, not the filtered page or its rank.
    // Mark pages stale without I/O; their sole owner reads once on explicit
    // return to the list. Never reconstruct SQL filters/order/totals locally.
    void queryClient.invalidateQueries({ queryKey: ["financial-traceability", contextKey], refetchType: "none" });
    if (isNew) {
      const destination = scope && value.voucherTypeCode !== scope ? "/dashboard/financial-traceability" : basePath;
      router.replace(`${destination}?document=${value.documentId}`, { scroll: false });
    }
  }
  function discardedDraft(documentId: string) {
    void queryClient.invalidateQueries({ queryKey: ["financial-traceability", contextKey], refetchType: "none" });
    queryClient.removeQueries({ queryKey: ["voucher-draft", tenantId, businessId, documentId] });
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
      setSendingId(null);
      // Sending from the visible list can change filter membership and totals;
      // request its authoritative page once, after posting has finished.
      await query.refetch();
    } catch (cause) {
      setSendError(cause instanceof Error ? cause.message : "No se pudo contabilizar el comprobante.");
    } finally {
      sendingRef.current = false;
      setSendingId(null);
    }
  }
  if (editing) {
    if (payableId && !canPreparePayment) return <div role="alert">No tienes permiso para preparar este egreso.<Button variant="outline" onClick={() => router.replace(basePath)}>Volver</Button></div>;
    if (payableId && paymentPreparation.isError) return <div role="alert">{paymentPreparation.error.message}<Button onClick={() => void paymentPreparation.refetch()}>Reintentar</Button><Button variant="outline" onClick={() => router.replace(basePath)}>Volver</Button></div>;
    if (payableId && !paymentPreparation.data) return <p>Cargando obligación para el egreso…</p>;
    if (draft.isError) return <div role="alert">No se pudo abrir el comprobante. <Button onClick={() => void draft.refetch()}>Reintentar</Button><Button variant="outline" onClick={() => router.replace(basePath)}>Volver</Button></div>;
    if (draftId && !draft.data) return <p>Cargando comprobante…</p>;
    return <VoucherDraftEditor key={[tenantId, businessId, draftId ?? payableId ?? "new"].join(":")} businessId={businessId} initial={draft.data} preparation={payableId ? paymentPreparation.data : undefined} voucherTypeScope={payableId ? "Disbursement" : scope} closeLabel={scope ? "Volver a recibos de caja" : "Volver a trazabilidad"} onSaved={savedDraft} onDiscarded={discardedDraft} onCloned={value => savedDraft(value, true)} onClose={() => router.replace(basePath)} />;
  }

  return <div className="space-y-6">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-wrap items-center gap-4"><span className="rounded-2xl bg-white/10 p-3"><FileSearch className="h-7 w-7" /></span><div className="min-w-0 flex-1"><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">{scope ? "Entradas de dinero" : "Documento → efecto → comprobante"}</p><h1 className="mt-1 text-3xl font-black">{scope ? "Recibos de caja" : "Trazabilidad financiera"}</h1><p className="mt-1 max-w-3xl text-sm text-cyan-50/80">{scope ? "Consulta las entradas de dinero recibidas de cualquier tercero y sus aplicaciones." : "Consulta en un solo lugar qué ocurrió fiscal y contablemente con ventas, compras, gastos, inventario, pagos, notas y devoluciones."}</p></div>{permissions.includes("accounting.manual.create") && <Button variant="secondary" onClick={() => router.push(`${basePath}?new=1`)}><FilePlus2 className="mr-2 h-4 w-4" />{scope ? "Nuevo recibo de caja" : "Nuevo comprobante"}</Button>}</div></header>
    <Card className="overflow-hidden rounded-3xl"><CardHeader className="space-y-4 border-b">
        <div><CardTitle>{scope ? "Recibos de caja" : "Documentos y comprobantes"}</CardTitle><p className="mt-1 text-sm text-muted-foreground">Consulta documentos, cuentas, terceros y su estado de procesamiento.</p></div>
        <Collapsible open={filtersOpen} onOpenChange={setFiltersOpen}>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center"><ServerSearchInput className="min-w-0 flex-1" value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={query.isFetching} placeholder={scope ? "Número o referencia del recibo" : "Número del documento, comprobante o identificador"} />
            <CollapsibleTrigger asChild><Button variant="outline" className="shrink-0" aria-label="Filtros de documentos"><SlidersHorizontal className="mr-2 h-4 w-4"/>Filtros{filterCount>0&&<Badge variant="secondary" className="ml-2">{filterCount}</Badge>}<ChevronDown className={`ml-2 h-4 w-4 transition-transform ${filtersOpen?"rotate-180":""}`}/></Button></CollapsibleTrigger>
          </div>
          <CollapsibleContent className="mt-4 grid gap-3 border-t pt-4 sm:grid-cols-2 xl:grid-cols-4">
            <Field label="Desde" htmlFor="financial-filter-from"><DatePicker today={localToday} id="financial-filter-from" value={from} onChange={value => { setFrom(value); setPage(1); }} /></Field><Field label="Hasta" htmlFor="financial-filter-to"><DatePicker today={localToday} id="financial-filter-to" value={to} onChange={value => { setTo(value); setPage(1); }} /></Field>{!scope && <Field label="Tipo de operación"><Select value={documentType} onValueChange={value => { setDocumentType(value); setPage(1); }} disabled={!!scope || documentTypes.isLoading || documentTypes.isError}><SelectTrigger aria-label="Tipo de operación"><SelectValue placeholder={documentTypes.isLoading ? "Cargando…" : "Todas las operaciones"} /></SelectTrigger><SelectContent><SelectItem value="all">Todas las operaciones</SelectItem>{(documentTypes.data ?? []).map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field>}<Field label="Estado" htmlFor="financial-filter-status"><Select value={status} onValueChange={value => { setStatus(value); setPage(1); }}><SelectTrigger id="financial-filter-status"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem>{statuses.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>{!scope && <Field label="Tipo de comprobante"><Select value={voucherTypeCode} onValueChange={value=>{setVoucherTypeCode(value);setPage(1);}} disabled={!!scope||voucherTypes.isLoading||voucherTypes.isError}><SelectTrigger aria-label="Tipo de comprobante"><SelectValue placeholder="Todos los comprobantes"/></SelectTrigger><SelectContent><SelectItem value="all">Todos los comprobantes</SelectItem>{voucherTypes.data?.map(option=><SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field>}
            <Field label="Concepto de las partidas"><FinancialConceptSelect value={conceptFilter?.financialConceptId??""} usage="Filter" selectedOption={conceptFilter?{value:conceptFilter.financialConceptId,label:conceptFilter.code+" · "+conceptFilter.name}:null} onChange={value=>{setConceptFilter(value);setPage(1);}}/></Field>
            <div className="min-w-0 sm:col-span-2"><Field label="Tercero"><PartySelect value={partyId} onChange={value => { setPartyId(value); setPage(1); }} leadingOptions={[{ value: "", label: "Todos los terceros" }]} placeholder="Todos los terceros" /></Field></div>
          </CollapsibleContent>
        </Collapsible>
      </CardHeader>
      <CardContent className="p-0">{query.isError && <p role="alert" className="p-4 text-destructive">{scope ? "No se pudieron cargar los recibos de caja." : "No se pudo cargar la trazabilidad."}</p>}{sendError && <p role="alert" className="p-4 text-destructive">{sendError}</p>}<Button variant="ghost" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw className="mr-2 h-4 w-4" />Actualizar</Button><div className="overflow-x-auto"><table className="w-full min-w-[1080px] text-sm [&_th]:px-3 [&_td]:px-3"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Documento</th><th className="text-left">Tercero principal</th>{!scope && <th className="text-left">Fiscal</th>}<th className="text-left">Comprobante</th><th className="text-left">Estado</th><th className="text-right">Débito</th><th className="text-right">Crédito</th><th className="pr-3 text-left">Observación</th><th className="p-3 text-right">Acciones</th></tr></thead><tbody>{(query.data?.items ?? []).map(row => <tr onClick={() => row.hasManualDraft ? router.push(`${basePath}?document=${row.sourceDocumentId}`) : setSelected(row.sourceDocumentId)} key={`${row.sourceDocumentType}-${row.sourceDocumentId}`} className="cursor-pointer border-t transition hover:bg-muted/40"><td className="p-3">{new Date(row.occurredAt).toLocaleDateString("es-CO", { timeZone: requireBusinessTimeZone(timeZone) })}</td><td><b>{row.sourceDocumentNumber ?? (documentTypes.data?.find(item => item.code === row.sourceDocumentType)?.label ?? row.sourceDocumentType)}</b><small className="block text-muted-foreground">{(documentTypes.data?.find(item => item.code === row.sourceDocumentType)?.label ?? row.sourceDocumentType)}</small></td><td>{row.mainPartyName ?? "—"}<small className="block text-muted-foreground">{row.mainPartyIdentification}</small></td>{!scope && <td>{row.dianNumber ?? "—"}<small className="block text-muted-foreground">{fiscalStatusLabel(row.fiscalStatus)}</small></td>}<td className="font-mono">{row.entryNumber ?? "—"}<small className="block font-sans text-muted-foreground">{voucherTypes.data?.find(type=>type.code===row.voucherTypeCode)?.label??row.voucherTypeCode??"Sin tipo informado"}</small></td><td><Badge variant={row.status === "Posted" ? "secondary" : "outline"}>{accountingReversalStatusLabel(row.reversalDocumentId,row.reversalStatus) ?? statuses.data?.find(item => item.code === row.status)?.label ?? row.status}</Badge></td><td className="text-right">{row.debitTotal == null ? "—" : money.format(row.debitTotal)}</td><td className="text-right">{row.creditTotal == null ? "—" : money.format(row.creditTotal)}</td><td className="pr-3 text-xs">{row.errorMessage ?? "—"}</td><td className="p-3 text-right whitespace-nowrap"><DocumentProcessingActions documentId={row.sourceDocumentId} accountingStatus={row.status} accountingErrorMessage={row.errorMessage}
 fiscalStatus={row.fiscalStatus} fiscalErrorMessage={row.fiscalErrorMessage} sendingDraft={sendingId===row.sourceDocumentId}
 onSendDraft={!sendingId&&!query.isFetching&&row.hasManualDraft&&row.status==="Created"&&permissions.includes("accounting.manual.send")?()=>void sendFromList(row.sourceDocumentId):undefined}
 onChanged={value=>{if(value.accountingStatus&&(status!=="all"||value.accountingStatus==="Posted")){void query.refetch();return;}queryClient.setQueryData<AccountingDocumentPage>(listKey,current=>current?{...current,items:current.items.map(item=>item.sourceDocumentId===row.sourceDocumentId?{...item,
 ...(value.accountingStatus?{status:value.accountingStatus,errorMessage:value.accountingErrorMessage??null}:{}),
 ...(value.fiscalStatus?{fiscalStatus:value.fiscalStatus,fiscalErrorMessage:value.fiscalErrorMessage??null}:{})}:item)}:current);}}/>
<Button variant="ghost" size="sm" aria-label="Ver documento" onClick={event => { event.stopPropagation(); if(row.hasManualDraft) router.push(basePath + "?document=" + row.sourceDocumentId); else setSelected(row.sourceDocumentId); }}><Eye className="mr-2 h-4 w-4" />Ver</Button></td></tr>)}</tbody></table>{query.isLoading && <p className="p-8 text-center text-sm text-muted-foreground">Cargando documentos…</p>}{!query.isError && !query.isLoading && !query.data?.items.length && <p className="p-8 text-center text-sm text-muted-foreground">No hay documentos para estos filtros.</p>}</div><div className="flex items-center justify-between border-t p-4 text-sm"><span>{query.data?.totalCount ?? 0} documentos</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><span className="px-2 py-2">Página {page} de {Math.max(1, query.data?.totalPages ?? 1)}</span><Button size="sm" variant="outline" disabled={page >= (query.data?.totalPages ?? 0)} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div></div></CardContent></Card>
    <AccountingDocumentDialog documentId={selected} hasFiscalDocument={scope ? false : undefined} onClose={() => setSelected(undefined)} onCloned={value => { setSelected(undefined); savedDraft(value, true); }} />
  </div>;
}

function Field({ label, children, htmlFor }: { label: string; children: ReactNode; htmlFor?: string }) { return <div className="space-y-2"><Label htmlFor={htmlFor}>{label}</Label>{children}</div>; }
