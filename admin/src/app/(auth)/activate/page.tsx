"use client";

import { FormEvent, Suspense, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { CheckCircle2, KeyRound, ShieldCheck } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { authApi } from "@/services/api/auth";

function ActivateTenantInvitationForm() {
  const params = useSearchParams();
  const token = useMemo(() => params.get("token")?.trim() ?? "", [params]);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string>();
  const [email, setEmail] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(undefined);
    if (token.length !== 64) return setError("El enlace de invitación no es válido.");
    if (password.length < 10) return setError("La contraseña debe tener al menos 10 caracteres.");
    if (password !== confirmation) return setError("Las contraseñas no coinciden.");
    setSubmitting(true);
    try {
      const result = await authApi.acceptInvitation({ token, password, passwordConfirmation: confirmation });
      setEmail(result.email);
    } catch (failure) {
      setError(failure && typeof failure === "object" && "message" in failure
        ? String(failure.message)
        : "No fue posible activar la invitación.");
    } finally {
      setSubmitting(false);
    }
  };

  if (email) return <section className="space-y-6 rounded-3xl border bg-white p-8 shadow-xl">
    <span className="grid h-14 w-14 place-items-center rounded-2xl bg-emerald-100 text-emerald-700"><CheckCircle2 className="h-7 w-7" /></span>
    <div><p className="text-sm font-semibold uppercase tracking-[.2em] text-emerald-700">Cuenta activada</p><h1 className="mt-2 text-3xl font-semibold">Tu empresa está lista</h1><p className="mt-2 text-sm text-muted-foreground">Ya puedes ingresar con <strong>{email}</strong>.</p></div>
    <Button className="w-full" asChild><Link href="/login">Ir a iniciar sesión</Link></Button>
  </section>;

  return <form onSubmit={submit} className="space-y-6 rounded-3xl border bg-white p-8 shadow-xl">
    <span className="grid h-14 w-14 place-items-center rounded-2xl bg-teal-100 text-teal-800"><ShieldCheck className="h-7 w-7" /></span>
    <div><p className="text-sm font-semibold uppercase tracking-[.2em] text-teal-700">Invitación Auraly</p><h1 className="mt-2 text-3xl font-semibold">Activa tu cuenta</h1><p className="mt-2 text-sm text-muted-foreground">Crea tu contraseña. También quedará preparada de forma segura para el ingreso offline en equipos enrolados.</p></div>
    <div className="space-y-2"><Label htmlFor="password">Contraseña</Label><div className="relative"><KeyRound className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input id="password" className="pl-9" type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} /></div></div>
    <div className="space-y-2"><Label htmlFor="confirmation">Confirma la contraseña</Label><Input id="confirmation" type="password" autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} /></div>
    {error && <p role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">{error}</p>}
    <Button className="w-full" type="submit" disabled={submitting}>{submitting ? "Activando…" : "Activar cuenta"}</Button>
  </form>;
}

export default function ActivateTenantInvitationPage() {
  return <Suspense fallback={<div className="rounded-3xl border bg-white p-8 text-sm text-muted-foreground shadow-xl">Preparando invitación…</div>}><ActivateTenantInvitationForm /></Suspense>;
}
