"use client";

import { CheckCircle2, FileKey2, Loader2, Pencil, ShieldAlert } from "lucide-react";
import { useEffect, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  fiscalConfigurationApi,
  type FiscalIssuerConnectionConfiguration,
  type SaveFiscalIssuerConnectionConfiguration,
} from "@/services/api/fiscal-configuration";
import { useAuthStore } from "@/stores/auth-store";

const today = new Date().toISOString().slice(0, 10);
const nextYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

function emptyForm(): SaveFiscalIssuerConnectionConfiguration {
  return {
    supplierTaxId: "",
    supplierCheckDigit: "",
    legalName: "",
    tradeName: null,
    taxLevelCode: "R-99-PN",
    taxSchemeId: "01",
    taxSchemeName: "IVA",
    identificationTypeCode: "31",
    addressLine: "",
    cityCode: "",
    cityName: "",
    departmentCode: "",
    departmentName: "",
    postalZone: null,
    softwareIdentificationCode: "",
    softwarePinSecretReference: "env://AURALY_DIAN_SOFTWARE_PIN",
    environment: 2,
    testSetId: null,
    certificateProvider: "WindowsCertificateStore",
    certificateKeyReference: "CurrentUser/My",
    certificateThumbprint: "",
    dianEndpoint: "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
    technicalAnnexVersion: "1.9",
    generatorVersion: "Auraly.Commerce",
    validFrom: today,
    validTo: nextYear,
  };
}

function toForm(value: FiscalIssuerConnectionConfiguration): SaveFiscalIssuerConnectionConfiguration {
  return {
    supplierTaxId: value.supplierTaxId ?? "",
    supplierCheckDigit: value.supplierCheckDigit ?? "",
    legalName: value.legalName ?? "",
    tradeName: value.tradeName,
    taxLevelCode: value.taxLevelCode ?? "R-99-PN",
    taxSchemeId: value.taxSchemeId ?? "01",
    taxSchemeName: value.taxSchemeName ?? "IVA",
    identificationTypeCode: value.identificationTypeCode ?? "31",
    addressLine: value.addressLine ?? "",
    cityCode: value.cityCode ?? "",
    cityName: value.cityName ?? "",
    departmentCode: value.departmentCode ?? "",
    departmentName: value.departmentName ?? "",
    postalZone: value.postalZone,
    softwareIdentificationCode: value.softwareIdentificationCode ?? "",
    softwarePinSecretReference: value.softwarePinSecretReference ?? "env://AURALY_DIAN_SOFTWARE_PIN",
    environment: value.environment ?? 2,
    testSetId: value.testSetId,
    certificateProvider: "WindowsCertificateStore",
    certificateKeyReference: value.certificateKeyReference ?? "CurrentUser/My",
    certificateThumbprint: value.certificateThumbprint ?? "",
    dianEndpoint: value.dianEndpoint ?? "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
    technicalAnnexVersion: value.technicalAnnexVersion ?? "1.9",
    generatorVersion: value.generatorVersion ?? "Auraly.Commerce",
    validFrom: value.validFrom?.slice(0, 10) ?? today,
    validTo: value.validTo?.slice(0, 10) ?? nextYear,
  };
}

