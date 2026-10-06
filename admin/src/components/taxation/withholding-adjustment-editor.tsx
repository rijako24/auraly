"use client";

import { useState } from "react";
import { Pencil, Plus, RotateCcw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ManualWithholdingDialog } from "./manual-withholding-dialog";
import type { WithholdingAdjustment, WithholdingCalculation, WithholdingKind, WithholdingRule } from "@/services/api/taxation";

type Draft = WithholdingAdjustment;
type Calculation = Pick<WithholdingCalculation, "grossAmount"> &
  {lines: WithholdingCalculation["lines"]};
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });

export function WithholdingAdjustmentEditor({ businessId, occurredAt, calculation, adjustments, rules, onChange, disabled = false }: {
  businessId: string;
  occurredAt: string;
  calculation: Calculation | null;
  adjustments: WithholdingAdjustment[];
  rules: WithholdingRule[];
  onChange: (adjustments: WithholdingAdjustment[]) => void;
  disabled?: boolean;
}) {
  const [draft, setDraft] = useState<Draft | null>(null);
  const [manualId, setManualId] = useState<string | null>(null);
  const selected = rules.find(rule => rule.ruleId === draft?.ruleId);

  function begin(line: Calculation["lines"][number]) {
    if (line.manualLineId) { setManualId(line.manualLineId); return; }
    const existing = adjustments.find(item => item.ruleId === line.ruleId);
    setDraft({ruleId:line.ruleId,action:existing?.action === "Add" ? "Add" : "Override",
      taxableBase:line.taxableBase,amount:line.amount,reason:existing?.reason ?? ""});
  }
  function apply(value: Draft) {
    onChange([...adjustments.filter(item => item.action === "Manual" || item.ruleId !== value.ruleId), value]);
    setDraft(null);
  }
  function restore(ruleId: string) {
    onChange(adjustments.filter(item => item.action === "Manual" || item.ruleId !== ruleId));
  }
  function removeManual(id: string) {
    onChange(adjustments.filter(item => item.action !== "Manual" || item.manualLineId !== id));
  }
  return <div className="space-y-3 rounded-xl border p-4">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div><h4 className="font-medium">Retenciones</h4><p className="text-xs text-muted-foreground">El cálculo automático sigue la configuración tributaria. Puedes ajustar una línea o agregar una retención puntual.</p></div>
      <Button type="button" size="sm" variant="outline" disabled={disabled || !calculation}
        onClick={() => setManualId("new")}>
        <Plus className="mr-1 h-4 w-4"/>Agregar retención
      </Button>
    </div>
    {calculation?.lines.map(line => {
      const adjustment = adjustments.find(item => line.manualLineId
        ? item.action === "Manual" && item.manualLineId === line.manualLineId
        : item.action !== "Manual" && item.ruleId === line.ruleId);
      return <div key={line.manualLineId ?? line.ruleId} className="flex flex-wrap items-center justify-between gap-2 border-t pt-2 text-sm">
        <span><b>{line.name}</b><small className="block text-muted-foreground">{line.manualLineId ? "Manual" : adjustment ? adjustment.action === "Add" ? "Agregada manualmente" : "Ajustada manualmente" : "Automática"} · Base {money.format(line.taxableBase)} · {line.rate}%</small></span>
        <div className="flex items-center gap-1"><b>{money.format(line.amount)}</b>
          <Button type="button" size="icon" variant="ghost" aria-label={`Editar ${line.name}`} disabled={disabled} onClick={() => begin(line)}><Pencil className="h-4 w-4"/></Button>
          {line.manualLineId ? <Button type="button" size="icon" variant="ghost" aria-label={`Quitar ${line.name}`} disabled={disabled} onClick={() => removeManual(line.manualLineId!)}><Trash2 className="h-4 w-4"/></Button> :
            adjustment ? <Button type="button" size="icon" variant="ghost" aria-label={`Restaurar ${line.name}`} disabled={disabled} onClick={() => restore(line.ruleId)}><RotateCcw className="h-4 w-4"/></Button> :
            <Button type="button" size="icon" variant="ghost" aria-label={`Excluir ${line.name}`} disabled={disabled} onClick={() => setDraft({ruleId:line.ruleId,action:"Exclude",taxableBase:null,amount:null,reason:""})}><Trash2 className="h-4 w-4"/></Button>}
        </div>
      </div>;
    })}
    {adjustments.filter(item => item.action === "Exclude").map(item => <div key={item.ruleId} className="flex items-center justify-between border-t pt-2 text-sm text-muted-foreground">
      <span>{rules.find(rule => rule.ruleId === item.ruleId)?.name ?? "Retención"} · Excluida: {item.reason}</span>
      <Button type="button" size="sm" variant="ghost" disabled={disabled} onClick={() => restore(item.ruleId)}>Restaurar</Button>
    </div>)}
    {adjustments.filter(item => item.action !== "Exclude" && !calculation?.lines.some(line =>
      item.action === "Manual" ? line.manualLineId === item.manualLineId : line.ruleId === item.ruleId)).map(item =>
      <div key={item.manualLineId ?? item.ruleId} className="flex items-center justify-between border-t pt-2 text-sm text-muted-foreground">
        <span>{item.name ?? rules.find(rule => rule.ruleId === item.ruleId)?.name ?? "Retención ajustada"} · Requiere revisar el cálculo</span>
        <Button type="button" size="sm" variant="ghost" disabled={disabled} onClick={() => item.action === "Manual" ? removeManual(item.manualLineId!) : restore(item.ruleId)}>Quitar ajuste</Button>
      </div>)}
    {manualId && calculation && <ManualWithholdingDialog key={manualId} businessId={businessId} occurredAt={occurredAt} rules={rules}
      grossAmount={calculation.grossAmount}
      existingLines={calculation.lines.filter(line => line.manualLineId !== manualId).map(line =>
        ({kind:line.kind as WithholdingKind,taxableBase:line.taxableBase,jurisdictionCode:line.jurisdictionCode}))}
      initial={manualId === "new" ? undefined : adjustments.find(item => item.action === "Manual" && item.manualLineId === manualId)}
      onClose={() => setManualId(null)} onApply={value => onChange([...adjustments.filter(item =>
        item.action !== "Manual" || item.manualLineId !== value.manualLineId),value])}/>}
    <Dialog open={!!draft} onOpenChange={open => {if (!open) setDraft(null)}}>
      <DialogContent className="sm:max-w-lg"><DialogHeader><DialogTitle>{draft?.action === "Add" ? "Agregar retención" : draft?.action === "Exclude" ? "Excluir retención" : "Ajustar retención"}</DialogTitle>
        <DialogDescription>El motivo y el cálculo final quedarán registrados con el documento.</DialogDescription></DialogHeader>
        {draft && <div className="space-y-4">
          {draft.action === "Add" && <p className="text-sm font-medium">{selected?.name ?? "Regla configurada"}</p>}
          {draft.action !== "Exclude" && <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1"><Label>Base de retención</Label><FormattedNumberInput kind="currency" value={draft.taxableBase ?? ""} onValueChange={base => setDraft({...draft,taxableBase:base,amount:base && selected ? Math.round(base * selected.rate * 100) / 10000 : null})}/></div>
            <div className="space-y-1"><Label>Valor retenido</Label><FormattedNumberInput kind="currency" value={draft.amount ?? ""} onValueChange={amount => setDraft({...draft,amount})}/></div>
          </div>}
          <div className="space-y-1"><Label>Motivo del ajuste</Label><Textarea maxLength={500} value={draft.reason} onChange={event => setDraft({...draft,reason:event.target.value})} placeholder="Explica por qué se cambia el cálculo"/></div>
          <DialogFooter><Button type="button" variant="outline" onClick={() => setDraft(null)}>Cerrar</Button><Button type="button" disabled={!draft.ruleId || !draft.reason.trim() || draft.reason.trim().length > 500 ||
            (draft.action !== "Exclude" && (!(draft.taxableBase && draft.taxableBase > 0) || !(draft.amount && draft.amount > 0) ||
              !!calculation && (draft.taxableBase > calculation.grossAmount || draft.amount > calculation.grossAmount)))}
            onClick={() => apply({...draft,reason:draft.reason.trim()})}>Aplicar</Button></DialogFooter>
        </div>}
      </DialogContent>
    </Dialog>
  </div>;
}
