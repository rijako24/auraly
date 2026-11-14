"use client";

import { useEffect, useMemo, useState } from "react";
import { X } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { accountingApi, type AccountingOpenPosition, type VoucherDraftLineView } from "@/services/api/accounting";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { AccountSelect } from "./account-select";
import { AccountingDocumentDialog } from "./accounting-document-dialog";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

type Kind = AccountingOpenPosition["subledgerKind"];
type LinkedPosition = { subledgerKind: Kind; subledgerId: string; amount: number; financialDiscountEarned?: boolean | null; creditCrossing?: boolean };
export type VoucherCreditTarget = VoucherDraftLineView & { availableForCreditAmount: number };
type DiscountDecision = "pending" | "earned" | "lost";
const supportedKind = (value: string): value is Kind =>
  value === "Payable" || value === "Receivable" || value === "SupplierCredit" || value === "CustomerCredit";

export function createVoucherPositionLine(position: AccountingOpenPosition, amount: number,
  financialGroupId: string | null, financialDiscountEarned: boolean | null = null): VoucherDraftLineView {
  return { value: {
    lineId: crypto.randomUUID(), accountId: position.accountId, partyId: position.partyId,
    partySiteId: position.partySiteId, costCenterId: null,
    description: `Aplicación ${position.documentNumber}`.slice(0, 500), reference: position.documentNumber.slice(0, 100),
    debit: (position.openingCredit ?? 0) > 0 ? amount : 0, credit: (position.openingDebit ?? 0) > 0 ? amount : 0,
    existingPosition: { subledgerKind: position.subledgerKind, subledgerId: position.subledgerId },
    financialGroupId, financialDiscountEarned,
  }, accountCode: position.accountCode, accountName: position.accountName,
    partyName: position.partyName, partyIdentification: position.partyIdentification, costCenterName: null };
}
function PendingDocumentRow({ position, kind, voucherTypeCode, sourceTypeLabel, linked, remainingLineCapacity, crossTargets, onCross, onAdd, onOpenOrigin }: {
  position: AccountingOpenPosition; kind: Kind; voucherTypeCode?: string | null;
  sourceTypeLabel: string;
  linked: LinkedPosition[]; remainingLineCapacity: number;
  crossTargets: ReadonlyArray<VoucherCreditTarget>;
  onCross: (position: AccountingOpenPosition, targetLineId: string, amount: number) => void;
  onAdd: (lines: VoucherDraftLineView[]) => void; onOpenOrigin: (id: string) => void;
}) {
  const money = useMemo(() => new Intl.NumberFormat("es-CO", {
    ...(position.currencyCode && /^[A-Z]{3}$/.test(position.currencyCode)
      ? { style: "currency", currency: position.currencyCode } as const : {}),
    maximumFractionDigits: 4,
  }), [position.currencyCode]);
  const assistedCredit = (voucherTypeCode === "CashReceipt" || voucherTypeCode === "Disbursement") &&
    (kind === "CustomerCredit" || kind === "SupplierCredit");
  const linkedAmount = linked.length === 0 ? null : linked.reduce((total, item) => total + item.amount, 0);
  const editableLinked = linked.filter(item => !item.creditCrossing);
  const crossAmount = linked.filter(item => item.creditCrossing).reduce((total, item) => total + item.amount, 0);
  const availableForMoney = Math.max(0, Number((position.availableAmount - crossAmount).toFixed(4)));
  const remainingCredit = Math.max(0, Number((position.availableAmount - (linkedAmount ?? 0)).toFixed(4)));
  const suggestedAmount = assistedCredit ? remainingCredit : editableLinked.length === 1 ? editableLinked[0].amount : availableForMoney;
  const [amount, setAmount] = useState<number | null>(suggestedAmount);
  const [crossOpen, setCrossOpen] = useState(false);
  const [targetId, setTargetId] = useState("");
  const targets = crossTargets.filter(line => line.value.existingPosition?.subledgerKind ===
    (kind === "SupplierCredit" ? "Payable" : "Receivable") &&
    (kind !== "SupplierCredit" || line.value.partySiteId == null || line.value.partySiteId === position.partySiteId));
  const target = targets.find(line => line.value.lineId === targetId);
  const targetAmount = target?.availableForCreditAmount ?? 0;
  const capturedTargetAmount = target ? target.value.debit + target.value.credit : 0;
  const [decision, setDecision] = useState<DiscountDecision>("pending");
  useEffect(() => { setAmount(suggestedAmount); }, [suggestedAmount]);
  const canCross = assistedCredit && !!position.accountId && !position.openingLinkIssue && position.currencyCode === "COP" &&
    ((position.openingDebit ?? 0) > 0 || (position.openingCredit ?? 0) > 0) &&
    !!target && amount !== null && amount > 0 && amount <= remainingCredit && amount <= targetAmount &&
    remainingLineCapacity >= (amount === capturedTargetAmount ? 1 : 2);
  const discountAvailable = voucherTypeCode === "Disbursement" && kind === "Payable" &&
    !!position.financialDiscountEligibleAmount && position.financialDiscountAnticipated != null &&
    !position.financialDiscountDecided && !position.financialDiscountPendingReturn && crossAmount === 0;
  const adjustment = discountAvailable && decision !== "pending"
    ? (decision === "earned" ? position.financialDiscountEligibleAmount! : 0) -
      (position.financialDiscountAnticipated ? position.financialDiscountEligibleAmount! : 0) : 0;
  const discountAccountId = adjustment > 0 ? position.financialDiscountEarnedAccountId
    : adjustment < 0 ? position.financialDiscountLostAccountId : null;
  const canAdd = !!position.accountId && !position.openingLinkIssue && position.currencyCode === "COP" &&
    editableLinked.length < 2 && !linked.some(item => item.financialDiscountEarned != null) &&
    (editableLinked.length > 0 || remainingLineCapacity >= (adjustment === 0 ? 1 : 2)) &&
    amount !== null && amount > 0 && amount <= availableForMoney &&
    ((position.openingDebit ?? 0) > 0 || (position.openingCredit ?? 0) > 0) &&
    (decision === "pending" || discountAvailable && linked.length === 0 && amount > adjustment &&
      (adjustment === 0 || !!discountAccountId));

  function add() {
    if (!canAdd || amount === null) return;
    const groupId = decision === "pending" ? null : crypto.randomUUID();
    const selected = createVoucherPositionLine(position, amount, groupId, decision === "pending" ? null : decision === "earned");
    const counterpart: VoucherDraftLineView[] = adjustment === 0 ? [] : [{ value: {
      lineId: crypto.randomUUID(), accountId: discountAccountId!, partyId: position.partyId,
      costCenterId: null, description: `Descuento financiero ${position.documentNumber}`.slice(0, 500),
      reference: position.documentNumber.slice(0, 100), debit: adjustment < 0 ? -adjustment : 0,
      credit: adjustment > 0 ? adjustment : 0, financialGroupId: groupId,
    }, accountCode: adjustment > 0 ? position.financialDiscountEarnedAccountCode ?? null : position.financialDiscountLostAccountCode ?? null,
      accountName: adjustment > 0 ? position.financialDiscountEarnedAccountName ?? null : position.financialDiscountLostAccountName ?? null,
      partyName: position.partyName, partyIdentification: position.partyIdentification, costCenterName: null }];
    onAdd([selected, ...counterpart]);
  }

  return <><tr className="border-t align-top text-sm" aria-label={`Documento ${position.documentNumber}`}>
    <td className="p-2"><button type="button" className="font-medium text-primary underline-offset-2 hover:underline" onClick={() => onOpenOrigin(position.sourceDocumentId)}>{position.documentNumber}</button>
      <p className="text-xs text-muted-foreground">{sourceTypeLabel} · {position.occurredAt.slice(0, 10)}</p>
      <p className="text-xs text-muted-foreground">{position.partyName}{position.partyIdentification ? ` · ${position.partyIdentification}` : ""}</p>
      {position.creditNature === "Advance" && <p className="text-xs font-medium text-primary">Anticipo</p>}
      {position.currencyCode !== "COP" && <p className="text-xs text-muted-foreground">Moneda: {position.currencyCode ?? "Por conciliar"}</p>}
      <p className="text-xs text-muted-foreground">Vence: {position.dueDate?.slice(0, 10) ?? "Sin vencimiento"}</p></td>
    <td className="p-2">{position.accountCode ? `${position.accountCode} · ${position.accountName}` : "Sin cuenta conciliada"}
      <p className="text-xs text-muted-foreground">D origen: {position.openingDebit == null ? "—" : money.format(position.openingDebit)} · C origen: {position.openingCredit == null ? "—" : money.format(position.openingCredit)}</p></td>
    <td className="p-2 text-right">{money.format(position.originalAmount)}</td>
    <td className="p-2 text-right">{position.paidAmount == null ? "—" : money.format(position.paidAmount)}</td>
    <td className="p-2 text-right">{position.otherMovementsAmount == null ? "—" : money.format(position.otherMovementsAmount)}</td>
    <td className="p-2 text-right">{money.format(position.outstandingAmount)}<p className="text-xs text-muted-foreground">Disponible: {money.format(position.availableAmount)}</p>
      {linkedAmount !== null && <p className="text-xs text-primary">En este comprobante: {money.format(linkedAmount)}</p>}</td>
    <td className="p-2 whitespace-nowrap">{position.overdueDays == null ? "—" : position.overdueDays === 0 ? "Al día" : `${position.overdueDays} días`}</td>
    <td className="p-2"><FormattedNumberInput kind="currency" ariaLabel={`Abono a ${position.documentNumber}`} value={amount ?? ""} onValueChange={setAmount} />
      {amount !== null && amount > (assistedCredit ? remainingCredit : availableForMoney) && <p role="alert" className="text-xs text-destructive">Supera el disponible.</p>}
      {assistedCredit && <p className="text-xs text-muted-foreground">Por aplicar aquí: {money.format(remainingCredit)}</p>}</td>
    <td className="p-2">{assistedCredit
      ? <Button size="sm" disabled={remainingCredit <= 0 || !!position.openingLinkIssue || position.currencyCode !== "COP" || !position.accountId}
          onClick={() => setCrossOpen(current => !current)}>Cruzar</Button>
      : <Button size="sm" disabled={!canAdd} onClick={add}>{editableLinked.length === 1 ? "Actualizar" : "Agregar"}</Button>}</td>
  </tr>
  {crossOpen && assistedCredit && <tr className="border-t bg-muted/20"><td colSpan={9} className="space-y-2 px-3 py-3 text-sm">
    <p>Selecciona la obligación que cubrirá este saldo a favor. El cruce agrega sus dos partidas y reduce el importe pendiente de cubrir con dinero nuevo; no cambia el dinero registrado.</p>
    {targets.length === 0 ? <p>Agrega primero la factura que quieres cubrir desde esta lista.</p> : <div className="flex flex-wrap items-center gap-3">
      <Select value={target ? targetId : ""} onValueChange={setTargetId}><SelectTrigger className="w-80" aria-label={`Factura para cruzar ${position.documentNumber}`}><SelectValue placeholder="Seleccionar factura agregada" /></SelectTrigger>
        <SelectContent>{targets.map(line => <SelectItem key={line.value.lineId} value={line.value.lineId!}>{line.value.reference ?? line.value.description} · {money.format(line.availableForCreditAmount)}</SelectItem>)}</SelectContent></Select>
      <Button size="sm" disabled={!canCross} onClick={() => { if (canCross && amount !== null) { onCross(position, targetId, amount); setCrossOpen(false); setTargetId(""); } }}>Agregar cruce</Button>
      {target && amount !== null && amount > targetAmount && <p role="alert" className="text-xs text-destructive">El cruce supera el abono seleccionado para esa factura.</p>}
    </div>}
  </td></tr>}
  {(discountAvailable || position.openingLinkIssue || position.currencyCode !== "COP" || !assistedCredit && editableLinked.length > 1 || linked.some(item => item.financialDiscountEarned != null)) &&
    <tr className="border-t bg-muted/20"><td colSpan={9} className="px-3 py-2 text-xs">
      {position.openingLinkIssue && <p role="alert" className="text-destructive">{position.openingLinkIssue}</p>}
      {position.currencyCode !== "COP" && <p role="alert" className="text-destructive">Este saldo requiere conciliación de moneda antes de aplicarse en COP.</p>}
      {!assistedCredit && editableLinked.length > 1 && <p role="alert" className="text-destructive">Este documento tiene varias partidas. Corrige sus valores en el comprobante.</p>}
      {linked.some(item => item.financialDiscountEarned != null) && <p role="alert" className="text-destructive">Este documento ya tiene una decisión de descuento. Corrige o retira el grupo completo antes de agregarlo.</p>}
      {discountAvailable && <div className="flex flex-wrap items-center gap-3"><Label>Descuento financiero</Label>
        <Select value={decision} onValueChange={value => setDecision(value as DiscountDecision)}>
          <SelectTrigger className="w-48" aria-label={`Decisión del descuento financiero de ${position.documentNumber}`}><SelectValue /></SelectTrigger>
          <SelectContent><SelectItem value="pending">Decidir después</SelectItem><SelectItem value="earned">Sí lo obtuve</SelectItem><SelectItem value="lost">No lo obtuve</SelectItem></SelectContent>
        </Select><span>Elegible: {money.format(position.financialDiscountEligibleAmount!)}.</span>
        {decision !== "pending" && <span>Salida de dinero requerida: {money.format((amount ?? 0) - adjustment)}.</span>}
        {decision !== "pending" && adjustment !== 0 && !discountAccountId && <span role="alert" className="text-destructive">Configura la cuenta de descuento antes de agregar.</span>}
      </div>}
    </td></tr>}
  </>;
}

