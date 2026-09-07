"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useUpdatePromotion } from "@/hooks/use-promotions";
import type { PromotionPayload } from "@/services/api/promotions";
import type { Business, Promotion, PromotionBenefit, PromotionCondition } from "@/types/entities";
import { PromotionBenefitType, PromotionBenefitTypeLabels, PromotionItemType, PromotionItemTypeLabels } from "@/types/enums";

type Props = {
  promotion: Promotion;
  businesses: Business[];
  onClose: () => void;
};

const itemTypes = Object.values(PromotionItemType).filter((value): value is PromotionItemType => typeof value === "number");
const benefitTypes = Object.values(PromotionBenefitType).filter((value): value is PromotionBenefitType => typeof value === "number");

export function PromotionEditDialog({ promotion, businesses, onClose }: Props) {
  const update = useUpdatePromotion();
  const [draft, setDraft] = useState<PromotionPayload>(() => ({
    name: promotion.name,
    description: promotion.description ?? "",
    isActive: promotion.isActive,
    startsAtUtc: promotion.startsAtUtc ?? null,
    endsAtUtc: promotion.endsAtUtc ?? null,
    priority: promotion.priority,
    isCombinable: promotion.isCombinable,
    appliesToAllBusinesses: promotion.appliesToAllBusinesses,
    applicableBusinessIds: promotion.applicableBusinessIds ?? [],
    couponCode: promotion.couponCode ?? "",
    conditions: promotion.conditions.map((condition) => ({ ...condition })),
    benefits: promotion.benefits.map((benefit) => ({ ...benefit })),
  }));

  function patch(value: Partial<PromotionPayload>) {
    setDraft((current) => ({ ...current, ...value }));
  }

  function patchCondition(index: number, value: Partial<PromotionCondition>) {
    patch({ conditions: draft.conditions.map((condition, position) => position === index ? { ...condition, ...value } : condition) });
  }

  function patchBenefit(index: number, value: Partial<PromotionBenefit>) {
    patch({ benefits: draft.benefits.map((benefit, position) => position === index ? { ...benefit, ...value } : benefit) });
  }

  async function save() {
    if (!draft.name.trim()) return toast.error("El nombre es obligatorio");
    if (!draft.appliesToAllBusinesses && !draft.applicableBusinessIds?.length)
      return toast.error("Selecciona al menos una sede");
    if (!draft.benefits.length) return toast.error("La promoción debe conservar al menos un beneficio");
    try {
      await update.mutateAsync({
        promotionId: promotion.promotionId,
        payload: {
          ...draft,
          name: draft.name.trim(),
          description: draft.description?.trim() || "",
          couponCode: draft.couponCode?.trim() || "",
          applicableBusinessIds: draft.appliesToAllBusinesses ? [] : draft.applicableBusinessIds,
        },
      });
      toast.success("Promoción actualizada y enviada a las cajas");
      onClose();
    } catch {
      toast.error("No se pudo actualizar la promoción");
    }
  }

  return <Dialog open onOpenChange={(open) => !open && onClose()}>
    <DialogContent className="flex max-h-[94dvh] w-[96vw] max-w-5xl flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle>Editar promoción</DialogTitle>
        <DialogDescription>Los cambios se publican en la configuración sincronizada de las cajas incluidas.</DialogDescription>
      </DialogHeader>
      <div className="space-y-6 overflow-y-auto p-6">
        <section className="grid gap-4 md:grid-cols-4">
          <Field label="Nombre" className="md:col-span-2"><Input value={draft.name} onChange={(event) => patch({ name: event.target.value })} /></Field>
          <Field label="Prioridad"><Input type="number" value={draft.priority} onChange={(event) => patch({ priority: Number(event.target.value) })} /></Field>
          <Field label="Cupón"><Input value={draft.couponCode ?? ""} onChange={(event) => patch({ couponCode: event.target.value })} /></Field>
          <Field label="Descripción" className="md:col-span-4"><Textarea value={draft.description ?? ""} onChange={(event) => patch({ description: event.target.value })} /></Field>
          <Field label="Inicio"><Input type="datetime-local" value={toLocalInput(draft.startsAtUtc)} onChange={(event) => patch({ startsAtUtc: toUtc(event.target.value) })} /></Field>
          <Field label="Fin"><Input type="datetime-local" value={toLocalInput(draft.endsAtUtc)} onChange={(event) => patch({ endsAtUtc: toUtc(event.target.value) })} /></Field>
          <Toggle label="Activa" checked={draft.isActive} onCheckedChange={(value) => patch({ isActive: value })} />
          <Toggle label="Combinable" checked={draft.isCombinable} onCheckedChange={(value) => patch({ isCombinable: value })} />
        </section>

        <section className="space-y-3 rounded-xl border p-4">
          <Toggle label="Todas las sedes" checked={draft.appliesToAllBusinesses} onCheckedChange={(value) => patch({ appliesToAllBusinesses: value })} />
          {!draft.appliesToAllBusinesses && <div className="grid gap-2 border-t pt-3 sm:grid-cols-2 lg:grid-cols-3">{businesses.map((business) => {
            const checked = draft.applicableBusinessIds?.includes(business.businessId) ?? false;
            return <label key={business.businessId} className="flex items-center gap-2 text-sm"><Checkbox checked={checked} onCheckedChange={(next) => patch({ applicableBusinessIds: next === true ? [...new Set([...(draft.applicableBusinessIds ?? []), business.businessId])] : (draft.applicableBusinessIds ?? []).filter((id) => id !== business.businessId) })} />{business.name}</label>;
          })}</div>}
        </section>

        <RuleSection title="Condiciones" onAdd={() => patch({ conditions: [...draft.conditions, emptyCondition()] })}>
          {draft.conditions.map((condition, index) => <div key={condition.promotionConditionId ?? `condition-${index}`} className="grid gap-3 rounded-xl border p-3 md:grid-cols-5">
            <Field label="Tipo"><ItemTypeSelect value={condition.itemType} onChange={(itemType) => patchCondition(index, clearConditionTarget(condition, itemType))} /></Field>
            <TargetFields itemType={condition.itemType} productId={condition.productId} serviceId={condition.serviceId} categoryName={condition.categoryName} onChange={(value) => patchCondition(index, value)} />
            <Field label="Cantidad mínima"><FormattedNumberInput value={condition.minQuantity} onValueChange={(value) => patchCondition(index, { minQuantity: Number(value ?? 0) })} /></Field>
            <Field label="Subtotal mínimo"><FormattedNumberInput kind="currency" value={condition.minSubtotal ?? ""} onValueChange={(value) => patchCondition(index, { minSubtotal: value == null ? null : Number(value) })} /></Field>
            <div className="flex items-end justify-end"><Button type="button" variant="ghost" size="icon" aria-label="Quitar condición" onClick={() => patch({ conditions: draft.conditions.filter((_, position) => position !== index) })}><Trash2 className="h-4 w-4" /></Button></div>
          </div>)}
        </RuleSection>

        <RuleSection title="Beneficios" onAdd={() => patch({ benefits: [...draft.benefits, emptyBenefit()] })}>
          {draft.benefits.map((benefit, index) => <div key={benefit.promotionBenefitId ?? `benefit-${index}`} className="grid gap-3 rounded-xl border p-3 md:grid-cols-6">
            <Field label="Beneficio"><Select value={String(benefit.benefitType)} onValueChange={(value) => patchBenefit(index, clearBenefitValue(benefit, Number(value) as PromotionBenefitType))}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{benefitTypes.map((value) => <SelectItem key={value} value={String(value)}>{PromotionBenefitTypeLabels[value]}</SelectItem>)}</SelectContent></Select></Field>
            <Field label="Aplica a"><ItemTypeSelect value={benefit.targetItemType} onChange={(targetItemType) => patchBenefit(index, clearBenefitTarget(benefit, targetItemType))} /></Field>
            <TargetFields itemType={benefit.targetItemType} productId={benefit.productId} serviceId={benefit.serviceId} categoryName={benefit.categoryName} onChange={(value) => patchBenefit(index, value)} />
            <Field label="Valor"><FormattedNumberInput kind={benefit.benefitType === PromotionBenefitType.PercentageDiscount ? "percent" : "currency"} value={benefitValue(benefit)} onValueChange={(value) => patchBenefit(index, benefitValuePatch(benefit.benefitType, Number(value ?? 0)))} /></Field>
            <div className="flex items-end justify-end"><Button type="button" variant="ghost" size="icon" aria-label="Quitar beneficio" disabled={draft.benefits.length === 1} onClick={() => patch({ benefits: draft.benefits.filter((_, position) => position !== index) })}><Trash2 className="h-4 w-4" /></Button></div>
          </div>)}
        </RuleSection>
      </div>
      <DialogFooter className="border-t p-4"><Button variant="outline" onClick={onClose}>Cancelar</Button><Button onClick={() => void save()} disabled={update.isPending}>{update.isPending ? "Guardando..." : "Guardar y sincronizar"}</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}

