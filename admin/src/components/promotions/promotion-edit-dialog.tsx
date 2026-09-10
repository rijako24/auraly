"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BadgePercent, Boxes, Building2, Package, ShoppingBasket, Sparkles } from "lucide-react";
import { toast } from "sonner";

import { ProductPicker } from "@/components/products/product-picker";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useCreatePromotion, useUpdatePromotion } from "@/hooks/use-promotions";
import { useProductCategories } from "@/hooks/use-products";
import { productsApi } from "@/services/api/products";
import type { PromotionPayload } from "@/services/api/promotions";
import type { Business, Promotion } from "@/types/entities";
import { PromotionBenefitType, PromotionItemType } from "@/types/enums";

type TargetKind = "all" | "product" | "category";
type SelectedTarget = { id: string; name: string } | null;

type Props = {
  promotion?: Promotion;
  businesses: Business[];
  businessId: string;
  onClose: () => void;
};

export function PromotionEditDialog({ promotion, businesses, businessId, onClose }: Props) {
  const create = useCreatePromotion();
  const update = useUpdatePromotion();
  const categoriesQuery = useProductCategories();
  const firstCondition = promotion?.conditions[0];
  const firstBenefit = promotion?.benefits[0];
  const [name, setName] = useState(promotion?.name ?? "");
  const [percent, setPercent] = useState(String(firstBenefit?.discountPercentage ?? 20));
  const [benefitKind, setBenefitKind] = useState<TargetKind>(toTargetKind(firstBenefit?.targetItemType));
  const [benefitTarget, setBenefitTarget] = useState<SelectedTarget>(() => targetFromBenefit(firstBenefit));
  const [hasCondition, setHasCondition] = useState(Boolean(firstCondition && firstCondition.itemType !== PromotionItemType.Any && firstCondition.itemType !== PromotionItemType.AnyProduct));
  const [conditionKind, setConditionKind] = useState<Exclude<TargetKind, "all">>(() => toConditionKind(firstCondition?.itemType));
  const [conditionTarget, setConditionTarget] = useState<SelectedTarget>(() => targetFromCondition(firstCondition));
  const [minimumQuantity, setMinimumQuantity] = useState(String(firstCondition?.minQuantity ?? 1));
  const [limitQuantity, setLimitQuantity] = useState(firstBenefit?.appliesToQuantity != null);
  const [appliesToQuantity, setAppliesToQuantity] = useState(String(firstBenefit?.appliesToQuantity ?? 1));
  const [appliesToAllBusinesses, setAppliesToAllBusinesses] = useState(promotion?.appliesToAllBusinesses ?? true);
  const [applicableBusinessIds, setApplicableBusinessIds] = useState<string[]>(promotion?.applicableBusinessIds?.length ? promotion.applicableBusinessIds : [businessId]);
  const [description, setDescription] = useState(promotion?.description ?? "");
  const [priority, setPriority] = useState(String(promotion?.priority ?? 0));
  const [isCombinable, setIsCombinable] = useState(promotion?.isCombinable ?? false);
  const [couponCode, setCouponCode] = useState(promotion?.couponCode ?? "");
  const [startsAt, setStartsAt] = useState(toLocalInput(promotion?.startsAtUtc));
  const [endsAt, setEndsAt] = useState(toLocalInput(promotion?.endsAtUtc));
  const mutationPending = create.isPending || update.isPending;

  const benefitProductName = useProductName(benefitKind === "product" ? benefitTarget?.id : undefined);
  const conditionProductName = useProductName(hasCondition && conditionKind === "product" ? conditionTarget?.id : undefined);
  const categoryById = useMemo(() => new Map((categoriesQuery.data ?? []).map((category) => [category.productCategoryId, category])), [categoriesQuery.data]);
  const benefitName = benefitKind === "category"
    ? categoryById.get(benefitTarget?.id ?? "")?.name
    : benefitProductName ?? benefitTarget?.name;
  const conditionName = conditionKind === "category"
    ? categoryById.get(conditionTarget?.id ?? "")?.name
    : conditionProductName ?? conditionTarget?.name;
  const preview = buildPreview({ percent, benefitKind, benefitName, hasCondition, conditionKind, conditionName, minimumQuantity, limitQuantity, appliesToQuantity });

  function selectCategory(kind: "benefit" | "condition", id: string) {
    const category = categoryById.get(id);
    const selected = category ? { id: category.productCategoryId, name: category.name } : null;
    if (kind === "benefit") setBenefitTarget(selected);
    else setConditionTarget(selected);
  }

  async function save() {
    const numericPercent = Number(percent);
    const numericMinimum = Number(minimumQuantity);
    const numericAppliesTo = Number(appliesToQuantity);
    if (!name.trim()) return toast.error("Escribe un nombre para identificar la promoción");
    if (!(numericPercent > 0 && numericPercent <= 100)) return toast.error("El porcentaje debe estar entre 0 y 100");
    if (benefitKind !== "all" && !benefitTarget) return toast.error("Selecciona qué producto o categoría recibe el descuento");
    if (hasCondition && !conditionTarget) return toast.error("Selecciona qué producto o categoría debe comprar el cliente");
    if (hasCondition && numericMinimum <= 0) return toast.error("La cantidad mínima debe ser mayor a cero");
    if (limitQuantity && numericAppliesTo <= 0) return toast.error("La cantidad beneficiada debe ser mayor a cero");
    if (!appliesToAllBusinesses && applicableBusinessIds.length === 0) return toast.error("Selecciona al menos una sede");
    if (startsAt && endsAt && new Date(startsAt) > new Date(endsAt)) return toast.error("La fecha inicial no puede ser posterior a la final");

    const payload: PromotionPayload = {
      name: name.trim(),
      description: description.trim() || null,
      isActive: promotion?.isActive ?? true,
      startsAtUtc: toUtc(startsAt),
      endsAtUtc: toUtc(endsAt),
      priority: Number(priority || 0),
      isCombinable,
      appliesToAllBusinesses,
      applicableBusinessIds: appliesToAllBusinesses ? [] : applicableBusinessIds,
      couponCode: couponCode.trim() || null,
      conditions: hasCondition ? [{
        itemType: conditionKind === "product" ? PromotionItemType.Product : PromotionItemType.ProductCategory,
        productId: conditionKind === "product" ? conditionTarget?.id : null,
        serviceId: null,
        productCategoryId: conditionKind === "category" ? conditionTarget?.id : null,
        serviceCategoryId: null,
        minQuantity: numericMinimum,
        minSubtotal: null,
      }] : [],
      benefits: [{
        benefitType: PromotionBenefitType.PercentageDiscount,
        targetItemType: benefitKind === "product" ? PromotionItemType.Product : benefitKind === "category" ? PromotionItemType.ProductCategory : PromotionItemType.AnyProduct,
        productId: benefitKind === "product" ? benefitTarget?.id : null,
        serviceId: null,
        productCategoryId: benefitKind === "category" ? benefitTarget?.id : null,
        serviceCategoryId: null,
        discountPercentage: numericPercent,
        discountAmount: null,
        fixedUnitPrice: null,
        appliesToQuantity: limitQuantity ? numericAppliesTo : null,
      }],
    };

    try {
      if (promotion) await update.mutateAsync({ promotionId: promotion.promotionId, payload });
      else await create.mutateAsync(payload);
      toast.success(promotion ? "Promoción actualizada y enviada a las cajas" : "Promoción creada y enviada a las cajas");
      onClose();
    } catch {
      toast.error(promotion ? "No fue posible actualizar la promoción" : "No fue posible crear la promoción");
    }
  }

  return <Dialog open onOpenChange={(open) => !open && onClose()}>
    <DialogContent className="flex max-h-[94dvh] w-[96vw] max-w-3xl flex-col overflow-hidden p-0">
      <DialogHeader className="border-b px-6 py-5">
        <DialogTitle>{promotion ? "Editar promoción" : "Nueva promoción"}</DialogTitle>
        <DialogDescription>Configura el descuento como una frase: qué recibe el cliente y, si aplica, qué debe comprar primero.</DialogDescription>
      </DialogHeader>
      <div className="space-y-6 overflow-y-auto p-6">
        <section className="space-y-2"><Label htmlFor="promotion-name">Nombre de la promoción</Label><Input id="promotion-name" value={name} onChange={(event) => setName(event.target.value)} placeholder="Ej. Compra café y recibe leche al 50%" /></section>

        <section className="space-y-4 rounded-2xl border border-emerald-200 bg-emerald-50/40 p-4">
          <div className="flex items-start gap-3"><span className="rounded-xl bg-emerald-100 p-2 text-emerald-700"><BadgePercent className="h-5 w-5" /></span><div><h3 className="font-semibold">1. Define el descuento</h3><p className="text-sm text-muted-foreground">Elige a qué productos se aplica y cuánto descuentas.</p></div></div>
          <TargetChoices value={benefitKind} onChange={(value) => { if (value !== benefitKind) { setBenefitKind(value); setBenefitTarget(null); } }} includeAll />
          <TargetPicker kind={benefitKind} value={benefitTarget} businessId={businessId} categories={categoriesQuery.data ?? []} disabled={mutationPending} onProduct={setBenefitTarget} onCategory={(id) => selectCategory("benefit", id)} idPrefix="benefit" />
          <div className="grid gap-4 sm:grid-cols-2"><Field label="Porcentaje de descuento"><FormattedNumberInput kind="percent" value={percent} onValueChange={(value) => setPercent(value?.toString() ?? "")} /></Field><label className="flex items-center justify-between gap-3 rounded-xl border bg-background px-3 py-2"><span><strong className="block text-sm">Limitar unidades</strong><small className="text-muted-foreground">Útil para “el otro producto al 50%”.</small></span><Switch checked={limitQuantity} onCheckedChange={setLimitQuantity} /></label></div>
          {limitQuantity && <Field label="Cantidad de unidades que reciben el descuento"><FormattedNumberInput value={appliesToQuantity} onValueChange={(value) => setAppliesToQuantity(value?.toString() ?? "")} /></Field>}
        </section>

        <section className="space-y-4 rounded-2xl border p-4">
          <div className="flex items-center justify-between gap-4"><div className="flex items-start gap-3"><span className="rounded-xl bg-slate-100 p-2 text-slate-700"><ShoppingBasket className="h-5 w-5" /></span><div><h3 className="font-semibold">2. ¿Debe comprar algo específico?</h3><p className="text-sm text-muted-foreground">Déjalo apagado para un descuento directo.</p></div></div><Switch checked={hasCondition} onCheckedChange={(checked) => { setHasCondition(checked); if (checked) setLimitQuantity(true); }} /></div>
          {hasCondition && <><TargetChoices value={conditionKind} onChange={(value) => { if (value !== "all" && value !== conditionKind) { setConditionKind(value); setConditionTarget(null); } }} /><TargetPicker kind={conditionKind} value={conditionTarget} businessId={businessId} categories={categoriesQuery.data ?? []} disabled={mutationPending} onProduct={setConditionTarget} onCategory={(id) => selectCategory("condition", id)} idPrefix="condition" /><Field label="Cantidad mínima que debe comprar"><FormattedNumberInput value={minimumQuantity} onValueChange={(value) => setMinimumQuantity(value?.toString() ?? "")} /></Field></>}
        </section>

        <section className="space-y-4 rounded-2xl border p-4">
          <div className="flex items-center justify-between gap-4"><div className="flex items-start gap-3"><span className="rounded-xl bg-slate-100 p-2 text-slate-700"><Building2 className="h-5 w-5" /></span><div><h3 className="font-semibold">3. Elige las sedes</h3><p className="text-sm text-muted-foreground">Por defecto queda disponible en toda la empresa.</p></div></div><Switch checked={appliesToAllBusinesses} onCheckedChange={setAppliesToAllBusinesses} /></div>
          <p className="text-sm font-medium">{appliesToAllBusinesses ? "Aplicar en todas las sedes" : "Aplicar solo en sedes seleccionadas"}</p>
          {!appliesToAllBusinesses && <div className="grid gap-2 border-t pt-3 sm:grid-cols-2">{businesses.map((business) => { const checked = applicableBusinessIds.includes(business.businessId); return <label key={business.businessId} className="flex items-center gap-2 rounded-xl border bg-background px-3 py-2 text-sm"><Checkbox checked={checked} onCheckedChange={(next) => setApplicableBusinessIds((current) => next === true ? [...new Set([...current, business.businessId])] : current.filter((id) => id !== business.businessId))} /><span>{business.name}</span></label>; })}</div>}
        </section>

        <div className="rounded-2xl bg-slate-950 p-4 text-white"><div className="flex items-center gap-2 text-sm font-semibold text-emerald-300"><Sparkles className="h-4 w-4" />Así funcionará</div><p className="mt-2 text-sm leading-6">{preview}</p></div>

        <details className="rounded-2xl border p-4"><summary className="cursor-pointer font-medium">Configuración avanzada</summary><div className="mt-4 grid gap-4 sm:grid-cols-2"><Field label="Descripción" className="sm:col-span-2"><Textarea value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Nota interna opcional" /></Field><Field label="Prioridad"><Input type="number" value={priority} onChange={(event) => setPriority(event.target.value)} /></Field><Field label="Cupón opcional"><Input value={couponCode} onChange={(event) => setCouponCode(event.target.value)} placeholder="Ej. CLIENTE20" /></Field><Field label="Fecha de inicio"><DateTimePicker value={startsAt} onChange={setStartsAt} /></Field><Field label="Fecha de fin"><DateTimePicker value={endsAt} onChange={setEndsAt} /></Field><label className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2 sm:col-span-2"><span><strong className="block text-sm">Combinar con otras promociones</strong><small className="text-muted-foreground">Permite acumular descuentos sobre la misma línea.</small></span><Switch checked={isCombinable} onCheckedChange={setIsCombinable} /></label></div></details>
      </div>
      <DialogFooter className="border-t px-6 py-4"><Button variant="outline" onClick={onClose}>Cerrar</Button><Button onClick={() => void save()} disabled={mutationPending}>{mutationPending ? "Guardando..." : promotion ? "Guardar y sincronizar" : "Crear y sincronizar"}</Button></DialogFooter>
    </DialogContent>
  </Dialog>;
}

