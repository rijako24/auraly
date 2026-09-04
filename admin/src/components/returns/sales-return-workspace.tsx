"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { HandCoins, Landmark, PackageCheck, ReceiptText, RotateCcw, Search, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useConfirmSalesReturn, useReturnableSales } from "@/hooks/use-sales-returns";
import { calculateSalesReturnSelection } from "./sales-return-calculation";
import { salesReturnsApi, type ReturnableSale, type ReturnableSaleListItem, type SalesReturnRefundMethod, type SalesReturnResolution, type SalesReturnScope } from "@/services/api/sales-returns";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { inventoryApi } from "@/services/api/inventory";
import { useReferenceOptions } from "@/hooks/use-reference-options";

export function SalesReturnWorkspace({ embedded = false, onCashRefundConfirmed }: { embedded?: boolean; onCashRefundConfirmed?: () => void | Promise<void> }) {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canCreate = permissions.has("sales.returns.create");
  const canConfirm = permissions.has("sales.returns.confirm");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [customer, setCustomer] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [onlyAvailable, setOnlyAvailable] = useState(true);
  const [selected, setSelected] = useState<ReturnableSale>();
  const list = useReturnableSales({
    page, pageSize, search: search.trim() || undefined,
    customer: customer.trim() || undefined,
    from: from || undefined, to: to || undefined,
    withAvailableQuantity: onlyAvailable || undefined,
  });

  const columns = useMemo<ColumnDef<ReturnableSaleListItem>[]>(() => [
    { accessorKey: "documentNumber", header: "Factura", cell: ({ row }) => <div><p className="font-semibold">{row.original.documentNumber}</p><p className="text-xs text-muted-foreground">DIAN {row.original.fiscalNumber}</p></div> },
    { accessorKey: "customerName", header: "Cliente", cell: ({ row }) => <div><p>{row.original.customerName}</p><p className="text-xs text-muted-foreground">{row.original.customerIdentification}</p></div> },
    { accessorKey: "warehouseName", header: "Bodega" },
    { accessorKey: "totalAmount", header: "Venta / devuelto", cell: ({ row }) => <div><p className="font-medium">{formatCurrency(row.original.totalAmount)}</p><p className="text-xs text-muted-foreground">Devuelto {formatCurrency(row.original.returnedAmount)}</p></div> },
    { accessorKey: "issuedAt", header: "Fecha", cell: ({ row }) => formatDateTime(row.original.issuedAt) },
    { accessorKey: "hasAvailableQuantity", header: "Estado", cell: ({ row }) => row.original.hasAvailableQuantity ? <Badge variant="secondary">Disponible</Badge> : <Badge variant="outline">Sin saldo</Badge> },
  ], []);

  const open = async (item: ReturnableSaleListItem) => {
    if (!item.hasAvailableQuantity) { toast.info("La factura ya no tiene cantidades disponibles para devolver."); return; }
    try { setSelected(await salesReturnsApi.getSale(item.documentId)); }
    catch { toast.error("No fue posible consultar el snapshot de la factura."); }
  };

  return <div className={embedded ? "space-y-4" : "space-y-6"}>
    {!embedded && <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end"><div><p className="text-sm font-medium text-primary">Ventas</p><h1 className="text-3xl font-semibold tracking-tight">Devoluciones de venta</h1><p className="mt-1 max-w-3xl text-muted-foreground">Busca la factura original. Auraly conserva sus precios e impuestos y compensa inventario, efectivo o cartera sin modificar la venta.</p></div><Badge className="w-fit" variant="outline"><ShieldCheck className="mr-2 h-4 w-4" /> Documento compensatorio DVT</Badge></header>}
    {!embedded && <section className="grid gap-3 md:grid-cols-3"><Summary icon={ReceiptText} label="Facturas encontradas" value={String(list.data?.totalCount ?? 0)} /><Summary icon={PackageCheck} label="Inventario" value="Reingreso obligatorio" /><Summary icon={HandCoins} label="Cómo devolver" value="Efectivo, cartera, banco o tarjeta" /></section>}
    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-2 xl:grid-cols-[minmax(15rem,1fr)_minmax(13rem,.8fr)_11rem_11rem_auto] md:items-end">
      <div className="relative min-w-0"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Factura, CUFE o producto" /></div>
      <div className="space-y-2"><Label>Cliente</Label><Input value={customer} onChange={(event) => { setCustomer(event.target.value); setPage(1); }} placeholder="Nombre o identificación" /></div>
      <div className="space-y-2"><Label>Desde</Label><DatePicker value={from} onChange={(value) => { setFrom(value); setPage(1); }} /></div>
      <div className="space-y-2"><Label>Hasta</Label><DatePicker value={to} onChange={(value) => { setTo(value); setPage(1); }} /></div>
      <Button variant={onlyAvailable ? "secondary" : "outline"} onClick={() => { setOnlyAvailable((value) => !value); setPage(1); }}>Solo con saldo</Button>
    </section>
    <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading} page={list.data?.page} pageSize={list.data?.pageSize} pageCount={list.data?.totalPages} totalItems={list.data?.totalCount} enableRowSelection={false} onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }} onRowClick={canCreate ? open : undefined} />
    <SalesReturnEditor key={selected?.documentId ?? "none"} sale={selected} open={!!selected} canConfirm={canConfirm} onCashRefundConfirmed={onCashRefundConfirmed} onClose={() => setSelected(undefined)} />
  </div>;
}

