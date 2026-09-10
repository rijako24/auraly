"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { inventoryApi, type WarehouseTransferDetail } from "@/services/api/inventory";

export function WarehouseTransferReceiptDialog({ businessId, transferId, onClose }: {
  businessId: string;
  transferId: string | null;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const [quantities, setQuantities] = useState<Record<number, string>>({});
  const [differenceReason, setDifferenceReason] = useState("");
  const [notes, setNotes] = useState("");
  const detail = useQuery({
    queryKey: ["warehouse-transfer", transferId],
    queryFn: () => inventoryApi.transfer(transferId!),
    enabled: Boolean(transferId),
  });
  const reasons = useQuery({
    queryKey: ["inventory-reasons", businessId, "WarehouseTransfer"],
    queryFn: () => inventoryApi.reasons({ operationType: "WarehouseTransfer" }),
    enabled: Boolean(businessId && transferId),
  });

  useEffect(() => {
    if (!detail.data) return;
    setQuantities(Object.fromEntries(detail.data.lines.map((line) => [line.lineNumber, ""])));
    setDifferenceReason("");
    setNotes("");
  }, [detail.data]);

  const allQuantitiesEntered = useMemo(() => detail.data?.lines.every(
    (line) => Boolean(quantities[line.lineNumber]?.trim()),
  ) ?? false, [detail.data, quantities]);
  const changed = useMemo(() => allQuantitiesEntered && (detail.data?.lines.some(
    (line) => Number(quantities[line.lineNumber] ?? 0) !== line.pendingQuantity,
  ) ?? false), [allQuantitiesEntered, detail.data, quantities]);
  const selectedDifferenceReason = (reasons.data ?? []).find((item) => item.code === differenceReason);
  const valid = Boolean(detail.data) && allQuantitiesEntered && detail.data!.lines.every((line) => {
    const value = Number(quantities[line.lineNumber]);
    return Number.isFinite(value) && value >= 0 && value <= line.pendingQuantity;
  }) && (
    detail.data!.lines.some((line) => Number(quantities[line.lineNumber]) > 0) ||
    (changed && Boolean(differenceReason))
  ) && (!changed || (
    Boolean(differenceReason) &&
    (!selectedDifferenceReason?.requiresReference || Boolean(notes.trim()))
  ));

  const receive = useMutation({
    mutationFn: async () => {
      const transfer = detail.data as WarehouseTransferDetail;
      return inventoryApi.receiveTransfer(transfer.transferId, {
        receiptId: crypto.randomUUID(),
        businessId,
        occurredAt: new Date().toISOString(),
        differenceReasonCode: changed ? differenceReason : null,
        notes: notes.trim() || null,
        rowVersion: transfer.rowVersion,
        lines: transfer.lines.map((line) => ({
          lineNumber: line.lineNumber,
          productId: line.productId,
          receivedQuantity: Number(quantities[line.lineNumber]),
        })),
      });
    },
    onSuccess: async (result) => {
      toast.success(`${result.documentNumber}: entrada enviada al motor`);
      await Promise.all([
        client.invalidateQueries({ queryKey: ["pending-warehouse-transfers"] }),
        client.invalidateQueries({ queryKey: ["inventory-operations"] }),
        client.invalidateQueries({ queryKey: ["inventory-balances"] }),
        client.invalidateQueries({ queryKey: ["inventory-movements"] }),
      ]);
      onClose();
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible confirmar la entrada"),
  });

  return <Dialog open={Boolean(transferId)} onOpenChange={(open) => !open && onClose()}>
    <DialogContent className="flex max-h-[92dvh] max-w-4xl flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle>Confirmar entrada{detail.data ? ` · ${detail.data.documentNumber}` : ""}</DialogTitle>
        <DialogDescription>Registra únicamente las cantidades que llegaron a la bodega destino.</DialogDescription>
      </DialogHeader>
      <div className="space-y-4 overflow-y-auto px-6 py-5">
        {detail.isLoading && <div className="flex items-center justify-center gap-2 p-10 text-muted-foreground"><RefreshCw className="h-5 w-5 animate-spin" />Cargando traslado…</div>}
        {detail.isError && <p className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">No fue posible cargar el traslado. Actualiza la lista e inténtalo de nuevo.</p>}
        {detail.data && <>
          <p className="text-sm text-muted-foreground">Salida confirmada desde <strong>{detail.data.sourceWarehouseName}</strong>. Verifica y ajusta lo que realmente llegó a <strong>{detail.data.destinationWarehouseName}</strong>.</p>
          <div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[700px] text-sm">
            <thead className="bg-muted/60"><tr><th className="px-3 py-3 text-left">Producto</th><th className="px-3 py-3 text-right">Salió</th><th className="px-3 py-3 text-right">Recibido antes</th><th className="px-3 py-3 text-left">Entra ahora</th></tr></thead>
            <tbody>{detail.data.lines.map((line) => <tr key={line.lineNumber} className="border-t"><td className="px-3 py-2"><strong>{line.productName}</strong><span className="block text-xs text-muted-foreground">{line.productCode}</span></td><td className="px-3 py-2 text-right tabular-nums">{line.dispatchedQuantity}</td><td className="px-3 py-2 text-right tabular-nums">{line.receivedQuantity}</td><td className="px-3 py-2"><Input className="w-36 text-right tabular-nums" inputMode="decimal" value={quantities[line.lineNumber] ?? ""} onChange={(event) => setQuantities((current) => ({ ...current, [line.lineNumber]: event.target.value }))} aria-label={`Cantidad recibida de ${line.productName}`} /></td></tr>)}</tbody>
          </table></div>
          {changed && <div className="space-y-2"><Label>Motivo del faltante definitivo</Label><Select value={differenceReason} onValueChange={setDifferenceReason}><SelectTrigger><SelectValue placeholder="Selecciona el motivo contable" /></SelectTrigger><SelectContent>{(reasons.data ?? []).filter((item) => item.counterpartAccountingCategory).map((item) => <SelectItem key={item.inventoryReasonId} value={item.code}>{item.name}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">La diferencia se cerrará como pérdida; no quedará cantidad pendiente en tránsito.</p></div>}
          <div className="space-y-2"><Label>Observaciones de recepción</Label><Textarea value={notes} onChange={(event) => setNotes(event.target.value)} maxLength={1000} /></div>
        </>}
      </div>
      <DialogFooter className="border-t px-6 py-4"><Button variant="outline" onClick={onClose} disabled={receive.isPending}>Cancelar</Button><Button disabled={!valid || receive.isPending} onClick={() => receive.mutate()}>{receive.isPending ? "Procesando…" : "Confirmar entrada"}</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}
