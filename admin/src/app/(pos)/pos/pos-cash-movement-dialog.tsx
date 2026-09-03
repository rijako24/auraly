"use client";

import { ArrowDownToLine, ArrowUpFromLine, Loader2, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type {
  PosCashMovementDirection,
  PosCashMovementReason,
  PosClient,
} from "@/services/pos/pos-edge-client";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatMoneyDraft, parseMoneyDraft } from "./pos-money-input";
import { cashMovementKeyboardAction } from "./pos-cash-movement-keyboard";

export function PosCashMovementDialog({
  client,
  initialDirection,
  responsibleName,
  onClose,
  onCompleted,
}: {
  client: PosClient;
  initialDirection: PosCashMovementDirection;
  responsibleName: string;
  onClose: () => void;
  onCompleted: (message: string) => void;
}) {
  const [direction, setDirection] = useState<PosCashMovementDirection>(initialDirection);
  const [reasons, setReasons] = useState<PosCashMovementReason[]>([]);
  const [reasonId, setReasonId] = useState("");
  const [amount, setAmount] = useState("");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const reasonTriggerRef = useRef<HTMLButtonElement>(null);
  const amountRef = useRef<HTMLInputElement>(null);
  const reason = useMemo(
    () => reasons.find((candidate) => candidate.reasonId === reasonId),
    [reasons, reasonId],
  );

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    setReasonId("");
    client.cashMovementReasons(direction)
      .then((items) => active && setReasons(items))
      .catch((caught) => active && setError(
        caught instanceof Error ? caught.message : "No fue posible cargar los motivos.",
      ))
      .finally(() => {
        if (!active) return;
        setLoading(false);
        window.requestAnimationFrame(() => reasonTriggerRef.current?.focus());
      });
    return () => { active = false; };
  }, [client, direction]);

  async function confirm() {
    const numericAmount = parseMoneyDraft(amount);
    if (!reason || !Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError("Selecciona un motivo e ingresa un valor mayor que cero.");
      return;
    }
    if (!reason.isAccountingConfigured) {
      setError("El motivo no tiene una contrapartida contable configurada.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const occurredAt = new Date().toISOString();
      const result = await client.confirmCashMovement({
        documentId: crypto.randomUUID(),
        reasonId: reason.reasonId,
        amount: numericAmount,
        occurredAt,
        reference: reference.trim() || null,
        notes: notes.trim() || null,
        costCenterId: reason.defaultCostCenterId,
      });
      let printWarning = "";
      try {
        await client.printCashMovement({
          documentId: result.documentId,
          direction,
          reasonName: reason.name,
          amount: numericAmount,
          occurredAt,
          reference: reference.trim() || null,
          notes: notes.trim() || null,
          responsibleName,
        });
      } catch (caught) {
        printWarning = `. Movimiento guardado; ${caught instanceof Error ? caught.message : "no fue posible imprimir el ticket"}`;
      }
      onCompleted(
        direction === "In"
          ? "Entrada de dinero registrada" +
            (result.documentNumber ? " / " + result.documentNumber : " y pendiente de sincronizacion") + printWarning
          : "Salida de dinero registrada" +
            (result.documentNumber ? " / " + result.documentNumber : " y pendiente de sincronizacion") + printWarning,
      );
    } catch (caught) {
      setError(caught instanceof Error
        ? caught.message
        : "No fue posible registrar el movimiento.");
    } finally {
      setSaving(false);
    }
  }

  function handleFieldKeyDown(event: React.KeyboardEvent<HTMLElement>) {
    const control = event.currentTarget.dataset.cashControl as
      | "select" | "textarea" | "field" | "button";
    const action = cashMovementKeyboardAction({ key: event.key, control });
    if (action === "native") return;
    event.preventDefault();
    if (action === "submit") {
      formRef.current?.requestSubmit();
      return;
    }
    const controls = Array.from(
      formRef.current?.querySelectorAll<HTMLElement>("[data-cash-control]:not(:disabled)") ?? [],
    );
    const current = controls.indexOf(event.currentTarget);
    const target = action === "next" ? controls[current + 1] : controls[current - 1];
    target?.focus();
  }

  return <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/65 p-4">
    <form ref={formRef} onSubmit={(event) => { event.preventDefault(); void confirm(); }}
      className="w-full max-w-lg overflow-hidden rounded-3xl bg-white shadow-2xl">
      <header className="flex items-start justify-between border-b px-6 py-5">
        <div>
          <p className="text-xs font-bold uppercase tracking-[.16em] text-teal-700">Cajon de dinero</p>
          <h2 className="mt-1 text-xl font-bold">Entrada o salida de caja</h2>
          <p className="mt-1 text-sm text-slate-600">Quedará incluida en el cierre de sesión y en la contabilización.</p>
        </div>
        <button type="button" onClick={onClose} disabled={saving}
          className="grid h-10 w-10 place-items-center rounded-xl hover:bg-slate-100"
          aria-label="Cerrar"><X className="h-5 w-5"/></button>
      </header>
      <div className="space-y-4 p-6">
        <div className="grid grid-cols-2 gap-2">
          <DirectionButton selected={direction==="In"} onClick={()=>setDirection("In")}
            icon={<ArrowDownToLine className="h-5 w-5"/>} label="Entrada"/>
          <DirectionButton selected={direction==="Out"} onClick={()=>setDirection("Out")}
            icon={<ArrowUpFromLine className="h-5 w-5"/>} label="Salida"/>
        </div>
        <Field label="Motivo">
          <Select value={reasonId||undefined} onValueChange={(value) => {
            setReasonId(value);
            window.requestAnimationFrame(() => amountRef.current?.focus());
          }} disabled={loading}>
            <SelectTrigger ref={reasonTriggerRef} data-cash-control="select"
              className="h-11 rounded-xl border-slate-300"><SelectValue placeholder={loading?"Cargando motivos...":"Selecciona un motivo"}/></SelectTrigger>
            <SelectContent>{reasons.map((item)=><SelectItem key={item.reasonId} value={item.reasonId}
              disabled={!item.isAccountingConfigured}>{item.name}{item.isAccountingConfigured?"":" (sin cuenta)"}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        {!loading && reasons.length === 0 && !error && <p className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">Aún no hay motivos contables disponibles para esta sede. Actualiza los datos o pide al administrador revisar el aprovisionamiento contable.</p>}
        <Field label="Valor">
          <div className="relative"><span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 font-bold text-slate-500">$</span><input ref={amountRef} value={amount} onChange={(event)=>setAmount(formatMoneyDraft(event.target.value))}
            data-cash-control="field" onKeyDown={handleFieldKeyDown}
            inputMode="numeric" autoComplete="off" placeholder="0"
            className="h-11 w-full rounded-xl border border-slate-300 pl-8 pr-3 text-right text-lg font-bold tabular-nums"/></div>
        </Field>
        <Field label="Referencia (opcional)">
          <input value={reference} onChange={(event)=>setReference(event.target.value)}
            data-cash-control="field" onKeyDown={handleFieldKeyDown}
            maxLength={120} className="h-11 w-full rounded-xl border border-slate-300 px-3"/>
        </Field>
        <Field label="Notas">
          <textarea value={notes} onChange={(event)=>setNotes(event.target.value)}
            data-cash-control="textarea" onKeyDown={handleFieldKeyDown}
            maxLength={500} rows={3} className="w-full rounded-xl border border-slate-300 p-3"/>
        </Field>
        {reason&&<p className="rounded-xl bg-slate-50 p-3 text-xs text-slate-600">
          Cuenta: {reason.accountCode||"sin configurar"} {reason.accountName||""}
          {reason.defaultCostCenterName?" / Centro: "+reason.defaultCostCenterName:""}
        </p>}
        {error&&<p className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
      </div>
      <footer className="flex justify-end gap-2 border-t px-6 py-4">
        <button type="button" onClick={onClose} disabled={saving}
          className="h-10 rounded-xl border px-4 text-sm font-semibold">Cancelar</button>
        <button type="submit" data-cash-control="button" disabled={saving||loading||!reason||parseMoneyDraft(amount)<=0}
          className="flex h-10 items-center gap-2 rounded-xl bg-teal-700 px-4 text-sm font-bold text-white disabled:opacity-50">
          {saving&&<Loader2 className="h-4 w-4 animate-spin"/>}Registrar
        </button>
      </footer>
    </form>
  </div>;
}

function Field({label,children}:{label:string;children:React.ReactNode}){
 return <label className="block space-y-2 text-sm font-semibold"><span>{label}</span>{children}</label>;
}
function DirectionButton({selected,onClick,icon,label}:{selected:boolean;onClick:()=>void;icon:React.ReactNode;label:string}){
 return <button type="button" onClick={onClick} className={selected
   ?"flex h-12 items-center justify-center gap-2 rounded-xl border border-teal-600 bg-teal-50 font-bold text-teal-800"
   :"flex h-12 items-center justify-center gap-2 rounded-xl border border-slate-200 font-semibold"}>
   {icon}{label}
  </button>;
}
