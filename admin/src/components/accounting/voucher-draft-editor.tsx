"use client";

import { CostCenterSelect, useCostCenterLabels } from "@/components/accounting/cost-center-select";
import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ArrowLeftRight, Copy, FileText, Plus, RotateCcw, Save, Send, Trash2 } from "lucide-react";
import { accountingApi, type AccountingOpenPosition, type SaveVoucherDraft, type VoucherDraft, type VoucherDraftAdjustment, type VoucherDraftLineView, type VoucherDraftPreparation } from "@/services/api/accounting";
import { fiscalDocumentsApi } from "@/services/api/fiscal-documents";
import { type PaymentMethod } from "@/services/api/payment-methods";
import { PaymentMethodSelect } from "@/components/payments/payment-method-select";
import { VoucherPaymentForms, type VoucherPaymentCapture } from "./voucher-payment-forms";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { AccountSelect } from "./account-select";
import { AccountingDocumentDialog } from "./accounting-document-dialog";
import { CloneVoucherButton } from "./clone-voucher-button";
import { VoucherPositionDialog } from "./voucher-position-dialog";
import { VoucherPendingDocuments } from "./voucher-pending-documents";
import { createVoucherPositionLine } from "@/lib/voucher-payment-preparation";
import { VoucherFiscalDialog } from "./voucher-fiscal-dialog";
import { accountingDateAtNoon, proposedAccountingDate } from "@/lib/accounting-document-date";
import { accountingStatusLabel, accountingReversalStatusLabel } from "@/lib/accounting-labels";
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
const bindDocumentLines=(lines:VoucherDraftLineView[],documentId:string)=>lines.map(line=>line.value.financialGroupId?line:
  {...line,value:{...line.value,financialGroupId:documentId}});
// Generated identity/group alone do not represent captured data. Every other
// populated field must survive assisted additions, even in an incomplete row.
const hasLineCapture=(line:VoucherDraftLineView)=>Object.entries(line.value).some(([key,value])=>
  key!=="lineId"&&key!=="financialGroupId"&&value!==null&&value!==undefined&&value!==""&&value!==0);
const editablePayloadVersion = (draft: VoucherDraft) => draft.documentType === "ManualAccountingVoucher"
  ? draft.payloadVersion === 4 ? 4 : 2 : null;

