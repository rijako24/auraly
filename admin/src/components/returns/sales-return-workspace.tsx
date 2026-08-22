"use client";

import { useMemo, useRef, useState } from "react";
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
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useConfirmSalesReturn, useReturnableSales } from "@/hooks/use-sales-returns";
import { calculateSalesReturnSelection } from "./sales-return-calculation";
import { salesReturnsApi, type ReturnableSale, type ReturnableSaleListItem, type SalesReturnDisposition, type SalesReturnResolution } from "@/services/api/sales-returns";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { inventoryApi } from "@/services/api/inventory";

export function SalesReturnWorkspace({ embedded = false }: { embedded?: boolean }) {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canCreate = permissions.has("sales.returns.create");
  const canConfirm = permissions.has("sales.returns.confirm");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [onlyAvailable, setOnlyAvailable] = useState(true);
  const [selected, setSelected] = useState<ReturnableSale>();
  const searchInput = useRef<HTMLInputElement>(null);
  const list = useReturnableSales({
    page, pageSize, search: search.trim() || undefined,
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
    {!embedded && <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end"><div><p className="text-sm font-medium text-primary">Ventas</p><h1 className="text-3xl font-semibold tracking-tight">Devoluciones de venta</h1><p className="mt-1 max-w-3xl text-muted-foreground">Busca la factura original. Auraly conserva sus precios e impuestos y compensa inventario, efectivo o cartera sin modificar la venta.</p></div><div className="flex flex-wrap items-center gap-2"><Badge className="w-fit" variant="outline"><ShieldCheck className="mr-2 h-4 w-4" /> Documento compensatorio DVT</Badge><Button disabled={!canCreate} onClick={() => { setOnlyAvailable(true); setSearch(""); window.requestAnimationFrame(() => searchInput.current?.focus()); }}><RotateCcw className="mr-2 h-4 w-4"/>Nueva devolución de venta</Button></div></header>}
    {!embedded && <section className="grid gap-3 md:grid-cols-3"><Summary icon={ReceiptText} label="Facturas encontradas" value={String(list.data?.totalCount ?? 0)} /><Summary icon={PackageCheck} label="Inventario" value="Reingreso controlado" /><Summary icon={HandCoins} label="Resolución" value="Efectivo o saldo cliente" /></section>}
    <section className="flex flex-col gap-3 rounded-2xl border bg-card p-4 md:flex-row">
      <div className="relative min-w-0 flex-1"><Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" /><Input ref={searchInput} className="pl-9" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Busca la factura que vas a devolver" /></div>
      <Button variant={onlyAvailable ? "secondary" : "outline"} onClick={() => { setOnlyAvailable((value) => !value); setPage(1); }}>Solo con saldo</Button>
    </section>
    <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading} page={list.data?.page} pageSize={list.data?.pageSize} pageCount={list.data?.totalPages} totalItems={list.data?.totalCount} enableRowSelection={false} onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }} onRowClick={canCreate ? open : undefined} />
    <SalesReturnEditor key={selected?.documentId ?? "none"} sale={selected} open={!!selected} canConfirm={canConfirm} onClose={() => setSelected(undefined)} />
  </div>;
}

