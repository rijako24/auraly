"use client";

import { useEffect, useMemo, useState } from "react";
import { ImageUp } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { TenantBrand } from "@/components/brand/tenant-brand";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { tenantsApi } from "@/services/api/tenants";
import type { Tenant } from "@/types/entities";
import { calculateTenantVerificationDigit, sanitizeTenantIdentification, supportsTenantVerificationDigit, validateTenantIdentification } from "@/lib/tenant-legal-identity";

type Props = { tenant: Tenant; open: boolean; onOpenChange: (open: boolean) => void; onSaved: () => Promise<unknown> };
type TenantEditForm = {
  name: string; email: string; legalName: string; identification: string; verificationDigit: string;
  entityType: "NaturalPerson" | "Organization"; identificationTypeCode: NonNullable<Tenant["identificationTypeCode"]>;
  inventoryCostBasis: "LatestReceiptCost" | "WeightedAverageCost";
  allowPromotionChannelCombination: boolean;
};

export function TenantEditDialog({ tenant, open, onOpenChange, onSaved }: Props) {
  const entityTypes = useReferenceOptions("tenant-entity-type", open);
  const identificationTypes = useReferenceOptions("tenant-identification-type", open);
  const [form, setForm] = useState(() => initial(tenant));
  const [logo, setLogo] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);
  const preview = useMemo(() => logo ? URL.createObjectURL(logo) : tenant.logoUrl, [logo, tenant.logoUrl]);

  useEffect(() => () => { if (logo && preview) URL.revokeObjectURL(preview); }, [logo, preview]);
  useEffect(() => { if (open) { setForm(initial(tenant)); setLogo(null); } }, [open, tenant]);

  const set = (key: keyof typeof form, value: string | boolean) => setForm(current => ({ ...current, [key]: value }));
  const availableIdentificationTypes = (identificationTypes.data ?? [])
    .filter(item => item.description === form.entityType);
  const identityMatches = availableIdentificationTypes.some(item => item.code === form.identificationTypeCode);
  const calculatedVerificationDigit = calculateTenantVerificationDigit(form.identificationTypeCode, form.identification);
  const identityError = form.identification
    ? validateTenantIdentification(form.identificationTypeCode, form.identification, form.identificationTypeCode === "NIT" && calculatedVerificationDigit !== null ? String(calculatedVerificationDigit) : null)
    : null;
  const valid = Boolean(form.name.trim() && form.email.trim() && form.legalName.trim()
    && form.identification.trim() && identityMatches && !identityError
    && (form.identificationTypeCode !== "NIT" || calculatedVerificationDigit !== null));

  async function save() {
    if (!valid || saving) return;
    setSaving(true);
    let profileSaved = false;
    try {
      await tenantsApi.update(tenant.tenantId, {
        name: form.name.trim(), email: form.email.trim(), legalName: form.legalName.trim(),
        nit: form.identification.trim(), verificationDigit: form.identificationTypeCode === "NIT" && calculatedVerificationDigit !== null ? String(calculatedVerificationDigit) : null,
        entityType: form.entityType, identificationTypeCode: form.identificationTypeCode,
        inventoryCostBasis: form.inventoryCostBasis as Tenant["inventoryCostBasis"],
        allowPromotionChannelCombination: form.allowPromotionChannelCombination,
      });
      profileSaved = true;
      if (logo) await tenantsApi.uploadLogo(tenant.tenantId, logo);
      await onSaved();
      onOpenChange(false);
      toast.success("Información del tenant actualizada");
    } catch (error) {
      if (profileSaved) await onSaved();
      toast.error(profileSaved ? "La información se guardó, pero no fue posible cargar el logo." : errorMessage(error));
    } finally { setSaving(false); }
  }

  return <Dialog open={open} onOpenChange={value => !saving && onOpenChange(value)}>
    <DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto">
      <DialogHeader><DialogTitle>Editar tenant</DialogTitle><DialogDescription>Actualiza la identidad, los datos de contacto y la marca que aparecerá en los reportes.</DialogDescription></DialogHeader>
      <div className="space-y-6">
        <section className="grid gap-4 sm:grid-cols-[11rem_1fr] sm:items-center">
          <div className="grid h-28 place-items-center overflow-hidden rounded-xl border bg-white">
            {preview ? <TenantBrand displayName={form.name || tenant.name} logoUrl={preview} showName={false} imageClassName="h-28 w-44 border-0" /> : <ImageUp className="h-8 w-8 text-muted-foreground" />}
          </div>
          <div className="space-y-2"><Label htmlFor="tenant-logo">Logo del tenant</Label><Input id="tenant-logo" type="file" accept="image/jpeg,image/png,image/webp" onChange={event => setLogo(event.target.files?.[0] ?? null)} /><p className="text-xs text-muted-foreground">JPG, PNG o WEBP, máximo 4 MB. Se usa en todos los reportes.</p></div>
        </section>
        <section className="grid gap-4 sm:grid-cols-2">
          <Field label="Tipo de persona"><Select value={form.entityType} onValueChange={value => { const entityType = value as TenantEditForm["entityType"]; const identificationTypeCode = (identificationTypes.data ?? []).find(item => item.description === entityType)?.code as TenantEditForm["identificationTypeCode"] | undefined; setForm(current => ({ ...current, entityType, identificationTypeCode: identificationTypeCode ?? (entityType === "Organization" ? "NIT" : "CC"), verificationDigit: "" })); }}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{(entityTypes.data ?? []).map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
          <Field label="Tipo de identificación"><Select value={form.identificationTypeCode} onValueChange={value => set("identificationTypeCode", value as TenantEditForm["identificationTypeCode"])}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{availableIdentificationTypes.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select>{!identityMatches && <p className="text-xs text-destructive">Selecciona un documento válido para el tipo de persona.</p>}</Field>
          <Field label="Nombre comercial"><Input value={form.name} onChange={event => set("name", event.target.value)} /></Field>
          <Field label={form.entityType === "NaturalPerson" ? "Nombre completo" : "Razón social"}><Input value={form.legalName} onChange={event => set("legalName", event.target.value)} /></Field>
          <Field label="Número de identificación"><Input inputMode={form.identificationTypeCode === "PA" || form.identificationTypeCode === "DE" ? "text" : "numeric"} maxLength={32} value={form.identification} onChange={event => set("identification", sanitizeTenantIdentification(form.identificationTypeCode, event.target.value))} />{identityError && form.identificationTypeCode !== "NIT" && <p className="text-xs text-destructive">{identityError}</p>}</Field>
          {supportsTenantVerificationDigit(form.identificationTypeCode) && <Field label="Dígito de verificación (calculado)"><Input aria-readonly="true" readOnly value={calculatedVerificationDigit ?? ""} className="bg-muted" />{identityError && <p className="text-xs text-destructive">{identityError}</p>}<p className="text-xs text-muted-foreground">Se calcula automáticamente y no se puede modificar.</p></Field>}
          <Field label="Correo empresarial" className="sm:col-span-2"><Input type="email" value={form.email} onChange={event => set("email", event.target.value)} /></Field>
          <Field label="Base para formar costos" className="sm:col-span-2"><Select value={form.inventoryCostBasis} onValueChange={value => set("inventoryCostBasis", value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="LatestReceiptCost">Último costo recibido</SelectItem><SelectItem value="WeightedAverageCost">Costo promedio ponderado</SelectItem></SelectContent></Select><p className="text-xs text-muted-foreground">Define la base que usa el tenant al preparar precios. El costo promedio consolida las sedes que comparten precios.</p></Field>
          <label className="flex items-center justify-between gap-4 rounded-xl border p-4 sm:col-span-2">
            <span><strong className="block text-sm">Combinar promociones con canal de precios</strong><small className="text-muted-foreground">Al activarlo, el descuento promocional se calcula sobre el precio del canal. Si está apagado, una promoción aplicable usa el precio público y reemplaza el canal.</small></span>
            <Switch checked={form.allowPromotionChannelCombination} onCheckedChange={value => set("allowPromotionChannelCombination", value)} />
          </label>
        </section>
      </div>
      <DialogFooter><Button variant="outline" disabled={saving} onClick={() => onOpenChange(false)}>Cancelar</Button><Button disabled={saving || !valid || entityTypes.isLoading || identificationTypes.isLoading} onClick={() => void save()}>{saving ? "Guardando…" : "Guardar cambios"}</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}

function Field({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) { return <div className={`space-y-2 ${className ?? ""}`}><Label>{label}</Label>{children}</div>; }
function initial(tenant: Tenant): TenantEditForm { return { name: tenant.name, email: tenant.email, legalName: tenant.legalName ?? tenant.name, identification: tenant.nit ?? "", verificationDigit: tenant.verificationDigit ?? "", entityType: tenant.entityType ?? "Organization", identificationTypeCode: tenant.identificationTypeCode ?? "NIT", inventoryCostBasis: tenant.inventoryCostBasis, allowPromotionChannelCombination: tenant.allowPromotionChannelCombination ?? false }; }
function errorMessage(error: unknown) { return error instanceof Error ? error.message : "No fue posible actualizar el tenant."; }
