"use client";

import { forwardRef, useCallback, useEffect, useImperativeHandle, useState } from "react";
import { ArrowRight, CheckCircle2, CircleDollarSign, Send } from "lucide-react";
import { toast } from "sonner";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useProductPricingContext, useSavePreparedProductPrice } from "@/hooks/use-pricing";
import { formatCurrency } from "@/lib/utils";
import {
  marginFromCostAndGrossSale,
  recalculateProductPricing,
  type ProductPricingField,
} from "@/lib/product-pricing-calculator";
import type { PreparedProductPrice, PricingRoundingMode } from "@/services/api/pricing";

export interface ProductPricingEditorHandle { save: () => Promise<void> }

export const ProductPricingEditor = forwardRef<ProductPricingEditorHandle, {
  embedded?: boolean;
  salesTaxRateOverride?: number;
  productId: string;
  productName: string;
  onSaved?: (result: PreparedProductPrice) => void;
}>(function ProductPricingEditor({ productId, productName, onSaved, embedded = false, salesTaxRateOverride }, ref) {
  const context = useProductPricingContext(productId);
  const savePrepared = useSavePreparedProductPrice();
  const [cost, setCost] = useState("");
  const [salePrice, setSalePrice] = useState("");
  const [margin, setMargin] = useState("");
  const [increment, setIncrement] = useState("1");
  const [roundingMode, setRoundingMode] = useState<PricingRoundingMode>("Nearest");
  const [lastEdited, setLastEdited] = useState<ProductPricingField>("salePrice");
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!context.data) return;
    const loadedCost = context.data.costBasisAmount;
    const loadedMargin = context.data.currentMarginPercent
      ?? (loadedCost === null
        ? 0
        : marginFromCostAndGrossSale(
            loadedCost,
            context.data.preparedSalePrice,
            context.data.salesTaxRate,
          ));
    setCost(loadedCost === null ? "" : editableNumber(loadedCost));
    setSalePrice(editableNumber(context.data.preparedSalePrice));
    setMargin(editableNumber(loadedMargin));
    setIncrement(editableNumber(context.data.roundingIncrement));
    setRoundingMode(context.data.roundingMode);
    setLastEdited(loadedCost === null ? "salePrice" : "margin");
    setDirty(false);
  }, [context.data]);

  const effectiveSalesTaxRate = salesTaxRateOverride ?? context.data?.salesTaxRate ?? 0;

  function change(field: ProductPricingField, raw: string) {
    setLastEdited(field);
    setDirty(true);
    if (field === "cost") setCost(raw);
    if (field === "margin") setMargin(raw);
    if (field === "salePrice") setSalePrice(raw);

    const value = decimalOrNull(raw);
    if (value === null) return;
    try {
      const calculated = recalculateProductPricing(field, value, {
        cost: decimalOrNull(cost) ?? 0,
        margin: decimalOrNull(margin) ?? 0,
        salePrice: decimalOrNull(salePrice) ?? 0,
        salesTaxRate: effectiveSalesTaxRate,
      });
      if (field !== "cost") setCost(editableNumber(calculated.cost));
      if (field !== "margin") setMargin(editableNumber(calculated.margin));
      if (field !== "salePrice") setSalePrice(editableNumber(calculated.salePrice));
    } catch {
      // The contextual validation remains visible while the user corrects the value.
    }
  }

  const resolvedCost = decimalOrNull(cost);
  const resolvedMargin = decimalOrNull(margin);
  const resolvedSale = decimalOrNull(salePrice);
  const resolvedIncrement = decimalOrNull(increment);
  const savesBySalePrice = lastEdited === "salePrice" || resolvedCost === null;
  const validMargin = resolvedMargin !== null && resolvedMargin > 0 && resolvedMargin < 100;
  const valid = resolvedSale !== null && resolvedSale > 0
    && validMargin
    && resolvedCost !== null && resolvedCost > 0
    && resolvedIncrement !== null && resolvedIncrement > 0;
  const salesTaxRate = effectiveSalesTaxRate;
  const netSalePrice = resolvedSale === null ? 0 : resolvedSale / (1 + salesTaxRate / 100);
  const marginAmount = resolvedCost === null ? 0 : netSalePrice - resolvedCost;
  const taxAmount = resolvedSale === null ? 0 : resolvedSale - netSalePrice;

  const save = useCallback(async () => {
    if (!valid || resolvedSale === null || resolvedMargin === null || resolvedIncrement === null) return;
    try {
      const result = await savePrepared.mutateAsync({
        productId,
        request: {
          inputMode: savesBySalePrice ? "SalePrice" : "Margin",
          targetMarginPercent: savesBySalePrice ? null : resolvedMargin,
          salePrice: savesBySalePrice ? resolvedSale : null,
          roundingIncrement: resolvedIncrement,
          roundingMode,
          costBasisAmount: resolvedCost,
        },
      });
      setDirty(false);
      toast.success(`Precio preparado: ${formatCurrency(result.preparedAmount)}. Aún no se ha publicado.`);
      onSaved?.(result);
      await context.refetch();
    } catch {
      toast.error("No fue posible guardar el precio. Revisa costo, margen, precio y redondeo.");
    }
  }, [context, onSaved, productId, resolvedCost, resolvedIncrement, resolvedMargin, resolvedSale, roundingMode, savePrepared, savesBySalePrice, valid]);

  useImperativeHandle(ref, () => ({
    save: async () => { if (dirty) await save(); },
  }), [dirty, save]);
  const costOrigin = context.data?.costBasisOrigin === "ObservedSupplierCost"
    ? "Último proveedor"
    : context.data?.costBasisOrigin === "Manual" ? "Costo manual" : "Sin costo registrado";

  if (context.isLoading) return <PricingState label="Preparando costo, margen y precios…" />;
  if (context.isError || !context.data) return <PricingState error label="No fue posible cargar la información de precios." />;

  return <section className={embedded ? "" : "overflow-hidden rounded-2xl border bg-card"}>
    {!embedded && <div className="flex flex-col gap-4 border-b bg-gradient-to-r from-emerald-950 to-teal-900 p-5 text-white sm:flex-row sm:items-center sm:justify-between">
      <div>
        <p className="flex items-center gap-2 text-xs font-bold uppercase tracking-[.14em] text-emerald-200"><CircleDollarSign className="h-4 w-4" /> Precio y rentabilidad</p>
        <h3 className="mt-1 text-lg font-semibold">{productName}</h3>
        <p className="mt-1 text-xs text-emerald-100/80">Edita costo, margen o precio; los demás valores se recalculan al instante.</p>
      </div>
      <div className="grid grid-cols-[auto_auto_auto] items-center gap-2 rounded-xl bg-white/10 px-3 py-2 text-sm">
        <span><small className="block text-emerald-100/70">Público</small><b>{formatCurrency(context.data.publicSalePrice)}</b></span>
        <ArrowRight className="h-4 w-4 text-emerald-200" />
        <span><small className="block text-emerald-100/70">Preparado</small><b>{formatCurrency(context.data.preparedSalePrice)}</b></span>
      </div>
    </div>}

    <div className={`space-y-5 ${embedded ? "" : "p-5"}`}>
      <section>
        <div className="mb-3"><h4 className="font-semibold">Datos para calcular el precio</h4><p className="text-xs text-muted-foreground">Costo y margen determinan primero el precio antes de IVA.</p></div>
        <div className="grid gap-4 lg:grid-cols-2">
          <PriceInput label="Costo base" helper={costOrigin} kind="currency" value={cost} onChange={(value) => change("cost", value)} />
          <PriceInput label="Margen sobre el precio antes de IVA" helper="No incluye IVA" kind="percent" value={margin} onChange={(value) => change("margin", value)} />
        </div>
      </section>

      <PricingFormula
        cost={resolvedCost ?? 0}
        marginPercent={resolvedMargin ?? 0}
        marginAmount={marginAmount}
        netSalePrice={netSalePrice}
        salesTaxRate={salesTaxRate}
        taxAmount={taxAmount}

        salePrice={salePrice}
        onSalePriceChange={(value) => change("salePrice", value)}
      />
      <div className="flex flex-col gap-4 rounded-xl bg-muted/40 p-4 lg:flex-row lg:items-end">
        <div className="grid flex-1 gap-4 sm:grid-cols-2">
          <div className="space-y-2">
            <Label htmlFor={`rounding-${productId}`}>Redondear a múltiplos de</Label>
            <FormattedNumberInput id={`rounding-${productId}`} kind="currency" value={increment} onValueChange={(value) => { setIncrement(value?.toString() ?? ""); setDirty(true); }} />
          </div>
          <div className="space-y-2">
            <Label>Dirección del redondeo</Label>
            <Select value={roundingMode} onValueChange={(value) => { setRoundingMode(value as PricingRoundingMode); setDirty(true); }}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Nearest">Al más cercano</SelectItem>
                <SelectItem value="Up">Hacia arriba</SelectItem>
                <SelectItem value="Down">Hacia abajo</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        {!embedded && <Button type="button" className="min-w-48" onClick={save} disabled={!dirty || !valid || savePrepared.isPending}>
          {savePrepared.isPending ? "Guardando…" : <><Send className="mr-2 h-4 w-4" />Guardar precio preparado</>}
        </Button>}
      </div>

      {!valid && dirty && <p className="text-sm text-destructive">El costo y el precio no pueden ser negativos; el margen debe ser menor de 100 % y el múltiplo debe ser mayor que cero.</p>}
      {resolvedMargin !== null && resolvedMargin < 0 && savesBySalePrice && <p className="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">Este precio produce una pérdida de {editableNumber(Math.abs(resolvedMargin))} %. Puedes guardarlo como precio explícito.</p>}
      <p className="flex items-center gap-2 text-xs text-muted-foreground"><CheckCircle2 className="h-4 w-4 text-emerald-600" />Guardar prepara el precio. Solo la vista Rentabilidad y precios lo publica en POS, pedidos y bot.</p>
    </div>
  </section>;
});

