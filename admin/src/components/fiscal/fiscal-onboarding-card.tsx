"use client";

import { AlertTriangle, CheckCircle2, FileKey2, FlaskConical, Loader2, LockKeyhole, Pencil, RefreshCw, Rocket, ShieldCheck, Upload } from "lucide-react";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import {
  fiscalConfigurationApi,
  type FiscalOnboardingConfiguration,
} from "@/services/api/fiscal-configuration";
import { habilitationFeedbackKind } from "@/services/api/fiscal-onboarding-events";

type Props = { businessId: string; canManage: boolean };

export function FiscalOnboardingCard({ businessId, canManage }: Props) {
  const router = useRouter();
  const [value, setValue] = useState<FiscalOnboardingConfiguration | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [saving, setSaving] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [activatingProduction, setActivatingProduction] = useState(false);
  const [activatingSupport, setActivatingSupport] = useState(false);
  const [softwareId, setSoftwareId] = useState("");
  const [softwarePin, setSoftwarePin] = useState("");
  const [testSetId, setTestSetId] = useState("");
  const [certificatePassword, setCertificatePassword] = useState("");
  const [certificate, setCertificate] = useState<File | null>(null);
  const [credentialError, setCredentialError] = useState("");
  const [selectedSupportRangeId, setSelectedSupportRangeId] = useState("");
  const [supportConfirmed, setSupportConfirmed] = useState(false);
  const [editingCredentials, setEditingCredentials] = useState(false);

  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      setLoadError("");
      const result = await fiscalConfigurationApi.getOnboarding(businessId);
      setValue(result);
      setSoftwareId(result.softwareIdentificationCode ?? "");
      setTestSetId(result.testSetId ?? "");
      setEditingCredentials(!result.hasCertificate);
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "No fue posible cargar la configuración fiscal.";
      setLoadError(errorMessage);
      if (silent) toast.error(errorMessage);
    } finally {
      if (!silent) setLoading(false);
    }
  }, [businessId]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => fiscalConfigurationApi.subscribeToOnboarding(
    businessId,
    () => { void load(true); },
  ), [businessId, load]);

  useEffect(() => {
    if (value?.stage !== "HabilitationReady") return;
    const timer = window.setInterval(() => { void load(true); }, 30_000);
    return () => window.clearInterval(timer);
  }, [load, value?.stage]);

  const available = useMemo(
    () => value?.availableRanges.filter((item) => item.isAvailable) ?? [],
    [value],
  );

  async function saveHabilitation(event: React.FormEvent) {
    event.preventDefault();
    if (!certificate) return toast.error("Selecciona el certificado PFX o P12.");
    setSaving(true);
    try {
      setCredentialError("");
      const result = await fiscalConfigurationApi.configureHabilitation(businessId, {
        softwareIdentificationCode: softwareId,
        softwarePin,
        testSetId,
        certificatePassword,
        certificate,
      });
      setValue(result);
      setSoftwarePin("");
      setCertificatePassword("");
      setCertificate(null);
      setEditingCredentials(false);
      toast.success("Credenciales DIAN verificadas y almacenadas de forma segura.");
    } catch (error) {
      const message = error instanceof Error ? error.message : "No fue posible guardar la configuración.";
      setCredentialError(message);
      toast.error(message);
    } finally {
      setSaving(false);
    }
  }

  async function synchronize() {
    setSyncing(true);
    try {
      const result = await fiscalConfigurationApi.synchronizeNumberingRanges(businessId);
      setValue(result);
      toast.success("Resoluciones sincronizadas directamente desde la DIAN.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible consultar las resoluciones DIAN.");
    } finally {
      setSyncing(false);
    }
  }

  async function activateSupportDocument() {
    if (!selectedSupportRangeId || !supportConfirmed) return;
    setActivatingSupport(true);
    try {
      const result = await fiscalConfigurationApi.activateSupportDocument(
        businessId,
        selectedSupportRangeId,
      );
      setValue(result);
      setSupportConfirmed(false);
      toast.success("Resolución de documento soporte activada para esta sede.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible activar documento soporte.");
    } finally {
      setActivatingSupport(false);
    }
  }

  async function activateProduction() {
    setActivatingProduction(true);
    try {
      const result = await fiscalConfigurationApi.activateProduction(businessId);
      setValue(result);
      toast.success("Producción DIAN activada. Cada caja habilitará factura electrónica cuando tenga su propia resolución.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible activar producción.");
    } finally {
      setActivatingProduction(false);
    }
  }

  function startHabilitationInvoice() {
    router.push("/pos?fiscalHabilitation=1");
  }

  if (loading) {
    return <Card><CardContent className="flex min-h-40 items-center justify-center gap-2"><Loader2 className="h-5 w-5 animate-spin" /> Verificando DIAN…</CardContent></Card>;
  }

  if (!value) {
    return <Card className="border-amber-200"><CardContent className="flex min-h-40 flex-col items-center justify-center gap-3 p-6 text-center"><p className="font-medium text-amber-950">{loadError || "No fue posible cargar la configuración fiscal."}</p><Button type="button" variant="outline" onClick={() => void load()}><RefreshCw className="mr-2 h-4 w-4" />Volver a intentar</Button></CardContent></Card>;
  }

  const missingLegalProfile = value.missingRequirements.includes("PerfilLegal");
  const feedbackKind = habilitationFeedbackKind(value.latestHabilitationAttempt);

  return (
    <div className="space-y-5">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2"><ShieldCheck className="h-5 w-5 text-primary" /> Activación de facturación electrónica</CardTitle>
          <CardDescription>{value.businessName} · {value.legalName}{value.supplierTaxId ? ` · NIT ${value.supplierTaxId}-${value.supplierCheckDigit}` : " · Perfil tributario pendiente"}</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 md:grid-cols-4">
            <Stage number="1" label="Certificado y software" active={value.stage !== "NotConfigured"} />
            <Stage number="2" label="Habilitación DIAN" active={value.habilitationAccepted} />
            <Stage number="3" label="Preparar resoluciones" active={value.stage === "ProductionReady" || value.productionActive} />
            <Stage number="4" label="Activar producción" active={value.productionActive} />
          </div>
        </CardContent>
      </Card>

      <Card className="overflow-hidden border-slate-200 bg-white">
        <CardContent className="p-5 md:p-6">
          <div className="flex flex-col justify-between gap-5 lg:flex-row lg:items-center">
            <div><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-700">Ambiente fiscal</p><h2 className="mt-1 text-xl font-black text-slate-950">De pruebas reales a producción, sin atajos</h2><p className="mt-1 max-w-2xl text-sm text-slate-600">La habilitación usa únicamente el TestSetId y siempre está conectada. Tras la aceptación, el operador activa producción; las resoluciones de cada caja se preparan y sincronizan por separado.</p></div>
            <div className="grid min-w-[min(100%,32rem)] grid-cols-2 rounded-2xl border border-slate-200 bg-slate-50 p-1.5">
              <EnvironmentMode icon={FlaskConical} title="Habilitación" subtitle={value.habilitationAccepted ? "Set aceptado" : "Pruebas DIAN"} active={!value.productionActive} complete={value.habilitationAccepted} />
              <EnvironmentMode icon={value.productionActive ? Rocket : LockKeyhole} title="Producción" subtitle={value.productionActive ? "Emisión activa" : value.habilitationAccepted ? "Lista para activar" : "Bloqueada"} active={value.productionActive} complete={value.productionActive} />
            </div>
          </div>
        </CardContent>
      </Card>

      {missingLegalProfile && <Card className="border-amber-200 bg-amber-50"><CardContent className="p-5 text-sm text-amber-950"><b className="block">Completa primero el perfil tributario de la empresa</b><p className="mt-1">La razón social, el NIT y el dígito de verificación son necesarios para configurar la facturación electrónica.</p></CardContent></Card>}

      {!value.productionActive && !missingLegalProfile && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2"><Upload className="h-5 w-5 text-primary" /> 1. Certificado y software DIAN</CardTitle>
            <CardDescription>Auraly toma razón social, NIT y dirección del perfil legal. Sólo pide las credenciales que no puede derivar.</CardDescription>
          </CardHeader>
          <CardContent>
            <form className="grid gap-4 md:grid-cols-2" onSubmit={saveHabilitation}>
              <Field label="Software ID"><Input required disabled={!editingCredentials} value={softwareId} onChange={(event) => setSoftwareId(event.target.value)} /></Field>
              <Field label="TestSetId"><Input required disabled={!editingCredentials} value={testSetId} onChange={(event) => setTestSetId(event.target.value)} /></Field>
              <Field label="PIN del software"><Input required={editingCredentials} disabled={!editingCredentials} type="password" autoComplete="new-password" value={editingCredentials ? softwarePin : "••••••••"} onChange={(event) => setSoftwarePin(event.target.value)} /></Field>
              <Field label="Certificado PFX/P12"><Input required={editingCredentials} disabled={!editingCredentials} accept=".pfx,.p12,application/x-pkcs12" type="file" onChange={(event) => { setCertificate(event.target.files?.[0] ?? null); setCredentialError(""); }} /></Field>
              <Field label="Contraseña del certificado"><Input required={editingCredentials} disabled={!editingCredentials} type="password" autoComplete="new-password" value={editingCredentials ? certificatePassword : "••••••••"} onChange={(event) => setCertificatePassword(event.target.value)} /></Field>
              <div className="flex items-end">{value.hasCertificate && !editingCredentials ? <Button className="w-full" variant="outline" disabled={!canManage} type="button" onClick={() => setEditingCredentials(true)}><Pencil className="mr-2 h-4 w-4" />Editar configuración</Button> : <Button className="w-full" disabled={!canManage || saving} type="submit">{saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <FileKey2 className="mr-2 h-4 w-4" />} Validar y guardar</Button>}</div>
            </form>
            {credentialError && <p role="alert" className="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-sm font-medium text-red-900">{credentialError}</p>}
            {value.hasCertificate && <p className="mt-4 rounded-xl bg-emerald-50 p-3 text-sm text-emerald-900">Certificado ••••{value.certificateThumbprintSuffix} válido hasta {formatDate(value.certificateValidTo)}. El PIN, la contraseña y la clave privada nunca se muestran.</p>}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>2. Habilitación DIAN</CardTitle>
          <CardDescription>Las facturas de prueba pasan por el mismo coordinador, firma, workers y transporte DIAN de Auraly.</CardDescription>
        </CardHeader>
        <CardContent>
          {value.habilitationAccepted ? (
            <p className="flex items-center gap-2 rounded-xl bg-emerald-50 p-4 text-sm font-medium text-emerald-900"><CheckCircle2 className="h-5 w-5" /> Set de pruebas aceptado por la DIAN {value.habilitationAcceptedAt ? `el ${formatDate(value.habilitationAcceptedAt)}` : ""}.</p>
          ) : value.stage === "HabilitationReady" ? (
            <div className="overflow-hidden rounded-2xl border border-violet-200 bg-white p-5">
              <div className="flex flex-col justify-between gap-5 md:flex-row md:items-center"><div><p className="flex items-center gap-2 font-bold text-violet-950"><FlaskConical className="h-5 w-5" />Asistente de habilitación</p><p className="mt-1 max-w-2xl text-sm text-slate-600">Abre la caja con factura electrónica protegida en ambiente de pruebas. La venta recorre numeración, UBL, firma, worker y envío real al TestSetId.</p></div><Button disabled={!canManage} onClick={startHabilitationInvoice} className="h-11 shrink-0 bg-violet-700 px-5 hover:bg-violet-800"><Rocket className="mr-2 h-4 w-4" />Emitir factura de habilitación</Button></div>
              {feedbackKind === "failure" ? (
                <div className="mt-4 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-950">
                  <p className="flex items-center gap-2 font-bold"><AlertTriangle className="h-5 w-5" />La prueba de habilitación falló</p>
                  <p className="mt-1">{value.latestHabilitationAttempt?.errorMessage ?? "El proceso fiscal terminó con un error."}</p>
                  <p className="mt-2 text-xs text-red-800">Estado: {value.latestHabilitationAttempt?.status}{value.latestHabilitationAttempt?.errorCode ? ` · Código: ${value.latestHabilitationAttempt.errorCode}` : ""}. Corrige la configuración y emite otra factura de habilitación.</p>
                </div>
              ) : feedbackKind === "processing" ? (
                <div className="mt-4 flex items-center gap-3 rounded-xl bg-slate-50 p-3 text-xs text-slate-600"><Loader2 className="h-4 w-4 animate-spin text-violet-600" />Procesando la prueba fiscal: {value.latestHabilitationAttempt?.status}.</div>
              ) : (
                <div className="mt-4 rounded-xl bg-slate-50 p-3 text-xs text-slate-600">Emite una factura de prueba. La confirmación o el error aparecerán aquí automáticamente.</div>
              )}
            </div>
          ) : (
            <p className="rounded-xl bg-muted p-4 text-sm text-muted-foreground">Primero carga y valida las credenciales.</p>
          )}
        </CardContent>
      </Card>

      {value.habilitationAccepted && !value.productionActive && (
        <Card className="border-teal-200 bg-teal-50/40">
          <CardHeader><CardTitle className="flex items-center gap-2"><Rocket className="h-5 w-5 text-teal-700"/>4. Activar producción</CardTitle><CardDescription>Este cambio habilita el ambiente productivo de la sede. No exige resoluciones en todas las cajas: cada emisor habilitará factura electrónica únicamente cuando tenga una resolución propia.</CardDescription></CardHeader>
          <CardContent><Button disabled={!canManage || activatingProduction} onClick={() => void activateProduction()}>{activatingProduction ? <Loader2 className="mr-2 h-4 w-4 animate-spin"/> : <Rocket className="mr-2 h-4 w-4"/>}Activar producción DIAN</Button></CardContent>
        </Card>
      )}

      {value.productionActive && (
        <Card className="border-emerald-200 bg-emerald-50/50">
          <CardHeader><CardTitle className="flex items-center gap-2 text-emerald-950"><CheckCircle2 className="h-5 w-5" /> Producción DIAN activa</CardTitle></CardHeader>
          <CardContent>{value.assignedRange ? <div className="grid gap-3 text-sm md:grid-cols-3"><Detail label="Resolución online" value={value.assignedRange.authorizationNumber} /><Detail label="Prefijo y rango" value={`${value.assignedRange.prefix}${value.assignedRange.rangeStart}–${value.assignedRange.rangeEnd}`} /><Detail label="Vigencia" value={`${value.assignedRange.validFrom} a ${value.assignedRange.validUntil}`} /></div> : <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-950">Producción está activa, pero la caja online todavía no tiene resolución. Solo podrá usar comprobantes hasta asignarle una.</p>}</CardContent>
        </Card>
      )}

      {value.productionActive && (
        <Card>
          <CardHeader>
            <CardTitle>Documento soporte electrónico</CardTitle>
            <CardDescription>Usa una resolución DIAN independiente. Las recepciones configuradas como documento soporte consumirán esta numeración y recorrerán el motor fiscal existente.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {value.assignedSupportDocumentRange ? (
              <div className="grid gap-3 rounded-xl bg-emerald-50 p-4 text-sm md:grid-cols-3"><Detail label="Resolución" value={value.assignedSupportDocumentRange.authorizationNumber}/><Detail label="Prefijo y rango" value={`${value.assignedSupportDocumentRange.prefix}${value.assignedSupportDocumentRange.rangeStart}–${value.assignedSupportDocumentRange.rangeEnd}`}/><Detail label="Vigencia" value={`${value.assignedSupportDocumentRange.validFrom} a ${value.assignedSupportDocumentRange.validUntil}`}/></div>
            ) : (
              <>
                <Button variant="outline" disabled={!canManage || syncing} onClick={() => void synchronize()}>{syncing ? <Loader2 className="mr-2 h-4 w-4 animate-spin"/> : <RefreshCw className="mr-2 h-4 w-4"/>} Consultar resoluciones en DIAN</Button>
                {available.length > 0 ? <><Field label="Resolución de documento soporte"><Select value={selectedSupportRangeId||undefined} onValueChange={item=>{setSelectedSupportRangeId(item);setSupportConfirmed(false)}}><SelectTrigger><SelectValue placeholder="Selecciona la resolución de documento soporte"/></SelectTrigger><SelectContent>{available.map(item=><SelectItem key={item.dianNumberingRangeId} value={item.dianNumberingRangeId}>{item.authorizationNumber} · {item.prefix}{item.rangeStart}–{item.rangeEnd} · vence {item.validUntil}</SelectItem>)}</SelectContent></Select></Field><label className="flex items-start justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950"><span>Confirmo que la resolución seleccionada corresponde a <b>documento soporte</b> para {value.businessName}.</span><Switch checked={supportConfirmed} onCheckedChange={setSupportConfirmed}/></label><Button disabled={!canManage||!selectedSupportRangeId||!supportConfirmed||activatingSupport} onClick={() => void activateSupportDocument()}>{activatingSupport&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>} Activar documento soporte</Button></> : <p className="text-sm text-muted-foreground">No hay resoluciones libres. Solicita y asocia la numeración de documento soporte en el portal DIAN, luego vuelve a consultar.</p>}
              </>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function Stage({ number, label, active }: { number: string; label: string; active: boolean }) { return <div className={`rounded-xl border p-3 text-sm ${active ? "border-emerald-200 bg-emerald-50 text-emerald-950" : "text-muted-foreground"}`}><b className="mr-2">{number}.</b>{label}</div>; }
function Field({ label, children }: { label: string; children: React.ReactNode }) { return <label className="space-y-2"><Label>{label}</Label>{children}</label>; }
function Detail({ label, value }: { label: string; value: string }) { return <div><span className="block text-xs text-muted-foreground">{label}</span><b>{value}</b></div>; }
function EnvironmentMode({ icon: Icon, title, subtitle, active, complete }: { icon: typeof FlaskConical; title: string; subtitle: string; active: boolean; complete: boolean }) { return <div className={`relative rounded-xl p-3 transition ${active ? "bg-gradient-to-br from-teal-200 to-emerald-200 text-slate-950" : "text-slate-500"}`}><div className="flex items-center gap-2"><Icon className="h-5 w-5"/><b>{title}</b>{complete&&<CheckCircle2 className="ml-auto h-4 w-4"/>}</div><p className={`mt-1 text-xs ${active ? "text-slate-700" : "text-slate-500"}`}>{subtitle}</p></div>; }
function formatDate(value: string | null) { return value ? new Intl.DateTimeFormat("es-CO", { dateStyle: "medium" }).format(new Date(value)) : "sin fecha"; }
