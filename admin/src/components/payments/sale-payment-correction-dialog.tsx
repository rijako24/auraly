"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { workSessionDifferencesApi, type ClosurePaymentVerification, type SalePaymentCorrectionContext } from "@/services/api/work-session-differences";
import { workSessionPaymentMethodName } from "@/services/pos/pos-work-session-close";
import { PaymentCorrectionEditor } from "./payment-correction-editor";
import { dispatchesApi, type DeliveryPayment } from "@/services/api/dispatches";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { accountingStatusLabel } from "@/lib/accounting-labels";
import { formatCurrency } from "@/lib/utils";

type CorrectionContext = SalePaymentCorrectionContext & { dispatchPayments?: DeliveryPayment[] };

export function SalePaymentCorrectionDialog({ documentId, documentNumber, businessId, dispatchId, onClose }: {
  documentId: string; documentNumber: string; businessId: string; dispatchId?: string | null; onClose: () => void;
}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const client = useQueryClient();
  const dispatchMethods = useReferenceOptions("cash-closure-method", Boolean(dispatchId));
  const dispatchCatalogError = Boolean(dispatchId) && dispatchMethods.isError;
  const key = ["sale-payment-correction", tenant, businessId, documentId, dispatchId];
  const context = useQuery<CorrectionContext>({ queryKey: key, queryFn: async () => {
    if (!dispatchId) return workSessionDifferencesApi.salePaymentCorrection(documentId);
    const payments = await dispatchesApi.paymentCorrections(dispatchId, documentId);
    return { documentId, documentNumber, workSessionClosureId: "", countableMethods: [],
      dispatchPayments: payments, items: payments.map(payment => ({
        verificationKey: payment.paymentId, paymentMethodCode: payment.paymentMethod === "DebitCard" || payment.paymentMethod === "CreditCard" ? "Card" : payment.paymentMethod ?? "",
        movementType: "Sale", sourceId: documentId, documentNumber, sourceNumber: 1, amount: payment.amount,
        reference: payment.reference, cardFranchiseCode: payment.cardFranchiseCode ?? null, approvalNumber: payment.approvalNumber ?? null,
        occurredAt: "", sourceDocumentType: "SalesReceipt", customerName: null, status: null,
        tenderMethodCode: payment.paymentMethod ?? undefined,
      })) };
  },
    enabled: Boolean(tenant && businessId), refetchOnWindowFocus: false });
  const [selected, setSelected] = useState<ClosurePaymentVerification | null>(null);
  const [operation, setOperation] = useState(() => crypto.randomUUID());
  const choose = (item: ClosurePaymentVerification) => { setOperation(crypto.randomUUID()); setSelected(item); };
  const single = context.data?.items.length === 1 && !context.data.items[0].appliedCorrectionId
    && (context.data.items[0].movementType !== "CreditSale" || (context.data.items[0].collectibleAmount ?? 0) > 0) ? context.data.items[0] : null;
  const active = selected ?? single;
  if (active && context.data && !dispatchCatalogError) return <PaymentCorrectionEditor item={active} businessId={businessId}
    methods={dispatchId ? (dispatchMethods.data ?? []).map(option => option.code) : context.data.countableMethods} movementLabel={active.movementType === "CreditSale" ? "Venta a cartera" : "Comprobante"}
    usageOverride={dispatchId ? "Receipts" : undefined} referenceMaxLength={dispatchId ? 120 : undefined} approvalMaxLength={dispatchId ? 80 : undefined}
    applicationNotice={dispatchId ? "La corrección se guarda en la revisión del despacho y se aplica al confirmar su liquidación." : "Guardar acepta esta corrección directamente, sin conciliar los demás documentos del cierre."}
    submitLabel="Guardar corrección" onClose={() => single ? onClose() : setSelected(null)} onRemove={() => undefined}
    onApply={async correction => {
      if (dispatchId) {
        const payment = context.data.dispatchPayments!.find(payment => payment.paymentId === correction.verificationKey)!;
        const result = await dispatchesApi.correctPayment(dispatchId, payment.paymentId, {
          paymentMethodId: correction.paymentMethodId!, paymentMethodVersion: correction.paymentMethodVersion!,
          methodCode: correction.tenderMethodCode!, reference: correction.reference ?? null,
          cardFranchiseCode: correction.cardFranchiseCode ?? null, approvalNumber: correction.approvalNumber ?? null,
          reason: correction.reason, expectedCorrectedAt: payment.correctedAt ?? null,
        });
        client.setQueryData<CorrectionContext>(key, value => value ? { ...value,
          dispatchPayments: value.dispatchPayments?.map(item => item.paymentId === result.payment.paymentId ? result.payment : item),
          items: value.items.map(item => item.verificationKey === correction.verificationKey
            ? { ...item, appliedCorrectionId: result.payment.paymentId, correctionAccountingStatus: "PendingDispatchSettlement",
              correctedTenderMethodCode: result.payment.paymentMethod ?? undefined, correctedAmount: result.payment.amount,
              correctedPaymentMethodName: result.payment.formName ?? correction.paymentMethodName } : item) } : value);
        setSelected(null);
        toast.success("Corrección guardada en la revisión del despacho. Se aplicará al confirmar su liquidación.");
        return;
      }
      const result = await workSessionDifferencesApi.correctSalePayment(documentId, operation, correction);
      client.setQueryData<CorrectionContext>(key, value => value ? { ...value,
        items: value.items.map(item => item.verificationKey === correction.verificationKey
          ? { ...item, appliedCorrectionId: result.correctionId, correctionAccountingStatus: result.accountingStatus,
            correctedTenderMethodCode: correction.tenderMethodCode, correctedAmount: correction.amount,
            correctedPaymentMethodName: correction.paymentMethodName } : item) } : value);
      setSelected(null);
      toast.success("Corrección aceptada. El resultado se reflejará al terminar la contabilización.");
    }} />;
  return <Dialog open onOpenChange={open => !open && onClose()}><DialogContent className="max-h-[90dvh] max-w-lg overflow-y-auto">
    <DialogHeader><DialogTitle>Corregir medio de recaudo</DialogTitle>
      <DialogDescription>{documentNumber} · Selecciona el recaudo que vas a corregir. El importe original se conserva.</DialogDescription>
    </DialogHeader>
    {context.isPending && <p role="status">Cargando los medios de este comprobante…</p>}
    {context.error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-800">{context.error instanceof Error ? context.error.message : "No fue posible consultar el comprobante."}</p>}
    {dispatchCatalogError && <p role="alert" className="text-sm text-red-800">No fue posible consultar los medios de recaudo del despacho. <Button variant="link" onClick={() => void dispatchMethods.refetch()}>Reintentar</Button></p>}
    {context.data?.items.length === 0 && <p className="text-sm text-muted-foreground">Este comprobante aún no tiene recaudos registrados para corregir.</p>}
    <div className="space-y-3">{context.data?.items.map(item => {
      const unavailable = Boolean(item.appliedCorrectionId) || item.movementType === "CreditSale" && !(item.collectibleAmount && item.collectibleAmount > 0);
      const done = item.correctionAccountingStatus === "Posted" || item.correctionAccountingStatus === "CommercialEffectsApplied";
      return <div key={item.verificationKey} className="flex items-center justify-between gap-3 rounded-xl border p-3">
        <div className="min-w-0"><p className="font-medium">{item.correctedPaymentMethodName ?? workSessionPaymentMethodName(item.correctedTenderMethodCode ?? item.tenderMethodCode ?? item.paymentMethodCode)}</p>
          <p className="text-sm">{formatCurrency(item.correctedAmount ?? (item.movementType === "CreditSale" ? item.collectibleAmount ?? 0 : item.amount), "COP", 4)}</p>
          {item.appliedCorrectionId && <p className="mt-1 text-xs text-muted-foreground">{done ? "Corrección aplicada" : item.correctionAccountingStatus === "PendingDispatchSettlement" ? "Guardada en la revisión del despacho" : item.correctionAccountingStatus === "Pending" ? "Corrección pendiente de contabilizar" : accountingStatusLabel(item.correctionAccountingStatus)}</p>}
          {!item.appliedCorrectionId && unavailable && <p className="mt-1 text-xs text-muted-foreground">Sin saldo disponible para recaudar.</p>}
        </div><Button variant="outline" disabled={unavailable || dispatchCatalogError} onClick={() => choose(item)}><Pencil className="mr-2 h-4 w-4"/>Corregir</Button>
      </div>;
    })}</div>
    <div className="flex justify-end gap-2"><Button variant="outline" disabled={context.isFetching} onClick={() => void context.refetch()}><RefreshCw className="mr-2 h-4 w-4"/>Actualizar estado</Button><Button onClick={onClose}>Cerrar</Button></div>
  </DialogContent></Dialog>;
}