function PricingFormula({ cost, marginPercent, marginAmount, netSalePrice, salesTaxRate, taxAmount, salePrice, onSalePriceChange }: {
  cost: number;
  marginPercent: number;
  marginAmount: number;
  netSalePrice: number;
  salesTaxRate: number;
  taxAmount: number;
  salePrice: string;
  onSalePriceChange: (value: string) => void;
}) {
  return <section className="rounded-2xl border border-emerald-200 bg-emerald-50/60 p-4 text-emerald-950">
    <div className="mb-3"><h4 className="font-semibold">Así se forma el precio de venta</h4><p className="text-xs text-emerald-900/75">El margen se calcula sobre el precio antes de IVA; después se agrega el IVA de venta.</p></div>
    <div className="grid items-stretch gap-2 md:grid-cols-[1fr_auto_1fr_auto_1fr_auto_1fr]">
      <FormulaStep label="Costo base" value={formatCurrency(cost)} />
      <FormulaOperator value={`÷ (1 − ${editableNumber(marginPercent)}%)`} />
      <FormulaStep label="Precio antes de IVA" value={formatCurrency(netSalePrice)} detail={`Margen: ${formatCurrency(marginAmount)}`} />
      <FormulaOperator value="+" />
      <FormulaStep label={`IVA de venta (${editableNumber(salesTaxRate)}%)`} value={formatCurrency(taxAmount)} />
      <FormulaOperator value="=" />
      <div className="rounded-xl bg-emerald-600 p-3 text-white"><div className="mb-2 flex items-center justify-between gap-2"><Label className="text-xs text-white">Precio de venta preparado</Label><span className="text-xs text-white/75">IVA incluido · editable</span></div><FormattedNumberInput className="h-11 border-white/30 bg-white text-lg font-bold text-emerald-950" kind="currency" value={salePrice} onValueChange={(next) => onSalePriceChange(next === null ? "" : next.toString())} /><p className="mt-2 text-xs text-white/75">Al cambiarlo se conserva el costo y se recalcula el margen.</p></div>
    </div>
    <p className="mt-3 rounded-lg bg-white/70 px-3 py-2 text-xs">Fórmula completa: precio antes de IVA = costo ÷ (1 − margen %). Precio de venta = precio antes de IVA + IVA.</p>
  </section>;
}

