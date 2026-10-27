"use client";

import { useEffect, useState } from "react";
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
type LinkedPosition = { subledgerKind: Kind; subledgerId: string; amount: number; financialDiscountEarned?: boolean | null };
type DiscountDecision = "pending" | "earned" | "lost";
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
const supportedKind = (value: string): value is Kind =>
  value === "Payable" || value === "Receivable" || value === "SupplierCredit" || value === "CustomerCredit";
const originTypeLabel = (value: string) => ({ Expense: "Gasto", GoodsReceipt: "Compra",
  ManualAccountingVoucher: "Comprobante contable" })[value as "Expense" | "GoodsReceipt" | "ManualAccountingVoucher"] ?? value;

function PendingDocumentRow({ position, kind, voucherTypeCode, linked, remainingLineCapacity, onAdd, onOpenOrigin }: {
  position: AccountingOpenPosition; kind: Kind; voucherTypeCode?: string | null;
  linked: LinkedPosition[]; remainingLineCapacity: number;
  onAdd: (lines: VoucherDraftLineView[]) => void; onOpenOrigin: (id: string) => void;
}) {
  const linkedAmount = linked.length === 1 ? linked[0].amount : null;
  const [amount, setAmount] = useState<number | null>(linkedAmount ?? position.availableAmount);
  const [decision, setDecision] = useState<DiscountDecision>("pending");
  useEffect(() => { setAmount(linkedAmount ?? position.availableAmount); }, [linkedAmount, position.availableAmount]);
  const discountAvailable = voucherTypeCode === "Disbursement" && kind === "Payable" &&
    !!position.financialDiscountEligibleAmount && position.financialDiscountAnticipated != null &&
    !position.financialDiscountDecided && !position.financialDiscountPendingReturn;
  const adjustment = discountAvailable && decision !== "pending"
    ? (decision === "earned" ? position.financialDiscountEligibleAmount! : 0) -
      (position.financialDiscountAnticipated ? position.financialDiscountEligibleAmount! : 0) : 0;
  const discountAccountId = adjustment > 0 ? position.financialDiscountEarnedAccountId
    : adjustment < 0 ? position.financialDiscountLostAccountId : null;
  const canAdd = !!position.accountId && !position.openingLinkIssue && position.currencyCode === "COP" &&
    linked.length < 2 && !linked.some(item => item.financialDiscountEarned != null) &&
    (linked.length > 0 || remainingLineCapacity >= (adjustment === 0 ? 1 : 2)) &&
    amount !== null && amount > 0 && amount <= position.availableAmount &&
    ((position.openingDebit ?? 0) > 0 || (position.openingCredit ?? 0) > 0) &&
    (decision === "pending" || discountAvailable && linked.length === 0 && amount > adjustment &&
      (adjustment === 0 || !!discountAccountId));

  function add() {
    if (!canAdd || amount === null) return;
    const groupId = decision === "pending" ? null : crypto.randomUUID();
    const selected: VoucherDraftLineView = { value: {
      lineId: crypto.randomUUID(), accountId: position.accountId, partyId: position.partyId,
      partySiteId: position.partySiteId, costCenterId: null,
      description: `Aplicación ${position.documentNumber}`.slice(0, 500),
      reference: position.documentNumber.slice(0, 100),
      debit: (position.openingCredit ?? 0) > 0 ? amount : 0,
      credit: (position.openingDebit ?? 0) > 0 ? amount : 0,
      existingPosition: { subledgerKind: kind, subledgerId: position.subledgerId },
      financialGroupId: groupId, financialDiscountEarned: decision === "pending" ? null : decision === "earned",
    }, accountCode: position.accountCode, accountName: position.accountName,
      partyName: position.partyName, partyIdentification: position.partyIdentification, costCenterName: null };
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
      <p className="text-xs text-muted-foreground">{originTypeLabel(position.sourceDocumentType)} · {position.occurredAt.slice(0, 10)}</p>
      <p className="text-xs text-muted-foreground">Vence: {position.dueDate?.slice(0, 10) ?? "Sin vencimiento"}</p></td>
    <td className="p-2">{position.accountCode ? `${position.accountCode} · ${position.accountName}` : "Sin cuenta conciliada"}</td>
    <td className="p-2">{position.partyName}<p className="text-xs text-muted-foreground">{position.partyIdentification}</p></td>
    <td className="p-2 text-right">{money.format(position.openingDebit ?? 0)}</td>
    <td className="p-2 text-right">{money.format(position.openingCredit ?? 0)}</td>
    <td className="p-2 text-right">{money.format(position.outstandingAmount)}<p className="text-xs text-muted-foreground">Disponible: {money.format(position.availableAmount)}</p>
      {linkedAmount !== null && <p className="text-xs text-primary">En este comprobante: {money.format(linkedAmount)}</p>}</td>
    <td className="p-2"><FormattedNumberInput kind="currency" ariaLabel={`Abono a ${position.documentNumber}`} value={amount ?? ""} onValueChange={setAmount} />
      {amount !== null && amount > position.availableAmount && <p role="alert" className="text-xs text-destructive">Supera el disponible.</p>}</td>
    <td className="p-2"><Button size="sm" disabled={!canAdd} onClick={add}>{linkedAmount !== null ? "Actualizar" : "Agregar"}</Button></td>
  </tr>
  {(discountAvailable || position.openingLinkIssue || position.currencyCode !== "COP" || linked.length > 1 || linked.some(item => item.financialDiscountEarned != null)) &&
    <tr className="border-t bg-muted/20"><td colSpan={8} className="px-3 py-2 text-xs">
      {position.openingLinkIssue && <p role="alert" className="text-destructive">{position.openingLinkIssue}</p>}
      {position.currencyCode !== "COP" && <p role="alert" className="text-destructive">Este saldo requiere conciliación de moneda antes de aplicarse en COP.</p>}
      {linked.length > 1 && <p role="alert" className="text-destructive">Este documento tiene varias partidas. Corrige sus valores en el comprobante.</p>}
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
  initialKind = "Payable", remainingLineCapacity, linkedPositions, onAdd }: {
  businessId: string; documentId: string; occurredAt: string; voucherTypeCode?: string | null; partyId: string;
  initialKind?: Kind; remainingLineCapacity: number; linkedPositions: ReadonlyArray<LinkedPosition>;
  onAdd: (lines: VoucherDraftLineView[]) => void;
}) {
  const [filtersOpen, setFiltersOpen] = useState(false);
  const kinds = useReferenceOptions("accounting-subledger-kind", filtersOpen);
  const [kind, setKind] = useState<Kind>(initialKind);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [accountId, setAccountId] = useState("");
  const [dueFrom, setDueFrom] = useState("");
  const [dueTo, setDueTo] = useState("");
  const [originId, setOriginId] = useState<string | null>(null);
  useEffect(() => { const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1); }, 300); return () => window.clearTimeout(timer); }, [searchInput]);
  const positions = useQuery({ queryKey: ["voucher-position-options", businessId, documentId, kind, partyId, accountId, dueFrom, dueTo, search, page, occurredAt.slice(0, 10)],
    queryFn: () => accountingApi.openPositions({ subledgerKind: kind, partyId, page, pageSize: 10, search: search || undefined,
      accountId: accountId || undefined, dueFrom: dueFrom || undefined, dueTo: dueTo || undefined,
      effectiveOn: occurredAt.slice(0, 10) }), enabled: !!partyId, staleTime: 60_000 });
  const totalPages = Math.ceil((positions.data?.totalCount ?? 0) / 10);

  return <section aria-label="Documentos pendientes del tercero" className="space-y-3 rounded-xl border bg-muted/10 p-4">
    <div><h3 className="font-semibold">Documentos pendientes del tercero</h3>
      <p className="text-xs text-muted-foreground">Escribe el abono y agrega la partida. Guardar no paga; contabilizar valida el saldo y aplica el cruce.</p></div>
    <details className="text-sm" onToggle={event => setFiltersOpen(event.currentTarget.open)}><summary className="cursor-pointer font-medium">Filtros</summary>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <div className="space-y-1"><Label>Tipo de saldo</Label><Select value={kind} onValueChange={value => { if (supportedKind(value)) { setKind(value); setPage(1); } }}>
          <SelectTrigger aria-label="Tipo de saldo pendiente"><SelectValue /></SelectTrigger>
          <SelectContent>{kinds.data?.filter(item => supportedKind(item.code)).map(item =>
            <SelectItem key={item.id} value={item.code}>{item.label}</SelectItem>)}</SelectContent>
        </Select></div>
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
    {positions.data && <><div className="overflow-x-auto"><table className="w-full min-w-[1120px] text-left">
      <thead className="bg-muted/50 text-xs"><tr><th className="p-2">Documento</th><th className="p-2">Cuenta</th><th className="p-2">Tercero</th>
        <th className="p-2 text-right">Débito origen</th><th className="p-2 text-right">Crédito origen</th><th className="p-2 text-right">Saldo</th>
        <th className="p-2">Abono</th><th className="p-2">Acción</th></tr></thead><tbody>
        {positions.data.items.map(position => <PendingDocumentRow key={`${kind}:${position.subledgerId}`} position={position} kind={kind}
          voucherTypeCode={voucherTypeCode} remainingLineCapacity={remainingLineCapacity}
          linked={linkedPositions.filter(item => item.subledgerKind === kind && item.subledgerId === position.subledgerId)}
          onAdd={onAdd} onOpenOrigin={setOriginId} />)}
      </tbody></table></div>
      {positions.data.items.length === 0 && <p className="text-sm text-muted-foreground">No hay documentos pendientes para este tercero y filtro.</p>}
      {totalPages > 1 && <div className="flex items-center justify-end gap-2 text-sm"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button>
        <span>Página {page} de {totalPages}</span><Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div>}
    </>}
    {originId && <AccountingDocumentDialog documentId={originId} onClose={() => setOriginId(null)} />}
  </section>;
}
