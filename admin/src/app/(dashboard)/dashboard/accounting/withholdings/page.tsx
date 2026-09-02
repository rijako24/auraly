"use client";

import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, ReceiptText } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { taxationApi, type SaveWithholdingRule, type WithholdingBaseKind, type WithholdingDirection, type WithholdingKind, type WithholdingRule } from "@/services/api/taxation";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { useReferenceOptions } from "@/hooks/use-reference-options";

export function WithholdingRulesWorkspace() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canManage = useAuthStore((state) => state.user?.permissions.includes("commerce.taxation.withholdings.manage") ?? false);
  const kinds = useReferenceOptions("accounting-withholding-kind");
  const kindLabels = new Map((kinds.data ?? []).map((item) => [item.code, item.label]));
  const rules = useQuery({ queryKey: ["withholding-rules", businessId], queryFn: () => taxationApi.listRules(true), enabled: Boolean(businessId) });
  const [editing, setEditing] = useState<WithholdingRule | null | undefined>(undefined);
  const grouped = useMemo(() => [...(rules.data ?? [])].sort((a, b) => a.code.localeCompare(b.code) || b.version - a.version), [rules.data]);
  const active = grouped.filter((rule) => rule.isActive).length;

  return <div className="space-y-6">
    <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
      <div><p className="text-sm font-medium text-emerald-600">Impuestos y cumplimiento</p><h1 className="text-3xl font-bold tracking-tight">Retenciones</h1><p className="mt-1 text-muted-foreground">Consulta primero las reglas vigentes. Cada cambio crea una versión trazable para no alterar documentos anteriores.</p></div>
      {canManage && <Button onClick={() => setEditing(null)}><Plus className="mr-2 h-4 w-4" />Nueva regla</Button>}
    </header>
    <div className="grid gap-4 sm:grid-cols-3"><Metric label="Reglas configuradas" value={grouped.length}/><Metric label="Reglas activas" value={active}/><Metric label="Tipos cubiertos" value={new Set(grouped.map((rule) => rule.kind)).size}/></div>
    <Card><CardContent className="p-0">
      <div className="border-b p-5"><h2 className="font-semibold">Reglas configuradas</h2><p className="text-sm text-muted-foreground">El motor cruza estas reglas con el perfil tributario guardado en clientes y proveedores.</p></div>
      {rules.isLoading ? <p className="p-8 text-center text-sm text-muted-foreground">Cargando reglas…</p> : grouped.length === 0 ? <div className="p-10 text-center"><ReceiptText className="mx-auto mb-3 h-9 w-9 text-primary"/><p className="font-medium">Aún no hay reglas de retención</p><p className="mt-1 text-sm text-muted-foreground">Crea la primera regla para comenzar el cálculo automático.</p></div> :
        <div className="overflow-x-auto"><table className="w-full text-sm"><thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="p-3 text-left font-semibold">Regla</th><th className="p-3 text-left font-semibold">Tipo</th><th className="p-3 text-right font-semibold">Tarifa</th><th className="p-3 text-right font-semibold">Base mínima</th><th className="p-3 text-left font-semibold">Vigencia</th><th className="p-3 text-left font-semibold">Estado</th><th className="p-3 text-right font-semibold">Acción</th></tr></thead><tbody>{grouped.map((rule) => <tr key={`${rule.ruleId}-${rule.version}`} className="border-t hover:bg-muted/20"><td className="p-3"><b>{rule.code} · {rule.name}</b><small className="block text-muted-foreground">Versión {rule.version}{rule.conceptCode ? ` · ${rule.conceptCode}` : ""}</small></td><td className="p-3">{kindLabels.get(rule.kind) ?? rule.kind}{rule.jurisdictionCode && <small className="block text-muted-foreground">Jurisdicción {rule.jurisdictionCode}</small>}</td><td className="p-3 text-right font-medium">{rule.rate}%</td><td className="p-3 text-right">$ {rule.minimumBase.toLocaleString("es-CO")}</td><td className="p-3">Desde {rule.effectiveFrom}</td><td className="p-3"><Badge variant={rule.isActive ? "secondary" : "outline"}>{rule.isActive ? "Activa" : "Inactiva"}</Badge></td><td className="p-3 text-right"><Button size="sm" variant="outline" onClick={() => setEditing(rule)}><Pencil className="mr-2 h-3.5 w-3.5"/>Nueva versión</Button></td></tr>)}</tbody></table></div>}
    </CardContent></Card>
    <div className="rounded-2xl border border-primary/20 bg-primary/5 p-5"><h2 className="font-semibold">¿Dónde se configura el tercero?</h2><p className="mt-1 text-sm text-muted-foreground">En <b>Terceros → Cliente/Proveedor → Retenciones y perfil tributario</b>. Allí se define si está sujeto a retención, sus responsabilidades y jurisdicción; esta vista queda dedicada a las reglas.</p></div>
    {editing !== undefined && <RuleDialog businessId={businessId ?? ""} source={editing} onClose={() => setEditing(undefined)} />}
  </div>;
}