export function FiscalIssuerConnectionCard({ businessId }: { businessId: string }) {
  const canManage = useAuthStore((state) =>
    state.user?.permissions?.includes("fiscal.configuration.manage") ?? false,
  );
  const [value, setValue] = useState<FiscalIssuerConnectionConfiguration | null>(null);
  const [form, setForm] = useState<SaveFiscalIssuerConnectionConfiguration>(emptyForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    let active = true;
    setLoading(true);
    fiscalConfigurationApi
      .getIssuerConnection(businessId)
      .then((result) => {
        if (!active) return;
        setValue(result);
        setForm(toForm(result));
      })
      .catch((error: unknown) => {
        if (active) toast.error(error instanceof Error ? error.message : "No fue posible cargar el emisor fiscal.");
      })
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [businessId]);

  const set = <K extends keyof SaveFiscalIssuerConnectionConfiguration>(
    key: K,
    next: SaveFiscalIssuerConnectionConfiguration[K],
  ) => setForm((current) => ({ ...current, [key]: next }));

  async function save() {
    setSaving(true);
    try {
      const payload = {
        ...form,
        validFrom: `${form.validFrom.slice(0, 10)}T00:00:00-05:00`,
        validTo: form.validTo ? `${form.validTo.slice(0, 10)}T23:59:59-05:00` : null,
      };
      const saved = await fiscalConfigurationApi.saveIssuerConnection(businessId, payload);
      setValue(saved);
      setForm(toForm(saved));
      setOpen(false);
      toast.success("Emisor y conexión DIAN actualizados");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible guardar la conexión DIAN.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileKey2 className="h-5 w-5 text-primary" /> Emisor y conexión DIAN
          </CardTitle>
          <CardDescription>Datos de habilitación usados por el motor fiscal del servidor.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {loading ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" /> Verificando configuración…
            </p>
          ) : (
            <>
              <div className={`flex gap-3 rounded-xl border p-3 text-sm ${value?.isReadyForHabilitation ? "border-emerald-200 bg-emerald-50 text-emerald-900" : "border-amber-200 bg-amber-50 text-amber-950"}`}>
                {value?.isReadyForHabilitation ? <CheckCircle2 className="h-5 w-5 shrink-0" /> : <ShieldAlert className="h-5 w-5 shrink-0" />}
                <div>
                  <b>{value?.isReadyForHabilitation ? "Datos requeridos registrados" : "Faltan datos para habilitación"}</b>
                  {value?.isReadyForHabilitation ? (
                    <p className="mt-1 text-xs">La conectividad real se valida únicamente al ejecutar el set de pruebas DIAN.</p>
                  ) : null}
                  {!value?.isReadyForHabilitation && value?.missingRequirements?.length ? (
                    <p className="mt-1 text-xs">{value.missingRequirements.join(" · ")}</p>
                  ) : null}
                </div>
              </div>
              <div className="grid gap-2 text-sm sm:grid-cols-2">
                <Detail label="Emisor" value={value?.legalName} />
                <Detail label="NIT" value={value?.supplierTaxId} />
                <Detail label="Software" value={value?.softwareIdentificationCode} />
                <Detail label="Anexo" value={value?.technicalAnnexVersion} />
                <Detail label="TestSetId" value={value?.testSetId} />
                <Detail label="Certificado" value={value?.certificateThumbprint ? `••••${value.certificateThumbprint.slice(-8)}` : null} />
              </div>
              <Button className="w-full" disabled={!canManage} onClick={() => setOpen(true)}>
                <Pencil className="mr-2 h-4 w-4" />
                {value?.isConfigured ? "Editar conexión DIAN" : "Configurar conexión DIAN"}
              </Button>
            </>
          )}
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-h-[92vh] max-w-4xl overflow-y-auto">
          <DialogHeader><DialogTitle>Emisor y conexión de habilitación DIAN</DialogTitle></DialogHeader>
          <form className="space-y-6" onSubmit={(event) => { event.preventDefault(); void save(); }}>
            <Group title="Identidad tributaria">
              <Field label="NIT"><Input required value={form.supplierTaxId} onChange={(event) => set("supplierTaxId", event.target.value)} /></Field>
              <Field label="Dígito de verificación"><Input required maxLength={2} value={form.supplierCheckDigit} onChange={(event) => set("supplierCheckDigit", event.target.value)} /></Field>
              <Field label="Razón social"><Input required value={form.legalName} onChange={(event) => set("legalName", event.target.value)} /></Field>
              <Field label="Nombre comercial"><Input value={form.tradeName ?? ""} onChange={(event) => set("tradeName", event.target.value || null)} /></Field>
              <Field label="Responsabilidad fiscal"><Input required value={form.taxLevelCode} onChange={(event) => set("taxLevelCode", event.target.value)} /></Field>
              <Field label="Tipo de identificación"><Input required value={form.identificationTypeCode} onChange={(event) => set("identificationTypeCode", event.target.value)} /></Field>
              <Field label="Código de impuesto"><Input required value={form.taxSchemeId} onChange={(event) => set("taxSchemeId", event.target.value)} /></Field>
              <Field label="Nombre del impuesto"><Input required value={form.taxSchemeName} onChange={(event) => set("taxSchemeName", event.target.value)} /></Field>
            </Group>
            <Group title="Ubicación fiscal">
              <Field label="Dirección"><Input required value={form.addressLine} onChange={(event) => set("addressLine", event.target.value)} /></Field>
              <Field label="Código de ciudad"><Input required value={form.cityCode} onChange={(event) => set("cityCode", event.target.value)} /></Field>
              <Field label="Ciudad"><Input required value={form.cityName} onChange={(event) => set("cityName", event.target.value)} /></Field>
              <Field label="Código de departamento"><Input required value={form.departmentCode} onChange={(event) => set("departmentCode", event.target.value)} /></Field>
              <Field label="Departamento"><Input required value={form.departmentName} onChange={(event) => set("departmentName", event.target.value)} /></Field>
              <Field label="Código postal"><Input value={form.postalZone ?? ""} onChange={(event) => set("postalZone", event.target.value || null)} /></Field>
            </Group>
            <Group title="Software y set de pruebas">
              <Field label="SoftwareIdentificationCode"><Input required value={form.softwareIdentificationCode} onChange={(event) => set("softwareIdentificationCode", event.target.value)} /></Field>
              <Field label="TestSetId"><Input required value={form.testSetId ?? ""} onChange={(event) => set("testSetId", event.target.value || null)} /></Field>
              <Field label="Variable segura del PIN"><Input required value={form.softwarePinSecretReference.replace(/^env:\/\//i, "")} onChange={(event) => set("softwarePinSecretReference", `env://${event.target.value.replace(/^env:\/\//i, "")}`)} /></Field>
              <Field label="Ambiente"><Select value={String(form.environment)} onValueChange={(next) => set("environment", Number(next))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="2">Habilitación</SelectItem><SelectItem value="1">Producción</SelectItem></SelectContent></Select></Field>
              <Field label="Endpoint DIAN"><Input required type="url" value={form.dianEndpoint} onChange={(event) => set("dianEndpoint", event.target.value)} /></Field>
              <Field label="Anexo técnico"><Input required readOnly value={form.technicalAnnexVersion} /></Field>
            </Group>
            <Group title="Certificado del servidor">
              <Field label="Almacén de Windows"><Select value={form.certificateKeyReference} onValueChange={(next) => set("certificateKeyReference", next)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="CurrentUser/My">Usuario actual</SelectItem><SelectItem value="LocalMachine/My">Equipo local</SelectItem></SelectContent></Select></Field>
              <Field label="Huella SHA del certificado"><Input required value={form.certificateThumbprint} onChange={(event) => set("certificateThumbprint", event.target.value.toUpperCase())} /></Field>
              <Field label="Válido desde"><Input required type="date" value={form.validFrom.slice(0, 10)} onChange={(event) => set("validFrom", event.target.value)} /></Field>
              <Field label="Válido hasta"><Input type="date" value={form.validTo?.slice(0, 10) ?? ""} onChange={(event) => set("validTo", event.target.value || null)} /></Field>
            </Group>
            <p className="rounded-xl bg-slate-100 p-3 text-xs text-slate-600">
              El PIN y la contraseña del certificado no se guardan aquí. Auraly solo conserva referencias seguras y el certificado permanece en el servidor.
            </p>
            <Button className="w-full" type="submit" disabled={saving}>
              {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />} Guardar conexión DIAN
            </Button>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return <fieldset className="grid gap-4 rounded-2xl border p-4 sm:grid-cols-2"><legend className="px-2 text-sm font-bold">{title}</legend>{children}</fieldset>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="space-y-2"><Label>{label}</Label>{children}</label>;
}

function Detail({ label, value }: { label: string; value?: string | null }) {
  return <div className="rounded-lg bg-muted/50 p-2"><span className="block text-xs text-muted-foreground">{label}</span><b className="break-all">{value || "Sin configurar"}</b></div>;
}
