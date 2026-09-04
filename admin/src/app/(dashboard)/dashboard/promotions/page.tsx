"use client";

import { useEffect, useState } from "react";
import { BadgePercent, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Checkbox } from "@/components/ui/checkbox";
import { ProductPicker } from "@/components/products/product-picker";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useCreatePromotion, useDeletePromotion, usePromotions } from "@/hooks/use-promotions";
import { useProductCategories } from "@/hooks/use-products";
import { useBusinesses } from "@/hooks/use-businesses";
import { PromotionBenefitType, PromotionBenefitTypeLabels, PromotionItemType, PromotionItemTypeLabels } from "@/types/enums";
import type { Promotion } from "@/types/entities";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export default function PromotionsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data, isLoading } = usePromotions({ page: 1, pageSize: 50 });
  const createPromotion = useCreatePromotion();
  const deletePromotion = useDeletePromotion();
  const categories = useProductCategories();
  const businesses = useBusinesses({ page: 1, pageSize: 200 });

  const [name, setName] = useState("");
  const [benefitType, setBenefitType] = useState(PromotionBenefitType.PercentageDiscount);
  const [targetType, setTargetType] = useState(PromotionItemType.Any);
  const [conditionType, setConditionType] = useState(PromotionItemType.Any);
  const [value, setValue] = useState("10");
  const [minQuantity, setMinQuantity] = useState("1");
  const [minSubtotal, setMinSubtotal] = useState("");
  const [priority, setPriority] = useState("0");
  const [isCombinable, setIsCombinable] = useState(false);
  const [appliesToAllBusinesses, setAppliesToAllBusinesses] = useState(false);
  const [applicableBusinessIds, setApplicableBusinessIds] = useState<string[]>([]);
  const [conditionProduct, setConditionProduct] = useState<{ id: string; name: string } | null>(null);
  const [benefitProduct, setBenefitProduct] = useState<{ id: string; name: string } | null>(null);
  const [conditionCategory, setConditionCategory] = useState("");
  const [benefitCategory, setBenefitCategory] = useState("");

  const promotions = data?.items ?? [];

  useEffect(() => {
    setAppliesToAllBusinesses(false);
    setApplicableBusinessIds(businessId ? [businessId] : []);
  }, [businessId]);

  async function handleCreate() {
    if (!businessId) return;
    if (!name.trim()) {
      toast.error("El nombre es obligatorio");
      return;
    }
    if (!appliesToAllBusinesses && applicableBusinessIds.length === 0) {
      toast.error("Selecciona al menos una sede o habilita todas las sedes");
      return;
    }
    if (conditionType === PromotionItemType.Product && !conditionProduct
      || conditionType === PromotionItemType.ProductCategory && !conditionCategory
      || targetType === PromotionItemType.Product && !benefitProduct
      || targetType === PromotionItemType.ProductCategory && !benefitCategory) {
      toast.error("Selecciona el producto o la categoría requeridos por la regla");
      return;
    }

    const numericValue = Number(value || 0);
    const benefit = {
      benefitType,
      targetItemType: targetType,
      productId: targetType === PromotionItemType.Product ? benefitProduct?.id ?? null : null,
      serviceId: null,
      categoryName: targetType === PromotionItemType.ProductCategory ? benefitCategory || null : null,
      discountPercentage: benefitType === PromotionBenefitType.PercentageDiscount ? numericValue : null,
      discountAmount: benefitType === PromotionBenefitType.AmountDiscount ? numericValue : null,
      fixedUnitPrice: benefitType === PromotionBenefitType.FixedUnitPrice ? numericValue : null,
      appliesToQuantity: benefitType === PromotionBenefitType.FreeItem ? 1 : null,
    };

    try {
      await createPromotion.mutateAsync({
        name: name.trim(),
        description: null,
        isActive: true,
        startsAtUtc: null,
        endsAtUtc: null,
        priority: Number(priority || 0),
        isCombinable,
        appliesToAllBusinesses,
        applicableBusinessIds: appliesToAllBusinesses ? [] : applicableBusinessIds,
        couponCode: null,
        conditions: [{
          itemType: conditionType,
          productId: conditionType === PromotionItemType.Product ? conditionProduct?.id ?? null : null,
          serviceId: null,
          categoryName: conditionType === PromotionItemType.ProductCategory ? conditionCategory || null : null,
          minQuantity: Math.max(1, Number(minQuantity || 1)),
          minSubtotal: minSubtotal ? Number(minSubtotal) : null,
        }],
        benefits: [benefit],
      });
      setName("");
      setConditionProduct(null);
      setBenefitProduct(null);
      toast.success("Promocion creada");
    } catch {
      toast.error("No se pudo crear la promocion");
    }
  }

  async function handleDelete(promotion: Promotion) {
    try {
      await deletePromotion.mutateAsync(promotion.promotionId);
      toast.success("Promocion desactivada");
    } catch {
      toast.error("No se pudo desactivar");
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Promociones</h1>
          <p className="text-sm text-muted-foreground">Reglas de descuento para productos y servicios del negocio activo.</p>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base"><BadgePercent className="h-4 w-4" /> Nueva promocion</CardTitle>
          <CardDescription>Define una condicion y un beneficio inicial; luego puedes extenderla desde la API.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-6">
            <div className="md:col-span-2 space-y-2">
              <Label>Nombre</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ej. Segunda unidad 50%" />
            </div>
            <div className="space-y-2">
              <Label>Condicion</Label>
              <Select value={String(conditionType)} onValueChange={(v) => setConditionType(Number(v) as PromotionItemType)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{Object.values(PromotionItemType).filter((v) => typeof v === "number").map((v) => <SelectItem key={v} value={String(v)}>{PromotionItemTypeLabels[v as PromotionItemType]}</SelectItem>)}</SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Cantidad min.</Label>
              <Input type="number" min="1" value={minQuantity} onChange={(e) => setMinQuantity(e.target.value)} />
            </div>
            <div className="space-y-2">
              <Label>Subtotal min.</Label>
              <FormattedNumberInput kind="currency" value={minSubtotal} onValueChange={(next) => setMinSubtotal(next?.toString() ?? "")} />
            </div>
            <div className="space-y-2">
              <Label>Beneficio</Label>
              <Select value={String(benefitType)} onValueChange={(v) => setBenefitType(Number(v) as PromotionBenefitType)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{Object.values(PromotionBenefitType).filter((v) => typeof v === "number").map((v) => <SelectItem key={v} value={String(v)}>{PromotionBenefitTypeLabels[v as PromotionBenefitType]}</SelectItem>)}</SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Valor</Label>
              <FormattedNumberInput kind={benefitType === PromotionBenefitType.PercentageDiscount ? "percent" : "currency"} value={value} onValueChange={(next) => setValue(next?.toString() ?? "")} />
            </div>
            <div className="space-y-2">
              <Label>Aplica a</Label>
              <Select value={String(targetType)} onValueChange={(v) => setTargetType(Number(v) as PromotionItemType)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{Object.values(PromotionItemType).filter((v) => typeof v === "number").map((v) => <SelectItem key={v} value={String(v)}>{PromotionItemTypeLabels[v as PromotionItemType]}</SelectItem>)}</SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Prioridad</Label>
              <Input type="number" value={priority} onChange={(e) => setPriority(e.target.value)} />
            </div>
            <label className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2 md:col-span-2">
              <span><strong className="block text-sm">Combinable</strong><small className="text-muted-foreground">Permite acumularla con otra promoción sobre la misma línea.</small></span>
              <Switch checked={isCombinable} onCheckedChange={setIsCombinable} />
            </label>
            <div className="space-y-3 rounded-xl border px-3 py-3 md:col-span-6">
              <label className="flex items-center justify-between gap-3">
                <span><strong className="block text-sm">Todas las sedes</strong><small className="text-muted-foreground">La promoción se sincroniza y aplica en cualquier sede de esta empresa.</small></span>
                <Switch checked={appliesToAllBusinesses} onCheckedChange={setAppliesToAllBusinesses} />
              </label>
              {!appliesToAllBusinesses && <div className="grid gap-2 border-t pt-3 sm:grid-cols-2 lg:grid-cols-3">
                {(businesses.data?.items ?? []).map((business) => {
                  const checked = applicableBusinessIds.includes(business.businessId);
                  return <label key={business.businessId} className="flex items-center gap-2 text-sm">
                    <Checkbox checked={checked} onCheckedChange={(next) => setApplicableBusinessIds((current) =>
                      next === true ? [...new Set([...current, business.businessId])] : current.filter((id) => id !== business.businessId))} />
                    <span>{business.name}</span>
                  </label>;
                })}
              </div>}
            </div>
            {conditionType === PromotionItemType.Product && businessId && <div className="md:col-span-3"><ProductPicker businessId={businessId} selectedProductIds={new Set(conditionProduct ? [conditionProduct.id] : [])} disabled={createPromotion.isPending} inputId="promotion-condition-product" label="Producto de la condición" showAddButton={false} onSelect={product => setConditionProduct({ id: product.productId, name: product.productName })} />{conditionProduct && <p className="mt-1 text-xs text-muted-foreground">Seleccionado: {conditionProduct.name}</p>}</div>}
            {conditionType === PromotionItemType.ProductCategory && <div className="space-y-2 md:col-span-3"><Label>Categoría de la condición</Label><Select value={conditionCategory} onValueChange={setConditionCategory}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{(categories.data ?? []).map(category => <SelectItem key={category.productCategoryId} value={category.name}>{category.path}</SelectItem>)}</SelectContent></Select></div>}
            {targetType === PromotionItemType.Product && businessId && <div className="md:col-span-3"><ProductPicker businessId={businessId} selectedProductIds={new Set(benefitProduct ? [benefitProduct.id] : [])} disabled={createPromotion.isPending} inputId="promotion-benefit-product" label="Producto beneficiado" showAddButton={false} onSelect={product => setBenefitProduct({ id: product.productId, name: product.productName })} />{benefitProduct && <p className="mt-1 text-xs text-muted-foreground">Seleccionado: {benefitProduct.name}</p>}</div>}
            {targetType === PromotionItemType.ProductCategory && <div className="space-y-2 md:col-span-3"><Label>Categoría beneficiada</Label><Select value={benefitCategory} onValueChange={setBenefitCategory}><SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{(categories.data ?? []).map(category => <SelectItem key={category.productCategoryId} value={category.name}>{category.path}</SelectItem>)}</SelectContent></Select></div>}
            <div className="flex items-end">
              <Button onClick={handleCreate} disabled={!businessId || createPromotion.isPending} className="w-full gap-2"><Plus className="h-4 w-4" /> Crear</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Promociones activas e historicas</CardTitle>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Beneficio</TableHead>
                <TableHead>Combinable</TableHead>
                <TableHead>Sedes</TableHead>
                <TableHead className="text-right">Prioridad</TableHead>
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {promotions.map((promotion) => {
                const benefit = promotion.benefits[0];
                const amount = benefit?.discountPercentage ?? benefit?.discountAmount ?? benefit?.fixedUnitPrice;
                return (
                  <TableRow key={promotion.promotionId}>
                    <TableCell className="font-medium">{promotion.name}</TableCell>
                    <TableCell><Badge variant={promotion.isActive ? "default" : "secondary"}>{promotion.isActive ? "Activa" : "Inactiva"}</Badge></TableCell>
                    <TableCell>{benefit ? `${PromotionBenefitTypeLabels[benefit.benefitType]} ${amount ? benefit.benefitType === PromotionBenefitType.PercentageDiscount ? `${amount}%` : money.format(amount) : ""}` : "Sin beneficio"}</TableCell>
                    <TableCell>{promotion.isCombinable ? "Sí" : "No"}</TableCell>
                    <TableCell>{promotion.appliesToAllBusinesses ? "Todas" : `${promotion.applicableBusinessIds?.length ?? 1} seleccionada(s)`}</TableCell>
                    <TableCell className="text-right">{promotion.priority}</TableCell>
                    <TableCell><Button variant="ghost" size="icon" onClick={() => handleDelete(promotion)} disabled={!promotion.isActive || deletePromotion.isPending}><Trash2 className="h-4 w-4" /></Button></TableCell>
                  </TableRow>
                );
              })}
              {!isLoading && promotions.length === 0 && <TableRow><TableCell colSpan={7} className="text-center text-muted-foreground">Sin promociones configuradas</TableCell></TableRow>}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
