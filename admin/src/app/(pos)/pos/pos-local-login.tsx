"use client";

import { CheckCircle2, Loader2, LockKeyhole, MonitorSmartphone, UserRound, Wifi, WifiOff } from "lucide-react";
import { FormEvent, useEffect, useRef, useState } from "react";

type Props = {
  deviceSeriesCode: string;
  businessName: string;
  warehouseName: string;
  serverConnected: boolean;
  preparing: boolean;
  identityReady: boolean;
  catalogReady: boolean;
  error: string | null;
  onLogin: (username: string, password: string) => Promise<void>;
};

export function PosLocalLogin({
  deviceSeriesCode,
  businessName,
  warehouseName,
  serverConnected,
  preparing,
  identityReady,
  catalogReady,
  error,
  onLogin,
}: Props) {
  const usernameRef = useRef<HTMLInputElement>(null);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!preparing) usernameRef.current?.focus();
  }, [preparing]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (preparing || submitting || !username.trim() || !password) return;
    setSubmitting(true);
    try {
      await onLogin(username.trim(), password);
      setPassword("");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="relative grid min-h-screen place-items-center overflow-hidden bg-[#071a1d] p-5 text-white">
      <div className="absolute -left-44 -top-52 h-[38rem] w-[38rem] rounded-full bg-teal-300/10 blur-3xl" />
      <div className="absolute -bottom-52 -right-36 h-[38rem] w-[38rem] rounded-full bg-cyan-300/10 blur-3xl" />
      <form
        onSubmit={submit}
        autoComplete="off"
        className="relative w-full max-w-md overflow-hidden rounded-[2rem] border border-white/10 bg-[#0b2428]/95 p-7 shadow-2xl"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="grid h-14 w-14 place-items-center rounded-2xl bg-teal-300 text-[#071a1d]">
            <MonitorSmartphone className="h-7 w-7" />
          </div>
          <div
            className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold ${
              serverConnected
                ? "bg-emerald-300/15 text-emerald-200"
                : "bg-amber-300/15 text-amber-100"
            }`}
          >
            {serverConnected ? <Wifi className="h-3.5 w-3.5" /> : <WifiOff className="h-3.5 w-3.5" />}
            {serverConnected ? "Conectado con Auraly" : "Acceso local disponible"}
          </div>
        </div>

        <p className="mt-8 text-xs font-bold uppercase tracking-[0.2em] text-teal-200">
          {businessName || "Sede"} · {warehouseName || "Bodega"}
        </p>
        <p className="mt-1 text-xs font-semibold text-slate-400">
          Dispositivo {deviceSeriesCode || "configurado"}
        </p>
        <h1 className="mt-2 text-3xl font-black tracking-tight">¿Quién va a facturar?</h1>
        <p className="mt-2 text-sm leading-6 text-slate-300">
          Usa la misma contraseña de Auraly. Tu venta en curso se conserva aunque cambie el usuario.
        </p>

        {preparing ? (
          <div className="mt-7 rounded-2xl border border-teal-200/15 bg-teal-200/10 p-6 text-center">
            <Loader2 className="mx-auto h-8 w-8 animate-spin text-teal-200" />
            <p className="mt-3 font-bold">Preparando la caja desconectada</p>
            <p className="mt-1 text-sm text-slate-300">
              La primera vez descargamos al equipo lo necesario para facturar sin internet.
            </p>
            <div className="mt-5 space-y-2 text-left text-sm" aria-live="polite">
              <PreparationItem
                label="Descargando usuarios y permisos"
                complete={identityReady}
                active={!identityReady}
              />
              <PreparationItem
                label="Descargando productos, precios y clientes"
                complete={catalogReady}
                active={identityReady && !catalogReady}
              />
              <PreparationItem
                label="Activando sincronización automática"
                complete={identityReady && catalogReady}
                active={identityReady && catalogReady}
              />
            </div>
            {error && (
              <p role="alert" className="mt-5 rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-left text-sm text-red-100">
                {error}
              </p>
            )}
          </div>
        ) : (
          <div className="mt-7 space-y-4">
            <label className="block">
              <span className="mb-2 block text-sm font-semibold text-slate-200">Usuario</span>
              <span className="flex h-13 items-center gap-3 rounded-xl border border-white/15 bg-white/5 px-4 focus-within:border-teal-300 focus-within:ring-4 focus-within:ring-teal-300/10">
                <UserRound className="h-5 w-5 text-teal-200" />
                <input
                  ref={usernameRef}
                  name="auraly-pos-local-user"
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                  autoComplete="off"
                  className="h-12 min-w-0 flex-1 bg-transparent font-semibold outline-none placeholder:text-slate-500"
                  placeholder="Tu usuario"
                />
              </span>
            </label>
            <label className="block">
              <span className="mb-2 block text-sm font-semibold text-slate-200">Contraseña</span>
              <span className="flex h-13 items-center gap-3 rounded-xl border border-white/15 bg-white/5 px-4 focus-within:border-teal-300 focus-within:ring-4 focus-within:ring-teal-300/10">
                <LockKeyhole className="h-5 w-5 text-teal-200" />
                <input
                  name="auraly-pos-local-secret"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  type="password"
                  autoComplete="new-password"
                  className="h-12 min-w-0 flex-1 bg-transparent font-semibold outline-none placeholder:text-slate-500"
                  placeholder="Tu contraseña"
                />
              </span>
            </label>
            {error && (
              <p role="alert" className="rounded-xl border border-red-300/20 bg-red-400/10 p-3 text-sm text-red-100">
                {error}
              </p>
            )}
            <button
              type="submit"
              disabled={submitting || !username.trim() || !password}
              className="flex h-13 w-full items-center justify-center gap-2 rounded-xl bg-teal-300 font-black text-[#071a1d] transition hover:bg-teal-200 disabled:cursor-not-allowed disabled:opacity-40"
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
              Entrar a facturación
            </button>
          </div>
        )}
      </form>
    </main>
  );
}

function PreparationItem({
  label,
  complete,
  active,
}: {
  label: string;
  complete: boolean;
  active: boolean;
}) {
  return (
    <div
      className={`flex items-center gap-2 overflow-hidden transition-all duration-700 ${
        complete && !active ? "max-h-0 -translate-y-1 opacity-0" : "max-h-8 opacity-100"
      }`}
    >
      {complete ? (
        <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-300" />
      ) : (
        <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${active ? "animate-pulse bg-teal-200" : "bg-white/20"}`} />
      )}
      <span className={active ? "font-semibold text-white" : "text-slate-400"}>{label}</span>
    </div>
  );
}
