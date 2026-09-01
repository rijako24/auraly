"use client";

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { HandCoins, PackageCheck, ReceiptText, RotateCcw, Search, ShieldCheck } from "lucide-react";
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
    {!embedded && <section className="grid gap-3 md:grid-cols-3"><Summary icon={ReceiptText} label="Facturas encontradas" value={String(list.data?.totalCount ?? 0)} /><Summary icon={PackageCheck} label="Inventario" value="Reingreso obligatorio" /><Summary icon={HandCoins} label="Resolución" value="Cartera o medio original" /></section>}
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
  const [reasonDescription, setReasonDescription] = useState("");
  const [notes, setNotes] = useState("");
  const resolutionMethods = useReferenceOptions("sales-return-resolution-method", open);
  const returnScopes = useReferenceOptions("sales-return-scope", open);
  const [returnScopeCode, setReturnScopeCode] = useState<SalesReturnScope>("Partial");
  const [lineSearch, setLineSearch] = useState("");
  const [resolutionMethod, setResolutionMethod] = useState<string>(sale?.receivableOutstanding ? "CustomerCredit" : "Cash");
  const [paymentNumber, setPaymentNumber] = useState("");
  const [quantities, setQuantities] = useState<Record<number, number>>({});
  useEffect(() => {
    if (!sale || resolutionMethod === "CustomerCredit") { setPaymentNumber(""); return; }
    const candidates = sale.payments.filter(payment =>
      payment.methodCode === resolutionMethod && payment.availableAmount > 0);
    setPaymentNumber(current => candidates.some(payment => String(payment.paymentNumber) === current)
      ? current : candidates.length === 1 ? String(candidates[0].paymentNumber) : "");
  }, [resolutionMethod, sale]);
  if (!sale || !businessId) return null;
  const selection = calculateSalesReturnSelection(sale.lines, quantities);
  const selectedLineNumbers = new Set(selection.selectedLineNumbers);
  const chosen = sale.lines.filter((line) => selectedLineNumbers.has(line.originalLineNumber));
  const estimated = selection.estimatedTotal;
  const economicResolution: SalesReturnResolution = resolutionMethod === "CustomerCredit" ? "CustomerCredit" : "Refund";
  const paymentsForMethod = sale.payments.filter((payment) => payment.methodCode === resolutionMethod && payment.availableAmount > 0);
  const availableMethods = (resolutionMethods.data ?? []).filter((method) => method.code === "CustomerCredit"
    ? sale.receivableOutstanding > 0
    : sale.payments.some((payment) => payment.methodCode === method.code && payment.availableAmount > 0));
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

  const submit = async () => {
    if (!reasonCode) { toast.error("Selecciona un motivo de devolución."); return; }
    if (!reasonDescription.trim()) { toast.error("Describe brevemente el motivo de la devolución."); return; }
    if (chosen.length === 0) { toast.error("Indica al menos una cantidad por devolver."); return; }
    if (!selection.isValid) { toast.error("Una cantidad supera el saldo disponible."); return; }
    if (economicResolution === "CustomerCredit" && (!sale.customerId || sale.receivableOutstanding <= 0 || estimated > sale.receivableOutstanding)) { toast.error("El abono no puede superar el saldo pendiente de la cuenta por cobrar."); return; }
    let workSessionId: string | null = null;
    let originalPaymentNumber: number | null = null;
    if (economicResolution === "Refund") {
      originalPaymentNumber = Number(paymentNumber);
      if (!paymentsForMethod.some((payment) => payment.paymentNumber === originalPaymentNumber)) { toast.error("Selecciona el pago original del mismo medio con saldo disponible."); return; }
      if (resolutionMethod === "Cash") {
        try { workSessionId = (await salesReturnsApi.openWorkSession(businessId, sale.warehouseId)).workSessionId; }
        catch { toast.error("No fue posible abrir la sesión del usuario en la bodega de la venta."); return; }
      }
    }
    try {
      const result = await confirm.mutateAsync({
        returnId: crypto.randomUUID(), businessId, warehouseId: sale.warehouseId,
        originalDocumentId: sale.documentId, returnedAt: new Date().toISOString(),
        returnScopeCode,
        economicResolution, refundMethodCode: economicResolution === "Refund" ? resolutionMethod as SalesReturnRefundMethod : null,
        reasonDescription: reasonDescription.trim(), reasonCode, notes: notes.trim() || null,
        workSessionId, originalPaymentNumber,
        lines: chosen.map((line) => ({ originalLineNumber: line.originalLineNumber, quantity: quantities[line.originalLineNumber], inventoryDisposition: "Sellable" as const })),
      });
      toast.success(`Devolución ${result.documentNumber} aceptada por el motor documental.`);
      if (resolutionMethod === "Cash") await onCashRefundConfirmed?.();
      onClose();
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible confirmar la devolución."); }
  };

  return <Dialog open={open} onOpenChange={(value) => { if (!value) onClose(); }}><DialogContent className="max-h-[94dvh] max-w-6xl overflow-y-auto"><DialogHeader><DialogTitle className="flex items-center gap-2"><RotateCcw className="h-5 w-5 text-primary" /> Nueva devolución</DialogTitle><DialogDescription>{sale.documentNumber} · DIAN {sale.fiscalNumber} · {sale.customerName} · {sale.warehouseName}</DialogDescription></DialogHeader>
    <div className="grid gap-3 md:grid-cols-[18rem_minmax(0,1fr)]"><div className="space-y-2"><Label>Alcance</Label><Select value={returnScopeCode} onValueChange={changeReturnScope}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{(returnScopes.data ?? []).map((scope) => <SelectItem key={scope.id} value={scope.code}>{scope.label}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Buscar producto en la factura</Label><div className="relative"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input className="pl-9" value={lineSearch} onChange={(event) => setLineSearch(event.target.value)} placeholder="Nombre, código, referencia o código de barras" /></div></div></div>
    <div className="overflow-hidden rounded-2xl border"><div className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem] gap-3 bg-muted/60 px-4 py-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground"><span>Producto</span><span>Vendido</span><span>Disponible</span><span>Devolver</span></div>{visibleLines.map((line) => <div key={line.originalLineNumber} className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem] items-center gap-3 border-t px-4 py-3"><div><p className="font-medium">{line.description}</p><p className="text-xs text-muted-foreground">{line.productCode}{line.reference ? ` · ${line.reference}` : ""} · {formatCurrency(line.unitPrice)} · IVA {line.taxRate}%</p></div><span>{line.soldQuantity}</span><span className="font-semibold text-primary">{line.availableQuantity}</span><Input type="number" min={0} max={line.availableQuantity} step="0.000001" disabled={returnScopeCode === "FullCancellation" || line.availableQuantity <= 0} value={quantities[line.originalLineNumber] ?? ""} onChange={(event) => setQuantities((current) => ({ ...current, [line.originalLineNumber]: Math.max(0, Number(event.target.value)) }))} /></div>)}{visibleLines.length === 0 && <p className="border-t p-8 text-center text-sm text-muted-foreground">No hay productos de esta factura que coincidan con la búsqueda.</p>}</div>
    <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)_18rem]"><div className="space-y-2"><Label>Motivo</Label><Select value={reasonCode} onValueChange={setReasonCode}><SelectTrigger><SelectValue placeholder="Selecciona un motivo" /></SelectTrigger><SelectContent>{(reasonsQuery.data??[]).map(item => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Descripción del motivo</Label><Input maxLength={300} value={reasonDescription} onChange={(event) => setReasonDescription(event.target.value)} placeholder="Qué ocurrió y por qué se devuelve" /></div><Card className="border-primary/20 bg-primary/5"><CardContent className="p-4"><p className="text-xs uppercase tracking-wide text-muted-foreground">Valor estimado</p><p className="text-2xl font-semibold">{formatCurrency(estimated)}</p><p className="text-xs text-muted-foreground">El servidor conserva el redondeo original.</p></CardContent></Card></div>
    <div className="grid gap-4 lg:grid-cols-3"><div className="space-y-2"><Label>Cómo devolver el valor</Label><Select value={resolutionMethod} onValueChange={setResolutionMethod}><SelectTrigger><SelectValue placeholder={resolutionMethods.isLoading ? "Cargando opciones…" : "Selecciona"} /></SelectTrigger><SelectContent>{availableMethods.map(method => <SelectItem key={method.id} value={method.code}>{method.label}</SelectItem>)}</SelectContent></Select>{resolutionMethod === "CustomerCredit" && <p className="text-xs text-muted-foreground">Se abonará a la cuenta por cobrar. Saldo disponible: {formatCurrency(sale.receivableOutstanding)}</p>}</div>{economicResolution === "Refund" && paymentsForMethod.length > 1 && <div className="space-y-2"><Label>Pago de origen a reversar</Label><Select value={paymentNumber} onValueChange={setPaymentNumber}><SelectTrigger><SelectValue placeholder="Selecciona el pago exacto" /></SelectTrigger><SelectContent>{paymentsForMethod.map((payment) => <SelectItem key={payment.paymentNumber} value={String(payment.paymentNumber)}>Pago {payment.paymentNumber} · disponible {formatCurrency(payment.availableAmount)}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">La venta tuvo varios pagos del mismo medio; elige cuál se reversará.</p></div>}{economicResolution === "Refund" && paymentsForMethod.length === 1 && <div className="rounded-xl border bg-muted/30 p-3 text-sm"><span className="block text-xs text-muted-foreground">Pago de origen</span><strong>Pago {paymentsForMethod[0].paymentNumber} · {formatCurrency(paymentsForMethod[0].availableAmount)}</strong><p className="mt-1 text-xs text-muted-foreground">Se enlaza automáticamente para no devolver más de lo originalmente pagado.</p></div>}<div className="space-y-2"><Label>Notas internas</Label><Textarea maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} /></div></div>
    <DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!canConfirm || !reasonCode || chosen.length === 0 || confirm.isPending} onClick={submit}><RotateCcw className="mr-2 h-4 w-4" /> {confirm.isPending ? "Confirmando..." : "Confirmar devolución"}</Button></DialogFooter>
  </DialogContent></Dialog>;
}

function Summary({ icon: Icon, label, value }: { icon: typeof ReceiptText; label: string; value: string }) { return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>; }

