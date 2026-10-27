"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { accountingApi } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export type PortfolioUnapplicationTarget = { subledgerKind: "Payable" | "Receivable"; subledgerId: string; transactionId: string };

export function PortfolioUnapplicationDialog({ businessId, target, onClose }: {
  businessId: string; target: PortfolioUnapplicationTarget; onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const capture = useQuery({
    queryKey: ["voucher-unapplication-capture", tenantId, businessId, target.subledgerKind, target.subledgerId, target.transactionId],
    queryFn: () => accountingApi.unapplicationCapture(target.subledgerKind, target.subledgerId, target.transactionId),
    retry: false, staleTime: 0, refetchOnMount: "always", refetchOnWindowFocus: false, refetchOnReconnect: false,
  });
  const prepared = capture.isSuccess && capture.isFetchedAfterMount;
  const restoresCredit = prepared && capture.data.lines.some(line => line.value.existingPosition?.subledgerKind === "SupplierCredit" || line.value.existingPosition?.subledgerKind === "CustomerCredit");
  return <Dialog open onOpenChange={open => { if (!open && !busy) onClose(); }}>
    <DialogContent showClose={!prepared} onEscapeKeyDown={event => { if (prepared) event.preventDefault(); }}
      onInteractOutside={event => event.preventDefault()} className="max-h-[94dvh] w-[calc(100%-2rem)] max-w-6xl overflow-y-auto rounded-3xl p-4 sm:p-6">
      <DialogHeader><DialogTitle>{restoresCredit ? "Desaplicar cruce" : "Desaplicar abono"}</DialogTitle><DialogDescription>{restoresCredit ? "Restituye el saldo a favor original. Ajusta el importe de las partidas; el cambio se aplica al contabilizar y no mueve dinero." : "Elige una cuenta de anticipo para conservar el dinero, o una cuenta de caja, banco o tarjeta si el dinero fue devuelto. Ajusta los importes; el cambio se aplica al contabilizar."}</DialogDescription></DialogHeader>
      {capture.isPending || !capture.isFetchedAfterMount ? <p role="status">Preparando las partidas del abono original...</p> : capture.isError ?
        <div role="alert" className="space-y-3 text-destructive"><p>{capture.error instanceof Error ? capture.error.message : "No fue posible preparar la desaplicación."}</p><Button variant="outline" disabled={capture.isFetching} onClick={() => void capture.refetch()}>Reintentar</Button></div> :
        // The editor owns closing and the unsaved-changes confirmation.
        <VoucherDraftEditor businessId={businessId} preparation={capture.data} onClose={onClose} onBusyChange={setBusy} modal closeLabel="Cerrar" />}
    </DialogContent>
  </Dialog>;
}
