"use client";

import { useState } from "react";
import { VoucherDraftEditor } from "@/components/accounting/voucher-draft-editor";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export function PortfolioAdjustmentDialog({ direction, businessId, open, obligationId, onClose }: {
  direction: "Receivable" | "Payable";
  businessId: string;
  open: boolean;
  obligationId: string | null;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  return (
    <Dialog open={open} onOpenChange={next => { if (!next && !busy) onClose(); }}>
      <DialogContent
        onInteractOutside={event => event.preventDefault()}
        className="max-h-[94dvh] w-[calc(100%-2rem)] max-w-6xl overflow-y-auto rounded-3xl p-4 sm:p-6"
      >
        <DialogHeader>
          <DialogTitle>Ajuste de cartera</DialogTitle>
          <DialogDescription>Selecciona la obligación y registra el ajuste contable. Guardar no cambia su saldo hasta contabilizar.</DialogDescription>
        </DialogHeader>
        {open && (
          <VoucherDraftEditor
            key={`${direction}:${obligationId ?? "new"}`}
            businessId={businessId}
            adjustment={{
              subledgerKind: direction,
              subledgerId: obligationId ?? "",
              direction: "Decrease",
              amount: 0,
              counterpartAccountId: null,
              costCenterId: null,
            }}
            onClose={onClose}
            onBusyChange={setBusy}
            modal
            closeLabel="Cerrar"
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