function RuleDialog({ businessId, source, onClose }: { businessId: string; source: WithholdingRule | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const kinds = useReferenceOptions("accounting-withholding-kind");
  const responsibilityOptions = useReferenceOptions("tax-responsibility");
  const [kind, setKind] = useState<WithholdingKind>(source?.kind ?? "IncomeTax");
  const [direction, setDirection] = useState<WithholdingDirection>(source?.direction ?? "Purchase");
  const [code, setCode] = useState(source?.code ?? ""); const [name, setName] = useState(source?.name ?? ""); const [rate, setRate] = useState(source ? String(source.rate) : "");
  const [minimumBase, setMinimumBase] = useState(source ? String(source.minimumBase) : "0"); const [conceptCode, setConceptCode] = useState(source?.conceptCode ?? "");
  const [jurisdictionCode, setJurisdictionCode] = useState(source?.jurisdictionCode ?? ""); const [responsibilities, setResponsibilities] = useState<Set<string>>(new Set(source?.requiredResponsibilities ?? []));
  const [responsibilityToAdd, setResponsibilityToAdd] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10)); const [isActive, setIsActive] = useState(source?.isActive ?? true);
  const baseKind: WithholdingBaseKind = kind === "Vat" ? "VatAmount" : "TaxExclusiveAmount";
  const save = useMutation({ mutationFn: (request: SaveWithholdingRule) => source ? taxationApi.updateRule(source.ruleId, request) : taxationApi.createRule(request), onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["withholding-rules", businessId] }); toast.success(source ? "Nueva versión de la regla creada" : "Regla creada"); onClose(); }, onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la regla") });
  const submit = (event: FormEvent) => { event.preventDefault(); if (kind === "IndustryCommerce" && !jurisdictionCode.trim()) return toast.error("ReteICA requiere municipio o jurisdicción"); save.mutate({ businessId, code: code.trim(), name: name.trim(), kind, direction, moment: "Accrual", baseKind, conceptCode: conceptCode.trim() || null, jurisdictionCode: jurisdictionCode.trim() || null, rate: Number(rate), minimumBase: Number(minimumBase), requiredResponsibilities: [...responsibilities], effectiveFrom, effectiveTo: null, isActive }); };
  return <Dialog open onOpenChange={(open) => !open && onClose()}><DialogContent className="max-h-[92vh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{source ? `Nueva versión de ${source.code}` : "Nueva regla de retención"}</DialogTitle><DialogDescription>{source ? "Se conservará la versión anterior para la trazabilidad de los documentos ya causados." : "Define cuándo y sobre qué base se calcula esta retención."}</DialogDescription></DialogHeader><form onSubmit={submit} className="grid gap-4 md:grid-cols-2">
    <Field label="Tipo"><Select value={kind} onValueChange={(value) => setKind(value as WithholdingKind)} disabled={kinds.isLoading}><SelectTrigger><SelectValue placeholder="Seleccionar tipo"/></SelectTrigger><SelectContent>{(kinds.data ?? []).map((item) => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Operación"><Select value={direction} onValueChange={(value) => setDirection(value as WithholdingDirection)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Purchase">Compra a proveedor</SelectItem><SelectItem value="Sale">Venta a cliente</SelectItem></SelectContent></Select></Field>
    <Field label="Base de cálculo"><Input value={baseKind === "VatAmount" ? "IVA del documento" : "Subtotal sin impuestos"} disabled/></Field>
    <Field label="Código"><Input value={code} onChange={(event) => setCode(event.target.value)} required maxLength={32}/></Field><Field label="Nombre"><Input value={name} onChange={(event) => setName(event.target.value)} required maxLength={120}/></Field>
    <Field label="Tarifa %"><Input type="number" step="0.000001" min="0.000001" max="100" value={rate} onChange={(event) => setRate(event.target.value)} required/></Field><Field label="Base mínima"><Input type="number" step="0.01" min="0" value={minimumBase} onChange={(event) => setMinimumBase(event.target.value)} required/></Field>
    <Field label="Concepto"><Input value={conceptCode} onChange={(event) => setConceptCode(event.target.value)} placeholder="Opcional"/></Field><Field label="Municipio o jurisdicción"><Input value={jurisdictionCode} onChange={(event) => setJurisdictionCode(event.target.value)} required={kind === "IndustryCommerce"} placeholder={kind === "IndustryCommerce" ? "Ej. 11001" : "Opcional"}/></Field>
    <Field label="Responsabilidades requeridas"><Select value={responsibilityToAdd} disabled={responsibilityOptions.isLoading || responsibilityOptions.isError || (responsibilityOptions.data ?? []).every((option) => responsibilities.has(option.code))} onValueChange={(value) => { setResponsibilityToAdd(""); setResponsibilities((current) => new Set(current).add(value)); }}><SelectTrigger><SelectValue placeholder={responsibilityOptions.isLoading ? "Cargando catálogo…" : "Agregar responsabilidad"}/></SelectTrigger><SelectContent>{(responsibilityOptions.data ?? []).filter((option) => !responsibilities.has(option.code)).map((option) => <SelectItem key={option.code} value={option.code}>{option.code} · {option.label}</SelectItem>)}</SelectContent></Select><div className="mt-2 flex min-h-10 flex-wrap gap-2 rounded-xl border p-2">{[...responsibilities].map((code) => <Badge key={code} variant="secondary" className="gap-1">{code}<button type="button" aria-label={`Quitar ${code}`} onClick={() => setResponsibilities((current) => { const next = new Set(current); next.delete(code); return next; })}>×</button></Badge>)}{responsibilities.size === 0 && <span className="text-sm text-muted-foreground">Sin requisito especial</span>}</div></Field><Field label="Vigente desde"><DatePicker value={effectiveFrom} onChange={setEffectiveFrom}/></Field>
    <label className="md:col-span-2 flex items-center gap-3 rounded-xl border p-4"><Checkbox checked={isActive} onCheckedChange={(value) => setIsActive(value === true)}/><span><b className="block text-sm">Regla activa</b><small className="text-muted-foreground">El motor podrá aplicarla desde la fecha de vigencia.</small></span></label>
    <DialogFooter className="md:col-span-2 mt-2 gap-2 sm:gap-2"><Button type="button" variant="outline" onClick={onClose}>Cancelar</Button><Button type="submit" disabled={save.isPending || !businessId}>{save.isPending ? "Guardando…" : source ? "Crear nueva versión" : "Crear regla"}</Button></DialogFooter>
  </form></DialogContent></Dialog>;
}

function Metric({ label, value }: { label: string; value: number }) { return <div className="rounded-2xl border bg-card p-5"><p className="text-sm text-muted-foreground">{label}</p><p className="mt-1 text-2xl font-bold">{value}</p></div>; }
function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }

export default function WithholdingRulesPage() {
  return <WithholdingRulesWorkspace />;
}
