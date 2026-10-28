"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { accountingApi } from "@/services/api/accounting";
import { workSessionsApi } from "@/services/api/work-sessions";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export type PaymentCorrectionTarget = {
  paymentId: string;
  documentNumber: string;
  direction: "payable" | "receivable";
  kind: "AnnulRegistration" | "ReturnMoney";
};

export function PortfolioPaymentCorrectionDialog({ businessId, target, onClose, onCorrected }: {
  businessId: string; target: PaymentCorrectionTarget; onClose: () => void; onCorrected: () => void;
}) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const [workSessionId, setWorkSessionId] = useState<string>();
  const [openingSession, setOpeningSession] = useState(false);
  const [sessionError, setSessionError] = useState<string>();
  const [editorBusy, setEditorBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const refund = target.kind === "ReturnMoney";
  const capture = useQuery({
    queryKey: ["payment-correction-capture", tenantId, businessId, target.direction, target.paymentId, target.kind],
    queryFn: () => accountingApi.paymentCorrectionCapture(target.direction === "payable" ? "Payable" : "Receivable",
      target.paymentId, target.kind),
    retry: false, staleTime: 0, refetchOnMount: "always", refetchOnWindowFocus: false, refetchOnReconnect: false,
  });
  const title = refund ? "Registrar devolución de dinero" : target.direction === "receivable"
    ? "Anular registro de recaudo" : "Anular registro de pago";
  async function continueRefund() {
    if (openingSession) return;
    setOpeningSession(true);
    setSessionError(undefined);
    try { setWorkSessionId((await workSessionsApi.currentOrOpen(businessId)).workSessionId); }
    catch (error) { setSessionError(error instanceof Error ? error.message : "No se pudo abrir la sesión de trabajo."); }
    finally { setOpeningSession(false); }
  }
  const ready = capture.isSuccess && capture.isFetchedAfterMount && (!refund || !!workSessionId);
  function close() { if (sent) onCorrected(); onClose(); }
  return <Dialog open onOpenChange={open => { if (!open && !openingSession && !editorBusy) close(); }}>
    <DialogContent showClose={!ready} onInteractOutside={event => event.preventDefault()}
      className="max-h-[94dvh] w-[calc(100%-2rem)] max-w-6xl overflow-y-auto rounded-3xl p-4 sm:p-6">
      <DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription>
        {target.documentNumber} · {refund
          ? "Reabre las obligaciones y registra la salida real de dinero en la sesión actual. La factura original se conserva."
          : "Reabre las obligaciones y reversa las cuentas del registro equivocado. Los cierres anteriores y la factura original se conservan; registra la corrección en la conciliación que corresponda."}
      </DialogDescription></DialogHeader>
      {capture.isPending || !capture.isFetchedAfterMount ? <p role="status">Preparando las partidas del pago...</p> :
        capture.isError ? <div role="alert" className="space-y-3 text-destructive"><p>{capture.error instanceof Error ? capture.error.message : "No se pudo preparar la corrección."}</p>
          <Button variant="outline" disabled={capture.isFetching} onClick={() => void capture.refetch()}>Reintentar</Button></div> :
        refund && !workSessionId ? <div className="space-y-3 text-sm"><p>Al continuar se usará tu sesión de trabajo actual para registrar la devolución.</p>
          {sessionError && <p role="alert" className="text-destructive">{sessionError}</p>}
          <Button disabled={openingSession} onClick={() => void continueRefund()}>{openingSession ? "Abriendo sesión..." : "Continuar con la devolución"}</Button></div> :
        <VoucherDraftEditor businessId={businessId} preparation={capture.data}
          correctionWorkSessionId={workSessionId} onClose={close} onBusyChange={setEditorBusy}
          onSaved={value => { if (value.sentAt) setSent(true); }} modal closeLabel="Cerrar" />}
    </DialogContent>
  </Dialog>;
}