function SalesReturnEditor({ sale, open, canConfirm, onCashRefundConfirmed, onClose }: { sale?: ReturnableSale; open: boolean; canConfirm: boolean; onCashRefundConfirmed?: () => void | Promise<void>; onClose: () => void }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const confirm = useConfirmSalesReturn();
  const reasonsQuery = useQuery({queryKey:["business-reasons","SalesReturn"],queryFn:()=>inventoryApi.businessReasons("SalesReturn"),enabled:open});
  const [reasonCode, setReasonCode] = useState("");
  const [notes, setNotes] = useState("");
  const resolutionMethods = useReferenceOptions("sales-return-resolution-method", open);
  const settlementConfiguration = useQuery({queryKey:["sales-settlement-configuration"],queryFn:salesReturnsApi.settlementConfiguration,enabled:open});
  const returnScopes = useReferenceOptions("sales-return-scope", open);
  const [returnScopeCode, setReturnScopeCode] = useState<SalesReturnScope>("Partial");
  const [lineSearch, setLineSearch] = useState("");
  const [resolutionMethod, setResolutionMethod] = useState<string>(sale?.receivableOutstanding ? "CustomerCredit" : "Cash");
  const [originalPaymentNumber, setOriginalPaymentNumber] = useState("");
  const [bankAccountId, setBankAccountId] = useState("");
  const [settlementReference, setSettlementReference] = useState("");
  const [settlementNotes, setSettlementNotes] = useState("");
  const [transferDialogOpen, setTransferDialogOpen] = useState(false);
  const [quantities, setQuantities] = useState<Record<number, number>>({});
  if (!sale || !businessId) return null;
  const selection = calculateSalesReturnSelection(sale.lines, quantities);
  const selectedLineNumbers = new Set(selection.selectedLineNumbers);
  const chosen = sale.lines.filter((line) => selectedLineNumbers.has(line.originalLineNumber));
  const estimated = selection.estimatedTotal;
  const economicResolution: SalesReturnResolution = resolutionMethod === "CustomerCredit" ? "CustomerCredit" : "Refund";
  const reversibleCardMethods = new Set(sale.payments
    .filter(payment => ["DebitCard","CreditCard"].includes(payment.methodCode) && payment.availableAmount > 0)
    .map(payment => payment.methodCode));
  const availableMethods = (resolutionMethods.data ?? []).filter((method) =>
    ["Cash", "CustomerCredit", "Transfer"].includes(method.code) || reversibleCardMethods.has(method.code));
  const cardRefund = resolutionMethod === "DebitCard" || resolutionMethod === "CreditCard";
  const transferRefund = resolutionMethod === "Transfer";
  const accountingEnabled = settlementConfiguration.data?.isAccountingEnabled ?? false;
  const bankAccounts = settlementConfiguration.data?.bankAccounts ?? [];
  const principalBankAccountId = bankAccounts.find(account => account.isPrimary)?.bankAccountId ?? "";
  const cardPayments = sale.payments.filter(payment =>
    payment.methodCode === resolutionMethod && payment.availableAmount > 0);
  const normalizedLineSearch = lineSearch.trim().toLocaleLowerCase("es-CO");
  const visibleLines = normalizedLineSearch
    ? sale.lines.filter((line) => [line.description, line.productCode, line.reference ?? "", line.barcodes]
        .some((value) => value.toLocaleLowerCase("es-CO").includes(normalizedLineSearch)))
    : sale.lines;

  const changeReturnScope = (value: string) => {
    const scope = value as SalesReturnScope;
    setReturnScopeCode(scope);
    if (scope === "FullCancellation") {
      setQuantities(Object.fromEntries(sale.lines
        .filter((line) => line.availableQuantity > 0)
        .map((line) => [line.originalLineNumber, line.availableQuantity])));
    } else {
      setQuantities({});
    }
  };

  const changeResolutionMethod = (value: string) => {
    setOriginalPaymentNumber("");
    setBankAccountId("");
    setSettlementReference("");
    setSettlementNotes("");
    if (value === "Transfer") {
      if (settlementConfiguration.isError || !settlementConfiguration.data) {
        toast.error("No fue posible consultar la configuración de transferencias.");
        return;
      }
      if (settlementConfiguration.data.isAccountingEnabled && bankAccounts.length === 0) {
        toast.error("Configura una cuenta bancaria activa en Contabilidad antes de devolver por transferencia.");
        return;
      }
      setBankAccountId(principalBankAccountId);
      setTransferDialogOpen(true);
    }
    setResolutionMethod(value);
  };

  const submit = async () => {
    if (!reasonCode) { toast.error("Selecciona un motivo de devolución."); return; }
    if (chosen.length === 0) { toast.error("Indica al menos una cantidad por devolver."); return; }
    if (!selection.isValid) { toast.error("Una cantidad supera el saldo disponible."); return; }
    if (economicResolution === "CustomerCredit" && (!sale.customerId || sale.receivableOutstanding <= 0 || estimated > sale.receivableOutstanding)) { toast.error("El abono no puede superar el saldo pendiente de la cuenta por cobrar."); return; }
    let workSessionId: string | null = null;
    if (cardRefund && !originalPaymentNumber) { toast.error("Selecciona la transacción de tarjeta que se va a reversar."); return; }
    if (cardRefund && estimated > (cardPayments.find(payment => payment.paymentNumber === Number(originalPaymentNumber))?.availableAmount ?? 0)) { toast.error("El valor supera el saldo de la transacción seleccionada."); return; }
    const selectedBankAccountId = bankAccountId || principalBankAccountId;
    if (transferRefund && !settlementReference.trim()) { toast.error("Registra la referencia de la transferencia."); setTransferDialogOpen(true); return; }
    if (transferRefund && accountingEnabled && !selectedBankAccountId) { toast.error("Configura o selecciona la cuenta bancaria de salida."); return; }
    if (economicResolution === "Refund") {
      try { workSessionId = (await salesReturnsApi.openWorkSession(businessId)).workSessionId; }
      catch { toast.error("No fue posible abrir la sesión operativa del usuario para registrar la devolución."); return; }
    }
    try {
      const result = await confirm.mutateAsync({
        returnId: crypto.randomUUID(), businessId, warehouseId: sale.warehouseId,
        originalDocumentId: sale.documentId, returnedAt: new Date().toISOString(),
        returnScopeCode,
        economicResolution, refundMethodCode: economicResolution === "Refund" ? resolutionMethod as SalesReturnRefundMethod : null,
        reasonDescription: reasonsQuery.data?.find(reason => reason.code === reasonCode)?.name ?? reasonCode,
        reasonCode, notes: notes.trim() || null,
        workSessionId, originalPaymentNumber: cardRefund ? Number(originalPaymentNumber) : null,
        bankAccountId: transferRefund && accountingEnabled ? selectedBankAccountId : null,
        settlementReference: transferRefund ? settlementReference.trim() : null,
        settlementNotes: transferRefund ? settlementNotes.trim() || null : null,
        lines: chosen.map((line) => ({ originalLineNumber: line.originalLineNumber, quantity: quantities[line.originalLineNumber], inventoryDisposition: "Sellable" as const })),
      });
      toast.success(`Devolución ${result.documentNumber} aceptada por el motor documental.`);
      if (resolutionMethod === "Cash") await onCashRefundConfirmed?.();
      onClose();
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible confirmar la devolución."); }
  };

  return <Dialog open={open} onOpenChange={(value) => { if (!value) onClose(); }}>
    <DialogContent className="flex max-h-[94dvh] w-[96vw] max-w-6xl flex-col overflow-hidden p-0">
      <DialogHeader className="shrink-0 border-b px-6 py-5">
        <DialogTitle className="flex items-center gap-2"><RotateCcw className="h-5 w-5 text-primary" /> Nueva devolución</DialogTitle>
        <DialogDescription>{sale.documentNumber} · DIAN {sale.fiscalNumber} · {sale.customerName} · {sale.warehouseName}</DialogDescription>
      </DialogHeader>
      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5">
        <section className="grid gap-4 rounded-2xl border bg-muted/20 p-4 md:grid-cols-2 xl:grid-cols-4">
          <Field label="Motivo"><Select value={reasonCode} onValueChange={setReasonCode}><SelectTrigger><SelectValue placeholder="Selecciona un motivo" /></SelectTrigger><SelectContent>{(reasonsQuery.data??[]).map(item => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Cómo devolver el valor"><Select value={resolutionMethod} onValueChange={changeResolutionMethod}><SelectTrigger><SelectValue placeholder={resolutionMethods.isLoading ? "Cargando opciones…" : "Selecciona"} /></SelectTrigger><SelectContent>{availableMethods.map(method => <SelectItem key={method.id} value={method.code} disabled={method.code === "CustomerCredit" && sale.receivableOutstanding <= 0}>{method.label}{method.code === "CustomerCredit" && sale.receivableOutstanding <= 0 ? " · sin saldo" : ""}</SelectItem>)}</SelectContent></Select>{resolutionMethod === "CustomerCredit" && <p className="text-xs text-muted-foreground">Máximo disponible para abonar: {formatCurrency(sale.receivableOutstanding)}. Si la devolución es mayor, registra operaciones separadas.</p>}</Field>
          {cardRefund && <Field label="Pago de tarjeta por reversar"><Select value={originalPaymentNumber} onValueChange={setOriginalPaymentNumber}><SelectTrigger><SelectValue placeholder="Selecciona la transacción original" /></SelectTrigger><SelectContent>{cardPayments.map(payment => <SelectItem key={payment.paymentNumber} value={String(payment.paymentNumber)}>{payment.cardFranchiseCode ?? payment.methodCode} · {payment.approvalNumber ?? `pago ${payment.paymentNumber}`} · disponible {formatCurrency(payment.availableAmount)}</SelectItem>)}</SelectContent></Select></Field>}
          {transferRefund && <Field label="Transferencia"><Button type="button" variant="outline" className="w-full justify-start" onClick={() => setTransferDialogOpen(true)}><Landmark className="mr-2 h-4 w-4"/>{settlementReference ? `${accountingEnabled ? `${bankAccounts.find(account => account.bankAccountId === (bankAccountId || principalBankAccountId))?.displayName ?? "Cuenta"} · ` : ""}${settlementReference}` : "Registrar cuenta y soporte"}</Button></Field>}
          <Field label="Alcance"><Select value={returnScopeCode} onValueChange={changeReturnScope}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{(returnScopes.data ?? []).map((scope) => <SelectItem key={scope.id} value={scope.code}>{scope.label}</SelectItem>)}</SelectContent></Select></Field>
          <Card className="border-primary/20 bg-primary/5 md:col-span-1"><CardContent className="p-4"><p className="text-xs uppercase tracking-wide text-muted-foreground">Valor estimado</p><p className="text-2xl font-semibold">{formatCurrency(estimated)}</p><p className="text-xs text-muted-foreground">El servidor conserva el redondeo original.</p></CardContent></Card>
        </section>
        <Field label="Buscar producto en la factura"><div className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={lineSearch} onChange={(event) => setLineSearch(event.target.value)} placeholder="Nombre, código, referencia o código de barras" /></div></Field>
        <div className="overflow-hidden rounded-2xl border"><div className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem] gap-3 bg-muted/60 px-4 py-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground"><span>Producto</span><span>Vendido</span><span>Disponible</span><span>Devolver</span></div>{visibleLines.map((line) => <div key={line.originalLineNumber} className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem] items-center gap-3 border-t px-4 py-3"><div><p className="font-medium">{line.description}</p><p className="text-xs text-muted-foreground">{line.productCode}{line.reference ? ` · ${line.reference}` : ""} · {formatCurrency(line.unitPrice)} · IVA {line.taxRate}%</p></div><span>{line.soldQuantity}</span><span className="font-semibold text-primary">{line.availableQuantity}</span><Input type="number" min={0} max={line.availableQuantity} step="0.000001" disabled={returnScopeCode === "FullCancellation" || line.availableQuantity <= 0} value={quantities[line.originalLineNumber] ?? ""} onChange={(event) => setQuantities((current) => ({ ...current, [line.originalLineNumber]: Math.max(0, Number(event.target.value)) }))} /></div>)}{visibleLines.length === 0 && <p className="border-t p-8 text-center text-sm text-muted-foreground">No hay productos de esta factura que coincidan con la búsqueda.</p>}</div>
        <Field label="Notas internas (opcional)"><Textarea className="min-h-24" maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} /></Field>
      </div>
      <DialogFooter className="shrink-0 border-t bg-background px-6 py-4"><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!canConfirm || !reasonCode || chosen.length === 0 || confirm.isPending} onClick={submit}><RotateCcw className="mr-2 h-4 w-4" /> {confirm.isPending ? "Confirmando..." : "Confirmar devolución"}</Button></DialogFooter>
    </DialogContent>
    <Dialog open={transferDialogOpen} onOpenChange={setTransferDialogOpen}>
      <DialogContent className="max-w-md">
        <DialogHeader><DialogTitle>Datos de la transferencia</DialogTitle><DialogDescription>La cuenta principal es solo el valor inicial; puedes cambiarla para esta devolución.</DialogDescription></DialogHeader>
        <div className="space-y-4">
          {accountingEnabled && <Field label="Cuenta bancaria de salida"><Select value={bankAccountId || principalBankAccountId} onValueChange={setBankAccountId}><SelectTrigger><SelectValue placeholder="Selecciona una cuenta" /></SelectTrigger><SelectContent>{bankAccounts.map(account => <SelectItem key={account.bankAccountId} value={account.bankAccountId}>{account.displayName} · {account.bankName} · {account.accountNumber}</SelectItem>)}</SelectContent></Select></Field>}
          <Field label="Referencia"><Input autoFocus value={settlementReference} maxLength={160} onChange={event => setSettlementReference(event.target.value)} placeholder="Número o referencia del comprobante" /></Field>
          <Field label="Nota (opcional)"><Textarea value={settlementNotes} maxLength={500} onChange={event => setSettlementNotes(event.target.value)} placeholder="Detalle útil para identificar la transferencia" /></Field>
        </div>
        <DialogFooter><Button type="button" disabled={!settlementReference.trim() || (accountingEnabled && !(bankAccountId || principalBankAccountId))} onClick={() => { if (!bankAccountId && principalBankAccountId) setBankAccountId(principalBankAccountId); setTransferDialogOpen(false); }}>Guardar transferencia</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </Dialog>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }

function Summary({ icon: Icon, label, value }: { icon: typeof ReceiptText; label: string; value: string }) { return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>; }