function TargetChoices({ value, onChange, includeAll = false, disabled = false }: { value: TargetKind; onChange: (value: TargetKind) => void; includeAll?: boolean; disabled?: boolean }) {
  const choices = [
    ...(includeAll ? [{ value: "all" as const, title: "Todos los productos", text: "Descuento general", icon: Boxes }] : []),
    { value: "product" as const, title: "Un producto", text: "Elige uno del catálogo", icon: Package },
    { value: "category" as const, title: "Una categoría", text: "Incluye sus productos", icon: ShoppingBasket },
  ];
  return <div className={`grid gap-2 ${includeAll ? "sm:grid-cols-3" : "sm:grid-cols-2"}`}>{choices.map((choice) => <button key={choice.value} type="button" disabled={disabled} onClick={() => onChange(choice.value)} className={`flex items-center gap-3 rounded-xl border p-3 text-left transition-colors ${value === choice.value ? "border-emerald-500 bg-emerald-50 text-emerald-950" : "bg-background hover:border-emerald-300"}`}><choice.icon className="h-5 w-5 shrink-0" /><span><strong className="block text-sm">{choice.title}</strong><small className="text-muted-foreground">{choice.text}</small></span></button>)}</div>;
}

function TargetPicker({ kind, value, businessId, categories, disabled, onProduct, onCategory, idPrefix }: { kind: TargetKind; value: SelectedTarget; businessId: string; categories: Array<{ productCategoryId: string; name: string; path: string }>; disabled: boolean; onProduct: (value: SelectedTarget) => void; onCategory: (id: string) => void; idPrefix: string }) {
  if (kind === "all") return null;
  if (kind === "category") return <Field label="Categoría"><Select value={value?.id ?? ""} onValueChange={onCategory} disabled={disabled}><SelectTrigger><SelectValue placeholder="Selecciona una categoría" /></SelectTrigger><SelectContent>{categories.map((category) => <SelectItem key={category.productCategoryId} value={category.productCategoryId}>{category.path}</SelectItem>)}</SelectContent></Select></Field>;
  return <div><ProductPicker businessId={businessId} selectedProductIds={new Set(value ? [value.id] : [])} disabled={disabled} inputId={`promotion-${idPrefix}-product`} label={idPrefix === "benefit" ? "Producto que recibe el descuento" : "Producto que debe comprar"} showAddButton={false} onSelect={(product) => onProduct({ id: product.productId, name: product.productName })} />{value && <p className="mt-2 rounded-lg bg-background px-3 py-2 text-sm">Seleccionado: <strong>{value.name}</strong></p>}</div>;
}