export function VoucherPendingDocuments({ businessId, documentId, occurredAt, voucherTypeCode, partyId,
  remainingLineCapacity, linkedPositions, crossTargets, onCross, onAdd }: {
  businessId: string; documentId: string; occurredAt: string; voucherTypeCode?: string | null; partyId: string;
  remainingLineCapacity: number; linkedPositions: ReadonlyArray<LinkedPosition>;
  crossTargets: ReadonlyArray<VoucherCreditTarget>;
  onCross: (position: AccountingOpenPosition, targetLineId: string, amount: number) => void;
  onAdd: (lines: VoucherDraftLineView[]) => void;
}) {
  const [filtersOpen, setFiltersOpen] = useState(false);
  const kinds = useReferenceOptions("accounting-subledger-kind", filtersOpen);
  const documentTypes = useReferenceOptions("accounting-document-type");
  const [kind, setKind] = useState<Kind | "All">("All");
  const cashReceipt = voucherTypeCode === "CashReceipt";
  const disbursement = voucherTypeCode === "Disbursement";
  const eligibleKind = (code: string) => (!cashReceipt || code === "Receivable" || code === "CustomerCredit") &&
    (!disbursement || code === "Payable" || code === "SupplierCredit");
  const effectiveKind = kind !== "All" && !eligibleKind(kind) ? "All" : kind;
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [accountId, setAccountId] = useState("");
  const [dueFrom, setDueFrom] = useState("");
  const [dueTo, setDueTo] = useState("");
  const [originId, setOriginId] = useState<string | null>(null);
  useEffect(() => { const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1); }, 300); return () => window.clearTimeout(timer); }, [searchInput]);
  const positions = useQuery({ queryKey: ["voucher-position-options", businessId, documentId, effectiveKind, cashReceipt, disbursement, partyId, accountId, dueFrom, dueTo, search, page, occurredAt.slice(0, 10)],
    queryFn: () => accountingApi.openPositions({ subledgerKind: effectiveKind, partyId, page, pageSize: 10, search: search || undefined,
      accountId: accountId || undefined, dueFrom: dueFrom || undefined, dueTo: dueTo || undefined,
      effectiveOn: occurredAt.slice(0, 10), voucherTypeCode: cashReceipt ? "CashReceipt" : disbursement ? "Disbursement" : undefined }), enabled: !!partyId, staleTime: 60_000 });
  const totalPages = Math.ceil((positions.data?.totalCount ?? 0) / 10);

  return <section aria-label="Documentos pendientes del tercero" className="space-y-3 rounded-xl border bg-muted/10 p-4">
    <div><h3 className="font-semibold">Documentos pendientes del tercero</h3>
      <p className="text-xs text-muted-foreground">Escribe el abono y agrega la partida. Guardar no paga; contabilizar valida el saldo y aplica el cruce.</p>
      {positions.data?.referenceDate && <p className="text-xs text-muted-foreground">Saldos y edad consultados al {positions.data.referenceDate.split("-").reverse().join("/")}. La fecha del pago se registra por separado.</p>}</div>
    <details className="text-sm" onToggle={event => setFiltersOpen(event.currentTarget.open)}><summary className="cursor-pointer font-medium">Filtros</summary>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <div className="space-y-1"><Label>Tipo de saldo</Label><div className="flex gap-1"><Select value={effectiveKind === "All" ? "" : effectiveKind} onValueChange={value => { if (supportedKind(value)) { setKind(value); setPage(1); } }}>
          <SelectTrigger aria-label="Tipo de saldo pendiente"><SelectValue placeholder="Todos los saldos" /></SelectTrigger>
          <SelectContent>{kinds.data?.filter(item => supportedKind(item.code) && eligibleKind(item.code)).map(item =>
            <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
        </Select>{kind !== "All" && <Button type="button" variant="ghost" size="icon" aria-label="Limpiar tipo de saldo" onClick={() => { setKind("All"); setPage(1); }}><X className="h-4 w-4" /></Button>}</div></div>
        <div className="space-y-1"><Label htmlFor="pending-document-search">Buscar documento</Label>
          <Input id="pending-document-search" value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Número o referencia" /></div>
        <div className="space-y-1"><Label>Cuenta (opcional)</Label><AccountSelect value={accountId} onChange={value => { setAccountId(value); setPage(1); }} /></div>
        <div className="space-y-1"><Label>Vence desde</Label><DatePicker value={dueFrom} onChange={value => { setDueFrom(value); setPage(1); }} /></div>
        <div className="space-y-1"><Label>Vence hasta</Label><DatePicker value={dueTo} onChange={value => { setDueTo(value); setPage(1); }} /></div>
      </div>
      {kinds.isError && <p role="alert" className="mt-2 text-sm text-destructive">No se pudieron cargar los tipos de saldo. <Button variant="link" onClick={() => void kinds.refetch()}>Reintentar tipos de saldo</Button></p>}
    </details>
    {positions.isPending && <p role="status" className="text-sm">Consultando documentos…</p>}
    {positions.isError && <p role="alert" className="text-sm text-destructive">No se pudieron consultar los documentos. <Button variant="link" onClick={() => void positions.refetch()}>Reintentar</Button></p>}
    {positions.data && <><div className="overflow-x-auto"><table className="w-full min-w-[1020px] text-left">
      <thead className="bg-muted/50 text-xs"><tr><th className="p-2">Documento / tercero</th><th className="p-2">Cuenta</th>
        <th className="p-2 text-right">Original</th><th className="p-2 text-right">Abonado / pagado</th><th className="p-2 text-right">Otros movimientos</th>
        <th className="p-2 text-right">Saldo</th>
        <th className="p-2">Edad de cartera</th>
        <th className="p-2">Valor a aplicar</th><th className="p-2">Acción</th></tr></thead><tbody>
        {positions.data.items.map(position => <PendingDocumentRow key={`${position.subledgerKind}:${position.subledgerId}`} position={position} kind={position.subledgerKind}
          sourceTypeLabel={documentTypes.data?.find(item => item.code === position.sourceDocumentType)?.label ?? position.sourceDocumentType}
          voucherTypeCode={voucherTypeCode} remainingLineCapacity={remainingLineCapacity}
          crossTargets={crossTargets} onCross={onCross}
          linked={linkedPositions.filter(item => item.subledgerKind === position.subledgerKind && item.subledgerId === position.subledgerId)}
          onAdd={onAdd} onOpenOrigin={setOriginId} />)}
      </tbody></table></div>
      {positions.data.items.length === 0 && <p className="text-sm text-muted-foreground">No hay documentos pendientes para este tercero y filtro.</p>}
      {totalPages > 1 && <div className="flex items-center justify-end gap-2 text-sm"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button>
        <span>Página {page} de {totalPages}</span><Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div>}
    </>}
    {originId && <AccountingDocumentDialog documentId={originId} onClose={() => setOriginId(null)} />}
  </section>;
}
