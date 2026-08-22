"use client";

import { CheckCircle2, FileKey2, Loader2, RefreshCw, ShieldCheck, Upload } from "lucide-react";
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

type Props = { businessId: string; canManage: boolean };

export function FiscalOnboardingCard({ businessId, canManage }: Props) {
  const [value, setValue] = useState<FiscalOnboardingConfiguration | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [saving, setSaving] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [activating, setActivating] = useState(false);
  const [softwareId, setSoftwareId] = useState("");
  const [softwarePin, setSoftwarePin] = useState("");
  const [testSetId, setTestSetId] = useState("");
  const [certificatePassword, setCertificatePassword] = useState("");
  const [certificate, setCertificate] = useState<File | null>(null);
  const [selectedRangeId, setSelectedRangeId] = useState("");
  const [confirmed, setConfirmed] = useState(false);

  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      setLoadError("");
      const result = await fiscalConfigurationApi.getOnboarding(businessId);
      setValue(result);
      setSoftwareId(result.softwareIdentificationCode ?? "");
      setTestSetId(result.testSetId ?? "");
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : "No fue posible cargar la configuración fiscal.";
      setLoadError(errorMessage);
      if (silent) toast.error(errorMessage);
    } finally {
      if (!silent) setLoading(false);
    }
  }, [businessId]);

  useEffect(() => { void load(); }, [load]);

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
      toast.success("Credenciales DIAN verificadas y almacenadas de forma segura.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible guardar la configuración.");
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

  async function activate() {
    if (!selectedRangeId || !confirmed) return;
    setActivating(true);
    try {
      const result = await fiscalConfigurationApi.activateProduction(businessId, selectedRangeId);
      setValue(result);
      setConfirmed(false);
      toast.success("Producción DIAN activada para esta sede.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible activar producción.");
    } finally {
      setActivating(false);
    }
  }

  if (loading) {
    return <Card><CardContent className="flex min-h-40 items-center justify-center gap-2"><Loader2 className="h-5 w-5 animate-spin" /> Verificando DIAN…</CardContent></Card>;
  }

  if (!value) {
    return <Card className="border-amber-200"><CardContent className="flex min-h-40 flex-col items-center justify-center gap-3 p-6 text-center"><p className="font-medium text-amber-950">{loadError || "No fue posible cargar la configuración fiscal."}</p><Button type="button" variant="outline" onClick={() => void load()}><RefreshCw className="mr-2 h-4 w-4" />Volver a intentar</Button></CardContent></Card>;
  }

  const missingLegalProfile = value.missingRequirements.includes("PerfilLegal");

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
            <Stage number="3" label="Asignar resolución" active={value.stage === "ProductionReady" || value.productionActive} />
            <Stage number="4" label="Producción" active={value.productionActive} />
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
              <Field label="Software ID"><Input required value={softwareId} onChange={(event) => setSoftwareId(event.target.value)} /></Field>
              <Field label="TestSetId"><Input required value={testSetId} onChange={(event) => setTestSetId(event.target.value)} /></Field>
              <Field label="PIN del software"><Input required type="password" autoComplete="new-password" value={softwarePin} onChange={(event) => setSoftwarePin(event.target.value)} /></Field>
              <Field label="Certificado PFX/P12"><Input required accept=".pfx,.p12,application/x-pkcs12" type="file" onChange={(event) => setCertificate(event.target.files?.[0] ?? null)} /></Field>
              <Field label="Contraseña del certificado"><Input required type="password" autoComplete="new-password" value={certificatePassword} onChange={(event) => setCertificatePassword(event.target.value)} /></Field>
              <div className="flex items-end"><Button className="w-full" disabled={!canManage || saving} type="submit">{saving ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <FileKey2 className="mr-2 h-4 w-4" />} Validar y guardar</Button></div>
            </form>
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
            <p className="rounded-xl bg-amber-50 p-4 text-sm text-amber-950">La configuración está lista. Emite las facturas de habilitación; esta pantalla cambiará automáticamente cuando el motor registre la aceptación de DIAN.</p>
          ) : (
            <p className="rounded-xl bg-muted p-4 text-sm text-muted-foreground">Primero carga y valida las credenciales.</p>
          )}
        </CardContent>
      </Card>

      {value.habilitationAccepted && !value.productionActive && (
        <Card>
          <CardHeader>
            <CardTitle>3. Resolución para esta sede</CardTitle>
            <CardDescription>Auraly consulta GetNumberingRange en producción. Una resolución se asigna una sola vez y no puede usarse en otra sede.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <Button variant="outline" disabled={!canManage || syncing} onClick={() => void synchronize()}>{syncing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />} Consultar resoluciones en DIAN</Button>
            {available.length > 0 ? (
              <>
                <Field label="Resolución disponible">
                  <Select value={selectedRangeId||undefined} onValueChange={value=>{setSelectedRangeId(value);setConfirmed(false)}}><SelectTrigger><SelectValue placeholder="Selecciona una resolución"/></SelectTrigger><SelectContent>{available.map((item) => <SelectItem key={item.dianNumberingRangeId} value={item.dianNumberingRangeId}>{item.authorizationNumber} · {item.prefix}{item.rangeStart}–{item.rangeEnd} · vence {item.validUntil}</SelectItem>)}</SelectContent></Select>
                </Field>
                <label className="flex items-start justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950"><span>Confirmo que esta resolución corresponde a <b>{value.businessName}</b>. Al activar quedará reservada para esta sede y no podrá trasladarse desde la aplicación.</span><Switch checked={confirmed} onCheckedChange={setConfirmed}/></label>
                <Button disabled={!canManage || !selectedRangeId || !confirmed || activating} onClick={() => void activate()}>{activating && <Loader2 className="mr-2 h-4 w-4 animate-spin" />} Activar producción</Button>
              </>
            ) : <p className="text-sm text-muted-foreground">No hay resoluciones libres. Solicita y asocia la numeración en el portal DIAN, luego vuelve a consultar.</p>}
            {value.availableRanges.filter((item) => !item.isAvailable).map((item) => <p key={item.dianNumberingRangeId} className="text-xs text-muted-foreground">{item.authorizationNumber} · {item.prefix}: asignada a {item.assignedBusinessName}</p>)}
          </CardContent>
        </Card>
      )}

      {value.productionActive && value.assignedRange && (
        <Card className="border-emerald-200 bg-emerald-50/50">
          <CardHeader><CardTitle className="flex items-center gap-2 text-emerald-950"><CheckCircle2 className="h-5 w-5" /> Producción DIAN activa</CardTitle></CardHeader>
          <CardContent className="grid gap-3 text-sm md:grid-cols-3"><Detail label="Resolución" value={value.assignedRange.authorizationNumber} /><Detail label="Prefijo y rango" value={`${value.assignedRange.prefix}${value.assignedRange.rangeStart}–${value.assignedRange.rangeEnd}`} /><Detail label="Vigencia" value={`${value.assignedRange.validFrom} a ${value.assignedRange.validUntil}`} /></CardContent>
        </Card>
      )}
    </div>
  );
}

function Stage({ number, label, active }: { number: string; label: string; active: boolean }) { return <div className={`rounded-xl border p-3 text-sm ${active ? "border-emerald-200 bg-emerald-50 text-emerald-950" : "text-muted-foreground"}`}><b className="mr-2">{number}.</b>{label}</div>; }
function Field({ label, children }: { label: string; children: React.ReactNode }) { return <label className="space-y-2"><Label>{label}</Label>{children}</label>; }
function Detail({ label, value }: { label: string; value: string }) { return <div><span className="block text-xs text-muted-foreground">{label}</span><b>{value}</b></div>; }
function formatDate(value: string | null) { return value ? new Intl.DateTimeFormat("es-CO", { dateStyle: "medium" }).format(new Date(value)) : "sin fecha"; }
