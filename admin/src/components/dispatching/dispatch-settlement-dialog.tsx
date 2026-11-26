"use client";

import { DocumentNumbers } from "@/components/documents/document-numbers";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { RotateCcw } from "lucide-react";
import { DispatchSettlementPanel } from "@/components/dispatching/transport-dispatch-app";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { dispatchesApi, type DispatchExecution, type DispatchPaymentReviewResult } from "@/services/api/dispatches";
import { useAuthStore } from "@/stores/auth-store";
import { useDispatchKey, useDispatchOutboxSynchronization } from "@/hooks/use-dispatches";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

// Both entry points use this detail and the existing settlement commands.
export function DispatchSettlementDialog({ id, onClose, onChanged, mode = "review" }: {
  id: string; mode?: "progress" | "review"; onClose: () => void; onChanged?: (updated: DispatchExecution) => void;
}) {
  useDispatchOutboxSynchronization();
  const prefix = useDispatchKey();
  const [, tenant, business, user] = prefix;
  const canSettle = useAuthStore(state => state.user?.permissions.includes("dispatches.settle") ?? false);
  const cache = useQueryClient();
  const dates = useBusinessDates();
  const key = [...prefix, "execution", id] as const;
  const execution = useQuery({ queryKey: key, queryFn: () => dispatchesApi.reviewExecution(id),
    enabled: Boolean(tenant && business && user && canSettle), refetchOnWindowFocus: false, refetchOnReconnect: false });
  const apply = (updated: DispatchExecution) => {
    cache.setQueryData(key, updated);
    // Other views consume the authoritative status when they become visible again.
    void cache.invalidateQueries({ queryKey: prefix, refetchType: "none" });
    void cache.invalidateQueries({ queryKey: ["work-session-closures", "inbox", tenant, business], refetchType: "none" });
    onChanged?.(updated);
  };
  const applyPaymentReview = (result: DispatchPaymentReviewResult) => {
    cache.setQueryData<DispatchExecution>(key, current => current ? {
      ...current,
      documents: current.documents.map(document => document.dispatchSourceDocumentId === result.dispatchSourceDocumentId
        ? {...document,payments:document.payments.map(payment => payment.paymentId === result.payment.paymentId ? result.payment : payment)} : document),
      settlement: current.settlement ? {...current.settlement,grossCash:result.grossCash,expectedCash:result.expectedCash,
        difference:result.cashDifference,depositTotal:result.transferTotal,cardTotal:result.cardTotal} : null,
    } : current);
    void cache.invalidateQueries({queryKey:["work-session-closures","inbox",tenant,business],refetchType:"none"});
  };
  const data = execution.isError ? undefined : execution.data;
  return <Dialog open onOpenChange={open => { if (!open) onClose(); }}>
    <DialogContent className="flex max-h-[94dvh] w-[calc(100%-1rem)] max-w-6xl flex-col overflow-hidden p-0 [&>button]:text-white">
      <DialogHeader className="shrink-0 border-b bg-gradient-to-r from-slate-950 to-teal-900 p-5 text-left text-white sm:p-6">
        <DialogTitle className="text-xl text-white">Recepción y liquidación del despacho</DialogTitle>
        <DialogDescription className="break-all text-slate-200">{data ? `${data.dispatchNumber} · ${data.driverName}${data.vehiclePlate ? ` · ${data.vehiclePlate}` : ""}` : "Entregas, recaudos, devoluciones y gastos del despacho."}</DialogDescription>
      </DialogHeader>
      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 pb-5 sm:px-6">
      {!canSettle ? <p role="alert">No tienes permiso para revisar liquidaciones de despacho.</p> :
        execution.isError ? <div role="alert" className="space-y-3 text-destructive">No fue posible cargar el despacho.<Button variant="outline" onClick={() => void execution.refetch()}>Reintentar</Button></div> :
        !data ? <p className="p-10 text-center text-muted-foreground">Cargando despacho…</p> : <>
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-muted/40 p-3">
            <p className="text-sm"><strong>{data.settlement ? "Cierre de despacho" : "Despacho sin cerrar"}</strong><span className="block text-muted-foreground">{dates.timeZone ? dates.formatDate(data.scheduledDate) : data.scheduledDate}</span></p>
            <Button size="sm" variant="outline" disabled={execution.isFetching} onClick={() => void execution.refetch()}><RotateCcw className="mr-2 h-4 w-4"/>Actualizar</Button>
          </div>
          <DispatchSettlementPanel key={id} data={data} onChanged={apply} onPaymentReview={applyPaymentReview} readOnly={mode === "progress"}/>
          <section aria-label="Entregas del despacho" className="space-y-3">
            <h3 className="font-bold">Entregas y recaudos · {data.documents.length} {data.documents.length === 1 ? "documento" : "documentos"}</h3>
            {data.documents.map(document => <details key={document.dispatchSourceDocumentId} className="rounded-2xl border p-3">
              <summary className="cursor-pointer text-sm"><DocumentNumbers internalNumber={document.documentNumber} fiscalNumber={document.fiscalNumber}/><strong className="block">{document.customerName}</strong><span className="mt-1 block text-muted-foreground">{document.deliveryStatus === "Pending" ? "Pendiente" : document.deliveryStatus === "NotDelivered" ? "No entregado" : document.deliveryStatus === "PartiallyDelivered" ? "Entrega parcial" : "Entregado"} · {money.format(document.documentTotal)}</span></summary>
              <div className="mt-3 space-y-2 text-sm">
                {document.deliveryAddress && <p>{document.deliveryAddress}</p>}
                {document.reason && <p>Motivo: {document.reason}</p>}
                {document.notes && <p>{document.notes}</p>}

                {document.returns.map(item => <p key={item.returnLineId}>Devolución: {item.productCode} · {item.description} · {item.quantity} · {item.reasonDescription}</p>)}
              </div>
            </details>)}
          </section>
        </>}
      </div>
      <footer className="flex shrink-0 justify-end border-t bg-slate-50 p-4 sm:p-5"><Button variant="outline" onClick={onClose}>Cerrar</Button></footer>
    </DialogContent>
  </Dialog>;
}
