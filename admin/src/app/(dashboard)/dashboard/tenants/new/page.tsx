"use client";

import { useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, Building2, Check, ChevronLeft, ChevronRight, MapPin, PackageCheck, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { tenantsApi, type ProvisionTenantRequest } from "@/services/api/tenants";
import { useCities, useCountries, useDivisions } from "@/hooks/use-parties";

const steps = [
  { title: "Empresa", detail: "Identidad legal", icon: Building2 },
  { title: "Sede", detail: "Operación inicial", icon: MapPin },
  { title: "Confirmar", detail: "Recursos iniciales", icon: ShieldCheck },
] as const;

const initial: ProvisionTenantRequest = {
  provisioningRequestId: "", legalName: "", tradeName: "", nit: "", verificationDigit: "",
  countryId: "", administrativeDivisionId: "", cityId: "", address: "", phone: "", email: "", taxResponsibilities: "R-99-PN",
  businessName: "Sede principal", businessAddress: "", businessPhone: "", businessEmail: "",
  timeZone: "America/Bogota", inventoryCostBasis: "LatestReceiptCost",
  invitationEmail: "", maximumUsers: 5, maximumEnrolledDevices: 1,
};

export default function NewTenantPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [step, setStep] = useState(0);
  const [form, setForm] = useState(initial);
  const [sameAddress, setSameBusinessData] = useState(true);
  const setSameAddress = (checked: boolean) => {
    setSameBusinessData(checked);
    if (checked) setForm((current) => ({ ...current, businessAddress: current.address, businessPhone: current.phone, businessEmail: current.email }));
  };
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const countries = useCountries();
  const divisions = useDivisions(form.countryId);
  const cities = useCities(form.administrativeDivisionId);

  const selectedCountry = countries.data?.find((x) => x.countryId === form.countryId)?.name;
  const selectedDivision = divisions.data?.find((x) => x.administrativeDivisionId === form.administrativeDivisionId)?.name;
  const selectedCity = cities.data?.find((x) => x.cityId === form.cityId)?.name;
  const update = <K extends keyof ProvisionTenantRequest>(key: K, value: ProvisionTenantRequest[K]) =>
    setForm((current) => {
      const next = { ...current, [key]: value };
      if (sameAddress && key === "address") next.businessAddress = String(value);
      if (sameAddress && key === "phone") next.businessPhone = String(value);
      if (sameAddress && key === "email") next.businessEmail = String(value);
      if (key === "email" && !current.invitationEmail) next.invitationEmail = String(value);
      return next;
    });

  const requiredByStep = useMemo(() => [
    ["legalName", "tradeName", "nit", "verificationDigit", "countryId", "administrativeDivisionId", "cityId", "address", "phone", "email"],
    ["businessName", "businessAddress", "businessPhone", "businessEmail", "invitationEmail"],
  ], []);

  const validateStep = (index: number) => {
    const next: Record<string, string> = {};
    for (const field of requiredByStep[index] ?? []) if (!String(form[field as keyof ProvisionTenantRequest] ?? "").trim()) next[field] = "Este dato es obligatorio.";
    for (const field of ["email", "businessEmail", "invitationEmail"]) {
      if ((requiredByStep[index] ?? []).includes(field) && form[field as keyof ProvisionTenantRequest] && !String(form[field as keyof ProvisionTenantRequest]).includes("@")) next[field] = "Escribe un correo válido.";
    }
    if(index===1&&form.maximumUsers<1)next.maximumUsers="Debe permitir al menos un usuario.";
    if(index===1&&form.maximumEnrolledDevices<0)next.maximumEnrolledDevices="No puede ser negativo.";
    setErrors(next);
    return Object.keys(next).length === 0;
  };

  const next = () => { if (!validateStep(step)) return; setStep((value) => Math.min(2, value + 1)); };
  const submit = async () => {
    setSubmitting(true);
    try {
      const result = await tenantsApi.create({ ...form, provisioningRequestId: form.provisioningRequestId || crypto.randomUUID() });
      sessionStorage.setItem(`tenant-provisioning-summary:${result.tenantId}`, JSON.stringify({ result, form }));
      await queryClient.invalidateQueries({ queryKey: ["execution-context", "tenants"] });
      await queryClient.refetchQueries({ queryKey: ["execution-context", "tenants"] });
      await queryClient.invalidateQueries({ queryKey: ["tenants"] });
      toast.success("Empresa aprovisionada", { description: "La sede, las bodegas, los cupos y la invitación quedaron preparados." });
      router.push(`/dashboard/tenants/${result.tenantId}?provisioned=1`);
    } catch (error) {
      const message = typeof error === "object" && error && "message" in error ? String(error.message) : "No fue posible aprovisionar la empresa.";
      toast.error("No se pudo crear la empresa", { description: message });
    } finally { setSubmitting(false); }
  };

  return <div className="mx-auto max-w-6xl space-y-6 pb-12">
    <header className="flex items-center gap-4"><Button variant="ghost" size="icon" asChild><Link href="/dashboard/tenants"><ArrowLeft className="h-4 w-4" /></Link></Button><div><p className="text-sm font-medium text-primary">Administración Auraly</p><h1 className="text-3xl font-semibold tracking-tight">Nueva empresa</h1><p className="text-muted-foreground">Auraly prepara la sede y los recursos esenciales en una sola operación.</p></div></header>
    <nav aria-label="Progreso" className="grid gap-3 md:grid-cols-3">{steps.map((item, index) => { const Icon = item.icon; const active = index === step; const done = index < step; return <button type="button" key={item.title} onClick={() => index < step && setStep(index)} className={`flex items-center gap-3 rounded-2xl border p-4 text-left transition ${active ? "border-primary bg-primary/5 shadow-sm" : done ? "border-emerald-200 bg-emerald-50" : "bg-card"}`}><span className={`grid h-10 w-10 place-items-center rounded-xl ${active ? "bg-primary text-primary-foreground" : done ? "bg-emerald-600 text-white" : "bg-muted"}`}>{done ? <Check className="h-5 w-5" /> : <Icon className="h-5 w-5" />}</span><span><strong className="block text-sm">{item.title}</strong><small className="text-muted-foreground">{item.detail}</small></span></button>; })}</nav>
    <main className="overflow-hidden rounded-3xl border bg-card shadow-sm"><div className="bg-gradient-to-r from-slate-950 to-emerald-950 px-7 py-6 text-white"><p className="text-xs font-semibold uppercase tracking-[.2em] text-teal-300">Paso {step + 1} de 3</p><h2 className="mt-1 text-2xl font-semibold">{steps[step].title}</h2></div><div className="space-y-6 p-7">
      {step === 0 && <><Section title="Identidad tributaria" description="Información legal compartida por todas las sedes."><Grid><Field label="Razón social" error={errors.legalName}><Input value={form.legalName} onChange={(e) => update("legalName", e.target.value)} /></Field><Field label="Nombre comercial" error={errors.tradeName}><Input value={form.tradeName} onChange={(e) => update("tradeName", e.target.value)} /></Field><Field label="NIT" error={errors.nit}><Input value={form.nit} onChange={(e) => update("nit", e.target.value)} /></Field><Field label="Dígito de verificación" error={errors.verificationDigit}><Input value={form.verificationDigit} maxLength={4} onChange={(e) => update("verificationDigit", e.target.value)} /></Field></Grid></Section><Section title="Ubicación y contacto" description="Los combos muestran únicamente maestros activos."><Grid><Field label="País" error={errors.countryId}><Select value={form.countryId} onValueChange={(v) => setForm((x) => ({ ...x, countryId: v, administrativeDivisionId: "", cityId: "" }))}><SelectTrigger><SelectValue placeholder={countries.isLoading ? "Cargando países…" : "Selecciona país"} /></SelectTrigger><SelectContent>{countries.data?.map((x) => <SelectItem key={x.countryId} value={x.countryId}>{x.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Departamento" error={errors.administrativeDivisionId}><Select disabled={!form.countryId || divisions.isLoading} value={form.administrativeDivisionId} onValueChange={(v) => setForm((x) => ({ ...x, administrativeDivisionId: v, cityId: "" }))}><SelectTrigger><SelectValue placeholder="Selecciona departamento" /></SelectTrigger><SelectContent>{divisions.data?.map((x) => <SelectItem key={x.administrativeDivisionId} value={x.administrativeDivisionId}>{x.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Ciudad" error={errors.cityId}><Select disabled={!form.administrativeDivisionId || cities.isLoading} value={form.cityId} onValueChange={(v) => update("cityId", v)}><SelectTrigger><SelectValue placeholder="Selecciona ciudad" /></SelectTrigger><SelectContent>{cities.data?.map((x) => <SelectItem key={x.cityId} value={x.cityId}>{x.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Dirección principal" error={errors.address}><Input value={form.address} onChange={(e) => { update("address", e.target.value); if (sameAddress) update("businessAddress", e.target.value); }} /></Field><Field label="Teléfono" error={errors.phone}><Input value={form.phone} onChange={(e) => update("phone", e.target.value)} /></Field><Field label="Correo empresarial" error={errors.email}><Input type="email" value={form.email} onChange={(e) => update("email", e.target.value)} /></Field></Grid></Section></>}
      {step === 1 && <><Section title="Sede principal" description="Esta será la primera sede operativa de la empresa."><div className="mb-5 flex items-center justify-between rounded-xl border bg-muted/30 p-4"><div><strong className="text-sm">Usar los mismos datos de la empresa</strong><p className="text-xs text-muted-foreground">Reutiliza dirección, teléfono y correo en la sede principal.</p></div><Switch checked={sameAddress} onCheckedChange={setSameAddress} /></div><Grid><Field label="Nombre de la sede" error={errors.businessName}><Input value={form.businessName} onChange={(e) => update("businessName", e.target.value)} /></Field><Field label="Dirección" error={errors.businessAddress}><Input disabled={sameAddress} value={sameAddress ? form.address : form.businessAddress} onChange={(e) => update("businessAddress", e.target.value)} /></Field><Field label="Teléfono" error={errors.businessPhone}><Input disabled={sameAddress} value={sameAddress ? form.phone : form.businessPhone} onChange={(e) => update("businessPhone", e.target.value)} /></Field><Field label="Correo" error={errors.businessEmail}><Input disabled={sameAddress} type="email" value={sameAddress ? form.email : form.businessEmail} onChange={(e) => update("businessEmail", e.target.value)} /></Field><Field label="Base para formar costos"><Select value={form.inventoryCostBasis} onValueChange={(v) => update("inventoryCostBasis", v as ProvisionTenantRequest["inventoryCostBasis"])}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="LatestReceiptCost">Último costo recibido</SelectItem><SelectItem value="WeightedAverageCost">Costo promedio ponderado</SelectItem></SelectContent></Select></Field></Grid></Section><Section title="Capacidad contratada" description="Solo un administrador de Auraly puede modificar estos cupos. El tenant podrá ver el consumo."><Grid><Field label="Usuarios activos permitidos" error={errors.maximumUsers}><Input inputMode="numeric" value={form.maximumUsers} onChange={(e) => update("maximumUsers",Number(e.target.value.replace(/\D/g,"")))} /></Field><Field label="Cajas enroladas permitidas" error={errors.maximumEnrolledDevices}><Input inputMode="numeric" value={form.maximumEnrolledDevices} onChange={(e) => update("maximumEnrolledDevices",Number(e.target.value.replace(/\D/g,"")))} /></Field></Grid></Section><Section title="Entrega de la invitación" description="Este correo solo recibe el enlace; la persona completa sus datos y contraseña al abrirlo."><Field label="Enviar enlace a" error={errors.invitationEmail}><Input type="email" value={form.invitationEmail} onChange={(e) => update("invitationEmail",e.target.value)} /></Field></Section><div className="grid gap-4 md:grid-cols-2"><Resource code="VEN" title="Bodega de venta" description="Predeterminada para facturación. Inicia bloqueando negativos."/><Resource code="PED" title="Bodega de pedidos" description="Preparación y despacho explícitos; crear un pedido no mueve inventario."/></div></>}
      {step === 2 && <div className="grid gap-5 lg:grid-cols-2"><Summary title="Empresa" rows={[["Razón social", form.legalName],["Nombre comercial",form.tradeName],["NIT",`${form.nit}-${form.verificationDigit}`],["Ubicación",[selectedCity,selectedDivision,selectedCountry].filter(Boolean).join(", ")],["Dirección",form.address]]}/><Summary title="Sede y bodegas" rows={[["Sede",form.businessName],["Dirección",form.businessAddress],["Inventario","Bodega de venta (VEN) y Bodega de pedidos (PED)"],["Negativos","Bloqueados inicialmente"]]}/><Summary title="Capacidad e invitación" rows={[["Usuarios activos",String(form.maximumUsers)],["Cajas enroladas",String(form.maximumEnrolledDevices)],["Entrega",form.invitationEmail],["Administrador","Completa sus datos desde el enlace"]]}/><Summary title="Cliente inicial" rows={[["Cliente","Consumidor final"],["Alcance","Asignado a la sede principal"],["Identificación","222222222222 · identificación DIAN"]]}/></div>}
    </div><footer className="flex items-center justify-between border-t bg-muted/20 px-7 py-5"><Button variant="outline" disabled={step===0||submitting} onClick={() => setStep((x)=>x-1)}><ChevronLeft className="mr-2 h-4 w-4"/>Atrás</Button>{step<2?<Button onClick={next}>Continuar<ChevronRight className="ml-2 h-4 w-4"/></Button>:<Button disabled={submitting} onClick={submit}>{submitting?"Aprovisionando…":"Crear organización y enviar invitación"}</Button>}</footer></main>
  </div>;
}

function Section({title,description,children}:{title:string;description:string;children:React.ReactNode}){return <section className="rounded-2xl border p-5"><h3 className="font-semibold">{title}</h3><p className="mb-5 text-sm text-muted-foreground">{description}</p>{children}</section>}
function Grid({children}:{children:React.ReactNode}){return <div className="grid gap-4 md:grid-cols-2">{children}</div>}
function Field({label,error,children}:{label:string;error?:string;children:React.ReactNode}){return <div className={`space-y-2 ${error?"[&_input]:border-destructive [&_button]:border-destructive [&_button]:ring-1 [&_button]:ring-destructive/20 [&_input]:ring-1 [&_input]:ring-destructive/20":""}`}><Label>{label}{error&&<span className="text-destructive"> *</span>}</Label>{children}{error&&<p role="alert" className="text-xs text-destructive">{error}</p>}</div>}
function Resource({code,title,description}:{code:string;title:string;description:string}){return <article className="flex gap-4 rounded-2xl border bg-emerald-50/50 p-5"><span className="grid h-11 w-11 place-items-center rounded-xl bg-emerald-600 text-white"><PackageCheck className="h-5 w-5"/></span><div><span className="text-xs font-bold text-emerald-700">{code}</span><h3 className="font-semibold">{title}</h3><p className="text-sm text-muted-foreground">{description}</p></div></article>}
function Summary({title,rows}:{title:string;rows:string[][]}){return <section className="rounded-2xl border p-5"><h3 className="mb-4 font-semibold">{title}</h3><dl className="space-y-3">{rows.map(([key,value])=><div key={key} className="flex justify-between gap-4 border-b pb-2 last:border-0"><dt className="text-sm text-muted-foreground">{key}</dt><dd className="text-right text-sm font-medium">{value||"—"}</dd></div>)}</dl></section>}