function SalesReturnEditor({ sale, open, canConfirm, onClose }: { sale?: ReturnableSale; open: boolean; canConfirm: boolean; onClose: () => void }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const confirm = useConfirmSalesReturn();
  const reasonsQuery = useQuery({queryKey:["business-reasons","SalesReturn"],queryFn:()=>inventoryApi.businessReasons("SalesReturn"),enabled:open});
  const [reasonCode, setReasonCode] = useState("");
  const [reasonDescription, setReasonDescription] = useState("");
  const [notes, setNotes] = useState("");
  const [resolution, setResolution] = useState<SalesReturnResolution>(sale?.customerId ? "CustomerCredit" : "Refund");
  const [paymentNumber, setPaymentNumber] = useState("");
  const [quantities, setQuantities] = useState<Record<number, number>>({});
  const [dispositions, setDispositions] = useState<Record<number, SalesReturnDisposition>>({});
  if (!sale || !businessId) return null;
  const selection = calculateSalesReturnSelection(sale.lines, quantities);
  const selectedLineNumbers = new Set(selection.selectedLineNumbers);
  const chosen = sale.lines.filter((line) => selectedLineNumbers.has(line.originalLineNumber));
  const estimated = selection.estimatedTotal;
  const cashPayments = sale.payments.filter((payment) => payment.methodCode === "Cash" && payment.availableAmount > 0);

  const submit = async () => {
    if (!reasonCode) { toast.error("Selecciona un motivo de devolución."); return; }
    if (!reasonDescription.trim()) { toast.error("Describe brevemente el motivo de la devolución."); return; }
    if (chosen.length === 0) { toast.error("Indica al menos una cantidad por devolver."); return; }
    if (!selection.isValid) { toast.error("Una cantidad supera el saldo disponible."); return; }
    if (resolution === "CustomerCredit" && !sale.customerId) { toast.error("El saldo a favor requiere un cliente identificado."); return; }
    let workSessionId: string | null = null;
    let originalPaymentNumber: number | null = null;
    if (resolution === "Refund") {
      originalPaymentNumber = Number(paymentNumber);
      if (!cashPayments.some((payment) => payment.paymentNumber === originalPaymentNumber)) { toast.error("Selecciona un pago en efectivo con saldo disponible."); return; }
      try { workSessionId = (await salesReturnsApi.openWorkSession(businessId, sale.warehouseId)).workSessionId; }
      catch { toast.error("No fue posible abrir la sesión del usuario en la bodega de la venta."); return; }
    }
    try {
      const result = await confirm.mutateAsync({
        returnId: crypto.randomUUID(), businessId, warehouseId: sale.warehouseId,
        originalDocumentId: sale.documentId, returnedAt: new Date().toISOString(),
        economicResolution: resolution, refundMethodCode: resolution === "Refund" ? "Cash" : null,
        reasonDescription: reasonDescription.trim(), reasonCode, notes: notes.trim() || null,
        workSessionId, originalPaymentNumber,
        lines: chosen.map((line) => ({ originalLineNumber: line.originalLineNumber, quantity: quantities[line.originalLineNumber], inventoryDisposition: dispositions[line.originalLineNumber] ?? "Sellable" })),
      });
      toast.success(`Devolución ${result.documentNumber} aceptada por el motor documental.`);
      onClose();
    } catch { toast.error("No fue posible confirmar. La disponibilidad o el pago original pudieron cambiar."); }
  };

  return <Dialog open={open} onOpenChange={(value) => { if (!value) onClose(); }}><DialogContent className="max-h-[94dvh] max-w-6xl overflow-y-auto"><DialogHeader><DialogTitle className="flex items-center gap-2"><RotateCcw className="h-5 w-5 text-primary" /> Nueva devolución</DialogTitle><DialogDescription>{sale.documentNumber} · DIAN {sale.fiscalNumber} · {sale.customerName} · {sale.warehouseName}</DialogDescription></DialogHeader>
    <div className="overflow-hidden rounded-2xl border"><div className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem_12rem] gap-3 bg-muted/60 px-4 py-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground"><span>Producto</span><span>Vendido</span><span>Disponible</span><span>Devolver</span><span>Destino</span></div>{sale.lines.map((line) => <div key={line.originalLineNumber} className="grid grid-cols-[minmax(0,1fr)_7rem_7rem_8rem_12rem] items-center gap-3 border-t px-4 py-3"><div><p className="font-medium">{line.description}</p><p className="text-xs text-muted-foreground">{line.productCode}{line.reference ? ` · ${line.reference}` : ""} · {formatCurrency(line.unitPrice)} · IVA {line.taxRate}%</p></div><span>{line.soldQuantity}</span><span className="font-semibold text-primary">{line.availableQuantity}</span><Input type="number" min={0} max={line.availableQuantity} step="0.000001" disabled={line.availableQuantity <= 0} value={quantities[line.originalLineNumber] ?? ""} onChange={(event) => setQuantities((current) => ({ ...current, [line.originalLineNumber]: Math.max(0, Number(event.target.value)) }))} /><Select value={dispositions[line.originalLineNumber] ?? "Sellable"} onValueChange={(value) => setDispositions((current) => ({ ...current, [line.originalLineNumber]: value as SalesReturnDisposition }))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Sellable">Vuelve a inventario</SelectItem><SelectItem value="NotReturned">Sin retorno físico</SelectItem></SelectContent></Select></div>)}</div>
    <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)_18rem]"><div className="space-y-2"><Label>Motivo</Label><Select value={reasonCode} onValueChange={setReasonCode}><SelectTrigger><SelectValue placeholder="Selecciona un motivo" /></SelectTrigger><SelectContent>{(reasonsQuery.data??[]).map(item => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent></Select></div><div className="space-y-2"><Label>Descripción del motivo</Label><Input maxLength={300} value={reasonDescription} onChange={(event) => setReasonDescription(event.target.value)} placeholder="Qué ocurrió y por qué se devuelve" /></div><Card className="border-primary/20 bg-primary/5"><CardContent className="p-4"><p className="text-xs uppercase tracking-wide text-muted-foreground">Valor estimado</p><p className="text-2xl font-semibold">{formatCurrency(estimated)}</p><p className="text-xs text-muted-foreground">El servidor conserva el redondeo original.</p></CardContent></Card></div>
    <div className="grid gap-4 lg:grid-cols-3"><div className="space-y-2"><Label>Resolución económica</Label><Select value={resolution} onValueChange={(value) => setResolution(value as SalesReturnResolution)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{sale.customerId && <SelectItem value="CustomerCredit">Aplicar a cartera / saldo a favor</SelectItem>}<SelectItem value="Refund">Devolver efectivo</SelectItem></SelectContent></Select></div>{resolution === "Refund" && <div className="space-y-2"><Label>Pago original en efectivo</Label><Select value={paymentNumber} onValueChange={setPaymentNumber}><SelectTrigger><SelectValue placeholder="Selecciona el pago" /></SelectTrigger><SelectContent>{cashPayments.map((payment) => <SelectItem key={payment.paymentNumber} value={String(payment.paymentNumber)}>Pago {payment.paymentNumber} · disponible {formatCurrency(payment.availableAmount)}</SelectItem>)}</SelectContent></Select></div>}<div className="space-y-2"><Label>Notas internas</Label><Textarea maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} /></div></div>
    <DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button disabled={!canConfirm || !reasonCode || chosen.length === 0 || confirm.isPending} onClick={submit}><RotateCcw className="mr-2 h-4 w-4" /> {confirm.isPending ? "Confirmando..." : "Confirmar devolución"}</Button></DialogFooter>
  </DialogContent></Dialog>;
}

function Summary({ icon: Icon, label, value }: { icon: typeof ReceiptText; label: string; value: string }) { return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>; }

