"use client";

import { FormEvent, Suspense, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { CheckCircle2, KeyRound, Loader2, Lock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

function ResetPasswordForm() {
  const token = useSearchParams().get("token")?.trim() ?? "";
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [loading, setLoading] = useState(false);
  const [complete, setComplete] = useState(false);
  const [error, setError] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (password !== confirmation) { setError("Las contraseñas no coinciden."); return; }
    setLoading(true);
    try {
      const response = await fetch("/api/auth/password-recovery/confirm", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ token, password, passwordConfirmation: confirmation }) });
      if (!response.ok) { const body = await response.json().catch(() => ({})); throw new Error(body.detail || body.message || "El enlace no es válido, ya fue usado o venció."); }
      setComplete(true);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "No fue posible cambiar la contraseña."); }
    finally { setLoading(false); }
  }

  if (complete) return <div className="rounded-[1.75rem] border border-white/70 bg-white p-9 text-[#102a2f] shadow-[0_24px_80px_rgba(3,16,19,.28)]"><CheckCircle2 className="h-14 w-14 text-emerald-600" /><h1 className="mt-6 text-3xl font-semibold">Contraseña actualizada</h1><p className="mt-3 leading-7 text-[#667f7d]">Ya puedes iniciar sesión. Por seguridad, cerramos las demás sesiones de tu cuenta.</p><Button asChild className="mt-7 h-12 w-full rounded-xl bg-[#147a73]"><Link href="/login">Iniciar sesión</Link></Button></div>;

  return <div className="rounded-[1.75rem] border border-white/70 bg-white p-7 text-[#102a2f] shadow-[0_24px_80px_rgba(3,16,19,.28)] sm:p-9"><span className="flex h-12 w-12 items-center justify-center rounded-2xl bg-[#e9f8f5] text-[#176a65]"><KeyRound /></span><h1 className="mt-5 text-3xl font-semibold">Crea una nueva contraseña</h1><p className="mt-2 text-sm leading-6 text-[#667f7d]">Usa al menos 10 caracteres. El enlace solo puede utilizarse una vez.</p>
    {!token && <p role="alert" className="mt-5 rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">El enlace está incompleto. Solicita uno nuevo.</p>}
    <form onSubmit={submit} className="mt-6 space-y-4">{error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}<div className="space-y-2"><Label htmlFor="new-password">Nueva contraseña</Label><div className="relative"><Lock className="absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#668c89]" /><Input id="new-password" type="password" className="h-12 rounded-xl pl-11" minLength={10} value={password} onChange={(event) => setPassword(event.target.value)} required autoComplete="new-password" /></div></div><div className="space-y-2"><Label htmlFor="confirm-password">Confirma la contraseña</Label><div className="relative"><Lock className="absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#668c89]" /><Input id="confirm-password" type="password" className="h-12 rounded-xl pl-11" minLength={10} value={confirmation} onChange={(event) => setConfirmation(event.target.value)} required autoComplete="new-password" /></div></div><Button type="submit" disabled={loading || !token} className="h-12 w-full rounded-xl bg-gradient-to-r from-[#0f5f5b] to-[#23988d]">{loading ? <><Loader2 className="h-4 w-4 animate-spin" />Actualizando…</> : "Actualizar contraseña"}</Button></form>
  </div>;
}

export default function ResetPasswordPage() { return <Suspense><ResetPasswordForm /></Suspense>; }