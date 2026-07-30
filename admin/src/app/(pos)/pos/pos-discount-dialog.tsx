"use client";

import { Percent } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";

import {
  calculatePosDiscount,
  PosDiscountMode,
} from "./pos-discount-calculation";
import { formatMoneyDraft, formatMoneyValue, parseMoneyDraft } from "./pos-money-input";

export function PosDiscountDialog({
  productName,
  currentDiscount,
  maximum,
  taxRate,
  busy,
  onConfirm,
  onCancel,
}: {
  productName: string;
  currentDiscount: number;
  maximum: number;
  taxRate: number;
  busy: boolean;
  onConfirm: (discount: number) => Promise<void>;
  onCancel: () => void;
}) {
  const [mode, setMode] = useState<PosDiscountMode>("value");
  const [value, setValue] = useState(formatMoneyValue(currentDiscount));
  const input =
    mode === "value"
      ? parseMoneyDraft(value)
      : Number(value.replace(",", "."));
  const calculation = calculatePosDiscount(mode, input, maximum, taxRate);
  const valid = calculation !== null;

  useEffect(() => {
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [busy, onCancel]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (calculation && !busy) await onConfirm(calculation.discount);
  }

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/65 p-4">
      <form onSubmit={submit} className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl">
        <div className="flex items-start gap-4">
          <span className="grid h-11 w-11 place-items-center rounded-xl bg-amber-100 text-amber-700">
            <Percent className="h-6 w-6" />
          </span>
          <div>
            <h2 className="text-lg font-bold">Descuento del producto</h2>
            <p className="mt-1 text-sm text-slate-600">{productName}</p>
          </div>
        </div>
        <div className="mt-5 grid grid-cols-2 rounded-xl bg-slate-100 p-1" role="group" aria-label="Tipo de descuento">
          {(["value", "percentage"] as const).map((candidate) => (
            <button key={candidate} type="button"
              onClick={() => {
                setMode(candidate);
                setValue(
                  candidate === "value"
                    ? formatMoneyValue(currentDiscount)
                    : String(maximum > 0 ? Math.round(currentDiscount * 10_000 / maximum) / 100 : 0),
                );
              }}
              className={`h-10 rounded-lg text-sm font-bold transition ${
                mode === candidate
                  ? "bg-white text-teal-800 shadow-sm"
                  : "text-slate-500 hover:text-slate-800"
              }`}>
              {candidate === "value" ? "Por valor" : "Por porcentaje"}
            </button>
          ))}
        </div>
        <label className="mt-5 block text-sm font-medium">
          {mode === "value" ? "Valor del descuento" : "Porcentaje del descuento"}
          <input autoFocus inputMode="decimal" value={value}
            onFocus={(event) => event.currentTarget.select()}
            onChange={(event) =>
              setValue(
                mode === "value"
                  ? formatMoneyDraft(event.target.value)
                  : event.target.value.replace(/[^\d,.]/g, ""),
              )
            }
            className="mt-1 h-14 w-full rounded-xl border-2 border-teal-700/25 px-4 text-right text-2xl font-bold tabular-nums outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/10" />
        </label>
        <p className={`mt-2 text-xs ${valid ? "text-slate-500" : "text-red-700"}`}>
          {mode === "value"
            ? `Máximo permitido: ${formatMoneyValue(maximum)}`
            : "Porcentaje permitido: 0% a 100%"}
        </p>
        {calculation && (
          <div className="mt-4 grid grid-cols-2 gap-3 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm">
            <span className="text-amber-900">Descuento</span>
            <strong className="text-right tabular-nums text-amber-950">
              {formatMoneyValue(calculation.discount)} ({calculation.percentage}%)
            </strong>
            <span className="text-amber-900">Nuevo total con IVA</span>
            <strong className="text-right tabular-nums text-amber-950">
              {formatMoneyValue(calculation.total)}
            </strong>
          </div>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onCancel} disabled={busy}
            className="h-11 rounded-xl border border-slate-300 px-5 font-semibold">Cancelar</button>
          <button autoFocus={false} type="submit" disabled={busy || !valid}
            className="h-11 rounded-xl bg-teal-700 px-5 font-bold text-white disabled:opacity-50">
            Aplicar
          </button>
        </div>
      </form>
    </div>
  );
}
