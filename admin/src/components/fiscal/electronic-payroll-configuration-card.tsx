"use client";

import { Loader2, ReceiptText } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { payrollApi, type PayrollOptions } from "@/services/api/payroll";
import { useTenantContextStore } from "@/stores/tenant-context-store";

type Props = { businessId: string; canManage: boolean };

export function ElectronicPayrollConfigurationCard({ businessId, canManage }: Props) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  return <ElectronicPayrollConfigurationForm key={`${tenantId}:${businessId}`} businessId={businessId} canManage={canManage}/>;
}

function ElectronicPayrollConfigurationForm({ businessId, canManage }: Props) {
  const [options, setOptions] = useState<PayrollOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [prefix, setPrefix] = useState("");
  const [nextConsecutive, setNextConsecutive] = useState(1);
  const [qrValidationUrl, setQrValidationUrl] = useState("");
  const [active, setActive] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await payrollApi.options();
      setOptions(result);
      setPrefix(result.electronicConfiguration?.prefix ?? "NIE");
      setNextConsecutive(result.electronicConfiguration?.nextConsecutive ?? 1);
      setQrValidationUrl(result.electronicConfiguration?.qrValidationUrl ??
        "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=");
      setActive(result.electronicConfiguration?.isActive ?? true);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible cargar la serie de nómina electrónica.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function save() {
    if (!options) return;
    const current = options.electronicConfiguration;
    const issuer = options.fiscalIssuers.find(item => item.isActive &&
      item.fiscalIssuerConfigurationId === current?.fiscalIssuerConfigurationId) ??
      options.fiscalIssuers.find(item => item.isActive);
    if (!issuer) return toast.error("Completa primero el certificado y software DIAN.");
    setSaving(true);
    try {
      await payrollApi.saveElectronicConfiguration({
        businessId,
        fiscalIssuerConfigurationId: issuer.fiscalIssuerConfigurationId,
        softwareIdentificationCode: current?.softwareIdentificationCode ?? issuer.softwareIdentificationCode,
        softwarePinSecretReference: current?.softwarePinSecretReference ?? issuer.softwarePinSecretReference,
        testSetId: current ? current.testSetId : issuer.testSetId,
        prefix,
        nextConsecutive,
        qrValidationUrl,
        isActive: active,
        rowVersion: current?.rowVersion ?? null,
      });
      toast.success("Serie guardada. Se conservaron las credenciales de nómina electrónica.");
      await load();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible guardar la serie de nómina.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <Card><CardContent className="flex min-h-32 items-center justify-center gap-2"><Loader2 className="h-4 w-4 animate-spin" />Cargando nómina electrónica…</CardContent></Card>;
  const issuer = options?.fiscalIssuers.find(item => item.isActive &&
    item.fiscalIssuerConfigurationId === options.electronicConfiguration?.fiscalIssuerConfigurationId) ??
    options?.fiscalIssuers.find(item => item.isActive);
  return <Card>
    <CardHeader>
      <CardTitle className="flex items-center gap-2"><ReceiptText className="h-5 w-5 text-primary" />Nómina electrónica</CardTitle>
      <CardDescription>Administra la serie de nómina. Conserva su software y habilitación propios; el certificado y el ambiente provienen de la configuración DIAN.</CardDescription>
    </CardHeader>
    <CardContent className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border bg-muted/20 p-3 text-sm md:col-span-2">
        <b>{issuer ? `${issuer.legalName} · v${issuer.version}` : "Configuración DIAN pendiente"}</b>
        <p className="text-muted-foreground">{issuer ? `${issuer.environment === 1 ? "Producción" : "Habilitación"} · credenciales protegidas` : "Guarda primero el certificado y software DIAN."}</p>
        {issuer && !options?.electronicConfiguration && <p className="mt-2">La serie inicial utilizará el software configurado arriba. Verifica que esté habilitado para nómina antes de transmitir.</p>}
      </div>
      <Field label="Prefijo de nómina"><Input disabled={!canManage} maxLength={10} value={prefix} onChange={event => setPrefix(event.target.value.toUpperCase())} /></Field>
      <Field label="Siguiente consecutivo"><Input disabled={!canManage} type="number" min={1} value={nextConsecutive} onChange={event => setNextConsecutive(event.currentTarget.valueAsNumber || 1)} /></Field>
      <Field label="URL de consulta QR"><Input disabled={!canManage} type="url" value={qrValidationUrl} onChange={event => setQrValidationUrl(event.target.value)} /></Field>
      <label className="flex items-center justify-between gap-3 rounded-xl border p-3 text-sm"><span><b className="block">Serie activa</b><span className="text-muted-foreground">Permite consolidar y transmitir períodos.</span></span><Switch disabled={!canManage} checked={active} onCheckedChange={setActive} /></label>
      <div className="flex justify-end md:col-span-2"><Button disabled={!canManage || saving || !issuer || !prefix || !qrValidationUrl || nextConsecutive < 1} onClick={() => void save()}>{saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Guardar serie de nómina</Button></div>
    </CardContent>
  </Card>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block space-y-2"><span className="block text-sm font-medium">{label}</span>{children}</label>;
}