function Field({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) {
  return <div className={`space-y-2 ${className ?? ""}`}><Label>{label}</Label>{children}</div>;
}
function Toggle({ label, checked, onCheckedChange }: { label: string; checked: boolean; onCheckedChange: (value: boolean) => void }) {
  return <label className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2"><span className="text-sm font-medium">{label}</span><Switch checked={checked} onCheckedChange={onCheckedChange} /></label>;
}
function RuleSection({ title, onAdd, children }: { title: string; onAdd: () => void; children: React.ReactNode }) {
  return <section className="space-y-3"><div className="flex items-center justify-between"><h3 className="font-semibold">{title}</h3><Button type="button" variant="outline" size="sm" onClick={onAdd}><Plus className="mr-1 h-4 w-4" />Agregar</Button></div>{children}</section>;
}
function ItemTypeSelect({ value, onChange }: { value: PromotionItemType; onChange: (value: PromotionItemType) => void }) {
  return <Select value={String(value)} onValueChange={(next) => onChange(Number(next) as PromotionItemType)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent>{itemTypes.map((item) => <SelectItem key={item} value={String(item)}>{PromotionItemTypeLabels[item]}</SelectItem>)}</SelectContent></Select>;
}
function TargetFields({ itemType, productId, serviceId, categoryName, onChange }: { itemType: PromotionItemType; productId?: string | null; serviceId?: string | null; categoryName?: string | null; onChange: (value: Partial<PromotionCondition & PromotionBenefit>) => void }) {
  if (itemType === PromotionItemType.Product) return <Field label="ID producto" className="md:col-span-2"><Input value={productId ?? ""} onChange={(event) => onChange({ productId: event.target.value || null })} /></Field>;
  if (itemType === PromotionItemType.Service) return <Field label="ID servicio" className="md:col-span-2"><Input value={serviceId ?? ""} onChange={(event) => onChange({ serviceId: event.target.value || null })} /></Field>;
  if (itemType === PromotionItemType.ProductCategory || itemType === PromotionItemType.ServiceCategory) return <Field label="Categoría" className="md:col-span-2"><Input value={categoryName ?? ""} onChange={(event) => onChange({ categoryName: event.target.value || null })} /></Field>;
  return <div className="md:col-span-2" />;
}
function emptyCondition(): PromotionCondition { return { itemType: PromotionItemType.Any, productId: null, serviceId: null, categoryName: null, minQuantity: 1, minSubtotal: null }; }
function emptyBenefit(): PromotionBenefit { return { benefitType: PromotionBenefitType.PercentageDiscount, targetItemType: PromotionItemType.Any, productId: null, serviceId: null, categoryName: null, discountPercentage: 10, discountAmount: null, fixedUnitPrice: null, appliesToQuantity: null }; }
function clearConditionTarget(condition: PromotionCondition, itemType: PromotionItemType): PromotionCondition { return { ...condition, itemType, productId: null, serviceId: null, categoryName: null }; }
function clearBenefitTarget(benefit: PromotionBenefit, targetItemType: PromotionItemType): PromotionBenefit { return { ...benefit, targetItemType, productId: null, serviceId: null, categoryName: null }; }
function clearBenefitValue(benefit: PromotionBenefit, benefitType: PromotionBenefitType): PromotionBenefit { return { ...benefit, benefitType, discountPercentage: null, discountAmount: null, fixedUnitPrice: null, appliesToQuantity: benefitType === PromotionBenefitType.FreeItem ? 1 : null, ...benefitValuePatch(benefitType, benefitType === PromotionBenefitType.FreeItem ? 1 : 10) }; }
function benefitValue(benefit: PromotionBenefit) { return benefit.discountPercentage ?? benefit.discountAmount ?? benefit.fixedUnitPrice ?? benefit.appliesToQuantity ?? ""; }
function benefitValuePatch(type: PromotionBenefitType, value: number): Partial<PromotionBenefit> { if (type === PromotionBenefitType.PercentageDiscount) return { discountPercentage: value }; if (type === PromotionBenefitType.AmountDiscount) return { discountAmount: value }; if (type === PromotionBenefitType.FixedUnitPrice) return { fixedUnitPrice: value }; return { appliesToQuantity: value }; }
function toLocalInput(value?: string | null) { if (!value) return ""; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); }
function toUtc(value: string) { return value ? new Date(value).toISOString() : null; }
