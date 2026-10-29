"use client";

import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowLeftRight, Copy, FileText, Plus, Save, Send, Trash2 } from "lucide-react";
import { accountingApi, type AccountingOpenPosition, type SaveVoucherDraft, type VoucherDraft, type VoucherDraftAdjustment, type VoucherDraftLineView, type VoucherDraftPreparation } from "@/services/api/accounting";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { AccountSelect } from "./account-select";
import { AccountingDocumentDialog } from "./accounting-document-dialog";
import { CloneVoucherButton } from "./clone-voucher-button";
import { VoucherPositionDialog } from "./voucher-position-dialog";
import { VoucherPendingDocuments } from "./voucher-pending-documents";
import { VoucherFiscalDialog } from "./voucher-fiscal-dialog";
import { proposedAccountingDate } from "@/lib/accounting-document-date";
import { ReportViewer } from "@/components/reports/report-viewer";
import { PartySelect } from "@/components/parties/party-role-select";
import { SupplierSiteSelect } from "@/components/parties/supplier-site-select";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

const noPermissions: string[] = [];
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
const newLine = (): VoucherDraftLineView => ({ value: { lineId: crypto.randomUUID(), accountId: null, partyId: null, costCenterId: null, description: "", debit: 0, credit: 0, reference: null, baseAmount: null }, accountCode: null, accountName: null, partyName: null, partyIdentification: null, costCenterName: null });
const identifyLines = (lines: VoucherDraftLineView[]) => lines.map(line => line.value.lineId ? line
  : { ...line, value: { ...line.value, lineId: crypto.randomUUID() } });
const editablePayloadVersion = (draft: VoucherDraft) => draft.documentType === "ManualAccountingVoucher"
  ? draft.payloadVersion === 4 ? 4 : 2 : null;