function Field({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) { return <div className={`space-y-2 ${className ?? ""}`}><Label>{label}</Label>{children}</div>; }
function toTargetKind(value?: PromotionItemType): TargetKind { return value === PromotionItemType.Product ? "product" : value === PromotionItemType.ProductCategory ? "category" : "all"; }
function toConditionKind(value?: PromotionItemType): Exclude<TargetKind, "all"> { return value === PromotionItemType.ProductCategory ? "category" : "product"; }
function targetFromBenefit(value?: Promotion["benefits"][number]): SelectedTarget { const id = value?.productId ?? value?.productCategoryId; return id ? { id, name: value?.productId ? "Producto configurado" : "Categoría configurada" } : null; }
function targetFromCondition(value?: Promotion["conditions"][number]): SelectedTarget { const id = value?.productId ?? value?.productCategoryId; return id ? { id, name: value?.productId ? "Producto configurado" : "Categoría configurada" } : null; }
function useProductName(productId?: string) { const query = useQuery({ queryKey: ["promotion-product-name", productId], queryFn: () => productsApi.getCatalog(productId!), enabled: Boolean(productId), staleTime: 300_000 }); return query.data?.name; }
function toLocalInput(value?: string | null) { if (!value) return ""; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16); }
function toUtc(value: string) { return value ? new Date(value).toISOString() : null; }
function buildPreview(value: { percent: string; benefitKind: TargetKind; benefitName?: string; hasCondition: boolean; conditionKind: Exclude<TargetKind, "all">; conditionName?: string; minimumQuantity: string; limitQuantity: boolean; appliesToQuantity: string }) {
  const benefit = value.benefitKind === "all" ? "todos los productos" : value.benefitName || (value.benefitKind === "product" ? "el producto seleccionado" : "la categoría seleccionada");
  const limit = value.limitQuantity ? ` en ${value.appliesToQuantity || 1} unidad(es)` : " en todas las unidades";
  if (!value.hasCondition) return `Aplica ${value.percent || 0}% de descuento a ${benefit}${limit}.`;
  const condition = value.conditionName || (value.conditionKind === "product" ? "el producto seleccionado" : "la categoría seleccionada");
  return `Cuando el cliente compra al menos ${value.minimumQuantity || 1} unidad(es) de ${condition}, aplica ${value.percent || 0}% de descuento a ${benefit}${limit}.`;
}
