"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Copy, FileText, Plus, Save, Send, Trash2 } from "lucide-react";
import { accountingApi, type SaveVoucherDraft, type VoucherDraft, type VoucherDraftAdjustment, type VoucherDraftLineView } from "@/services/api/accounting";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { AccountSelect } from "./account-select";
import { AccountingDocumentDialog } from "./accounting-document-dialog";
import { ReportViewer } from "@/components/reports/report-viewer";
import { PartySelect } from "@/components/parties/party-role-select";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { payablesApi, type PayableDetail } from "@/services/api/payables";
import { receivablesApi, type ReceivableDetail } from "@/services/api/receivables";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const noPermissions: string[] = [];
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
const newLine = (): VoucherDraftLineView => ({ value: { accountId: null, partyId: null, costCenterId: null, description: "", debit: 0, credit: 0, reference: null }, accountCode: null, accountName: null, partyName: null, partyIdentification: null, costCenterName: null });
type AdjustmentOption = { id: string; number: string; party: string; outstanding: number; currency: string };

export function VoucherDraftEditor({ businessId, initial, adjustment, onSaved, onClose, onBusyChange, modal = false, closeLabel = "Volver a trazabilidad" }: {
  businessId: string; initial?: VoucherDraft; adjustment?: VoucherDraftAdjustment;
  onSaved?: (value: VoucherDraft) => void; onClose: () => void; onBusyChange?: (busy: boolean) => void;
  modal?: boolean; closeLabel?: string;
}) {
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const [saved, setSaved] = useState(initial);
  const [form, setForm] = useState<SaveVoucherDraft>(() => initial ? { ...initial, lines: initial.lines.map(line => line.value) } : {
    documentId: crypto.randomUUID(), documentType: adjustment ? "AccountAdjustment" : "ManualAccountingVoucher",
    occurredAt: `${new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date())}T12:00:00-05:00`,
    conceptCode: adjustment ? "ACCOUNT_ADJUSTMENT" : "MANUAL_VOUCHER", description: "", reference: null,
    lines: [], adjustment: adjustment ?? null, rowVersion: null,
  });
  const [lines, setLines] = useState<VoucherDraftLineView[]>(() => initial?.lines ?? (adjustment ? [] : [newLine(), newLine()]));
  const [scrollTop, setScrollTop] = useState(0);
  const [dirty, setDirty] = useState(!initial);
  const [phase, setPhase] = useState<"save" | "send" | "refresh" | "retry" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [entryOpen, setEntryOpen] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const [chosenPartyId, setChosenPartyId] = useState("");
  const concepts = useReferenceOptions("accounting-manual-concept");
  const directions = useReferenceOptions("accounting-adjustment-direction", !!form.adjustment);
  const statuses = useReferenceOptions("accounting-document-status");
  const centers = useQuery({ queryKey: ["voucher-centers", businessId], queryFn: accountingApi.costCenters, staleTime: 300_000 });
  const adjustmentKind = form.adjustment?.subledgerKind;
  const obligationId = form.adjustment?.subledgerId ?? "";
  const obligation = useQuery<PayableDetail | ReceivableDetail>({ queryKey: ["voucher-adjustment-obligation", businessId, adjustmentKind, obligationId],
    queryFn: async () => adjustmentKind === "Payable" ? await payablesApi.get(obligationId) : await receivablesApi.get(obligationId),
    enabled: !!adjustmentKind && !!obligationId, staleTime: 60_000 });
  const obligationPartyId = obligation.data && ("supplierId" in obligation.data ? obligation.data.supplierId : obligation.data.customerId);
  const partyId = chosenPartyId || obligationPartyId || "";
  const obligationPartyName = obligation.data && ("supplierName" in obligation.data ? obligation.data.supplierName : obligation.data.customerName);
  const obligationIdentification = obligation.data && ("supplierIdentification" in obligation.data ? obligation.data.supplierIdentification : obligation.data.customerIdentification);
  const selectedObligation = obligation.data && { value: obligationId, label: obligation.data.documentNumber,
    description: `${obligationPartyName} · Saldo ${money.format(obligation.data.outstandingAmount)}` };
  const readOnly = !!saved?.sentAt || !permissions.includes("accounting.manual.create");
  const busy = phase !== null;
  useEffect(() => { onBusyChange?.(busy); }, [busy, onBusyChange]);
  const debit = lines.reduce((sum, line) => sum + line.value.debit, 0);
  const credit = lines.reduce((sum, line) => sum + line.value.credit, 0);
  const rowHeight = 56;
  const start = lines.length >= 100 ? Math.max(0, Math.floor(scrollTop / rowHeight) - 4) : 0;
  const end = lines.length >= 100 ? Math.min(lines.length, start + 24) : lines.length;

  function change(patch: Partial<SaveVoucherDraft>) { setForm(current => ({ ...current, ...patch })); setDirty(true); }
  function updateLine(index: number, patch: Partial<VoucherDraftLineView>) {
    setLines(current => current.map((value, position) => position === index ? { ...value, ...patch } : value));
    setDirty(true);
  }
  function updateValue(index: number, patch: Partial<VoucherDraftLineView["value"]>) {
    setLines(current => current.map((line, position) => position === index ? { ...line, value: { ...line.value, ...patch } } : line));
    setDirty(true);
  }
  function accept(value: VoucherDraft) {
    setSaved(value); setForm({ ...value, lines: value.lines.map(line => line.value) }); setLines(value.lines);
    setDirty(false); onSaved?.(value);
  }
  async function save() {
    if (busy) return;
    if (!form.description.trim() || !form.occurredAt || !form.conceptCode) { setError("Completa fecha, concepto y descripción."); return; }
    if (form.adjustment && (!form.adjustment.subledgerId || !obligation.data)) { setError("Selecciona la factura u obligación que vas a ajustar."); return; }
    const missingDescription = lines.findIndex(line => !line.value.description.trim() || line.value.description.length > 500);
    if (missingDescription !== -1) { setError(`La descripción de la partida ${missingDescription + 1} es obligatoria y admite máximo 500 caracteres.`); return; }
    setPhase("save"); setError(null);
    try { accept(await accountingApi.saveVoucherDraft({ ...form, lines: lines.map(line => line.value) })); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo guardar el comprobante."); }
    finally { setPhase(null); }
  }
  async function send() {
    if (!saved || dirty || busy) return;
    setPhase("send"); setError(null);
    try { accept(await accountingApi.sendVoucherDraft(saved.documentId, saved.rowVersion)); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo contabilizar. Consulta el mismo comprobante antes de reintentar."); }
    finally { setPhase(null); }
  }
  async function refresh() {
    if (!saved || busy) return;
    setPhase("refresh"); setError(null);
    try { accept(await accountingApi.voucherDraft(saved.documentId)); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo consultar el estado."); }
    finally { setPhase(null); }
  }
  async function retry() {
    if (!saved || busy) return;
    setPhase("retry"); setError(null);
    try {
      const posting = await accountingApi.retryPosting(saved.documentId);
      accept({ ...saved, status: posting.status, row: { ...saved.row, status: posting.status,
        errorCode: posting.errorCode, errorMessage: posting.errorMessage, entryId: posting.entryId } });
    } catch (value) { setError(value instanceof Error ? value.message : "No se pudo reintentar la contabilización."); }
    finally { setPhase(null); }
  }
  function close() {
    if (busy) return;
    if (dirty && !modal) setDiscardOpen(true);
    else onClose();
  }

  if (reportOpen && saved) return <ReportViewer onClose={() => setReportOpen(false)}
    documentDownloads
    title={saved.row.entryNumber ?? "Comprobante manual"}
    description={`${saved.status === "Posted" ? "CONTABILIZADO" : "NO CONTABILIZADO"} · ${saved.occurredAt.slice(0, 10)} · ${saved.reference ?? saved.documentId} · ${saved.description} · ${saved.currencyCode}`}
    fileName={`comprobante-${saved.row.entryNumber ?? saved.documentId}`}
    rows={saved.lines.length ? saved.lines.map((line, index) => ({ id: index + 1, cuenta: line.accountCode, nombre: line.accountName,
      tercero: line.partyName, identificacion: line.partyIdentification, centro: line.costCenterName,
      descripcion: line.value.description, referencia: line.value.reference, debito: line.value.debit, credito: line.value.credit })) : [
        { id: 1, descripcion: saved.description, referencia: saved.adjustment?.subledgerId,
          direccion: directions.data?.find(item => item.code === saved.adjustment?.direction)?.label ?? saved.adjustment?.direction,
          valor: saved.adjustment?.amount }]}
    columns={saved.adjustment ? [{ key: "referencia", label: "Obligación" }, { key: "descripcion", label: "Motivo" },
      { key: "direccion", label: "Movimiento del saldo" }, { key: "valor", label: "Valor", align: "right", format: value => money.format(Number(value ?? 0)) }] : [{ key: "id", label: "Partida" }, { key: "cuenta", label: "Cuenta" }, { key: "nombre", label: "Nombre" },
      { key: "tercero", label: "Tercero" }, { key: "identificacion", label: "Identificación" }, { key: "centro", label: "Centro de costo" },
      { key: "descripcion", label: "Descripción" }, { key: "referencia", label: "Referencia" },
      { key: "debito", label: "Débito", align: "right", format: value => money.format(Number(value ?? 0)) },
      { key: "credito", label: "Crédito", align: "right", format: value => money.format(Number(value ?? 0)) }]} />;

  return <div className="space-y-5">
    {!modal && <header className="flex flex-wrap items-center gap-4 rounded-3xl bg-gradient-to-r from-slate-950 to-teal-800 p-6 text-white">
      <Button variant="secondary" size="icon" aria-label={closeLabel} disabled={busy} onClick={close}><ArrowLeft className="h-4 w-4" /></Button>
      <div className="flex-1"><h1 className="text-2xl font-bold">{form.adjustment ? "Ajuste de cartera" : "Comprobante manual"}</h1><p className="text-sm text-white/80">{saved ? statuses.data?.find(item => item.code === saved.status)?.label ?? saved.status : "Nueva captura"} · Documento interno, sin envío a DIAN</p></div>
      {saved?.row.entryId && <Button variant="secondary" onClick={() => setEntryOpen(true)}><FileText className="mr-2 h-4 w-4" />Ver comprobante contabilizado</Button>}
      {saved && !saved.row.entryId && <Button variant="secondary" disabled={dirty || busy} onClick={() => setReportOpen(true)}><FileText className="mr-2 h-4 w-4" />Imprimir / exportar</Button>}
    </header>}
    {error && <div role="alert" className="rounded-xl border border-destructive bg-destructive/10 p-4 text-sm">{error}</div>}
    {saved?.row.errorMessage && <div role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4">{saved.row.errorMessage}</div>}
    {form.adjustment && <section className="space-y-4 rounded-2xl border bg-card p-5" aria-label="Obligación a ajustar">
      <h2 className="font-semibold">{adjustmentKind === "Payable" ? "Cuenta por pagar" : "Cuenta por cobrar"}</h2>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2"><Label>{adjustmentKind === "Payable" ? "Proveedor" : "Cliente"}</Label><PartyRoleSelect role={adjustmentKind === "Payable" ? "Supplier" : "Customer"} value={partyId} selectedOption={obligationPartyId === partyId && obligationPartyName ? { value: partyId, label: obligationPartyName, description: obligationIdentification || undefined } : null} onChange={id => { setChosenPartyId(id); change({ adjustment: { ...form.adjustment!, subledgerId: "" } }); }} disabled={busy || readOnly} placeholder={adjustmentKind === "Payable" ? "Buscar proveedor" : "Buscar cliente"} /></div>
        <div className="space-y-2"><Label>Factura u obligación</Label><PagedEntitySelect<AdjustmentOption> queryKey={["voucher-adjustment-options", businessId, adjustmentKind, partyId]} value={obligationId} selectedOption={selectedObligation || null} disabled={busy || readOnly || !partyId} onChange={id => change({ adjustment: { ...form.adjustment!, subledgerId: id } })} onClear={obligationId ? () => change({ adjustment: { ...form.adjustment!, subledgerId: "" } }) : undefined} loadPage={async (search, page, pageSize) => {
          if (adjustmentKind === "Payable") {
            const result = await payablesApi.list({ supplierId: partyId, search, page, pageSize });
            return { ...result, items: result.items.map(item => ({ id: item.payableId, number: item.documentNumber, party: item.supplierName,
              outstanding: item.outstandingAmount, currency: item.currencyCode })) };
          }
          const result = await receivablesApi.list({ customerId: partyId, search, page, pageSize });
          return { ...result, items: result.items.map(item => ({ id: item.receivableId, number: item.documentNumber, party: item.customerName,
            outstanding: item.outstandingAmount, currency: item.currencyCode })) };
        }} getOption={item => ({ value: item.id, label: item.number, description: `${item.party} · Saldo ${money.format(item.outstanding)}` })} ariaLabel="Seleccionar factura para ajuste de cartera" searchPlaceholder="Buscar factura o documento…" emptyMessage="No hay obligaciones para este tercero." /></div>
      </div>
      {obligation.isError && <p role="alert" className="text-sm text-destructive">No se pudo cargar la obligación seleccionada. <Button variant="link" onClick={() => void obligation.refetch()}>Reintentar</Button></p>}
      {obligation.data && <div className="grid gap-3 rounded-xl bg-muted/50 p-4 text-sm sm:grid-cols-5"><div><span className="block text-muted-foreground">Tercero</span><strong>{obligationPartyName} · {obligationIdentification}</strong></div><div><span className="block text-muted-foreground">Documento</span><strong>{obligation.data.documentNumber}</strong></div><div><span className="block text-muted-foreground">Valor original</span><strong>{money.format(obligation.data.originalAmount)}</strong></div><div><span className="block text-muted-foreground">Saldo actual</span><strong>{money.format(obligation.data.outstandingAmount)}</strong></div><div><span className="block text-muted-foreground">Vencimiento</span><strong>{obligation.data.dueDate.slice(0, 10)}</strong></div></div>}
      {!obligationId && <p className="text-sm text-muted-foreground">Selecciona un tercero y luego su factura para habilitar el ajuste.</p>}
    </section>}
    <fieldset disabled={busy || readOnly || !!form.adjustment && !obligation.data} className="space-y-5 disabled:opacity-80">
      <section className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-3">
        <div className="space-y-2"><Label>Fecha contable</Label><DatePicker value={form.occurredAt.slice(0, 10)} onChange={date => change({ occurredAt: date ? `${date}T12:00:00-05:00` : "" })} /></div>
        <div className="space-y-2"><Label>Concepto</Label><Select value={form.conceptCode} onValueChange={conceptCode => change({ conceptCode })} disabled={busy || readOnly}><SelectTrigger aria-label="Concepto contable"><SelectValue /></SelectTrigger><SelectContent>{concepts.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label htmlFor="voucher-reference">Referencia</Label><Input id="voucher-reference" maxLength={100} value={form.reference ?? ""} onChange={event => change({ reference: event.target.value || null })} /></div>
        <div className="space-y-2 sm:col-span-3"><Label htmlFor="voucher-description">Descripción</Label><Input id="voucher-description" maxLength={500} value={form.description} onChange={event => change({ description: event.target.value })} placeholder="Motivo del comprobante" /></div>
      </section>
      {form.adjustment ? <section className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-3">
        <div className="space-y-2"><Label>Dirección del ajuste</Label><Select value={form.adjustment.direction} disabled={readOnly || busy} onValueChange={direction => change({ adjustment: { ...form.adjustment!, direction: direction as "Increase" | "Decrease" } })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{directions.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label>Valor del ajuste</Label><FormattedNumberInput kind="currency" ariaLabel="Valor del ajuste" value={form.adjustment.amount} onValueChange={amount => change({ adjustment: { ...form.adjustment!, amount: amount ?? 0 } })} /></div>
        <div className="space-y-2"><Label>Cuenta de contrapartida</Label><AccountSelect value={form.adjustment.counterpartAccountId ?? ""} onChange={counterpartAccountId => change({ adjustment: { ...form.adjustment!, counterpartAccountId } })} /></div>
        <p className="text-sm text-muted-foreground sm:col-span-3">La obligación elegida se conserva vinculada. Guardar no cambia su saldo; contabilizar valida el saldo disponible.</p>
      </section> : <section className="overflow-hidden rounded-2xl border bg-card">
        <div className="flex items-center justify-between gap-3 border-b p-4"><div><h2 className="font-semibold">Partidas del comprobante</h2><p className="text-sm text-muted-foreground">Edita cada partida en su fila. D = débito, C = crédito. Puedes guardar un comprobante incompleto.</p></div><Button variant="outline" disabled={lines.length >= 500} onClick={() => { setLines(current => [...current, newLine()]); setDirty(true); }}><Plus className="mr-2 h-4 w-4" />Agregar partida</Button></div>
        <div className="max-h-[32rem] overflow-auto" onScroll={event => setScrollTop(event.currentTarget.scrollTop)}><table className="w-full min-w-[1080px] table-fixed text-sm"><colgroup><col className="w-9" /><col className="w-[220px]" /><col className="w-[170px]" /><col className="w-[140px]" /><col className="w-[205px]" /><col className="w-[110px]" /><col className="w-[110px]" /><col className="w-[89px]" /></colgroup><thead className="sticky top-0 z-10 bg-muted"><tr><th className="p-2 text-left">D/C</th><th className="p-2 text-left">Cuenta PUC</th><th className="p-2 text-left">Tercero</th><th className="p-2 text-left">Centro de costo</th><th className="p-2 text-left">Descripción u observación</th><th className="p-2 text-right">Débito</th><th className="p-2 text-right">Crédito</th><th className="p-2 text-right">Acciones</th></tr></thead><tbody>
          {start > 0 && <tr aria-hidden><td colSpan={8} style={{ height: start * rowHeight }} /></tr>}
          {lines.slice(start, end).map((item, offset) => {
            const index = start + offset;
            const kind = item.value.debit > 0 && item.value.credit > 0 ? "D/C" : item.value.debit > 0 ? "D" : item.value.credit > 0 ? "C" : "—";
            return <tr key={index} aria-label={`Partida ${index + 1}`} className="h-[56px] border-t align-middle hover:bg-muted/30">
              <td className="px-2 text-center"><span title={kind === "D" ? "Débito" : kind === "C" ? "Crédito" : undefined} className="inline-flex min-w-7 justify-center rounded-md bg-muted px-1 py-1 font-bold">{kind}</span></td>
              <td className="p-1"><AccountSelect value={item.value.accountId ?? ""} accounts={item.value.accountId && item.accountCode ? [{ accountId: item.value.accountId, code: item.accountCode, name: item.accountName ?? "" }] : []} onChange={(accountId, account) => updateLine(index, { value: { ...item.value, accountId }, accountCode: account?.code ?? null, accountName: account?.name ?? null })} /></td>
              <td className="p-1"><PartySelect value={item.value.partyId ?? ""} selectedOption={item.value.partyId && item.partyName ? { value: item.value.partyId, label: item.partyName, description: item.partyIdentification ?? undefined } : null} onChange={(partyId, party) => updateLine(index, { value: { ...item.value, partyId: partyId || null }, partyName: party?.displayName ?? null, partyIdentification: party?.identification ?? null })} /></td>
              <td className="p-1"><Select value={item.value.costCenterId ?? "auto"} disabled={busy || readOnly} onValueChange={value => updateValue(index, { costCenterId: value === "auto" ? null : value })}><SelectTrigger aria-label={`Centro de costo de la partida ${index + 1}`}><SelectValue /></SelectTrigger><SelectContent><SelectItem value="auto">Automático</SelectItem>{centers.data?.filter(center => center.isActive).map(center => <SelectItem key={center.costCenterId} value={center.costCenterId}>{center.code} · {center.name}</SelectItem>)}</SelectContent></Select></td>
              <td className="p-1"><Input required maxLength={500} aria-label="Descripción de la partida" placeholder="Descripción u observación" value={item.value.description} onChange={event => updateValue(index, { description: event.target.value })} className="h-9" /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Débito de la partida" className="text-right tabular-nums" value={item.value.debit} onValueChange={debit => updateValue(index, { debit: debit ?? 0 })} /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Crédito de la partida" className="text-right tabular-nums" value={item.value.credit} onValueChange={credit => updateValue(index, { credit: credit ?? 0 })} /></td>
              <td className="p-1 text-right"><Button variant="ghost" size="icon" title="Duplicar partida" aria-label={`Duplicar partida ${index + 1}`} disabled={lines.length >= 500} onClick={() => { setLines(current => [...current.slice(0, index + 1), { ...item, value: { ...item.value } }, ...current.slice(index + 1)]); setDirty(true); }}><Copy className="h-4 w-4" /></Button><Button variant="ghost" size="icon" title="Eliminar partida" aria-label={`Eliminar partida ${index + 1}`} onClick={() => { setLines(current => current.filter((_, position) => position !== index)); setDirty(true); }}><Trash2 className="h-4 w-4" /></Button></td>
            </tr>;
          })}
          {end < lines.length && <tr aria-hidden><td colSpan={8} style={{ height: (lines.length - end) * rowHeight }} /></tr>}
        </tbody></table></div>
      </section>}
    </fieldset>
    <footer className="sticky bottom-0 flex flex-wrap items-center gap-4 rounded-2xl border bg-background p-4 shadow-lg">
      <div className="flex-1 text-sm">{!form.adjustment && <><b>Débito {money.format(debit)}</b> · Crédito {money.format(credit)}<p className={Math.abs(debit - credit) > 0.00001 ? "text-destructive" : "text-muted-foreground"}>Diferencia {money.format(debit - credit)} · {lines.length} partidas</p></>}<p className="text-muted-foreground">{saved?.sentAt ? "Documento enviado: contenido inmutable." : dirty ? "Cambios sin guardar. Guardar no contabiliza." : "Guardado. Se contabilizará únicamente cuando lo solicites."}</p></div>
      <Button variant="outline" disabled={busy} onClick={close}>{modal ? closeLabel : "Volver"}</Button>
      {saved && !dirty && <Button variant="outline" disabled={busy} onClick={() => void refresh()}>{phase === "refresh" ? "Consultando estado…" : "Actualizar estado"}</Button>}
      {saved?.sentAt && saved.status !== "Posted" && permissions.includes("accounting.postings.retry") && <Button disabled={busy} onClick={() => void retry()}>{phase === "retry" ? "Solicitando reintento…" : "Reintentar contabilización"}</Button>}
      {!readOnly && <Button variant="outline" disabled={busy || !dirty} onClick={() => void save()}><Save className="mr-2 h-4 w-4" />{phase === "save" ? "Guardando…" : "Guardar comprobante"}</Button>}
      {saved?.canSend && <Button disabled={busy || dirty} onClick={() => void send()}><Send className="mr-2 h-4 w-4" />{phase === "send" ? "Enviando a contabilidad…" : "Contabilizar"}</Button>}
    </footer>
    {entryOpen && <AccountingDocumentDialog documentId={saved?.documentId} hasFiscalDocument={false} onClose={() => setEntryOpen(false)} />}
    {!modal && <Dialog open={discardOpen} onOpenChange={setDiscardOpen}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>¿Salir sin guardar?</DialogTitle>
          <DialogDescription>Los cambios de este comprobante se perderán.</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setDiscardOpen(false)}>Seguir editando</Button>
          <Button variant="destructive" onClick={() => { setDiscardOpen(false); onClose(); }}>Salir sin guardar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>}
  </div>;
}