export function VoucherDraftEditor({ businessId, initial, adjustment, preparation, onSaved, onCloned, onClose, onBusyChange, modal = false, closeLabel = "Volver a trazabilidad" }: {
  businessId: string; initial?: VoucherDraft; adjustment?: VoucherDraftAdjustment; preparation?: VoucherDraftPreparation;
  onSaved?: (value: VoucherDraft) => void; onClose: () => void; onBusyChange?: (busy: boolean) => void;
  onCloned?: (value: VoucherDraft) => void;
  modal?: boolean; closeLabel?: string;
}) {
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(initial);
  const [form, setForm] = useState<SaveVoucherDraft>(() => initial ? { ...initial,
    workSessionId: initial.sentAt ? initial.workSessionId : null,
    payloadVersion: editablePayloadVersion(initial), lines: initial.lines.map(line => line.value) } : {
    documentId: crypto.randomUUID(), documentType: adjustment ? "AccountAdjustment" : "ManualAccountingVoucher",
    occurredAt: proposedAccountingDate(),
    conceptCode: adjustment ? "ACCOUNT_ADJUSTMENT" : "MANUAL_VOUCHER", description: preparation?.description ?? "", reference: preparation?.reference ?? null,
    mainPartyId: preparation?.mainPartyId ?? null, voucherTypeCode: adjustment ? null : "Accounting",
    lines: [], adjustment: adjustment ?? null, rowVersion: null, payloadVersion: adjustment ? null : 4,
    workSessionId: null,
  });
  const [lines, setLines] = useState<VoucherDraftLineView[]>(() => initial ? identifyLines(initial.lines) : preparation ? identifyLines(preparation.lines) : (adjustment ? [] : [newLine(), newLine()]));
  const [scrollTop, setScrollTop] = useState(0);
  const [dirty, setDirty] = useState(!initial || !initial.sentAt && initial.documentType === "ManualAccountingVoucher" &&
    (initial.payloadVersion !== editablePayloadVersion(initial) || !!initial.workSessionId));
  const [phase, setPhase] = useState<"save" | "send" | "refresh" | "retry" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [entryOpen, setEntryOpen] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const confirmUnsavedChanges = !modal || Boolean(preparation);
  const [positionOpen, setPositionOpen] = useState(false);
  const [showPendingDocuments, setShowPendingDocuments] = useState(false);
  const [fiscalOpen, setFiscalOpen] = useState(false);
  const [fiscalBusy, setFiscalBusy] = useState(false);
  const [detailLineId, setDetailLineId] = useState<string | null>(null);
  const [chosenPartyId, setChosenPartyId] = useState("");
  const [mainParty, setMainParty] = useState({ name: initial?.mainPartyName ?? preparation?.mainPartyName ?? null, identification: initial?.mainPartyIdentification ?? preparation?.mainPartyIdentification ?? null });
  const voucherTypes = useReferenceOptions("accounting-voucher-type", form.payloadVersion === 4);
  const franchises = useReferenceOptions("card-franchise", detailLineId !== null);
  const directions = useReferenceOptions("accounting-adjustment-direction", !!form.adjustment);
  const statuses = useReferenceOptions("accounting-document-status");
  const centers = useQuery({ queryKey: ["voucher-centers", businessId], queryFn: accountingApi.costCenters, staleTime: 300_000 });
  const adjustmentKind = form.adjustment?.subledgerKind;
  const obligationId = form.adjustment?.subledgerId ?? "";
  const obligation = useQuery<AccountingOpenPosition | null>({ queryKey: ["voucher-adjustment-obligation", businessId, adjustmentKind, obligationId],
    queryFn: async () => (await accountingApi.openPositions({ subledgerKind: adjustmentKind!, subledgerId: obligationId, page: 1, pageSize: 1 })).items[0] ?? null,
    enabled: !!adjustmentKind && !!obligationId, staleTime: 60_000 });
  const obligationPartyId = obligation.data?.counterpartyRoleId;
  const partyId = chosenPartyId || obligationPartyId || "";
  const obligationPartyName = obligation.data?.partyName;
  const obligationIdentification = obligation.data?.partyIdentification;
  const selectedObligation = obligation.data && { value: obligationId, label: obligation.data.documentNumber,
    description: `${obligationPartyName} · Disponible ${money.format(obligation.data.availableAmount)}` };
  const readOnly = !!saved?.sentAt || !permissions.includes("accounting.manual.create");
  const statusLabel = saved ? statuses.data?.find(item => item.code === saved.status)?.label ?? saved.status : "Nueva captura";
  const postedEntryButton = saved?.row.entryId && <Button variant="secondary" onClick={() => setEntryOpen(true)}><FileText className="mr-2 h-4 w-4" />Ver comprobante contabilizado</Button>;
  const busy = phase !== null || fiscalBusy;
  const detailIndex = lines.findIndex(line => line.value.lineId === detailLineId);
  const detailLine = detailIndex < 0 ? null : lines[detailIndex];
  const financialGroups = [...new Set(lines.map(line => line.value.financialGroupId).filter((id): id is string => !!id))];
  const linkedPositions = lines.flatMap(line => line.value.existingPosition ? [{ ...line.value.existingPosition,
    amount: line.value.debit + line.value.credit, financialDiscountEarned: line.value.financialDiscountEarned }] : []);
  const supportCancellation = form.voucherTypeCode === "SupportDocumentCancellation";
  const fiscalDocument = form.voucherTypeCode === "SupportDocument" || supportCancellation;
  useEffect(() => { onBusyChange?.(busy); }, [busy, onBusyChange]);
  const debit = lines.reduce((sum, line) => sum + line.value.debit, 0);
  const credit = lines.reduce((sum, line) => sum + line.value.credit, 0);
  const rowHeight = 56;
  const start = lines.length >= 100 ? Math.max(0, Math.floor(scrollTop / rowHeight) - 4) : 0;
  const end = lines.length >= 100 ? Math.min(lines.length, start + 24) : lines.length;

  function markChanged(remainingLineIds?: Set<string>) {
    setForm(current => {
      if (!current.fiscalCapture) return current;
      const acquisitions = current.fiscalCapture.acquisitions.filter(item => !remainingLineIds || remainingLineIds.has(item.sourceLineId))
        .map(item => remainingLineIds && item.inputVatSourceLineId && !remainingLineIds.has(item.inputVatSourceLineId)
          ? { ...item, inputVatSourceLineId: null } : item);
      return { ...current, fiscalCapture: acquisitions.length || current.voucherTypeCode === "SupportDocumentCancellation" ? { ...current.fiscalCapture, acquisitions, calculationHash: null,
        withholdingBindings: current.fiscalCapture.withholdingBindings?.filter(item => !remainingLineIds || remainingLineIds.has(item.sourceLineId)) } : null };
    });
    setDirty(true);
  }
  function change(patch: Partial<SaveVoucherDraft>) {
    setForm(current => ({ ...current, ...patch, fiscalCapture: Object.hasOwn(patch, "fiscalCapture") ? patch.fiscalCapture
      : patch.voucherTypeCode && patch.voucherTypeCode !== current.voucherTypeCode ? null
      : current.fiscalCapture ? { ...current.fiscalCapture, calculationHash: null } : null }));
    setDirty(true);
  }
  function updateLine(index: number, patch: Partial<VoucherDraftLineView>) {
    setLines(current => current.map((value, position) => position === index ? { ...value, ...patch } : value));
    markChanged();
  }
  function updateValue(index: number, patch: Partial<VoucherDraftLineView["value"]>) {
    setLines(current => current.map((line, position) => position === index ? { ...line, value: { ...line.value, ...patch } } : line));
    markChanged();
  }
  function accept(value: VoucherDraft) {
    setSaved(value); setForm({ ...value, workSessionId: value.sentAt ? value.workSessionId : null,
      payloadVersion: editablePayloadVersion(value),
      lines: value.lines.map(line => line.value) }); setLines(identifyLines(value.lines));
    setMainParty({ name: value.mainPartyName ?? null, identification: value.mainPartyIdentification ?? null });
    setDirty(!value.sentAt && value.documentType === "ManualAccountingVoucher" &&
      (value.payloadVersion !== editablePayloadVersion(value) || !!value.workSessionId)); onSaved?.(value);
  }
  async function save() {
    if (busy) return;
    if (!form.description.trim() || !form.occurredAt || !form.conceptCode) { setError("Completa fecha y descripción."); return; }
    if (form.adjustment && (!form.adjustment.subledgerId || !obligation.data)) { setError("Selecciona la factura u obligación que vas a ajustar."); return; }
    const missingDescription = lines.findIndex(line => !line.value.description.trim() || line.value.description.length > 500);
    if (missingDescription !== -1) { setError(`La descripción de la partida ${missingDescription + 1} es obligatoria y admite máximo 500 caracteres.`); return; }
    setPhase("save"); setError(null);
    try { accept(await accountingApi.saveVoucherDraft({ ...form, workSessionId: null, lines: lines.map(line => line.value) })); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo guardar el comprobante."); }
    finally { setPhase(null); }
  }
  async function send() {
    if (!saved || dirty || busy) return;
    if (saved.payloadVersion === 4 && !saved.mainPartyId) {
      setError("Selecciona el tercero principal antes de contabilizar el comprobante."); return;
    }
    setPhase("send"); setError(null);
    try { accept(await accountingApi.sendVoucherDraft(saved.documentId, saved.rowVersion)); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo contabilizar. Consulta el mismo comprobante antes de reintentar."); }
    finally { setPhase(null); }
  }
  async function reviewCancellation() {
    if (busy || !supportCancellation) return;
    setFiscalBusy(true); setError(null);
    try {
      const preview = await accountingApi.previewVoucherFiscal(businessId,
        { ...form, lines: lines.map(line => line.value), fiscalCapture: { acquisitions: [] } });
      if (!preview.canConfirm) throw new Error(preview.diagnostics.join(" "));
      change({ fiscalCapture: { acquisitions: [], calculationHash: preview.calculationHash } });
    } catch (value) { setError(value instanceof Error ? value.message : "No se pudo revisar la anulación. No se anuló el soporte."); }
    finally { setFiscalBusy(false); }
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
    if (dirty && confirmUnsavedChanges) setDiscardOpen(true);
    else onClose();
  }
  function updateEvidence(patch: Partial<NonNullable<VoucherDraftLineView["value"]["paymentEvidence"]>>) {
    if (!detailLine) return;
    const evidence = { ...detailLine.value.paymentEvidence, ...patch };
    updateValue(detailIndex, { paymentEvidence: Object.values(evidence).some(value => value !== null && value !== undefined && value !== "") ? evidence : null });
  }

  function addPositionLines(added: VoucherDraftLineView[]) {
    setLines(current => {
      if (added.length > 1) return [...current.filter(item => item.value.accountId || item.value.description || item.value.debit || item.value.credit || item.value.existingPosition), ...added];
      const line = added[0];
      const existing = current.findIndex(item => item.value.existingPosition?.subledgerKind === line.value.existingPosition?.subledgerKind &&
        item.value.existingPosition?.subledgerId === line.value.existingPosition?.subledgerId);
      if (existing >= 0) return current.map((item, index) => index === existing ? { ...item,
        accountCode: line.accountCode, accountName: line.accountName, partyName: line.partyName,
        partyIdentification: line.partyIdentification, value: { ...item.value, accountId: line.value.accountId,
          partyId: line.value.partyId, partySiteId: line.value.partySiteId, debit: line.value.debit,
          credit: line.value.credit, financialDiscountEarned: line.value.financialDiscountEarned } } : item);
      const empty = current.findIndex(item => !item.value.accountId && !item.value.description && !item.value.debit &&
        !item.value.credit && !item.value.existingPosition);
      return empty < 0 ? [...current, line] : current.map((item, index) => index === empty ? line : item);
    });
    markChanged();
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

  return <div className="min-w-0 space-y-5">
    {!modal && <header className="flex flex-wrap items-center gap-4 rounded-3xl bg-gradient-to-r from-slate-950 to-teal-800 p-6 text-white">
      <Button variant="secondary" size="icon" aria-label={closeLabel} disabled={busy} onClick={close}><ArrowLeft className="h-4 w-4" /></Button>
      <div className="flex-1"><h1 className="text-2xl font-bold">{form.adjustment ? "Ajuste de cartera" : "Comprobante manual"}</h1><p className="text-sm text-white/80">{statusLabel} · {supportCancellation ? "Nota de ajuste del documento soporte" : fiscalDocument ? "Documento soporte electrónico" : "Documento interno, sin envío a DIAN"}</p></div>
      {saved && (!saved.adjustment || saved.row.entryId) && permissions.includes("accounting.manual.create") && permissions.includes("accounting.read") &&
        <CloneVoucherButton documentId={saved.documentId} disabled={busy || dirty} onCloned={onCloned} />}
      {postedEntryButton}
      {saved && !saved.row.entryId && <Button variant="secondary" disabled={dirty || busy} onClick={() => setReportOpen(true)}><FileText className="mr-2 h-4 w-4" />Imprimir / exportar</Button>}
    </header>}
    {modal && saved && <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-muted/20 p-3"><p role="status" className="text-sm font-medium">{statusLabel}</p>{postedEntryButton}</div>}
    {error && <div role="alert" className="rounded-xl border border-destructive bg-destructive/10 p-4 text-sm">{error}</div>}
    {form.payloadVersion === 4 && voucherTypes.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los tipos de comprobante. <Button variant="link" onClick={() => void voucherTypes.refetch()}>Reintentar tipos de comprobante</Button></p>}
    {form.adjustment && directions.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las direcciones del ajuste. <Button variant="link" onClick={() => void directions.refetch()}>Reintentar direcciones</Button></p>}
    {centers.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los centros de costo. <Button variant="link" onClick={() => void centers.refetch()}>Reintentar centros de costo</Button></p>}
    {saved?.row.errorMessage && <div role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4">{saved.row.errorMessage}</div>}
    {form.clonedFromDocumentId && <p className="rounded-xl border bg-muted/30 p-4 text-sm">Copia nueva. Revisa la fecha, los valores y la referencia de la nueva operación antes de contabilizar.</p>}
    {lines.some(line => line.validationError) && <p role="alert" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm">Revisa las partidas marcadas: algunas cuentas o dimensiones dejaron de estar disponibles.</p>}
    {form.adjustment && <section className="space-y-4 rounded-2xl border bg-card p-5" aria-label="Obligación a ajustar">
      <h2 className="font-semibold">{adjustmentKind === "Payable" ? "Cuenta por pagar" : "Cuenta por cobrar"}</h2>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2"><Label>{adjustmentKind === "Payable" ? "Proveedor" : "Cliente"}</Label><PartyRoleSelect role={adjustmentKind === "Payable" ? "Supplier" : "Customer"} value={partyId} selectedOption={obligationPartyId === partyId && obligationPartyName ? { value: partyId, label: obligationPartyName, description: obligationIdentification || undefined } : null} onChange={id => { setChosenPartyId(id); change({ adjustment: { ...form.adjustment!, subledgerId: "" } }); }} disabled={busy || readOnly} placeholder={adjustmentKind === "Payable" ? "Buscar proveedor" : "Buscar cliente"} /></div>
        <div className="space-y-2"><Label>Factura u obligación</Label><PagedEntitySelect<AccountingOpenPosition> queryKey={["voucher-adjustment-options", businessId, adjustmentKind, partyId]} value={obligationId} selectedOption={selectedObligation || null} disabled={busy || readOnly || !partyId} onChange={(id, _option, item) => {
          if (item) queryClient.setQueryData(["voucher-adjustment-obligation", businessId, adjustmentKind, id], item);
          change({ adjustment: { ...form.adjustment!, subledgerId: id } });
        }} onClear={obligationId ? () => change({ adjustment: { ...form.adjustment!, subledgerId: "" } }) : undefined} loadPage={async (search, page, pageSize) => {
          const result = await accountingApi.openPositions({ subledgerKind: adjustmentKind!, counterpartyRoleId: partyId, search, page, pageSize });
          return { ...result, totalPages: Math.ceil(result.totalCount / result.pageSize) };
        }} getOption={item => ({ value: item.subledgerId, label: item.documentNumber,
          description: `${item.accountCode ?? "Sin cuenta de apertura"} · ${item.partyName} · Disponible ${money.format(item.availableAmount)}` })} ariaLabel="Seleccionar factura para ajuste de cartera" searchPlaceholder="Buscar factura o documento…" emptyMessage="No hay obligaciones para este tercero." /></div>
      </div>
      {obligation.isError && <p role="alert" className="text-sm text-destructive">No se pudo cargar la obligación seleccionada. <Button variant="link" onClick={() => void obligation.refetch()}>Reintentar</Button></p>}
      {obligationId && obligation.data === null && !obligation.isPending && <p role="alert" className="text-sm text-destructive">El documento no está disponible para ajustes contables en COP en esta sede.</p>}
      {obligation.data && <div className="grid gap-3 rounded-xl bg-muted/50 p-4 text-sm sm:grid-cols-3">
        <div><span className="block text-muted-foreground">Tercero</span><strong>{[obligationPartyName, obligationIdentification].filter(Boolean).join(" · ")}</strong></div>
        <div><span className="block text-muted-foreground">Documento</span><strong>{obligation.data.documentNumber}</strong></div>
        <div><span className="block text-muted-foreground">Valor original</span><strong>{money.format(obligation.data.originalAmount)}</strong></div>
        <div><span className="block text-muted-foreground">Saldo al consultar</span><strong>{money.format(obligation.data.outstandingAmount)}</strong></div>
        <div><span className="block text-muted-foreground">Disponible para disminuir</span><strong>{money.format(obligation.data.availableAmount)}</strong></div>
        <div><span className="block text-muted-foreground">Vencimiento</span><strong>{obligation.data.dueDate?.slice(0, 10) ?? "Sin vencimiento"}</strong></div>
        <div className="sm:col-span-3"><span className="block text-muted-foreground">Cuenta de origen</span>
          <strong>{obligation.data.accountCode ? `${obligation.data.accountCode} · ${obligation.data.accountName}` : obligation.data.openingLinkIssue}</strong>
          {obligation.data.accountCode && obligation.data.openingLinkIssue && <p role="alert" className="text-destructive">{obligation.data.openingLinkIssue}</p>}
          {obligation.data.openingDebit !== null && obligation.data.openingCredit !== null && <small className="block text-muted-foreground">Débito de apertura {money.format(obligation.data.openingDebit)} · Crédito de apertura {money.format(obligation.data.openingCredit)}</small>}
        </div>
      </div>}
      {!obligationId && <p className="text-sm text-muted-foreground">Selecciona un tercero y luego su factura para habilitar el ajuste.</p>}
    </section>}
    <fieldset disabled={busy || readOnly || !!form.adjustment && !obligation.data} className="min-w-0 space-y-5 disabled:opacity-80">
      <section className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-3">
        {!form.adjustment && form.payloadVersion === 4 && <div className="space-y-2"><Label>Tipo de comprobante</Label><Select value={form.voucherTypeCode ?? ""} disabled={busy || readOnly} onValueChange={voucherTypeCode => change({ voucherTypeCode })}>
          <SelectTrigger aria-label="Tipo de comprobante"><SelectValue /></SelectTrigger><SelectContent>{voucherTypes.data?.map(item => <SelectItem key={item.id} value={item.code}
            disabled={item.code !== "Accounting" && item.code !== "Disbursement" && item.code !== "SupportDocument" && item.code !== "SupportDocumentCancellation"}>{item.label}</SelectItem>)}</SelectContent></Select>
          <p className="text-xs text-muted-foreground">Los documentos soporte requieren su captura fiscal.</p></div>}
        {!form.adjustment && form.payloadVersion !== 4 && !readOnly && <div className="sm:col-span-3"><Button variant="outline" onClick={() => change({ payloadVersion: 4, voucherTypeCode: "Accounting" })}>Usar captura financiera</Button><p className="mt-1 text-xs text-muted-foreground">Valida la configuración financiera de los auxiliares antes de contabilizar.</p></div>}
        {!form.adjustment && <div className="space-y-2 sm:col-span-3"><Label>Tercero principal{form.payloadVersion === 4 ? " *" : ""}</Label><div className="flex items-start gap-2"><div className="min-w-0 flex-1"><PartySelect value={form.mainPartyId ?? ""} ariaLabel="Seleccionar tercero principal"
          selectedOption={form.mainPartyId && mainParty.name ? { value: form.mainPartyId, label: mainParty.name, description: mainParty.identification } : null}
          disabled={busy || readOnly} onChange={(id, party) => {
            change({ mainPartyId: id || null });
            setShowPendingDocuments(false);
            setMainParty({ name: party?.displayName ?? null, identification: party?.identification ?? null });
            setLines(current => current.map(line => !line.value.accountId && !line.value.partyId && !line.value.description && !line.value.debit && !line.value.credit
              ? { ...line, value: { ...line.value, partyId: id || null }, partyName: party?.displayName ?? null, partyIdentification: party?.identification ?? null } : line));
          }}/></div>{form.payloadVersion === 4 && !fiscalDocument && <Button type="button" variant="outline" disabled={!form.mainPartyId}
            onClick={() => setShowPendingDocuments(current => !current)}>{showPendingDocuments ? "Ocultar documentos" : "Ver documentos"}</Button>}</div>
          <p className="text-xs text-muted-foreground">{form.payloadVersion === 4 ? "Obligatorio para contabilizar. " : ""}Se propone en las nuevas partidas. Cada partida conserva su propio tercero.</p></div>}
        {!form.adjustment && form.payloadVersion === 4 && !fiscalDocument && form.mainPartyId && showPendingDocuments && !readOnly &&
          <div id="voucher-pending-documents" className="sm:col-span-3"><VoucherPendingDocuments key={form.mainPartyId}
            businessId={businessId} documentId={form.documentId} occurredAt={form.occurredAt}
            voucherTypeCode={form.voucherTypeCode} partyId={form.mainPartyId}
            remainingLineCapacity={500 - lines.length} linkedPositions={linkedPositions} onAdd={addPositionLines} /></div>}
        <div className="space-y-2"><Label>Fecha contable</Label><DatePicker value={form.occurredAt.slice(0, 10)} onChange={date => change({ occurredAt: date ? `${date}T12:00:00-05:00` : "" })} /></div>
        <div className="space-y-2 sm:col-span-2"><Label htmlFor="voucher-description">Descripción</Label><Input id="voucher-description" maxLength={500} value={form.description} onChange={event => change({ description: event.target.value })} placeholder="Motivo del comprobante" /></div>
        {!form.adjustment && form.payloadVersion === 4 && form.voucherTypeCode === "SupportDocument" && <div className="flex flex-wrap items-center gap-3 sm:col-span-3">
          <Button variant="outline" disabled={busy || readOnly || !form.mainPartyId} onClick={() => setFiscalOpen(true)}>Datos fiscales del soporte</Button>
          <p role="status" className="text-xs text-muted-foreground">{form.fiscalCapture?.calculationHash ? "Cálculo fiscal revisado" : "Completa las partidas y revisa el cálculo fiscal antes de contabilizar."}</p>
        </div>}
        {!form.adjustment && form.payloadVersion === 4 && supportCancellation && <div className="flex flex-wrap items-center gap-3 sm:col-span-3">
          <Button variant="outline" disabled={busy || readOnly} onClick={() => setPositionOpen(true)}>Seleccionar soporte original</Button>
          <Button variant="outline" disabled={busy || readOnly || !form.fiscalCapture} onClick={() => void reviewCancellation()}>{fiscalBusy ? "Revisando anulación…" : "Revisar anulación"}</Button>
          <p role="status" className="text-xs text-muted-foreground">{form.fiscalCapture?.calculationHash ? "Anulación revisada. Guarda el comprobante antes de contabilizar." : "Selecciona el soporte y revisa sus dependencias antes de contabilizar."}</p>
        </div>}
      </section>
      {form.adjustment ? <section className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-3">
        <div className="space-y-2"><Label>Dirección del ajuste</Label><Select value={form.adjustment.direction} disabled={readOnly || busy} onValueChange={direction => change({ adjustment: { ...form.adjustment!, direction: direction as "Increase" | "Decrease" } })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{directions.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label>Valor del ajuste</Label><FormattedNumberInput kind="currency" ariaLabel="Valor del ajuste" value={form.adjustment.amount} onValueChange={amount => change({ adjustment: { ...form.adjustment!, amount: amount ?? 0 } })} /></div>
        <div className="space-y-2"><Label>Cuenta de contrapartida</Label><AccountSelect value={form.adjustment.counterpartAccountId ?? ""} onChange={counterpartAccountId => change({ adjustment: { ...form.adjustment!, counterpartAccountId } })} /></div>
        <p className="text-sm text-muted-foreground sm:col-span-3">La obligación elegida se conserva vinculada. Guardar no cambia su saldo; contabilizar valida el saldo disponible.</p>
      </section> : <fieldset disabled={supportCancellation} className="min-w-0 overflow-hidden rounded-2xl border bg-card">
        <div className="flex items-center justify-between gap-3 border-b p-4"><div><h2 className="font-semibold">Partidas del comprobante</h2><p className="text-sm text-muted-foreground">{supportCancellation ? "Partidas inversas del asiento original. Para cambiar el origen, selecciona otro soporte." : "Edita cada partida en su fila. D = débito, C = crédito. Puedes guardar un comprobante incompleto."}</p></div><Button variant="outline" disabled={lines.length >= 500} onClick={() => { setLines(current => { const line = newLine(); return [...current, {...line,value:{...line.value,partyId:form.mainPartyId??null},partyName:mainParty.name,partyIdentification:mainParty.identification}]; }); markChanged(); }}><Plus className="mr-2 h-4 w-4" />Agregar partida</Button></div>
        <div className="max-h-[32rem] overflow-auto" onScroll={event => setScrollTop(event.currentTarget.scrollTop)}><table className="w-full min-w-[1200px] table-fixed text-sm"><colgroup><col className="w-9" /><col className="w-[220px]" /><col className="w-[170px]" /><col className="w-[140px]" /><col className="w-[205px]" /><col className="w-[120px]" /><col className="w-[110px]" /><col className="w-[110px]" /><col className="w-[160px]" /></colgroup><thead className="sticky top-0 z-10 bg-muted"><tr><th className="p-2 text-left">D/C</th><th className="p-2 text-left">Cuenta PUC</th><th className="p-2 text-left">Tercero</th><th className="p-2 text-left">Centro de costo</th><th className="p-2 text-left">Descripción u observación</th><th className="p-2 text-right">Base (opcional)</th><th className="p-2 text-right">Débito</th><th className="p-2 text-right">Crédito</th><th className="p-2 text-right">Acciones</th></tr></thead><tbody>
          {start > 0 && <tr aria-hidden><td colSpan={9} style={{ height: start * rowHeight }} /></tr>}
          {lines.slice(start, end).map((item, offset) => {
            const index = start + offset;
            const kind = item.value.debit > 0 && item.value.credit > 0 ? "D/C" : item.value.debit > 0 ? "D" : item.value.credit > 0 ? "C" : "—";
            return <tr key={item.value.lineId ?? index} aria-label={`Partida ${index + 1}`} title={item.validationError ?? undefined} className={`h-[56px] border-t align-middle hover:bg-muted/30 ${item.validationError ? "bg-amber-100" : ""}`}>
              <td className="px-2 text-center"><span title={kind === "D" ? "Débito" : kind === "C" ? "Crédito" : undefined} className="inline-flex min-w-7 justify-center rounded-md bg-muted px-1 py-1 font-bold">{kind}</span></td>
              <td className="p-1"><AccountSelect value={item.value.accountId ?? ""} resolveMissing={false} disabled={!!item.value.existingPosition} accounts={item.value.accountId && item.accountCode ? [{ accountId: item.value.accountId, code: item.accountCode, name: item.accountName ?? "" }] : []} onChange={(accountId, account) => updateLine(index, { value: { ...item.value, accountId }, accountCode: account?.code ?? null, accountName: account?.name ?? null })} /></td>
              <td className="p-1"><PartySelect value={item.value.partyId ?? ""} disabled={!!item.value.existingPosition} selectedOption={item.value.partyId && item.partyName ? { value: item.value.partyId, label: item.partyName, description: item.partyIdentification ?? undefined } : null} onChange={(partyId, party) => updateLine(index, { value: { ...item.value, partyId: partyId || null }, partyName: party?.displayName ?? null, partyIdentification: party?.identification ?? null })} /></td>
              <td className="p-1"><Select value={item.value.costCenterId ?? "auto"} disabled={busy || readOnly} onValueChange={value => updateValue(index, { costCenterId: value === "auto" ? null : value })}><SelectTrigger aria-label={`Centro de costo de la partida ${index + 1}`}><SelectValue /></SelectTrigger><SelectContent><SelectItem value="auto">Automático</SelectItem>{centers.data?.filter(center => center.isActive).map(center => <SelectItem key={center.costCenterId} value={center.costCenterId}>{center.code} · {center.name}</SelectItem>)}</SelectContent></Select></td>
              <td className="p-1"><Input required maxLength={500} aria-label="Descripción de la partida" placeholder="Descripción u observación" value={item.value.description} onChange={event => updateValue(index, { description: event.target.value })} className="h-9" /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" allowNegative ariaLabel="Base de la partida" placeholder="Sin registrar" className="text-right tabular-nums" value={item.value.baseAmount ?? ""} onValueChange={baseAmount => updateValue(index, { baseAmount })} /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Débito de la partida" className="text-right tabular-nums" value={item.value.debit} onValueChange={debit => updateValue(index, { debit: debit ?? 0 })} /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Crédito de la partida" className="text-right tabular-nums" value={item.value.credit} onValueChange={credit => updateValue(index, { credit: credit ?? 0 })} /></td>
              <td className="p-1 text-right">{form.payloadVersion === 4 && <Button variant="ghost" size="icon" title="Detalles de la partida" aria-label={`Detalles de la partida ${index + 1}`} onClick={() => setDetailLineId(item.value.lineId!)}><FileText className="h-4 w-4" /></Button>}<Button variant="ghost" size="icon" title="Intercambiar débito y crédito" aria-label={`Intercambiar débito y crédito de la partida ${index + 1}`} onClick={() => updateValue(index, {debit:item.value.credit,credit:item.value.debit})}><ArrowLeftRight className="h-4 w-4" /></Button><Button variant="ghost" size="icon" title="Duplicar partida" aria-label={`Duplicar partida ${index + 1}`} disabled={lines.length >= 500} onClick={() => { setLines(current => [...current.slice(0, index + 1), { ...item, value: { ...item.value, lineId: crypto.randomUUID() } }, ...current.slice(index + 1)]); markChanged(); }}><Copy className="h-4 w-4" /></Button><Button variant="ghost" size="icon" title="Eliminar partida" aria-label={`Eliminar partida ${index + 1}`} onClick={() => { setLines(current => current.filter((_, position) => position !== index)); markChanged(new Set(lines.filter((_, position) => position !== index).map(line => line.value.lineId!))); }}><Trash2 className="h-4 w-4" /></Button></td>
            </tr>;
          })}
          {end < lines.length && <tr aria-hidden><td colSpan={9} style={{ height: (lines.length - end) * rowHeight }} /></tr>}
        </tbody></table></div>
      </fieldset>}
    </fieldset>
    <footer className="sticky bottom-0 z-20 flex flex-wrap items-center gap-4 rounded-2xl border bg-background p-4 shadow-lg">
      <div className="flex-1 text-sm">{!form.adjustment && <><b>Débito {money.format(debit)}</b> · Crédito {money.format(credit)}<p className={Math.abs(debit - credit) > 0.00001 ? "text-destructive" : "text-muted-foreground"}>Diferencia {money.format(debit - credit)} · {lines.length} partidas</p></>}<p className="text-muted-foreground">{saved?.sentAt ? "Documento enviado: contenido inmutable." : dirty ? "Cambios sin guardar. Guardar no contabiliza." : "Guardado. Se contabilizará únicamente cuando lo solicites."}</p></div>
      <Button variant="outline" disabled={busy} onClick={close}>{modal ? closeLabel : "Volver"}</Button>
      {saved && !dirty && <Button variant="outline" disabled={busy} onClick={() => void refresh()}>{phase === "refresh" ? "Consultando estado…" : "Actualizar estado"}</Button>}
      {saved?.sentAt && saved.status !== "Posted" && permissions.includes("accounting.postings.retry") && <Button disabled={busy} onClick={() => void retry()}>{phase === "retry" ? "Solicitando reintento…" : "Reintentar contabilización"}</Button>}
      {!readOnly && <Button variant="outline" disabled={busy || !dirty} onClick={() => void save()}><Save className="mr-2 h-4 w-4" />{phase === "save" ? "Guardando…" : "Guardar comprobante"}</Button>}
      {saved?.canSend && <Button disabled={busy || dirty || fiscalDocument && !form.fiscalCapture?.calculationHash} onClick={() => void send()}><Send className="mr-2 h-4 w-4" />{phase === "send" ? supportCancellation ? "Registrando anulación…" : fiscalDocument ? "Registrando soporte…" : "Enviando a contabilidad…" : "Contabilizar"}</Button>}
    </footer>
    {entryOpen && <AccountingDocumentDialog documentId={saved?.documentId} hasFiscalDocument={!!saved?.row.fiscalDocumentType} onClose={() => setEntryOpen(false)} />}
    {fiscalOpen && <VoucherFiscalDialog businessId={businessId} draft={form} lines={lines} onBusyChange={setFiscalBusy}
      onApply={fiscalCapture => { change({ fiscalCapture }); setFiscalOpen(false); }} onClose={() => setFiscalOpen(false)} />}
    {positionOpen && <VoucherPositionDialog businessId={businessId} documentId={form.documentId} occurredAt={form.occurredAt}
      mainPartyId={form.mainPartyId} mainPartyName={mainParty.name} mainPartyIdentification={mainParty.identification}
      onPrepared={prepared => {
        setLines(identifyLines(prepared.lines));
        setMainParty({ name: prepared.mainPartyName, identification: prepared.mainPartyIdentification ?? null });
        change({ description: prepared.description, reference: prepared.reference, mainPartyId: prepared.mainPartyId,
          workSessionId: null, fiscalCapture: { acquisitions: [] } });
      }} onClose={() => setPositionOpen(false)} />}
    {detailLine && <Dialog open onOpenChange={open => { if (!open) setDetailLineId(null); }}><DialogContent className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-2xl">
      <DialogHeader><DialogTitle>Detalles de la partida {detailIndex + 1}</DialogTitle><DialogDescription>{detailLine.accountCode} · {detailLine.accountName}. La cuenta y sus vínculos determinan el efecto; el motor lo valida al contabilizar.</DialogDescription></DialogHeader>
      {franchises.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las franquicias. <Button variant="link" onClick={() => void franchises.refetch()}>Reintentar franquicias</Button></p>}
      <fieldset disabled={busy || readOnly} className="grid gap-4 sm:grid-cols-2">
        {form.voucherTypeCode === "SupportDocument" && detailLine.value.partyId && <div className="space-y-2 sm:col-span-2"><Label>Sede del proveedor de la partida</Label>
          <SupplierSiteSelect value={detailLine.value.partySiteId ?? ""} partyId={detailLine.value.partyId} disabled={busy || readOnly}
            onChange={partySiteId => updateValue(detailIndex, { partySiteId: partySiteId || null })} />
          <p className="text-xs text-muted-foreground">Sin selección explícita se usa la sede predeterminada. La sede de la obligación se conserva en el soporte fiscal.</p>
        </div>}
        <div className="space-y-2"><Label htmlFor="line-document-reference">Referencia del documento</Label><Input id="line-document-reference" maxLength={100} value={detailLine.value.reference ?? ""} onChange={event => updateValue(detailIndex, { reference: event.target.value || null })} /></div>
        <div className="space-y-2"><Label>Vencimiento de nueva obligación</Label><DatePicker value={detailLine.value.dueDate?.slice(0, 10) ?? ""} onChange={date => updateValue(detailIndex, { dueDate: date ? `${date}T12:00:00-05:00` : null })} /><p className="text-xs text-muted-foreground">Solo al abrir una cuenta por pagar o cobrar. Un anticipo no lleva vencimiento.</p></div>
        <div className="space-y-2 sm:col-span-2"><Label>Grupo de partidas vinculadas</Label><Select value={detailLine.value.financialGroupId ?? "auto"} disabled={busy || readOnly} onValueChange={value => updateValue(detailIndex, { financialGroupId: value === "auto" ? null : value === "new" ? crypto.randomUUID() : value })}>
          <SelectTrigger aria-label="Grupo de la partida"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="auto">Automático para un solo tercero</SelectItem>{financialGroups.map((id, index) => <SelectItem key={id} value={id}>Grupo {index + 1}</SelectItem>)}<SelectItem value="new">Crear otro grupo</SelectItem></SelectContent></Select><p className="text-xs text-muted-foreground">Para varios pagos o cruces, asigna el mismo grupo a sus obligaciones y contrapartidas.</p></div>
        {detailLine.value.existingPosition && <div className="rounded-xl border p-3 text-sm sm:col-span-2"><p>Documento vinculado: {detailLine.value.reference ?? detailLine.value.existingPosition.subledgerId}</p><Button variant="link" onClick={() => updateValue(detailIndex, { existingPosition: null })}>Quitar vínculo con el documento</Button></div>}
        <div className="space-y-2"><Label htmlFor="line-payment-approval">Autorización del pago</Label><Input id="line-payment-approval" maxLength={100} value={detailLine.value.paymentEvidence?.approvalNumber ?? ""} onChange={event => updateEvidence({ approvalNumber: event.target.value || null })} /></div>
        <div className="space-y-2"><Label>Franquicia de tarjeta</Label><Select value={detailLine.value.paymentEvidence?.cardFranchiseCode ?? "none"} disabled={busy || readOnly} onValueChange={value => updateEvidence({ cardFranchiseCode: value === "none" ? null : value })}><SelectTrigger aria-label="Franquicia del pago"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="none">Sin tarjeta</SelectItem>{franchises.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label>Efectivo entregado</Label><FormattedNumberInput kind="currency" ariaLabel="Efectivo entregado en la partida" value={detailLine.value.paymentEvidence?.tenderedAmount ?? ""} onValueChange={tenderedAmount => updateEvidence({ tenderedAmount })} /></div>
        <div className="space-y-2"><Label htmlFor="line-payment-notes">Observación del pago</Label><Input id="line-payment-notes" maxLength={500} value={detailLine.value.paymentEvidence?.notes ?? ""} onChange={event => updateEvidence({ notes: event.target.value || null })} /></div>
        <p className="text-xs text-muted-foreground sm:col-span-2">La evidencia de pago corresponde únicamente a la partida de caja, banco o tarjeta. El banco y el medio se obtienen de la configuración del auxiliar.</p>
      </fieldset>
      <DialogFooter><Button onClick={() => setDetailLineId(null)}>Cerrar detalles</Button></DialogFooter>
    </DialogContent></Dialog>}
    {confirmUnsavedChanges && <Dialog open={discardOpen} onOpenChange={setDiscardOpen}>
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