export function VoucherDraftEditor({ businessId, initial, adjustment, preparation, onSaved, onDiscarded, onCloned, onClose, onBusyChange, modal = false, closeLabel = "Volver a trazabilidad", voucherTypeScope }: {
  businessId: string; initial?: VoucherDraft; adjustment?: VoucherDraftAdjustment; preparation?: VoucherDraftPreparation;
  onSaved?: (value: VoucherDraft) => void; onDiscarded?: (documentId: string) => void;
  onClose: () => void; onBusyChange?: (busy: boolean) => void;
  onCloned?: (value: VoucherDraft) => void;
  modal?: boolean; closeLabel?: string;
  voucherTypeScope?: "CashReceipt" | "Disbursement";
}) {
  const permissions = useAuthStore(state => state.user?.permissions ?? noPermissions);
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const timeZone = useBusinessContextStore(state => state.businesses.find(business => business.businessId === businessId)?.timeZone);
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(initial);
  const [form, setForm] = useState<SaveVoucherDraft>(() => initial ? { ...initial,
    workSessionId: initial.sentAt ? initial.workSessionId : null,
    payloadVersion: editablePayloadVersion(initial), lines: initial.lines.map(line => line.value) } : {
    documentId: crypto.randomUUID(), documentType: adjustment ? "AccountAdjustment" : "ManualAccountingVoucher",
    occurredAt: proposedAccountingDate(timeZone),
    conceptCode: adjustment ? "ACCOUNT_ADJUSTMENT" : "MANUAL_VOUCHER", description: preparation?.description ?? "", reference: preparation?.reference ?? null,
    mainPartyId: preparation?.mainPartyId ?? null, voucherTypeCode: adjustment ? null : voucherTypeScope ?? "Accounting",
    lines: [], adjustment: adjustment ?? null, rowVersion: null, payloadVersion: adjustment ? null : 4,
    workSessionId: null,
  });
  const [lines, setLines] = useState<VoucherDraftLineView[]>(() => initial ? identifyLines(initial.lines) : preparation ? bindDocumentLines(identifyLines(preparation.lines), form.documentId) : (adjustment ? [] : [newLine(), newLine()]));
  const [scrollTop, setScrollTop] = useState(0);
  const [dirty, setDirty] = useState(!initial || !initial.sentAt && initial.documentType === "ManualAccountingVoucher" &&
    (initial.payloadVersion !== editablePayloadVersion(initial) || !!initial.workSessionId));
  const [phase, setPhase] = useState<"save" | "send" | "fiscal" | "refresh" | "retry" | "discard" | "cancel" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [entryOpen, setEntryOpen] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [reversalOpen, setReversalOpen] = useState(false);
  const cancellationId = useRef(crypto.randomUUID());
  const confirmUnsavedChanges = !modal || Boolean(preparation);
  const [positionOpen, setPositionOpen] = useState(false);
  const [showPendingDocuments, setShowPendingDocuments] = useState(false);
  const [fiscalOpen, setFiscalOpen] = useState(false);
  const [fiscalBusy, setFiscalBusy] = useState(false);
  const [detailLineId, setDetailLineId] = useState<string | null>(null);
  const [chosenPartyId, setChosenPartyId] = useState("");
  const [mainParty, setMainParty] = useState({ name: initial?.mainPartyName ?? preparation?.mainPartyName ?? null, identification: initial?.mainPartyIdentification ?? preparation?.mainPartyIdentification ?? null });
  const voucherTypes = useReferenceOptions("accounting-voucher-type", form.payloadVersion === 4);
  const cashReceipt = form.payloadVersion === 4 && form.voucherTypeCode === "CashReceipt";
  const inlinePaymentVoucher = form.payloadVersion === 4 && (cashReceipt || form.voucherTypeCode === "Disbursement");
  const receiptInvoiceIds = new Set(lines.filter(line => line.value.existingPosition?.subledgerKind === "Receivable" &&
    !line.value.existingPosition.reversesTransactionId && line.value.credit > 0).map(line => line.value.existingPosition!.subledgerId));
  const paymentLines = lines.flatMap((line, index) => line.value.paymentEvidence && !line.value.existingPosition && !line.value.financialConceptId ? [{ line, index }] : []);
  const receiptAttributionConflict = cashReceipt && receiptInvoiceIds.size > 1 && paymentLines.length > 1;
  const paymentAddBlockReason = cashReceipt && receiptInvoiceIds.size > 1 && paymentLines.length > 0
    ? "Este recibo aplica a varias facturas y admite una sola forma de pago. Puedes cambiar o quitar la existente." : undefined;
  const franchises = useReferenceOptions("card-franchise", !inlinePaymentVoucher && detailLineId !== null);
  const directions = useReferenceOptions("accounting-adjustment-direction", !!form.adjustment);
  const statuses = useReferenceOptions("accounting-document-status");
  const centers = useCostCenterLabels(lines.map(line => line.value.costCenterId));
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
  const postingQueued = !!saved?.row.accountingRetryQueued;
  const statusLabel = accountingReversalStatusLabel(saved?.reversalDocumentId,saved?.reversalStatus)
    ?? (postingQueued ? "Reintento en cola" : saved ? statuses.data?.find(item => item.code === saved.status)?.label ?? accountingStatusLabel(saved.status) : "Nueva captura");
  const postedEntryButton = saved?.row.entryId && <Button variant="secondary" onClick={() => setEntryOpen(true)}><FileText className="mr-2 h-4 w-4" />Ver comprobante contabilizado</Button>;
  const busy = phase !== null || fiscalBusy;
  const detailIndex = lines.findIndex(line => line.value.lineId === detailLineId);
  const detailLine = detailIndex < 0 ? null : lines[detailIndex];
  const paymentOutgoing = form.voucherTypeCode === "Disbursement" || form.voucherTypeCode !== "CashReceipt" && (detailLine?.value.credit ?? 0) > 0;
  const paymentUsage = form.voucherTypeCode === "CashReceipt" ? "Receipts" : form.voucherTypeCode === "Disbursement" ? "SupplierPayments"
    : paymentOutgoing ? "VoucherPayments" : "VoucherReceipts";
  const appliedPayments = new Map(saved?.appliedPaymentMethods?.map(item => [item.sourceLineId, item]));
  // These options are the captured document lines, not another portfolio fetch.
  const adjustmentTargets = lines.filter(line => line.value.lineId && line.value.partyId === form.mainPartyId &&
    line.value.existingPosition && !line.value.existingPosition.reversesTransactionId &&
    (line.value.existingPosition.subledgerKind === "Receivable" && line.value.credit > 0 && line.value.debit === 0 ||
     line.value.existingPosition.subledgerKind === "Payable" && line.value.debit > 0 && line.value.credit === 0));
  const compatibleTargets = (side:string|null|undefined) => adjustmentTargets.filter(line =>
    line.value.existingPosition!.subledgerKind === (side === "Debit" ? "Receivable" : "Payable"));
  const targetLabel = (line:VoucherDraftLineView) => `${line.value.reference || line.value.description || line.value.existingPosition!.subledgerId} · ${money.format(line.value.debit + line.value.credit)}`;
  const groupedLines = new Map<string, VoucherDraftLineView[]>();
  for (const line of lines) if (line.value.financialGroupId) {
    const captured = groupedLines.get(line.value.financialGroupId);
    if (captured) captured.push(line); else groupedLines.set(line.value.financialGroupId, [line]);
  }
  // A selected balance alone is editable. Only the explicit debt/credit pair
  // forms a crossing; sharing the document ID does not establish that effect.
  const creditGroups = new Set([...groupedLines].filter(([, captured]) =>
    captured.every(line => line.value.existingPosition && !line.value.existingPosition.reversesTransactionId) &&
    captured.some(line => line.value.existingPosition?.subledgerKind === "CustomerCredit" || line.value.existingPosition?.subledgerKind === "SupplierCredit") &&
    captured.some(line => line.value.existingPosition?.subledgerKind === "Receivable" || line.value.existingPosition?.subledgerKind === "Payable"))
    .map(([reference]) => reference));
  const linkedPositions = lines.flatMap(line => line.value.existingPosition ? [{ ...line.value.existingPosition,
    amount: line.value.debit + line.value.credit, financialDiscountEarned: line.value.financialDiscountEarned,
    creditCrossing: !!line.value.financialGroupId && creditGroups.has(line.value.financialGroupId) }] : []);
  const capturedReductions = new Map<string,number>();
  for(const line of lines) if(line.value.financialConceptTargetLineId) capturedReductions.set(line.value.financialConceptTargetLineId,
    (capturedReductions.get(line.value.financialConceptTargetLineId)??0)+line.value.debit+line.value.credit);
  const crossTargets = lines.filter(line => line.value.lineId && line.value.accountId && line.value.partyId === form.mainPartyId &&
    !line.value.existingPosition?.reversesTransactionId && line.value.financialDiscountEarned == null &&
    (!line.value.financialGroupId || !creditGroups.has(line.value.financialGroupId)) &&
    (line.value.existingPosition?.subledgerKind === "Receivable" && line.value.credit > 0 && line.value.debit === 0 ||
      line.value.existingPosition?.subledgerKind === "Payable" && line.value.debit > 0 && line.value.credit === 0))
    .map(line=>({...line,availableForCreditAmount:Math.max(0,Number((line.value.debit+line.value.credit-(capturedReductions.get(line.value.lineId!)??0)).toFixed(4)))}))
    .filter(line=>line.availableForCreditAmount>0);
  const removableCrossings = lines.filter(line => line.value.financialGroupId &&
    creditGroups.has(line.value.financialGroupId) &&
    (line.value.existingPosition?.subledgerKind === "CustomerCredit" || line.value.existingPosition?.subledgerKind === "SupplierCredit") &&
    groupedLines.get(line.value.financialGroupId)!.every(item =>
      item.value.existingPosition && !item.value.existingPosition.reversesTransactionId));
  const supportCancellation = form.voucherTypeCode === "SupportDocumentCancellation";
  const fiscalDocument = form.voucherTypeCode === "SupportDocument" || supportCancellation;
  const effects = saved?.financialEffects;
  const canCancelPlainJournal = !!saved && saved.status === "Posted" && !saved.reversalDocumentId &&
    saved.voucherTypeCode === "Accounting" && saved.payloadVersion === 4 && !saved.fiscalCapture &&
    !!effects && effects.positions.length === 0 && effects.payments.length === 0 &&
    effects.creditApplications.length === 0 && effects.directMovements.length === 0 &&
    (effects.unapplications?.length ?? 0) === 0 &&
    permissions.includes("accounting.manual.create") && permissions.includes("accounting.manual.send");
  useEffect(() => { onBusyChange?.(busy); }, [busy, onBusyChange]);
  const debit = lines.reduce((sum, line) => sum + line.value.debit, 0);
  const credit = lines.reduce((sum, line) => sum + line.value.credit, 0);
  const difference = debit-credit;
  const differencePending = Math.abs(difference)>0.00001;
  const rowHeight = 80;
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
  function applySnapshot(value: VoucherDraft) {
    // The selection page was read before this operation. Once accepted, show
    // the document's applied effects rather than stale available balances.
    if (value.sentAt) setShowPendingDocuments(false);
    setSaved(value); setForm({ ...value, workSessionId: value.sentAt ? value.workSessionId : null,
      payloadVersion: editablePayloadVersion(value),
      lines: value.lines.map(line => line.value) }); setLines(identifyLines(value.lines));
    setMainParty({ name: value.mainPartyName ?? null, identification: value.mainPartyIdentification ?? null });
    setDirty(!value.sentAt && value.documentType === "ManualAccountingVoucher" &&
      (value.payloadVersion !== editablePayloadVersion(value) || !!value.workSessionId));
  }
  function accept(value: VoucherDraft) {
    applySnapshot(value);
    onSaved?.(value);
  }
  async function persistDraft(): Promise<VoucherDraft | null> {
    if (busy) return null;
    if (!form.description.trim() || !form.occurredAt || !form.conceptCode) { setError("Completa fecha y descripción."); return null; }
    if (form.adjustment && (!form.adjustment.subledgerId || !obligation.data)) { setError("Selecciona la factura u obligación que vas a ajustar."); return null; }
    const missingDescription = lines.findIndex(line => !line.value.description.trim() || line.value.description.length > 500);
    if (missingDescription !== -1) { setError(`La descripción de la partida ${missingDescription + 1} es obligatoria y admite máximo 500 caracteres.`); return null; }
    setPhase("save"); setError(null);
    try {
      const result = await accountingApi.saveVoucherDraft({ ...form, workSessionId: null, lines: lines.map(line => line.value) });
      // The composed save/post action still owns this editor. Publishing this
      // intermediate result navigates to the saved URL and remounts it before
      // the posting response can apply its authoritative state.
      applySnapshot(result);
      return result;
    }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo guardar el comprobante."); return null; }
    finally { setPhase(null); }
  }
  async function save(): Promise<VoucherDraft | null> {
    const result = await persistDraft();
    if (result) onSaved?.(result);
    return result;
  }
  async function discard() {
    if (!saved || saved.sentAt || busy) return;
    setPhase("discard"); setError(null);
    try {
      await accountingApi.discardVoucherDraft(saved.documentId, saved.rowVersion);
      onDiscarded?.(saved.documentId);
      onClose();
    } catch (value) {
      setError(value instanceof Error ? value.message : "No se pudo descartar el borrador.");
    } finally { setPhase(null); setDeleteOpen(false); }
  }
  async function send(target?: VoucherDraft) {
    const current = target ?? saved;
    if (!current || (!target && dirty) || busy) return;
    if (current.payloadVersion === 4 && !current.mainPartyId) {
      setError("Selecciona el tercero principal antes de contabilizar el comprobante."); return;
    }
    setPhase("send"); setError(null);
    try { accept(await accountingApi.sendVoucherDraft(current.documentId, current.rowVersion)); }
    catch (value) { setError(value instanceof Error ? value.message : "No se pudo contabilizar. Consulta el mismo comprobante antes de reintentar."); }
    finally { setPhase(null); }
  }
  async function saveAndPost() {
    if (busy || saved?.sentAt) return;
    if (!form.adjustment && differencePending) {
      setError(cashReceipt && difference>0
        ? `Faltan ${money.format(difference)} por clasificar. Si recibiste ese excedente como anticipo, agrega una partida con la cuenta de anticipo correspondiente y su valor; también puedes corregir el importe recibido o aplicado. No se ha creado un anticipo automático.`
        : `El comprobante tiene una diferencia de ${money.format(Math.abs(difference))}. Revisa los importes o agrega la partida con su cuenta y valor antes de guardar el comprobante definitivo.`);
      return;
    }
    const current = dirty || !saved ? await persistDraft() : saved;
    if (!current) return;
    if (!current.canSend) {
      setError("El borrador quedó guardado. Completa las partidas y corrige los avisos antes de guardar el comprobante definitivo.");
      return;
    }
    await send(current);
  }
  async function sendFiscal() {
    if (!saved || busy || saved.status !== "Posted" ||
      saved.row.fiscalStatus !== "AwaitingManualSubmission") return;
    setPhase("fiscal"); setError(null);
    try {
      const fiscal = await fiscalDocumentsApi.sendManualSupport(saved.documentId);
      accept({ ...saved, row: { ...saved.row, fiscalStatus: fiscal.status } });
    } catch (value) {
      setError(value instanceof Error ? value.message : "No se pudo solicitar el envío a la DIAN.");
    } finally { setPhase(null); }
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
        errorCode: posting.errorCode, errorMessage: posting.errorMessage, entryId: posting.entryId,
        accountingRetryQueued: !!posting.retryQueued } });
    } catch (value) { setError(value instanceof Error ? value.message : "No se pudo reintentar la contabilización."); }
    finally { setPhase(null); }
  }
  async function cancelJournal() {
    if (!saved || busy || !canCancelPlainJournal) return;
    setPhase("cancel"); setError(null);
    try {
      const today = proposedAccountingDate(timeZone);
      const occurredAt = new Date(today) < new Date(saved.occurredAt) ? saved.occurredAt : today;
      const result = await accountingApi.cancelJournalVoucher(saved.documentId, cancellationId.current, occurredAt);
      const updated = { ...saved, reversalDocumentId: result.documentId, reversalStatus: result.status,
        row: { ...saved.row, reversalDocumentId: result.documentId, reversalStatus: result.status } };
      setSaved(updated); onSaved?.(updated); setCancelOpen(false);
    } catch (value) { setError(value instanceof Error ? value.message : "No se pudo anular el comprobante."); }
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

  function addPaymentForm() {
    if (busy || readOnly || !form.mainPartyId) return;
    const empty = lines.findIndex(item => !hasLineCapture(item));
    if (empty < 0 && lines.length >= 500) return;
    const line = empty < 0 ? newLine() : lines[empty];
    const prepared = { ...line, partyName: mainParty.name, partyIdentification: mainParty.identification,
      value: { ...line.value, partyId: form.mainPartyId, financialGroupId: form.documentId } };
    const groupId = form.documentId;
    setLines(previous => {
      const grouped = bindDocumentLines(previous,groupId);
      return empty < 0 ? [...grouped, prepared] : grouped.map((item, index) => index === empty ? prepared : item);
    });
    setDetailLineId(prepared.value.lineId!); markChanged();
  }

  function selectPaymentForm(method: PaymentMethod, requestedIndex = detailIndex, capture?: VoucherPaymentCapture) {
    if (busy || readOnly || inlinePaymentVoucher && !form.mainPartyId) return false;
    if (requestedIndex < 0 && paymentAddBlockReason) { setError(paymentAddBlockReason); return false; }
    const index = requestedIndex >= 0 ? requestedIndex : lines.findIndex(item => !hasLineCapture(item));
    if (index < 0 && lines.length >= 500) return false;
    if (lines.some((line, itemIndex) => itemIndex !== index && line.value.paymentEvidence?.paymentMethodId === method.paymentMethodId)) {
      setError("La forma ya está en las partidas. Cambia su importe o selecciona otra forma."); return false;
    }
    const line = index < 0 ? newLine() : lines[index];
    const sameType = line.value.paymentEvidence?.paymentMethodTypeCode === method.methodCode;
    const groupId = line.value.financialGroupId ?? form.documentId;
    const card = method.methodCode === "DebitCard" || method.methodCode === "CreditCard";
    const prepared = { ...line, accountCode: method.accountCode, accountName: method.accountName, financialConceptCode:null, financialConceptName:null,
      partyName: line.value.partyId ? line.partyName : mainParty.name,
      partyIdentification: line.value.partyId ? line.partyIdentification : mainParty.identification,
      value: { ...line.value, partyId: line.value.partyId ?? form.mainPartyId ?? null,
      financialGroupId: inlinePaymentVoucher ? groupId : line.value.financialGroupId,
      debit: capture ? form.voucherTypeCode === "Disbursement" ? 0 : capture.amount : line.value.debit,
      credit: capture ? form.voucherTypeCode === "Disbursement" ? capture.amount : 0 : line.value.credit,
      reference: inlinePaymentVoucher ? method.methodCode === "Transfer" ? capture?.reference ?? (sameType ? line.value.reference : null) : null : line.value.reference,
      accountId: method.accountId, description: line.value.description || method.name,financialConceptId:null,financialConceptTargetLineId:null,
      paymentEvidence: { ...line.value.paymentEvidence, paymentMethodId: method.paymentMethodId,
        paymentMethodTypeCode: method.methodCode, paymentMethodVersion: method.version,
        paymentMethodCode: method.code, paymentMethodName: method.name, tenderedAmount: null,
        cardFranchiseCode: inlinePaymentVoucher ? card ? capture?.cardFranchiseCode ?? (sameType ? line.value.paymentEvidence?.cardFranchiseCode : null) : null : sameType ? line.value.paymentEvidence?.cardFranchiseCode : null,
        approvalNumber: inlinePaymentVoucher ? card ? capture?.approvalNumber ?? (sameType ? line.value.paymentEvidence?.approvalNumber : null) : null : sameType ? line.value.paymentEvidence?.approvalNumber : null } } };
    setLines(previous => {
      const updated = index < 0 ? [...previous, prepared] : previous.map((item, itemIndex) => itemIndex === index ? prepared : item);
      return inlinePaymentVoucher ? bindDocumentLines(updated, groupId) : updated;
    });
    markChanged(); setError(null); return true;
  }

  function addPositionLines(added: VoucherDraftLineView[]) {
    if (cashReceipt && paymentLines.length > 1 && new Set([...receiptInvoiceIds,
      ...added.filter(line => line.value.existingPosition?.subledgerKind === "Receivable" && line.value.credit > 0)
        .map(line => line.value.existingPosition!.subledgerId)]).size > 1) {
      setError("Este recibo tiene varias formas de pago y admite una sola factura. Retira las formas adicionales para agregar otra factura."); return;
    }
    added = bindDocumentLines(added, form.documentId);
    setLines(previous => {
      const current = bindDocumentLines(previous,form.documentId);
      if (added.length > 1) return [...current.filter(hasLineCapture), ...added];
      const line = added[0];
      const existing = current.findIndex(item => item.value.existingPosition?.subledgerKind === line.value.existingPosition?.subledgerKind &&
        item.value.existingPosition?.subledgerId === line.value.existingPosition?.subledgerId &&
        (!item.value.financialGroupId || !creditGroups.has(item.value.financialGroupId)));
      if (existing >= 0) return current.map((item, index) => index === existing ? { ...item,
        accountCode: line.accountCode, accountName: line.accountName, partyName: line.partyName,
        partyIdentification: line.partyIdentification, value: { ...item.value, accountId: line.value.accountId,
          partyId: line.value.partyId, partySiteId: line.value.partySiteId, debit: line.value.debit,
          credit: line.value.credit, financialDiscountEarned: line.value.financialDiscountEarned } } : item);
      const empty = current.findIndex(item => !hasLineCapture(item));
      return empty < 0 ? [...current, line] : current.map((item, index) => index === empty ? line : item);
    });
    markChanged();
  }

  function crossCredit(position: AccountingOpenPosition, targetLineId: string, amount: number) {
    const target = crossTargets.find(line => line.value.lineId === targetLineId);
    const supplier = position.subledgerKind === "SupplierCredit";
    const used = linkedPositions.filter(item => item.subledgerKind === position.subledgerKind && item.subledgerId === position.subledgerId)
      .reduce((total, item) => total + item.amount, 0);
    const total = target ? target.value.debit + target.value.credit : 0;
    if (!target || position.partyId !== form.mainPartyId || !position.accountId || position.openingLinkIssue || position.currencyCode !== "COP" ||
      target.value.existingPosition?.subledgerKind !== (supplier ? "Payable" : "Receivable") ||
      supplier && target.value.partySiteId != null && target.value.partySiteId !== position.partySiteId ||
      amount <= 0 || amount > target.availableForCreditAmount || Number((amount + used).toFixed(4)) > position.availableAmount ||
      lines.length + (amount === total ? 1 : 2) > 500) {
      setError("No se agregó el cruce: revisa la factura seleccionada y el importe disponible del anticipo."); return;
    }
    const positionLineId = crypto.randomUUID();
    const remaining = Number((total - amount).toFixed(4));
    const updated = lines.flatMap(line => line.value.lineId === targetLineId
      ? remaining === 0 ? [] : [{ ...line, value: { ...line.value, debit: supplier ? remaining : 0, credit: supplier ? 0 : remaining } }]
      : line.value.accountId || line.value.description || line.value.debit || line.value.credit || line.value.existingPosition ? [line] : [])
      .map(line => line.value.financialGroupId ? line : { ...line, value: { ...line.value, financialGroupId: form.documentId } });
    updated.push({ ...target, value: { ...target.value, lineId: positionLineId, financialGroupId: positionLineId,
      debit: supplier ? amount : 0, credit: supplier ? 0 : amount } }, createVoucherPositionLine(position,amount,positionLineId));
    setLines(updated); setError(null); markChanged();
  }

  function copyMainPartyToLines() {
    const partyId = form.mainPartyId;
    if (!partyId || readOnly || supportCancellation) return;
    setLines(current => current.map(line => line.value.existingPosition ? line : {
      ...line,
      value: { ...line.value, partyId },
      partyName: mainParty.name,
      partyIdentification: mainParty.identification,
    }));
    markChanged();
  }

  function replicateDescription() {
    if (!form.description.trim() || readOnly || supportCancellation) return;
    setLines(current => current.map(line => ({
      ...line, value: { ...line.value, description: form.description.trim() },
    })));
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
      <div className="flex-1"><h1 className="text-2xl font-bold">{form.adjustment ? "Ajuste de cartera" : form.voucherTypeCode === "CashReceipt" ? "Recibo de caja" : "Comprobante manual"}</h1><p className="text-sm text-white/80">{statusLabel} · {supportCancellation ? "Nota de ajuste del documento soporte" : fiscalDocument ? "Documento soporte electrónico" : "Documento interno, sin envío a DIAN"}</p></div>
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
    {saved?.reversalDocumentId && <div role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm">{saved.reversalStatus === "Posted" ? "Comprobante anulado mediante asiento inverso." : `${statusLabel}. El asiento original sigue visible.`} <Button variant="link" onClick={() => setReversalOpen(true)}>Ver asiento de anulación</Button></div>}
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
        {!form.adjustment && form.payloadVersion === 4 && <div className="space-y-2"><Label>Tipo de comprobante</Label><Select value={form.voucherTypeCode ?? ""} disabled={busy || readOnly || !!voucherTypeScope} onValueChange={voucherTypeCode => { change({ voucherTypeCode }); setShowPendingDocuments(false); }}>
          <SelectTrigger aria-label="Tipo de comprobante"><SelectValue /></SelectTrigger><SelectContent>{voucherTypes.data?.map(item => <SelectItem key={item.id} value={item.code}
            disabled={item.code !== "Accounting" && item.code !== "Disbursement" && item.code !== "CashReceipt" && item.code !== "SupportDocument" && item.code !== "SupportDocumentCancellation"}>{item.label}</SelectItem>)}</SelectContent></Select>
          {!voucherTypeScope && <p className="text-xs text-muted-foreground">Los documentos soporte requieren su captura fiscal.</p>}</div>}
        {!form.adjustment && form.payloadVersion !== 4 && !readOnly && <div className="sm:col-span-3"><Button variant="outline" onClick={() => change({ payloadVersion: 4, voucherTypeCode: "Accounting" })}>Usar captura financiera</Button><p className="mt-1 text-xs text-muted-foreground">Valida la configuración financiera de los auxiliares antes de contabilizar.</p></div>}
        {!form.adjustment && <div className="min-w-0 space-y-2 sm:col-span-3"><Label>Tercero principal{form.payloadVersion === 4 ? " *" : ""}</Label><div className="flex flex-col gap-2 sm:flex-row sm:items-start"><div className="w-full min-w-0 flex-1"><PartySelect value={form.mainPartyId ?? ""} ariaLabel="Seleccionar tercero principal"
          selectedOption={form.mainPartyId && mainParty.name ? { value: form.mainPartyId, label: mainParty.name, description: mainParty.identification } : null}
          disabled={busy || readOnly} onChange={(id, party) => {
            change({ mainPartyId: id || null });
            setShowPendingDocuments(!!id && (form.voucherTypeCode === "CashReceipt" || form.voucherTypeCode === "Disbursement"));
            setMainParty({ name: party?.displayName ?? null, identification: party?.identification ?? null });
            setLines(current => current.map(line => !line.value.accountId && !line.value.partyId && !line.value.description && !line.value.debit && !line.value.credit
              ? { ...line, value: { ...line.value, partyId: id || null }, partyName: party?.displayName ?? null, partyIdentification: party?.identification ?? null } : line));
          }}/></div>{form.payloadVersion === 4 && !fiscalDocument && <Button type="button" variant="outline" className="self-start" disabled={!form.mainPartyId}
            onClick={() => setShowPendingDocuments(current => !current)}>{showPendingDocuments ? "Ocultar documentos" : "Ver documentos"}</Button>}</div>
          <div className="flex flex-wrap items-center gap-2"><p className="text-xs text-muted-foreground">{form.payloadVersion === 4 ? "Obligatorio para contabilizar. " : ""}{form.voucherTypeCode === "CashReceipt" ? "Las partidas del recibo deben corresponder al tercero seleccionado." : "Se propone en las nuevas partidas. Cada partida conserva su propio tercero."}</p>
            {!readOnly && !supportCancellation && <Button type="button" size="sm" variant="outline" disabled={!form.mainPartyId || busy} onClick={copyMainPartyToLines}>Copiar tercero a partidas sin vínculo</Button>}</div></div>}
        {!form.adjustment && form.payloadVersion === 4 && !fiscalDocument && form.mainPartyId && showPendingDocuments && !readOnly &&
          <div id="voucher-pending-documents" className="sm:col-span-3"><VoucherPendingDocuments key={form.mainPartyId}
            businessId={businessId} documentId={form.documentId} occurredAt={form.occurredAt}
            voucherTypeCode={form.voucherTypeCode} partyId={form.mainPartyId}
            remainingLineCapacity={500 - lines.length} linkedPositions={linkedPositions} crossTargets={crossTargets}
            receivableSelectionLimit={inlinePaymentVoucher && paymentLines.length > 1 ? 1 : undefined}
            onCross={crossCredit} onAdd={addPositionLines} /></div>}
        <div className="space-y-2"><Label htmlFor="voucher-occurred-at">{form.voucherTypeCode === "CashReceipt" || form.voucherTypeCode === "Disbursement" ? "Fecha de pago" : "Fecha contable"}</Label><DatePicker id="voucher-occurred-at" value={form.occurredAt.slice(0, 10)} onChange={date => {
          try { change({ occurredAt: date ? accountingDateAtNoon(date, timeZone) : "" }); }
          catch (value) { setError(value instanceof Error ? value.message : "No se pudo cambiar la fecha del pago."); }
        }} /></div>
        <div className="space-y-2 sm:col-span-2"><Label htmlFor="voucher-description">Descripción</Label><Input id="voucher-description" maxLength={500} value={form.description} onChange={event => change({ description: event.target.value })} placeholder="Motivo del comprobante" />
          {!form.adjustment && !readOnly && !supportCancellation && <Button type="button" size="sm" variant="outline" disabled={!form.description.trim() || busy} onClick={replicateDescription}>Replicar descripción en todas las partidas</Button>}</div>
        {!form.adjustment && form.payloadVersion === 4 && form.voucherTypeCode === "SupportDocument" && <div className="flex flex-wrap items-center gap-3 sm:col-span-3">
          <Button variant="outline" disabled={busy || readOnly || !form.mainPartyId} onClick={() => setFiscalOpen(true)}>Datos fiscales del soporte</Button>
          <p role="status" className="text-xs text-muted-foreground">{form.fiscalCapture?.calculationHash ? "Cálculo fiscal revisado" : "Completa las partidas y revisa el cálculo fiscal antes de contabilizar."}</p>
        </div>}
        {!form.adjustment && form.payloadVersion === 4 && supportCancellation && <div className="flex flex-wrap items-center gap-3 sm:col-span-3">
          <Button variant="outline" disabled={busy || readOnly} onClick={() => setPositionOpen(true)}>Seleccionar soporte original</Button>
          <Button variant="outline" disabled={busy || readOnly || !form.fiscalCapture} onClick={() => void reviewCancellation()}>{fiscalBusy ? "Revisando anulación…" : "Revisar anulación"}</Button>
          <p role="status" className="text-xs text-muted-foreground">{form.fiscalCapture?.calculationHash ? "Anulación revisada. Ya puedes guardar el comprobante." : "Selecciona el soporte y revisa sus dependencias antes de guardar el comprobante."}</p>
        </div>}
      </section>
      {inlinePaymentVoucher && <>
        <VoucherPaymentForms tenantId={tenantId} businessId={businessId} direction={form.voucherTypeCode === "Disbursement" ? "outgoing" : "incoming"}
          lines={paymentLines}
          applied={appliedPayments} disabled={busy} readOnly={readOnly}
          addBlockReason={paymentAddBlockReason}
          canAdd={!!form.mainPartyId && (lines.length < 500 || lines.some(line => !hasLineCapture(line)))}
          onSelect={selectPaymentForm} onChange={updateValue}
          onEvidence={(index, patch) => updateValue(index, { paymentEvidence: { ...lines[index].value.paymentEvidence, ...patch } })}
          onRemove={index => {
            const remaining = lines.filter((_line, position) => position !== index);
            setLines(remaining); markChanged(new Set(remaining.map(line => line.value.lineId!)));
          }} />
      </>}
      {!readOnly && receiptAttributionConflict && <p role="alert" className="rounded-xl border p-4 text-sm text-destructive">Para varias facturas se usa una sola forma de pago. Conserva una factura o retira las formas adicionales; tus valores se mantienen.</p>}
      {form.adjustment ? <section className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-3">
        <div className="space-y-2"><Label>Dirección del ajuste</Label><Select value={form.adjustment.direction} disabled={readOnly || busy} onValueChange={direction => change({ adjustment: { ...form.adjustment!, direction: direction as "Increase" | "Decrease" } })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{directions.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        <div className="space-y-2"><Label>Valor del ajuste</Label><FormattedNumberInput kind="currency" ariaLabel="Valor del ajuste" value={form.adjustment.amount} onValueChange={amount => change({ adjustment: { ...form.adjustment!, amount: amount ?? 0 } })} /></div>
        <div className="space-y-2"><Label>Cuenta de contrapartida</Label><AccountSelect value={form.adjustment.counterpartAccountId ?? ""} onChange={counterpartAccountId => change({ adjustment: { ...form.adjustment!, counterpartAccountId } })} /></div>
        <p className="text-sm text-muted-foreground sm:col-span-3">La obligación elegida se conserva vinculada. Guardar borrador no cambia su saldo; Guardar comprobante valida el saldo disponible y contabiliza.</p>
      </section> : <fieldset disabled={supportCancellation} className="min-w-0 overflow-hidden rounded-2xl border bg-card">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b p-4"><div><h2 className="font-semibold">Partidas del comprobante</h2><p className="text-sm text-muted-foreground">{supportCancellation ? "Partidas inversas del asiento original. Para cambiar el origen, selecciona otro soporte." : "Edita cada partida en su fila. D = débito, C = crédito. Puedes guardar un comprobante incompleto."}</p></div><div className="flex flex-wrap gap-2">
          {form.payloadVersion === 4 && !inlinePaymentVoucher && !fiscalDocument && <Button variant="outline" disabled={!form.mainPartyId || lines.length >= 500} onClick={addPaymentForm}>Agregar forma de pago</Button>}
          <Button variant="outline" disabled={lines.length >= 500} onClick={() => { setLines(current => { const line = newLine(); return [...current, {...line,value:{...line.value,partyId:form.mainPartyId??null,financialGroupId:form.documentId},partyName:mainParty.name,partyIdentification:mainParty.identification}]; }); markChanged(); }}><Plus className="mr-2 h-4 w-4" />Agregar partida</Button></div></div>
        {!!saved?.appliedPaymentMethods?.length && <section aria-label="Formas de pago utilizadas" className="space-y-1 border-b p-4 text-sm">
          <p className="font-medium">Formas y cuentas utilizadas al contabilizar</p>
          {saved.appliedPaymentMethods.map(method => <p key={method.sourceLineId}>{method.name} · {method.accountCode} · {method.accountName}</p>)}
        </section>}
        {removableCrossings.length > 0 && <section aria-label="Cruces de saldos a favor" className="space-y-2 border-b p-4 text-sm">
          <p className="font-medium">Cruces agregados · sin entrada ni salida de dinero</p>
          {removableCrossings.map(line => <div key={line.value.lineId} className="flex flex-wrap items-center justify-between gap-2">
            <span>{line.value.reference ?? line.value.description} · {money.format(line.value.debit + line.value.credit)} → {groupedLines.get(line.value.financialGroupId!)!
              .filter(item => item.value.existingPosition?.subledgerKind === "Receivable" || item.value.existingPosition?.subledgerKind === "Payable")
              .map(item => item.value.reference ?? item.value.description).join(", ")}</span>
            <Button variant="outline" size="sm" onClick={() => {
              const remaining = lines.filter(item => item.value.financialGroupId !== line.value.financialGroupId);
              setLines(remaining); markChanged(new Set(remaining.map(item => item.value.lineId!)));
            }}>Retirar cruce {line.value.reference}</Button>
          </div>)}
          <p className="text-xs text-muted-foreground">Retirar el cruce deja esa parte de la factura pendiente. El dinero capturado conserva su importe.</p>
        </section>}
        <div className="max-h-[32rem] overflow-auto" onScroll={event => setScrollTop(event.currentTarget.scrollTop)}><table className="w-full min-w-[1420px] table-fixed text-sm"><colgroup><col className="w-9" /><col className="w-[220px]" /><col className="w-[170px]" /><col className="w-[140px]" /><col className="w-[205px]" /><col className="w-[160px]" /><col className="w-[160px]" /><col className="w-[160px]" /><col className="w-[160px]" /></colgroup><thead className="sticky top-0 z-10 bg-muted"><tr><th className="p-2 text-left">D/C</th><th className="p-2 text-left">Cuenta PUC</th><th className="p-2 text-left">Tercero</th><th className="p-2 text-left">Centro de costo</th><th className="p-2 text-left">Descripción u observación</th><th className="p-2 text-right">Base (opcional)</th><th className="p-2 text-right">Débito</th><th className="p-2 text-right">Crédito</th><th className="p-2 text-right">Acciones</th></tr></thead><tbody>
          {start > 0 && <tr aria-hidden><td colSpan={9} style={{ height: start * rowHeight }} /></tr>}
          {lines.slice(start, end).map((item, offset) => {
            const index = start + offset;
            const kind = item.value.debit > 0 && item.value.credit > 0 ? "D/C" : item.value.debit > 0 ? "D" : item.value.credit > 0 ? "C" : "—";
            return <tr key={item.value.lineId ?? index} aria-label={`Partida ${index + 1}`} title={item.validationError ?? undefined} style={{ height: rowHeight }} className={`border-t align-middle hover:bg-muted/30 ${item.validationError ? "bg-amber-100" : ""}`}>
              <td className="px-2 text-center"><span title={kind === "D" ? "Débito" : kind === "C" ? "Crédito" : undefined} className="inline-flex min-w-7 justify-center rounded-md bg-muted px-1 py-1 font-bold">{kind}</span></td>
              <td className="p-1">{appliedPayments.has(item.value.lineId!) ? <p title={`Cuenta utilizada al contabilizar: ${appliedPayments.get(item.value.lineId!)!.accountCode} · ${appliedPayments.get(item.value.lineId!)!.accountName}`} className="truncate px-2 text-xs">{appliedPayments.get(item.value.lineId!)!.accountCode} · {appliedPayments.get(item.value.lineId!)!.accountName}</p> : <AccountSelect value={item.value.accountId ?? ""} resolveMissing={false} disabled={!!item.value.existingPosition || !!item.value.paymentEvidence?.paymentMethodId || !!item.value.financialConceptId} accounts={item.value.accountId && item.accountCode ? [{ accountId: item.value.accountId, code: item.accountCode, name: item.accountName ?? "" }] : []} onChange={(accountId, account) => updateLine(index, { value: { ...item.value, accountId }, accountCode: account?.code ?? null, accountName: account?.name ?? null })} />}
                {item.value.paymentEvidence?.paymentMethodName && <p title={appliedPayments.get(item.value.lineId!)?.name ?? item.value.paymentEvidence.paymentMethodName} className="truncate px-2 text-xs text-muted-foreground">{appliedPayments.get(item.value.lineId!)?.name ?? item.value.paymentEvidence.paymentMethodName}</p>}
                {item.value.financialConceptId&&<p title={`${item.financialConceptCode} · ${item.financialConceptName}`} className="truncate px-2 text-xs text-muted-foreground">{item.financialConceptCode} · {item.financialConceptName}</p>}
              </td>
              <td className="p-1"><PartySelect value={item.value.partyId ?? ""} disabled={!!item.value.existingPosition} selectedOption={item.value.partyId && item.partyName ? { value: item.value.partyId, label: item.partyName, description: item.partyIdentification ?? undefined } : null} onChange={(partyId, party) => updateLine(index, { value: { ...item.value, partyId: partyId || null }, partyName: party?.displayName ?? null, partyIdentification: party?.identification ?? null })} /></td>
              <td className="p-1"><CostCenterSelect value={item.value.costCenterId} onChange={value=>updateValue(index,{costCenterId:value})} selected={item.value.costCenterId?centers.data?.get(item.value.costCenterId):undefined} selectedName={item.costCenterName} disabled={busy||readOnly} ariaLabel={`Centro de costo de la partida ${index+1}`}/></td>
              <td className="p-1"><Input required maxLength={500} aria-label="Descripción de la partida" placeholder="Descripción u observación" value={item.value.description} onChange={event => updateValue(index, { description: event.target.value })} className="h-9" /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" allowNegative ariaLabel="Base de la partida" placeholder="Sin registrar" className="text-right tabular-nums" value={item.value.baseAmount ?? ""} onValueChange={baseAmount => updateValue(index, { baseAmount })} /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Débito de la partida" disabled={!!item.value.financialConceptId&&item.financialConceptSideCode!=="Debit"} className="text-right tabular-nums" value={item.value.debit} onValueChange={debit => updateValue(index, { debit: debit ?? 0 })} /></td>
              <td className="p-1"><FormattedNumberInput kind="currency" ariaLabel="Crédito de la partida" disabled={!!item.value.financialConceptId&&item.financialConceptSideCode!=="Credit"} className="text-right tabular-nums" value={item.value.credit} onValueChange={credit => updateValue(index, { credit: credit ?? 0 })} /></td>
              <td className="p-1 text-right">{form.payloadVersion === 4 && (!inlinePaymentVoucher || !item.value.paymentEvidence?.paymentMethodId) && <Button variant="ghost" size="icon" title="Detalles de la partida" aria-label={`Detalles de la partida ${index + 1}`} onClick={() => setDetailLineId(item.value.lineId!)}><FileText className="h-4 w-4" /></Button>}<Button variant="ghost" size="icon" title="Intercambiar débito y crédito" disabled={!!item.value.financialConceptId} aria-label={`Intercambiar débito y crédito de la partida ${index + 1}`} onClick={() => updateValue(index, {debit:item.value.credit,credit:item.value.debit})}><ArrowLeftRight className="h-4 w-4" /></Button><Button variant="ghost" size="icon" title="Duplicar partida" aria-label={`Duplicar partida ${index + 1}`} disabled={lines.length >= 500} onClick={() => { setLines(current => [...current.slice(0, index + 1), { ...item, value: { ...item.value, lineId: crypto.randomUUID() } }, ...current.slice(index + 1)]); markChanged(); }}><Copy className="h-4 w-4" /></Button><Button variant="ghost" size="icon" title="Eliminar partida" aria-label={`Eliminar partida ${index + 1}`} disabled={lines.some(line=>line.value.financialConceptTargetLineId===item.value.lineId)} onClick={() => { setLines(current => current.filter((_, position) => position !== index)); markChanged(new Set(lines.filter((_, position) => position !== index).map(line => line.value.lineId!))); }}><Trash2 className="h-4 w-4" /></Button></td>
            </tr>;
          })}
          {end < lines.length && <tr aria-hidden><td colSpan={9} style={{ height: (lines.length - end) * rowHeight }} /></tr>}
        </tbody></table></div>
      </fieldset>}
    </fieldset>
    <footer aria-label="Totales y acciones del comprobante" className="relative z-20 space-y-3 rounded-2xl border bg-background p-4 shadow-lg sm:sticky sm:bottom-0">
      <div className="min-w-0 text-sm">{!form.adjustment && <div className="flex flex-wrap gap-x-4 gap-y-1"><b>Débito {money.format(debit)}</b><span>Crédito {money.format(credit)}</span><span className={differencePending ? "text-destructive" : "text-muted-foreground"}>Diferencia {money.format(difference)} · {lines.length} partidas</span></div>}
        <p className="text-muted-foreground">{saved?.status === "Posted" ? "Comprobante contabilizado: contenido inmutable." : saved?.sentAt ? "Comprobante aceptado; consulta su estado contable. El contenido está congelado." : "Guarda un borrador para seguir editando, o guarda el comprobante definitivo para contabilizarlo."}</p></div>
      <div className="flex flex-wrap items-center justify-end gap-2">
      <Button variant="outline" disabled={busy} onClick={close}>{modal ? closeLabel : "Volver"}</Button>
      {saved && !dirty && <Button variant="outline" disabled={busy} onClick={() => void refresh()}>{phase === "refresh" ? "Consultando estado…" : "Actualizar estado"}</Button>}
      {saved?.sentAt && saved.status !== "Posted" && saved.status !== "CommercialEffectsApplied" && !postingQueued &&
        permissions.includes("accounting.postings.retry") && <Button disabled={busy} onClick={() => void retry()}>{phase === "retry" ? "Solicitando reintento…" : "Reintentar contabilización"}</Button>}
      {saved?.status === "Posted" && saved.row.fiscalStatus === "AwaitingManualSubmission" && permissions.includes("fiscal.manual-support.send") &&
        <Button disabled={busy} onClick={() => void sendFiscal()}><Send className="mr-2 h-4 w-4" />{phase === "fiscal" ? "Solicitando envío…" : "Enviar documento soporte a la DIAN"}</Button>}
      {canCancelPlainJournal && <Button variant="destructive" disabled={busy} onClick={() => setCancelOpen(true)}><RotateCcw className="mr-2 h-4 w-4" />Anular comprobante</Button>}
      {saved && !saved.sentAt && !readOnly && <Button variant="destructive" disabled={busy} onClick={() => setDeleteOpen(true)}>{phase === "discard" ? "Descartando…" : "Descartar borrador"}</Button>}
      {!readOnly && <Button variant="outline" disabled={busy || !dirty} onClick={() => void save()}><Save className="mr-2 h-4 w-4" />{phase === "save" ? "Guardando…" : "Guardar borrador"}</Button>}
      {!readOnly && permissions.includes("accounting.manual.send") && <Button disabled={busy || receiptAttributionConflict || fiscalDocument && !form.fiscalCapture?.calculationHash} onClick={() => void saveAndPost()}><Send className="mr-2 h-4 w-4" />{phase === "send" ? "Contabilizando…" : "Guardar comprobante"}</Button>}
      </div>
    </footer>
    {entryOpen && <AccountingDocumentDialog documentId={saved?.documentId} hasFiscalDocument={!!saved?.row.fiscalDocumentType} onClose={() => setEntryOpen(false)} />}
    {reversalOpen && saved?.reversalDocumentId && <AccountingDocumentDialog documentId={saved.reversalDocumentId} onClose={() => setReversalOpen(false)} />}
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
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      {!inlinePaymentVoucher && franchises.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las franquicias. <Button variant="link" onClick={() => void franchises.refetch()}>Reintentar franquicias</Button></p>}
      <fieldset disabled={busy || readOnly} className="grid gap-4 sm:grid-cols-2">
        {detailLine.value.financialConceptId&&<p className="text-sm text-muted-foreground sm:col-span-2">Concepto registrado: {detailLine.financialConceptCode} · {detailLine.financialConceptName}</p>}
        {detailLine.value.financialConceptId&&detailLine.value.financialConceptTargetLineId&&<div className="space-y-2 sm:col-span-2">
          <Label>Documento ajustado</Label><Select value={detailLine.value.financialConceptTargetLineId??"none"} disabled={busy||readOnly} onValueChange={value=>{
            if(value==="none"){updateValue(detailIndex,{financialConceptTargetLineId:null});return;}
            const target=compatibleTargets(detailLine.financialConceptSideCode).find(line=>line.value.lineId===value);
            if(!target)return;const groupId=target.value.financialGroupId??form.documentId;
            setLines(current=>current.map(line=>line.value.lineId===value?{...line,value:{...line.value,financialGroupId:groupId}}:
              line.value.lineId===detailLineId?{...line,value:{...line.value,financialConceptTargetLineId:value,financialGroupId:groupId}}:line));markChanged();
          }}><SelectTrigger aria-label="Documento ajustado"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin documento vinculado</SelectItem>
            {compatibleTargets(detailLine.financialConceptSideCode).map(line=><SelectItem key={line.value.lineId} value={line.value.lineId!}>{targetLabel(line)}</SelectItem>)}
          </SelectContent></Select>
        </div>}
        {!inlinePaymentVoucher && !detailLine.value.existingPosition && !detailLine.value.financialConceptId && !readOnly && form.payloadVersion === 4 && !fiscalDocument && <div className="space-y-2 sm:col-span-2"><Label>Forma de pago</Label>
          <PaymentMethodSelect tenantId={tenantId} businessId={businessId} usage={paymentUsage}
            value={detailLine.value.paymentEvidence?.paymentMethodId ?? ""} name={detailLine.value.paymentEvidence?.paymentMethodName}
            accountLabel={`${detailLine.accountCode} · ${detailLine.accountName}`} disabled={busy}
            ariaLabel="Forma de pago de la partida" onChange={selectPaymentForm} />
          {detailLine.value.paymentEvidence?.paymentMethodId && <>
            <p className="text-sm">Cuenta: {detailLine.accountCode} · {detailLine.accountName}</p>
            <FormattedNumberInput kind="currency" ariaLabel={paymentOutgoing ? "Valor pagado por la forma" : "Valor recibido por la forma"}
              value={paymentOutgoing ? detailLine.value.credit : detailLine.value.debit}
              onValueChange={amount => updateValue(detailIndex, paymentOutgoing ? { credit: amount ?? 0, debit: 0 } : { debit: amount ?? 0, credit: 0 })} />
            <Button variant="link" onClick={() => updateEvidence({ paymentMethodId: null, paymentMethodTypeCode: null, paymentMethodVersion: null, paymentMethodCode: null, paymentMethodName: null })}>Quitar forma seleccionada</Button>
          </>}
        </div>}
        {form.voucherTypeCode === "SupportDocument" && detailLine.value.partyId && <div className="space-y-2 sm:col-span-2"><Label>Sede del proveedor de la partida</Label>
          <SupplierSiteSelect value={detailLine.value.partySiteId ?? ""} partyId={detailLine.value.partyId} disabled={busy || readOnly}
            onChange={partySiteId => updateValue(detailIndex, { partySiteId: partySiteId || null })} />
          <p className="text-xs text-muted-foreground">Sin selección explícita se usa la sede predeterminada. La sede de la obligación se conserva en el soporte fiscal.</p>
        </div>}
        {(!inlinePaymentVoucher || !detailLine.value.paymentEvidence?.paymentMethodId) && <>
        <div className="space-y-2"><Label htmlFor="line-document-reference">Referencia del documento</Label><Input id="line-document-reference" maxLength={100} value={detailLine.value.reference ?? ""} onChange={event => updateValue(detailIndex, { reference: event.target.value || null })} /></div>
        <div className="space-y-2"><Label htmlFor="voucher-due-date">Vencimiento de nueva obligación</Label><DatePicker id="voucher-due-date" value={detailLine.value.dueDate?.slice(0, 10) ?? ""} onChange={date => {
          try { updateValue(detailIndex, { dueDate: date ? accountingDateAtNoon(date, timeZone) : null }); }
          catch (value) { setError(value instanceof Error ? value.message : "No se pudo cambiar el vencimiento."); }
        }} /><p className="text-xs text-muted-foreground">Solo al abrir una cuenta por pagar o cobrar. Un anticipo no lleva vencimiento.</p></div>
        </>}
        {detailLine.value.existingPosition && <div className="rounded-xl border p-3 text-sm sm:col-span-2"><p>Documento vinculado: {detailLine.value.reference ?? detailLine.value.existingPosition.subledgerId}</p><Button variant="link" disabled={lines.some(line=>line.value.financialConceptTargetLineId===detailLineId)} onClick={() => updateValue(detailIndex, { existingPosition: null })}>Quitar vínculo con el documento</Button></div>}
        {!inlinePaymentVoucher && <>
        <div className="space-y-2"><Label htmlFor="line-payment-approval">Autorización del pago</Label><Input id="line-payment-approval" maxLength={100} value={detailLine.value.paymentEvidence?.approvalNumber ?? ""} onChange={event => updateEvidence({ approvalNumber: event.target.value || null })} /></div>
        <div className="space-y-2"><Label>Franquicia de tarjeta</Label><Select value={detailLine.value.paymentEvidence?.cardFranchiseCode ?? "none"} disabled={busy || readOnly} onValueChange={value => updateEvidence({ cardFranchiseCode: value === "none" ? null : value })}><SelectTrigger aria-label="Franquicia del pago"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="none">Sin tarjeta</SelectItem>{franchises.data?.map(item => <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></div>
        {form.voucherTypeCode !== "CashReceipt" && form.voucherTypeCode !== "Disbursement" && <div className="space-y-2"><Label>Efectivo entregado</Label><FormattedNumberInput kind="currency" ariaLabel="Efectivo entregado en la partida" value={detailLine.value.paymentEvidence?.tenderedAmount ?? ""} onValueChange={tenderedAmount => updateEvidence({ tenderedAmount })} /></div>}
        <div className="space-y-2"><Label htmlFor="line-payment-notes">Observación del pago</Label><Input id="line-payment-notes" maxLength={500} value={detailLine.value.paymentEvidence?.notes ?? ""} onChange={event => updateEvidence({ notes: event.target.value || null })} /></div>
        <p className="text-xs text-muted-foreground sm:col-span-2">La evidencia de pago corresponde únicamente a la partida de caja, banco o tarjeta. El banco y el medio se obtienen de la configuración del auxiliar.</p>
        </>}
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
    <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
      <DialogContent className="sm:max-w-md"><DialogHeader><DialogTitle>¿Descartar este borrador?</DialogTitle>
        <DialogDescription>Se eliminará la captura guardada de este comprobante. Esta acción no afecta la contabilidad porque aún no se ha contabilizado.</DialogDescription></DialogHeader>
        <DialogFooter><Button variant="outline" onClick={() => setDeleteOpen(false)}>Seguir editando</Button>
          <Button variant="destructive" disabled={busy} onClick={() => void discard()}>Descartar borrador</Button></DialogFooter>
      </DialogContent>
    </Dialog>
    <Dialog open={cancelOpen} onOpenChange={open => { if (!busy) setCancelOpen(open); }}>
      <DialogContent className="sm:max-w-md"><DialogHeader><DialogTitle>¿Anular este comprobante?</DialogTitle>
        <DialogDescription>Se contabilizará un asiento inverso vinculado al original. El asiento original seguirá visible en la trazabilidad.</DialogDescription></DialogHeader>
        <DialogFooter><Button variant="outline" disabled={busy} onClick={() => setCancelOpen(false)}>Volver</Button>
          <Button variant="destructive" disabled={busy} onClick={() => void cancelJournal()}>{phase === "cancel" ? "Anulando…" : "Anular comprobante"}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </div>;
}