function FormulaStep({ label, value, detail, accent = false }: { label: string; value: string; detail?: string; accent?: boolean }) {
  return <div className={`p-3 ${accent ? "rounded-xl bg-emerald-600 text-white" : ""}`}><p className={`text-xs ${accent ? "text-white/75" : "text-emerald-900/70"}`}>{label}</p><p className="mt-1 text-lg font-bold">{value}</p>{detail && <p className={`mt-1 text-xs ${accent ? "text-white/75" : "text-emerald-900/70"}`}>{detail}</p>}</div>;
}

function FormulaOperator({ value }: { value: string }) {
  return <div className="flex min-w-12 items-center justify-center rounded-lg px-1 py-2 text-center text-xs font-bold text-emerald-800">{value}</div>;
}

function PriceInput({ label, helper, kind, value, onChange, emphasized = false }: { label: string; helper: string; kind: "currency" | "percent"; value: string; onChange: (value: string) => void; emphasized?: boolean }) {
  return <div className={`rounded-xl border p-4 ${emphasized ? "border-primary/40 bg-background" : "bg-background"}`}><div className="mb-3 flex items-start justify-between gap-2"><Label>{label}</Label><span className="text-xs text-muted-foreground">{helper}</span></div><FormattedNumberInput className="h-12 text-lg font-semibold" kind={kind} value={value} onValueChange={(next) => onChange(next === null ? "" : next.toString())} /></div>;
}

function PricingState({ label, error = false }: { label: string; error?: boolean }) {
  return <div className={`rounded-2xl border p-8 text-center text-sm ${error ? "border-destructive/30 text-destructive" : "text-muted-foreground"}`}>{label}</div>;
}

function decimalOrNull(value: string) {
  const normalized = value.replace(/\s/g, "").replace(",", ".").trim();
  if (!normalized) return null;
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function editableNumber(value: number): string {
  return Number(value.toFixed(4)).toString();
}
