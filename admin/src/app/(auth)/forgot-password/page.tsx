"use client";

import { FormEvent, Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ArrowLeft, Building2, CheckCircle2, Loader2, Mail, Send, User } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { readRememberedTenantKey } from "@/lib/remembered-tenant-key";

function RecoveryRequestForm() {
  const search = useSearchParams();
  const tenantFromUrl = search.get("tenant")?.trim() ?? "";
  const [tenantKey, setTenantKey] = useState(tenantFromUrl);
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [maskedEmail, setMaskedEmail] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!tenantFromUrl) setTenantKey(readRememberedTenantKey());
  }, [tenantFromUrl]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    setLoading(true);
    try {
      const response = await fetch("/api/auth/password-recovery/request", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tenantKey: tenantKey.trim(), username: username.trim(), email: email.trim() }),
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(body.detail || body.message || body.title || "No fue posible procesar la solicitud.");
      setMaskedEmail(body.maskedEmail || "el correo indicado");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "No fue posible procesar la solicitud.");
    } finally {
      setLoading(false);
    }
  }

  if (maskedEmail) return <div className="rounded-[1.75rem] border border-white/70 bg-white p-7 text-[#102a2f] shadow-[0_24px_80px_rgba(3,16,19,.28)] sm:p-9">
    <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-emerald-100 text-emerald-700"><CheckCircle2 className="h-7 w-7" /></span>
    <h1 className="mt-6 text-3xl font-semibold tracking-tight">Revisa tu correo</h1>
    <p className="mt-3 leading-7 text-[#667f7d]">Si los datos coinciden con una cuenta activa, enviamos un enlace de recuperación a <strong className="text-[#17383c]">{maskedEmail}</strong>.</p>
    <div className="mt-5 rounded-xl border border-[#cfe4e1] bg-[#f0faf8] p-4 text-sm leading-6 text-[#315d59]">El enlace vence en 30 minutos y funciona una sola vez. Revisa también correo no deseado.</div>
    <Button asChild className="mt-7 h-12 w-full rounded-xl bg-[#147a73] hover:bg-[#0f665f]"><Link href={`/login?tenant=${encodeURIComponent(tenantKey.trim())}`}>Volver al inicio de sesión</Link></Button>
  </div>;

  return <div className="rounded-[1.75rem] border border-white/70 bg-white p-7 text-[#102a2f] shadow-[0_24px_80px_rgba(3,16,19,.28)] sm:p-9">
    <span className="inline-flex items-center gap-2 rounded-full bg-[#e9f8f5] px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[.12em] text-[#176a65]"><Mail className="h-3.5 w-3.5" />Recuperación segura</span>
    <h1 className="mt-5 text-3xl font-semibold tracking-tight">Recupera tu acceso</h1>
    <p className="mt-2 text-sm leading-6 text-[#667f7d]">Confirma tu empresa, usuario y correo. Te enviaremos un enlace para crear una nueva contraseña.</p>
    <form onSubmit={submit} className="mt-7 space-y-4">
      {error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</p>}
      <Field id="recovery-tenant" label="Empresa" icon={Building2}><Input id="recovery-tenant" value={tenantKey} onChange={(event) => setTenantKey(event.target.value)} placeholder="@auraly" required disabled={loading || Boolean(tenantFromUrl)} autoCapitalize="none" /></Field>
      <Field id="recovery-username" label="Usuario" icon={User}><Input id="recovery-username" value={username} onChange={(event) => setUsername(event.target.value)} placeholder="tu_usuario" required disabled={loading} autoComplete="username" /></Field>
      <Field id="recovery-email" label="Correo asociado" icon={Mail}><Input id="recovery-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="tu@empresa.com" required disabled={loading} autoComplete="email" /></Field>
      <Button type="submit" disabled={loading} className="h-12 w-full rounded-xl bg-gradient-to-r from-[#0f5f5b] to-[#23988d] font-semibold">{loading ? <><Loader2 className="h-4 w-4 animate-spin" />Enviando…</> : <><Send className="h-4 w-4" />Enviar enlace</>}</Button>
    </form>
    <Link href={`/login${tenantKey ? `?tenant=${encodeURIComponent(tenantKey.trim())}` : ""}`} className="mt-6 flex items-center justify-center gap-2 text-sm font-semibold text-[#176f6a]"><ArrowLeft className="h-4 w-4" />Volver al inicio de sesión</Link>
  </div>;
}

function Field({ id, label, icon: Icon, children }: { id: string; label: string; icon: typeof Mail; children: React.ReactNode }) {
  return <div className="space-y-2"><Label htmlFor={id}>{label}</Label><div className="relative"><Icon className="pointer-events-none absolute left-3.5 top-1/2 z-10 h-4 w-4 -translate-y-1/2 text-[#668c89]" /><div className="[&_input]:h-12 [&_input]:rounded-xl [&_input]:pl-11">{children}</div></div></div>;
}

export default function ForgotPasswordPage() { return <Suspense><RecoveryRequestForm /></Suspense>; }