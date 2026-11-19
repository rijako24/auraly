"use client";

import { X } from "lucide-react";
import { useId, type Ref, type ReactNode } from "react";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

export type CardPaymentDetails = { franchiseCode: string; approvalNumber: string };
export type TransferPaymentDetails = { reference: string; notes: string };

type DialogActions = { onSave: () => void; onClose: () => void; inputRef?: Ref<HTMLInputElement>; useDialogHost?: boolean };

function PaymentDetailsSurface({ children, title, onClose, useDialogHost }: {
  children: ReactNode; title: string; onClose: () => void; useDialogHost?: boolean;
}) {
  if (useDialogHost) return <Dialog open onOpenChange={open => !open && onClose()}>
    <DialogContent showClose={false} aria-describedby={undefined} overlayClassName="z-[1100]" className="z-[1101] max-w-md gap-0 border-0 bg-transparent p-0 shadow-none">
      <DialogTitle className="sr-only">{title}</DialogTitle>{children}
    </DialogContent>
  </Dialog>;
  return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/65 p-4" data-pos-focus-surface="modal">{children}</div>;
}

// These are the existing payment capture surfaces. The consuming workspace owns
// the draft and the catalog; opening a dialog never writes or creates a payment.
export function CardPaymentDetailsDialog({ value, onChange, franchises, onSave, onClose, inputRef,
  returnHint = "Enter guarda y vuelve al valor recibido.",
  catalogFailed = false,
  approvalMaxLength = 100, useDialogHost = false, paymentMethodField, canSave = true,
}: DialogActions & {
  value: CardPaymentDetails;
  onChange: (value: CardPaymentDetails) => void;
  franchises: ReadonlyArray<{ code: string; label: string }>;
  returnHint?: string;
  catalogFailed?: boolean;
  approvalMaxLength?: number;
  paymentMethodField?: ReactNode;
  canSave?: boolean;
}) {
  const titleId = useId();
  return <PaymentDetailsSurface title="Datos de la tarjeta" onClose={onClose} useDialogHost={useDialogHost}>
    <div onKeyDown={event => {
      if (event.key === "Enter" && !event.defaultPrevented && !(event.target as HTMLElement).closest("[role='combobox'],[cmdk-root],[role='listbox']")) { event.preventDefault(); if (canSave) onSave(); }
      if (event.key === "Escape" && !useDialogHost && !event.defaultPrevented) { event.stopPropagation(); onClose(); }
    }} className="max-h-[calc(100dvh-2rem)] w-full max-w-md overflow-y-auto rounded-2xl bg-white p-5 shadow-2xl" role={useDialogHost ? undefined : "dialog"} aria-modal={useDialogHost ? undefined : true} aria-labelledby={titleId}>
      <div className="flex items-start justify-between gap-3"><div><h3 id={titleId} className="text-lg font-bold text-slate-950">Datos de la tarjeta</h3><p className="mt-1 text-sm text-slate-500">Selecciona la franquicia y registra la aprobación del datáfono.</p></div><button type="button" onClick={onClose} aria-label="Cerrar datos de tarjeta" className={`grid ${useDialogHost ? "h-11 w-11 shrink-0" : "h-9 w-9"} place-items-center rounded-lg text-slate-500 hover:bg-slate-100`}><X className="h-5 w-5"/></button></div>
      {paymentMethodField && <div className="mt-5">{paymentMethodField}</div>}
      <fieldset disabled={!canSave}>
      <label className="mt-5 block text-sm font-medium text-slate-700">Franquicia<Select value={value.franchiseCode} onValueChange={franchiseCode => onChange({ ...value, franchiseCode })}><SelectTrigger className="mt-1 h-11"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{franchises.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></label>
      {catalogFailed && <p role="alert" className="mt-2 text-sm text-red-700">No fue posible cargar las franquicias. Cierra y vuelve a abrir los datos para reintentar.</p>}
      <label className="mt-4 block text-sm font-medium text-slate-700">Número de aprobación<input ref={inputRef} autoFocus required maxLength={approvalMaxLength} value={value.approvalNumber} onChange={event => onChange({ ...value, approvalNumber: event.target.value })} className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-lg font-semibold tracking-wide outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Aprobación del datáfono"/></label>
      </fieldset>
      <p className="mt-4 rounded-lg bg-slate-100 px-3 py-2 text-xs text-slate-600">{returnHint}</p><button type="button" onClick={onSave} disabled={!canSave || !value.franchiseCode || !value.approvalNumber.trim()} className="mt-4 h-11 w-full rounded-xl bg-teal-700 font-bold text-white disabled:opacity-45">Guardar datos · Enter</button>
    </div>
  </PaymentDetailsSurface>;
}

export function TransferPaymentDetailsDialog({ value, onChange, accountLabel, canSave, onSave, onClose, inputRef,
  referenceMaxLength = 160, showNotes = true, useDialogHost = false, paymentMethodField,
}: DialogActions & {
  value: TransferPaymentDetails;
  onChange: (value: TransferPaymentDetails) => void;
  accountLabel: string;
  canSave: boolean;
  referenceMaxLength?: number;
  showNotes?: boolean;
  paymentMethodField?: ReactNode;
}) {
  const titleId = useId();
  return <PaymentDetailsSurface title="Datos de la transferencia" onClose={onClose} useDialogHost={useDialogHost}>
    <div onKeyDown={event => { if (event.key === "Escape" && !useDialogHost && !event.defaultPrevented) { event.stopPropagation(); onClose(); } }} className="max-h-[calc(100dvh-2rem)] w-full max-w-md overflow-y-auto rounded-2xl bg-white p-5 shadow-2xl" role={useDialogHost ? undefined : "dialog"} aria-modal={useDialogHost ? undefined : true} aria-labelledby={titleId}>
      <div className="flex items-start justify-between gap-3"><div><h3 id={titleId} className="text-lg font-bold text-slate-950">Datos de la transferencia</h3><p className="mt-1 text-sm text-slate-500">Registra el soporte del movimiento. La cuenta corresponde a la forma de pago seleccionada.</p></div><button type="button" onClick={onClose} aria-label="Cerrar datos de transferencia" className={`grid ${useDialogHost ? "h-11 w-11 shrink-0" : "h-9 w-9"} place-items-center rounded-lg text-slate-500 hover:bg-slate-100`}><X className="h-5 w-5"/></button></div>
      {paymentMethodField && <div className="mt-5">{paymentMethodField}</div>}
      <div className="mt-5 text-sm"><span className="font-medium text-slate-700">Cuenta de la forma de pago</span><p className="mt-1 break-words rounded-lg border bg-slate-50 px-3 py-2 [overflow-wrap:anywhere]">{accountLabel}</p></div>
      <fieldset disabled={!canSave}>
      <label className="mt-4 block text-sm font-medium text-slate-700">Referencia<input ref={inputRef} autoFocus required maxLength={referenceMaxLength} value={value.reference} onChange={event => onChange({ ...value, reference: event.target.value })} className="mt-1 h-11 w-full rounded-lg border border-slate-300 px-3 text-base font-semibold outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Número o referencia del comprobante"/></label>
      {showNotes && <label className="mt-4 block text-sm font-medium text-slate-700">Nota <span className="font-normal text-slate-400">(opcional)</span><textarea maxLength={500} rows={3} value={value.notes} onChange={event => onChange({ ...value, notes: event.target.value })} className="mt-1 w-full resize-none rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-teal-600 focus:ring-2 focus:ring-teal-600/15" placeholder="Detalle útil para identificar la transferencia"/></label>}
      </fieldset>
      <button type="button" onClick={onSave} disabled={!value.reference.trim() || !canSave} className="mt-4 h-11 w-full rounded-xl bg-teal-700 font-bold text-white disabled:opacity-45">Guardar transferencia</button>
    </div>
  </PaymentDetailsSurface>;
}
