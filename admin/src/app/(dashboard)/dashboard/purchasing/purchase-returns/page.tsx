"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowDownLeft, PackageCheck, RotateCcw, Search, ShieldCheck, WalletCards } from "lucide-react";
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
import { useConfirmPurchaseReturn, useReturnableReceipts } from "@/hooks/use-purchase-returns";
import { purchaseReturnsApi, type ReturnableReceipt, type ReturnableReceiptListItem } from "@/services/api/purchase-returns";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { inventoryApi } from "@/services/api/inventory";

export default function PurchaseReturnsPage() {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canCreate = permissions.has("purchasing.purchase-returns.create");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [onlyAvailable, setOnlyAvailable] = useState(true);
  const [selected, setSelected] = useState<ReturnableReceipt>();
  const list = useReturnableReceipts({ page, pageSize, search: search.trim() || undefined,
    from: from || undefined, to: to || undefined,
    withAvailableQuantity: onlyAvailable || undefined });

  const columns = useMemo<ColumnDef<ReturnableReceiptListItem>[]>(() => [
    {
      accessorKey: "documentNumber", header: "Entrada",
      cell: ({ row }) => <div>
        <p className="font-semibold">{row.original.documentNumber}</p>
        <p className="text-xs text-muted-foreground">
          {row.original.supplierInvoiceNumber
            ? `Factura proveedor ${row.original.supplierInvoiceNumber}`
            : "Sin factura de proveedor"}
        </p>
      </div>,
    },
    { accessorKey: "supplierName", header: "Proveedor" },
    { accessorKey: "warehouseName", header: "Bodega" },
    {
      accessorKey: "grandTotal", header: "Recibido / devuelto",
      cell: ({ row }) => <div>
        <p className="font-medium">{formatCurrency(row.original.grandTotal)}</p>
        <p className="text-xs text-muted-foreground">
          Devuelto {formatCurrency(row.original.returnedTotal)}
        </p>
      </div>,
    },
    {
      accessorKey: "hasAvailableQuantity", header: "Disponibilidad",
      cell: ({ row }) => row.original.hasAvailableQuantity
        ? <Badge variant="secondary">Con saldo por devolver</Badge>
        : <Badge variant="outline">Devuelta completamente</Badge>,
    },
    {
      accessorKey: "receivedAt", header: "Recibida",
      cell: ({ row }) => formatDateTime(row.original.receivedAt),
    },
  ], []);

  const open = async (item: ReturnableReceiptListItem) => {
    if (!item.hasAvailableQuantity) {
      toast.info("Esta entrada ya no tiene cantidades disponibles para devolver.");
      return;
    }
    try { setSelected(await purchaseReturnsApi.getReceipt(item.goodsReceiptId)); }
    catch { toast.error("No fue posible consultar la entrada y sus cantidades disponibles."); }
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div>
        <p className="text-sm font-medium text-primary">Compras e inventario</p>
        <h1 className="text-3xl font-semibold tracking-tight">Devoluciones a proveedores</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Selecciona una entrada procesada. Auraly conserva el costo original y compensa
          inventario, cuenta por pagar y contabilidad sin alterar el documento recibido.
        </p>
      </div>
      <Badge className="w-fit" variant="outline"><ShieldCheck className="mr-2 h-4 w-4" /> Documento compensatorio DCP</Badge>
    </header>

    <section className="grid gap-3 md:grid-cols-3">
      <Summary icon={PackageCheck} label="Entradas encontradas" value={String(list.data?.totalCount ?? 0)} />
      <Summary icon={ArrowDownLeft} label="Efecto físico" value="Salida al costo original" />
      <Summary icon={WalletCards} label="Efecto financiero" value="CxP o saldo a favor" />
    </section>

    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-[minmax(16rem,1fr)_11rem_11rem_auto] md:items-end">
      <div className="relative min-w-0">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9" value={search} onChange={(event) => {
          setSearch(event.target.value); setPage(1);
        }} placeholder="Entrada, factura, proveedor, bodega o producto" />
      </div>
      <div className="space-y-2"><Label>Desde</Label><DatePicker value={from} onChange={(value) => { setFrom(value); setPage(1); }} /></div>
      <div className="space-y-2"><Label>Hasta</Label><DatePicker value={to} onChange={(value) => { setTo(value); setPage(1); }} /></div>
      <Button variant={onlyAvailable ? "secondary" : "outline"} onClick={() => { setOnlyAvailable((value) => !value); setPage(1); }}>Solo con saldo</Button>
    </section>

    <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading}
      page={list.data?.page} pageSize={list.data?.pageSize} pageCount={list.data?.totalPages}
      totalItems={list.data?.totalCount} enableRowSelection={false}
      onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }}
      onRowClick={canCreate ? open : undefined} />

    <ReturnEditor key={selected?.goodsReceiptId ?? "none"} receipt={selected} open={!!selected} canConfirm={permissions.has("purchasing.purchase-returns.confirm")}
      onClose={() => setSelected(undefined)} />
  </div>;
}

