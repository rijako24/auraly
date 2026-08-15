"use client";

import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { taxationApi, type SaveWithholdingRule, type WithholdingBaseKind, type WithholdingDirection, type WithholdingKind } from "@/services/api/taxation";
import { goodsReceiptsApi } from "@/services/api/goods-receipts";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";

const kindLabels: Record<WithholdingKind, string> = { IncomeTax: "Retefuente", Vat: "ReteIVA", IndustryCommerce: "ReteICA" };

export default function WithholdingRulesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<WithholdingKind>("IncomeTax");
  const direction: WithholdingDirection = "Purchase";
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [rate, setRate] = useState("");
  const [minimumBase, setMinimumBase] = useState("0");
  const [conceptCode, setConceptCode] = useState("");
  const [jurisdictionCode, setJurisdictionCode] = useState("");
  const [responsibilities, setResponsibilities] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10));
  const [supplierId, setSupplierId] = useState("");
  const [profileResponsibilities, setProfileResponsibilities] = useState("");
  const [profileJurisdiction, setProfileJurisdiction] = useState("");

  const suppliers = useQuery({ queryKey: ["withholding-suppliers", businessId], queryFn: goodsReceiptsApi.options, enabled: Boolean(businessId) });
  const rules = useQuery({ queryKey: ["withholding-rules", businessId], queryFn: () => taxationApi.listRules(true), enabled: Boolean(businessId) });
  const profile = useQuery({
    queryKey: ["withholding-profile", businessId, supplierId],
    queryFn: () => taxationApi.getProfile(supplierId),
    enabled: Boolean(businessId && supplierId),
    retry: false,
  });
  const baseKind: WithholdingBaseKind = kind === "Vat" ? "VatAmount" : "TaxExclusiveAmount";
  const save = useMutation({
    mutationFn: (request: SaveWithholdingRule) => taxationApi.createRule(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["withholding-rules", businessId] });
      setCode(""); setName(""); setRate("");
      toast.success("Regla de retención creada");
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la regla"),
  });
  const saveProfile = useMutation({
    mutationFn: () => taxationApi.saveProfile({
      businessId: businessId!, counterpartyId: supplierId,
      responsibilities: profileResponsibilities.split(",").map((value) => value.trim()).filter(Boolean),
      jurisdictionCode: profileJurisdiction.trim() || null,
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["withholding-profile", businessId, supplierId] });
      toast.success("Perfil tributario del proveedor guardado");
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar el perfil"),
  });

  useEffect(() => {
    if (!supplierId) {
      setProfileResponsibilities("");
      setProfileJurisdiction("");
      return;
    }
    if (profile.data) {
      setProfileResponsibilities(profile.data.responsibilities.join(", "));
      setProfileJurisdiction(profile.data.jurisdictionCode ?? "");
    } else if (!profile.isFetching) {
      setProfileResponsibilities("");
      setProfileJurisdiction("");
    }
  }, [profile.data, profile.isFetching, supplierId]);

  const grouped = useMemo(() => [...(rules.data ?? [])].sort((a, b) => a.code.localeCompare(b.code)), [rules.data]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!businessId) return toast.error("Selecciona un negocio");
    if (kind === "IndustryCommerce" && !jurisdictionCode.trim()) return toast.error("ReteICA requiere municipio o jurisdicción");
    save.mutate({
      businessId, code, name, kind, direction, moment: "Accrual", baseKind,
      conceptCode: conceptCode.trim() || null,
      jurisdictionCode: jurisdictionCode.trim() || null,
      rate: Number(rate), minimumBase: Number(minimumBase),
      requiredResponsibilities: responsibilities.split(",").map((value) => value.trim()).filter(Boolean),
      effectiveFrom, effectiveTo: null, isActive: true,
    });
  };

  return <div className="space-y-6">
    <div><h1 className="text-2xl font-semibold">Retenciones</h1><p className="text-sm text-muted-foreground">Reglas versionadas para retefuente, ReteIVA y ReteICA. Las facturas guardan una copia inmutable del cálculo aplicado.</p></div>
    <Card><CardHeader><CardTitle>Nueva regla</CardTitle></CardHeader><CardContent>
      <form onSubmit={submit} className="grid gap-4 md:grid-cols-3">
        <Field label="Tipo"><Select value={kind} onValueChange={(value) => setKind(value as WithholdingKind)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="IncomeTax">Retefuente</SelectItem><SelectItem value="Vat">ReteIVA</SelectItem><SelectItem value="IndustryCommerce">ReteICA</SelectItem></SelectContent></Select></Field>
        <Field label="Operación"><Input value="Compras" disabled /></Field>
        <Field label="Reconocimiento"><Input value="En la causación" disabled /></Field>
        <Field label="Base"><Input value={baseKind === "VatAmount" ? "IVA del documento" : "Subtotal sin impuestos"} disabled /></Field>
        <Field label="Código"><Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={32} /></Field>
        <Field label="Nombre"><Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={120} /></Field>
        <Field label="Tarifa %"><Input type="number" step="0.000001" min="0.000001" max="100" value={rate} onChange={(e) => setRate(e.target.value)} required /></Field>
        <Field label="Base mínima"><Input type="number" step="0.01" min="0" value={minimumBase} onChange={(e) => setMinimumBase(e.target.value)} required /></Field>
        <Field label="Concepto"><Input value={conceptCode} onChange={(e) => setConceptCode(e.target.value)} placeholder="Opcional" /></Field>
        <Field label="Municipio / jurisdicción"><Input value={jurisdictionCode} onChange={(e) => setJurisdictionCode(e.target.value)} required={kind === "IndustryCommerce"} placeholder={kind === "IndustryCommerce" ? "Ej. 11001" : "Opcional"} /></Field>
        <Field label="Responsabilidades requeridas"><Input value={responsibilities} onChange={(e) => setResponsibilities(e.target.value)} placeholder="Separadas por coma" /></Field>
        <Field label="Vigente desde"><Input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} required /></Field>
        <div className="flex items-end"><Button type="submit" disabled={save.isPending}>{save.isPending ? "Guardando…" : "Crear versión"}</Button></div>
      </form>
    </CardContent></Card>
    <Card><CardHeader><CardTitle>Perfil tributario del proveedor</CardTitle></CardHeader><CardContent>
      <div className="grid gap-4 md:grid-cols-3">
        <Field label="Proveedor"><Select value={supplierId} onValueChange={setSupplierId}><SelectTrigger><SelectValue placeholder={suppliers.isLoading ? "Cargando proveedores…" : "Selecciona proveedor"} /></SelectTrigger><SelectContent>{(suppliers.data?.suppliers ?? []).map((supplier) => <SelectItem key={supplier.supplierId} value={supplier.supplierId}>{supplier.identification} — {supplier.name}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Responsabilidades"><Input value={profileResponsibilities} onChange={(event) => setProfileResponsibilities(event.target.value)} placeholder="Separadas por coma" /></Field>
        <Field label="Municipio / jurisdicción"><Input value={profileJurisdiction} onChange={(event) => setProfileJurisdiction(event.target.value)} placeholder="Ej. 11001" /></Field>
      </div>
      <div className="mt-4 flex justify-end"><Button type="button" disabled={!supplierId || saveProfile.isPending} onClick={() => saveProfile.mutate()}>
        Guardar perfil tributario
      </Button></div>
      <p className="mt-3 text-xs text-muted-foreground">Las responsabilidades determinan qué reglas aplican. La factura conserva una copia de la regla y tarifa efectivamente usadas.</p>
    </CardContent></Card>

    <Card><CardHeader><CardTitle>Reglas configuradas</CardTitle></CardHeader><CardContent>
      {rules.isLoading ? <p className="text-sm text-muted-foreground">Cargando reglas…</p> : grouped.length === 0 ? <p className="text-sm text-muted-foreground">No hay reglas configuradas.</p> :
        <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left"><th className="py-2">Código</th><th>Tipo</th><th>Operación</th><th>Tarifa</th><th>Base mínima</th><th>Vigencia</th><th>Estado</th></tr></thead><tbody>{grouped.map((rule) => <tr key={`${rule.ruleId}-${rule.version}`} className="border-b"><td className="py-3 font-medium">{rule.code}<span className="ml-2 text-xs text-muted-foreground">v{rule.version}</span></td><td>{kindLabels[rule.kind]}</td><td>{rule.direction === "Purchase" ? "Compras" : "Ventas"}</td><td>{rule.rate}%</td><td>{rule.minimumBase.toLocaleString("es-CO")}</td><td>{rule.effectiveFrom}</td><td><Badge variant={rule.isActive ? "secondary" : "outline"}>{rule.isActive ? "Activa" : "Inactiva"}</Badge></td></tr>)}</tbody></table></div>}
    </CardContent></Card>
  </div>;
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <div className="space-y-2"><Label>{label}</Label>{children}</div>;
}
