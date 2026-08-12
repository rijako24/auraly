"use client";

import { CheckCircle2, KeyRound, Loader2, ShieldCheck, Smartphone } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { posApprovalClient, type PosApprovalRequest } from "@/services/pos/pos-approval-client";

export function PosSupervisorApprovalDialog({
  approval,
  allowRemote,
  busy,
  error,
  onRemoteApproved,
  onLocalSecret,
  onCancel,
}: {
  approval: PosApprovalRequest | null;
  allowRemote: boolean;
  busy: boolean;
  error: string | null;
  onRemoteApproved: (approvalId: string) => Promise<void>;
  onLocalSecret: (secret: string) => Promise<void>;
  onCancel: () => void;
}) {
  const [secret, setSecret] = useState("");
  const [showLocal, setShowLocal] = useState(!allowRemote);
  const [channelError, setChannelError] = useState<string | null>(null);

  useEffect(() => {
    if (!approval || !allowRemote) return;
    let active = true;
    let dispose: (() => void) | undefined;
    const refresh = async () => {
      const current = await posApprovalClient.get(approval.approvalRequestId);
      if (!active) return;
      if (current.status === "Approved")
        await onRemoteApproved(current.approvalRequestId);
      else if (current.status === "Rejected" || current.status === "Expired")
        setChannelError(current.status === "Rejected"
          ? "El supervisor rechazó la acción."
          : "La solicitud venció; inténtala nuevamente.");
    };
    void posApprovalClient.subscribe(() => void refresh())
      .then((stop) => {
        dispose = stop;
        return refresh();
      })
      .catch((caught) => {
        if (!active) return;
        setChannelError(caught instanceof Error ? caught.message : "Falló el canal de aprobación.");
        setShowLocal(true);
      });
    return () => { active = false; dispose?.(); };
  }, [allowRemote, approval, onRemoteApproved]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (secret.trim()) await onLocalSecret(secret);
  }

  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-950/70 p-4">
      <section role="dialog" aria-modal="true" className="w-full max-w-lg overflow-hidden rounded-3xl bg-white shadow-2xl">
        <header className="bg-gradient-to-r from-slate-950 to-teal-950 p-6 text-white">
          <div className="flex items-center gap-3">
            <span className="grid h-12 w-12 place-items-center rounded-2xl bg-teal-400/15 text-teal-200">
              <ShieldCheck className="h-6 w-6" />
            </span>
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.2em] text-teal-200">Acción protegida</p>
              <h2 className="mt-1 text-xl font-black">Autorización de supervisor</h2>
            </div>
          </div>
        </header>
        <div className="p-6">
          {allowRemote && !showLocal ? (
            <div className="rounded-2xl border border-teal-200 bg-teal-50 p-5 text-center">
              <Smartphone className="mx-auto h-8 w-8 text-teal-700" />
              <h3 className="mt-3 font-bold text-slate-950">Esperando respuesta remota</h3>
              <p className="mt-1 text-sm text-slate-600">El supervisor recibió la solicitud. La venta continuará automáticamente al aprobarse.</p>
              <Loader2 className="mx-auto mt-4 h-5 w-5 animate-spin text-teal-700" />
            </div>
          ) : (
            <form onSubmit={submit}>
              <div className="flex items-start gap-3 rounded-2xl bg-amber-50 p-4 text-amber-950">
                <KeyRound className="mt-0.5 h-5 w-5 shrink-0" />
                <p className="text-sm">Un supervisor autorizado puede escribir aquí su credencial secundaria. No se guarda ni se reutiliza.</p>
              </div>
              <label className="mt-5 block text-sm font-bold text-slate-800">
                Credencial del supervisor
                <input autoFocus type="password" autoComplete="off" value={secret}
                  onChange={(event) => setSecret(event.target.value)}
                  className="mt-2 h-12 w-full rounded-xl border border-slate-300 px-4 text-lg tracking-widest outline-none focus:border-teal-600 focus:ring-4 focus:ring-teal-600/15" />
              </label>
              <button type="submit" disabled={busy || !secret.trim()}
                className="mt-4 flex h-12 w-full items-center justify-center gap-2 rounded-xl bg-teal-700 font-bold text-white disabled:opacity-50">
                {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
                Autorizar una vez
              </button>
            </form>
          )}
          {(channelError || error) && <p className="mt-4 rounded-xl bg-red-50 p-3 text-sm font-medium text-red-700">{error || channelError}</p>}
          <div className="mt-5 flex items-center justify-between gap-3">
            <span />
            <button type="button" onClick={onCancel} disabled={busy} className="h-10 rounded-xl border border-slate-300 px-4 font-semibold text-slate-700">
              Cancelar
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}
