"use client";

import { FileCheck2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { dianQuotaExhaustedMessage } from "@/services/pos/pos-fiscal-guard";

export function DianQuotaNotice({ open, onClose }: { open: boolean; onClose: () => void }) {
  return <Dialog open={open} onOpenChange={(value) => { if (!value) onClose(); }}>
    <DialogContent className="max-w-md overflow-hidden border-teal-200">
      <DialogHeader>
        <div className="mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-50 text-teal-700"><FileCheck2 aria-hidden="true" className="h-6 w-6"/></div>
        <DialogTitle>No tienes documentos DIAN disponibles</DialogTitle>
        <DialogDescription className="pt-2 leading-6">{dianQuotaExhaustedMessage}</DialogDescription>
      </DialogHeader>
      <p className="text-sm text-muted-foreground">Lo que has digitado se conserva. El administrador puede revisar el cupo en Suscripción.</p>
      <DialogFooter><Button onClick={onClose}>Volver al comprobante</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}
