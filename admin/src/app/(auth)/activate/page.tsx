"use client";

import { FormEvent, Suspense, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { CheckCircle2, KeyRound, ShieldCheck } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { authApi } from "@/services/api/auth";

function ActivateTenantInvitationForm() {
  const params = useSearchParams();
  const token = useMemo(() => params.get("token")?.trim() ?? "", [params]);
  const [profile, setProfile] = useState({ identificationType: "CC", identification: "", firstName: "", lastName: "", email: "", phone: "", address: "" });
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [email, setEmail] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const set = (field: keyof typeof profile, value: string) => setProfile((current) => ({ ...current, [field]: value }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(undefined);
    const errors: Record<string, string> = {};
    if (!profile.identification.trim()) errors.identification = "Este campo es requerido";
    if (!profile.firstName.trim()) errors.firstName = "Este campo es requerido";
    if (!profile.lastName.trim()) errors.lastName = "Este campo es requerido";
    if (!profile.email.trim() || !profile.email.includes("@")) errors.email = "Escribe un correo válido";
    if (!profile.phone.trim()) errors.phone = "Este campo es requerido";
    if (!profile.address.trim()) errors.address = "Este campo es requerido";
    if (password.length < 10) errors.password = "Debe tener al menos 10 caracteres";
    if (password !== confirmation) errors.confirmation = "Las contraseñas no coinciden";
    setFieldErrors(errors);
    if (token.length !== 64) return setError("El enlace de invitación no es válido.");
    if (Object.keys(errors).length) return;
    setSubmitting(true);
    try {
      const result = await authApi.acceptInvitation({ token, ...profile, password, passwordConfirmation: confirmation });
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
    <div><p className="text-sm font-semibold uppercase tracking-[.2em] text-teal-700">Invitación Auraly</p><h1 className="mt-2 text-3xl font-semibold">Completa tu registro</h1><p className="mt-2 text-sm text-muted-foreground">Estos datos crearán tu identidad y acceso como administrador. Nada se crea hasta terminar este formulario.</p></div>
    <div className="grid gap-4 sm:grid-cols-2">
      <Field label="Tipo de identificación"><Select value={profile.identificationType} onValueChange={(value) => set("identificationType", value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="CC">Cédula de ciudadanía</SelectItem><SelectItem value="CE">Cédula de extranjería</SelectItem><SelectItem value="PAS">Pasaporte</SelectItem></SelectContent></Select></Field>
      <Field label="Número de identificación" error={fieldErrors.identification}><Input value={profile.identification} onChange={(event) => set("identification", event.target.value)} /></Field>
      <Field label="Nombres" error={fieldErrors.firstName}><Input value={profile.firstName} onChange={(event) => set("firstName", event.target.value)} /></Field>
      <Field label="Apellidos" error={fieldErrors.lastName}><Input value={profile.lastName} onChange={(event) => set("lastName", event.target.value)} /></Field>
      <Field label="Correo de acceso" error={fieldErrors.email}><Input type="email" autoComplete="email" value={profile.email} onChange={(event) => set("email", event.target.value)} /></Field>
      <Field label="Teléfono" error={fieldErrors.phone}><Input autoComplete="tel" value={profile.phone} onChange={(event) => set("phone", event.target.value)} /></Field>
      <div className="sm:col-span-2"><Field label="Dirección" error={fieldErrors.address}><Input autoComplete="street-address" value={profile.address} onChange={(event) => set("address", event.target.value)} /></Field></div>
      <Field label="Contraseña" error={fieldErrors.password}><div className="relative"><KeyRound className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input id="password" className="pl-9" type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} /></div></Field>
      <Field label="Confirma la contraseña" error={fieldErrors.confirmation}><Input id="confirmation" type="password" autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} /></Field>
    </div>
    {error && <p role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">{error}</p>}
    <Button className="w-full" type="submit" disabled={submitting}>{submitting ? "Creando acceso…" : "Crear mi acceso de administrador"}</Button>
  </form>;
}

function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return <div className={`space-y-2 ${error ? "[&_input]:border-destructive [&_button]:border-destructive" : ""}`}><Label>{label}{error && <span className="text-destructive"> *</span>}</Label>{children}{error && <p role="alert" className="text-xs text-destructive">{error}</p>}</div>;
}

export default function ActivateTenantInvitationPage() {
  return <Suspense fallback={<div className="rounded-3xl border bg-white p-8 text-sm text-muted-foreground shadow-xl">Preparando invitación…</div>}><ActivateTenantInvitationForm /></Suspense>;
}
