"use client";

import { forwardRef, useCallback, useEffect, useImperativeHandle, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { taxProfilesApi, type ProductTaxConfiguration } from "@/services/api/tax-profiles";
import { useBusinessContextStore } from "@/stores/business-context-store";

export interface ProductTaxEditorHandle { validate: () => void; save: () => Promise<void> }

export const ProductTaxEditor = forwardRef<ProductTaxEditorHandle, { productId: string; embedded?: boolean; onSalesTaxRateChange?: (rate: number) => void }>(function ProductTaxEditor({ productId, embedded = false, onSalesTaxRateChange }, ref) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  const taxes = useQuery({
    queryKey: ["tax-profiles", businessId],
    queryFn: () => taxProfilesApi.list(false),
    enabled: !!businessId,
  });
  const current = useQuery({
    queryKey: ["product-tax-configuration", businessId, productId],
    queryFn: () => taxProfilesApi.getProduct(productId),
    enabled: !!businessId && !!productId,
  });
  const [salesTaxProfileId, setSalesTaxProfileId] = useState("");
  const [purchaseTaxProfileId, setPurchaseTaxProfileId] = useState("");
  const [purchaseTaxTreatment, setPurchaseTaxTreatment] =
    useState<ProductTaxConfiguration["purchaseTaxTreatment"]>("DeductibleInputVat");
  const [validationError, setValidationError] = useState<string>();

  useEffect(() => {
    if (!current.data) return;
    setSalesTaxProfileId(current.data.salesTaxProfileId);
    setPurchaseTaxProfileId(current.data.purchaseTaxProfileId);
    setPurchaseTaxTreatment(current.data.purchaseTaxTreatment);
  }, [current.data]);

  const save = useMutation({
    mutationFn: () => taxProfilesApi.saveProduct(productId, {
      salesTaxProfileId,
      purchaseTaxProfileId,
      purchaseTaxTreatment,
    }),
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ["product-tax-configuration", businessId, productId] }),
        client.invalidateQueries({ queryKey: ["catalog-product", businessId, productId] }),
        client.invalidateQueries({ queryKey: ["products", businessId] }),
        client.invalidateQueries({ queryKey: ["pricing-product", businessId, productId] }),
        client.invalidateQueries({ queryKey: ["price-revision-proposals", businessId] }),
      ]);
      toast.success("IVA de compra y venta actualizados.");
    },
    onError: () => toast.error("No fue posible actualizar los IVA del producto."),
  });
  const validate = useCallback(() => {
      if (!salesTaxProfileId || !purchaseTaxProfileId) {
        const message = !salesTaxProfileId && !purchaseTaxProfileId ? "Selecciona el IVA de venta y el IVA de compra." : !salesTaxProfileId ? "Selecciona el IVA de venta." : "Selecciona el IVA de compra.";
        setValidationError(message);
        throw new Error(message);
      }
      const selectedPurchaseTax = taxes.data?.find((tax) => tax.taxProfileId === purchaseTaxProfileId);
      if ((selectedPurchaseTax?.rate ?? 0) === 0 && purchaseTaxTreatment !== "NotApplicable") {
        const message = "Un IVA de compra del 0 % debe usar el tratamiento No aplica.";
        setValidationError(message);
        throw new Error(message);
      }
      if ((selectedPurchaseTax?.rate ?? 0) > 0 && purchaseTaxTreatment === "NotApplicable") {
        const message = "Selecciona IVA descontable o Mayor valor del costo para un IVA de compra mayor que 0 %.";
        setValidationError(message);
        throw new Error(message);
      }
      setValidationError(undefined);
  }, [purchaseTaxProfileId, purchaseTaxTreatment, salesTaxProfileId, taxes.data]);
  useImperativeHandle(ref, () => ({
    validate,
    save: async () => {
      validate();
      await save.mutateAsync();
    },
  }), [save, validate]);
  const salesTax = taxes.data?.find((tax) => tax.taxProfileId === salesTaxProfileId);
  const purchaseTax = taxes.data?.find((tax) => tax.taxProfileId === purchaseTaxProfileId);
  useEffect(() => {
    if (salesTax) onSalesTaxRateChange?.(salesTax.rate);
  }, [onSalesTaxRateChange, salesTax]);
return <section className={`space-y-4 ${embedded ? "" : "rounded-xl border bg-muted/15 p-4"}`}>
    <div>
      <h3 className="text-sm font-semibold">IVA de compra y venta</h3>
      <p className="text-xs text-muted-foreground">Venta alimenta facturación; compra propone el IVA al recibir mercancía.</p>
    </div>
    {(current.isError || validationError) && <div role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive">{validationError ?? "No fue posible cargar la configuración tributaria guardada. Puedes seleccionar nuevamente los IVA y guardar."}</div>}
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="space-y-2">
        <Label>IVA de venta <span className="text-destructive">*</span></Label>
        <Select value={salesTaxProfileId} onValueChange={(value) => { setSalesTaxProfileId(value); setValidationError(undefined); }}>
          <SelectTrigger aria-invalid={Boolean(validationError && !salesTaxProfileId)}><SelectValue placeholder="Selecciona IVA de venta" /></SelectTrigger>
          <SelectContent>{(taxes.data ?? []).map((tax) =>
            <SelectItem key={tax.taxProfileId} value={tax.taxProfileId}>{tax.name} · {tax.rate.toLocaleString("es-CO")} %</SelectItem>)}</SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label>IVA de compra <span className="text-destructive">*</span></Label>
        <Select value={purchaseTaxProfileId} onValueChange={(value) => { const rate = taxes.data?.find((tax) => tax.taxProfileId === value)?.rate ?? 0; setPurchaseTaxProfileId(value); setPurchaseTaxTreatment((current) => rate === 0 ? "NotApplicable" : current === "NotApplicable" ? "DeductibleInputVat" : current); setValidationError(undefined); }}>
          <SelectTrigger aria-invalid={Boolean(validationError && !purchaseTaxProfileId)}><SelectValue placeholder="Selecciona IVA de compra" /></SelectTrigger>
          <SelectContent>{(taxes.data ?? []).map((tax) =>
            <SelectItem key={tax.taxProfileId} value={tax.taxProfileId}>{tax.name} · {tax.rate.toLocaleString("es-CO")} %</SelectItem>)}</SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label>Tratamiento del IVA de compra</Label>
        <Select value={purchaseTaxTreatment} disabled={(purchaseTax?.rate ?? 0) === 0} onValueChange={(value) => setPurchaseTaxTreatment(value as ProductTaxConfiguration["purchaseTaxTreatment"])}>
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="DeductibleInputVat">IVA descontable · no aumenta el costo</SelectItem>
            <SelectItem value="CapitalizedCost">Mayor valor del costo · sí aumenta el costo</SelectItem>
            <SelectItem value="NotApplicable">No aplica</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
    {!embedded && <div className="flex justify-end">
      <Button type="button" variant="outline" onClick={() => save.mutate()}
        disabled={!salesTaxProfileId || !purchaseTaxProfileId || save.isPending}>
        {save.isPending ? "Guardando…" : "Guardar configuración tributaria"}
      </Button>
    </div>}
  </section>;
});
