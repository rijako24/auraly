"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { accountingApi } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export type PaymentCorrectionTarget = {
  paymentId: string;
  documentNumber: string;
  direction: "payable" | "receivable";
};

export function PortfolioPaymentCorrectionDialog({ businessId, target, onClose, onCorrected }: {
  businessId: string; target: PaymentCorrectionTarget; onClose: () => void; onCorrected: () => void;
}) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const [editorBusy, setEditorBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const capture = useQuery({
    queryKey: ["payment-correction-capture", tenantId, businessId, target.direction, target.paymentId],
    queryFn: () => accountingApi.paymentCorrectionCapture(target.direction === "payable" ? "Payable" : "Receivable",
      target.paymentId),
    retry: false, staleTime: 0, refetchOnMount: "always", refetchOnWindowFocus: false, refetchOnReconnect: false,
  });
  const title = target.direction === "receivable" ? "Anular recaudo" : "Anular pago";
  const ready = capture.isSuccess && capture.isFetchedAfterMount;
  function close() { if (sent) onCorrected(); onClose(); }
  return <Dialog open onOpenChange={open => { if (!open && !editorBusy) close(); }}>
    <DialogContent showClose={!ready} onInteractOutside={event => event.preventDefault()}
      className="max-h-[94dvh] w-[calc(100%-2rem)] max-w-6xl overflow-y-auto rounded-3xl p-4 sm:p-6">
      <DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription>
        {target.documentNumber} · Revierte las cuentas y reabre los documentos pagados. El pago original queda en el historial como anulado y las obligaciones pueden pagarse de nuevo.
      </DialogDescription></DialogHeader>
      {capture.isPending || !capture.isFetchedAfterMount ? <p role="status">Preparando las partidas del pago...</p> :
        capture.isError ? <div role="alert" className="space-y-3 text-destructive"><p>{capture.error instanceof Error ? capture.error.message : "No se pudo preparar la corrección."}</p>
          <Button variant="outline" disabled={capture.isFetching} onClick={() => void capture.refetch()}>Reintentar</Button></div> :
        <VoucherDraftEditor businessId={businessId} preparation={capture.data}
          onClose={close} onBusyChange={setEditorBusy}
          onSaved={value => { if (value.sentAt) setSent(true); }} modal closeLabel="Cerrar" />}
    </DialogContent>
  </Dialog>;
}