function ReturnEditor({ receipt, open, canConfirm, onClose }: {
  receipt?: ReturnableReceipt; open: boolean; canConfirm: boolean; onClose: () => void;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const confirm = useConfirmPurchaseReturn();
  const reasonsQuery = useQuery({queryKey:["business-reasons","PurchaseReturn"],queryFn:()=>inventoryApi.businessReasons("PurchaseReturn"),enabled:open});
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [quantities, setQuantities] = useState<Record<number, number>>({});
  if (!receipt || !businessId) return null;
  const chosen = receipt.lines.filter((line) => (quantities[line.originalLineNumber] ?? 0) > 0);
  const estimatedTotal = chosen.reduce((total, line) => total +
    line.lineTotal * (quantities[line.originalLineNumber] ?? 0) / line.receivedQuantity, 0);

  const submit = async () => {
    if (!reason) { toast.error("Selecciona un motivo de devolución."); return; }
    if (chosen.length === 0) { toast.error("Indica al menos una cantidad por devolver."); return; }
    if (chosen.some((line) => (quantities[line.originalLineNumber] ?? 0) > line.availableQuantity)) {
      toast.error("Una cantidad supera el saldo disponible de la entrada."); return;
    }
    try {
      const result = await confirm.mutateAsync({
        returnId: crypto.randomUUID(), businessId,
        originalGoodsReceiptId: receipt.goodsReceiptId,
        returnedAt: new Date().toISOString(), reasonCode: reason,
        notes: notes.trim() || null,
        lines: chosen.map((line) => ({
          originalLineNumber: line.originalLineNumber,
          quantity: quantities[line.originalLineNumber],
        })),
      });
      toast.success(`Devolución ${result.documentNumber} confirmada y procesada por el motor.`);
      onClose();
    } catch { toast.error("No fue posible confirmar. Revisa existencias y cantidades disponibles."); }
  };

  return <Dialog open={open} onOpenChange={(value) => { if (!value) onClose(); }}>
    <DialogContent className="max-h-[92vh] max-w-5xl overflow-y-auto">
      <DialogHeader>
        <DialogTitle className="flex items-center gap-2"><RotateCcw className="h-5 w-5 text-primary" /> Nueva devolución</DialogTitle>
        <DialogDescription>
          {receipt.documentNumber} · {receipt.supplierName} · {receipt.warehouseName}
        </DialogDescription>
      </DialogHeader>

      <div className="grid gap-4 rounded-2xl border bg-muted/30 p-4 md:grid-cols-3">
        <Info label="Factura del proveedor" value={receipt.supplierInvoiceNumber ?? "Sin referencia"} />
        <Info label="Fecha de entrada" value={formatDateTime(receipt.receivedAt)} />
        <Info label="Total recibido" value={formatCurrency(receipt.grandTotal)} />
      </div>

      <div className="flex justify-end">
        <Button type="button" variant="outline" size="sm" onClick={() =>
          setQuantities(Object.fromEntries(receipt.lines
            .filter((line) => line.availableQuantity > 0)
            .map((line) => [line.originalLineNumber, line.availableQuantity])))}>
          Devolver todo lo disponible
        </Button>
      </div>

      <div className="overflow-hidden rounded-2xl border">
        <div className="grid grid-cols-[minmax(0,1fr)_8rem_9rem_9rem] gap-3 bg-muted/60 px-4 py-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          <span>Producto</span><span>Recibido</span><span>Disponible</span><span>Devolver</span>
        </div>
        {receipt.lines.map((line) => <div key={line.originalLineNumber}
          className="grid grid-cols-[minmax(0,1fr)_8rem_9rem_9rem] items-center gap-3 border-t px-4 py-3">
          <div><p className="font-medium">{line.description}</p><p className="text-xs text-muted-foreground">Costo {formatCurrency(line.unitCost)} · Devuelto {line.returnedQuantity}</p></div>
          <span>{line.receivedQuantity}</span><span className="font-semibold text-primary">{line.availableQuantity}</span>
          <Input type="number" min={0} max={line.availableQuantity} step="0.000001"
            disabled={line.availableQuantity <= 0} value={quantities[line.originalLineNumber] ?? ""}
            onChange={(event) => setQuantities((current) => ({ ...current,
              [line.originalLineNumber]: Math.max(0, Number(event.target.value)) }))} />
        </div>)}
      </div>

      <div className="grid gap-4 md:grid-cols-[18rem_minmax(0,1fr)_14rem] md:items-end">
        <div className="space-y-2"><Label>Motivo</Label><Select value={reason} onValueChange={setReason}>
          <SelectTrigger><SelectValue /></SelectTrigger><SelectContent>
            {(reasonsQuery.data??[]).map(item => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}
          </SelectContent></Select></div>
        <div className="space-y-2"><Label>Observación</Label><Textarea value={notes}
          onChange={(event) => setNotes(event.target.value)} placeholder="Evidencia o acuerdo con el proveedor" /></div>
        <Card className="border-primary/20 bg-primary/5"><CardContent className="p-4">
          <p className="text-xs uppercase tracking-wide text-muted-foreground">Total estimado</p>
          <p className="text-2xl font-semibold">{formatCurrency(estimatedTotal)}</p>
          <p className="text-xs text-muted-foreground">El servidor conserva el redondeo original.</p>
        </CardContent></Card>
      </div>

      <DialogFooter>
        <Button variant="outline" onClick={onClose}>Cancelar</Button>
        <Button disabled={!canConfirm || !reason || chosen.length === 0 || confirm.isPending} onClick={submit}>
          <RotateCcw className="mr-2 h-4 w-4" /> Confirmar devolución
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function Summary({ icon: Icon, label, value }: { icon: typeof PackageCheck; label: string; value: string }) {
  return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>;
}
function Info({ label, value }: { label: string; value: string }) {
  return <div><p className="text-xs text-muted-foreground">{label}</p><p className="font-medium">{value}</p></div>;
}
