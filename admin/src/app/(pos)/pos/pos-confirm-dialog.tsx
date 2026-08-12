"use client";

import { AlertTriangle, Loader2 } from "lucide-react";
import { FormEvent, useEffect } from "react";

export function PosConfirmDialog({
  title,
  description,
  confirmLabel,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  description: string;
  confirmLabel: string;
  busy: boolean;
  onConfirm: () => Promise<void>;
  onCancel: () => void;
}) {
  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [busy, onCancel]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!busy) await onConfirm();
  }

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/65 p-4">
      <form
        onSubmit={submit}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="pos-confirm-title"
        aria-describedby="pos-confirm-description"
        className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl"
      >
        <div className="flex items-start gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-amber-100 text-amber-700">
            <AlertTriangle className="h-6 w-6" />
          </span>
          <div>
            <h2 id="pos-confirm-title" className="text-lg font-bold text-slate-950">
              {title}
            </h2>
            <p id="pos-confirm-description" className="mt-1 text-sm leading-6 text-slate-600">
              {description}
            </p>
          </div>
        </div>

        <p className="mt-5 rounded-lg bg-slate-100 px-3 py-2 text-xs text-slate-600">
          Enter acepta · Esc cancela
        </p>
        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="h-11 rounded-xl border border-slate-300 px-5 font-semibold text-slate-700 outline-none transition hover:bg-slate-50 focus:ring-2 focus:ring-slate-400/30 disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            autoFocus
            type="submit"
            disabled={busy}
            className="flex h-11 min-w-32 items-center justify-center gap-2 rounded-xl bg-red-700 px-5 font-bold text-white outline-none transition hover:bg-red-800 focus:ring-4 focus:ring-red-500/25 disabled:opacity-50"
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            {confirmLabel}
          </button>
        </div>
      </form>
    </div>
  );
}
