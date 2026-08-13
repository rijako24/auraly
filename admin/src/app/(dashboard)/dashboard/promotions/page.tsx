"use client";

import { useState } from "react";
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
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useCreatePromotion, useDeletePromotion, usePromotions } from "@/hooks/use-promotions";
import { PromotionBenefitType, PromotionBenefitTypeLabels, PromotionItemType, PromotionItemTypeLabels } from "@/types/enums";
import type { Promotion } from "@/types/entities";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export default function PromotionsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data, isLoading } = usePromotions({ page: 1, pageSize: 50 });
  const createPromotion = useCreatePromotion();
  const deletePromotion = useDeletePromotion();

  const [name, setName] = useState("");
  const [benefitType, setBenefitType] = useState(PromotionBenefitType.PercentageDiscount);
  const [targetType, setTargetType] = useState(PromotionItemType.Any);
  const [conditionType, setConditionType] = useState(PromotionItemType.Any);
  const [value, setValue] = useState("10");
  const [minQuantity, setMinQuantity] = useState("1");
  const [priority, setPriority] = useState("0");

  const promotions = data?.items ?? [];

  async function handleCreate() {
    if (!businessId) return;
    if (!name.trim()) {
      toast.error("El nombre es obligatorio");
      return;
    }

    const numericValue = Number(value || 0);
    const benefit = {
      benefitType,
      targetItemType: targetType,
      productId: null,
      serviceId: null,
      categoryName: null,
      discountPercentage: benefitType === PromotionBenefitType.PercentageDiscount ? numericValue : null,
      discountAmount: benefitType === PromotionBenefitType.AmountDiscount ? numericValue : null,
      fixedUnitPrice: benefitType === PromotionBenefitType.FixedUnitPrice ? numericValue : null,
      appliesToQuantity: benefitType === PromotionBenefitType.FreeItem ? 1 : null,
    };

    try {
      await createPromotion.mutateAsync({
        businessId,
        name: name.trim(),
        description: null,
        isActive: true,
        startsAtUtc: null,
        endsAtUtc: null,
        priority: Number(priority || 0),
        isCombinable: false,
        couponCode: null,
        conditions: [{
          itemType: conditionType,
          productId: null,
          serviceId: null,
          categoryName: null,
          minQuantity: Math.max(1, Number(minQuantity || 1)),
          minSubtotal: null,
        }],
        benefits: [benefit],
      });
      setName("");
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
                    <TableCell className="text-right">{promotion.priority}</TableCell>
                    <TableCell><Button variant="ghost" size="icon" onClick={() => handleDelete(promotion)} disabled={!promotion.isActive || deletePromotion.isPending}><Trash2 className="h-4 w-4" /></Button></TableCell>
                  </TableRow>
                );
              })}
              {!isLoading && promotions.length === 0 && <TableRow><TableCell colSpan={5} className="text-center text-muted-foreground">Sin promociones configuradas</TableCell></TableRow>}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
