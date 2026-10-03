"use client";

import { useState } from "react";
import { Pencil, Plus, RotateCcw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import type { WithholdingAdjustment, WithholdingCalculation, WithholdingRule } from "@/services/api/taxation";

type Draft = WithholdingAdjustment;
type Calculation = Pick<WithholdingCalculation, "grossAmount"> &
  {lines: Array<Pick<WithholdingCalculation["lines"][number], "ruleId"|"name"|"taxableBase"|"rate"|"amount">>};
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });

export function WithholdingAdjustmentEditor({ calculation, adjustments, rules, onChange, disabled = false }: {
  calculation: Calculation | null;
  adjustments: WithholdingAdjustment[];
  rules: WithholdingRule[];
  onChange: (adjustments: WithholdingAdjustment[]) => void;
  disabled?: boolean;
}) {
  const [draft, setDraft] = useState<Draft | null>(null);
  const available = rules.filter(rule => rule.isActive && rule.direction === "Purchase" &&
    rule.moment === "Accrual" && !calculation?.lines.some(line => line.ruleId === rule.ruleId) &&
    !adjustments.some(item => item.ruleId === rule.ruleId));
  const selected = rules.find(rule => rule.ruleId === draft?.ruleId);

  function begin(line: Calculation["lines"][number]) {
    const existing = adjustments.find(item => item.ruleId === line.ruleId);
    setDraft({ruleId:line.ruleId,action:existing?.action === "Add" ? "Add" : "Override",
      taxableBase:line.taxableBase,amount:line.amount,reason:existing?.reason ?? ""});
  }
  function apply(value: Draft) {
    onChange([...adjustments.filter(item => item.ruleId !== value.ruleId), value]);
    setDraft(null);
  }
  function restore(ruleId: string) {
    onChange(adjustments.filter(item => item.ruleId !== ruleId));
  }
  return <div className="space-y-3 rounded-xl border p-4">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div><h4 className="font-medium">Retenciones</h4><p className="text-xs text-muted-foreground">El cálculo automático sigue la configuración tributaria. Los ajustes requieren motivo.</p></div>
      <Button type="button" size="sm" variant="outline" disabled={disabled || !calculation || available.length === 0}
        onClick={() => setDraft({ruleId:"",action:"Add",taxableBase:null,amount:null,reason:""})}>
        <Plus className="mr-1 h-4 w-4"/>Agregar retención
      </Button>
    </div>
    {calculation?.lines.map(line => {
      const adjustment = adjustments.find(item => item.ruleId === line.ruleId);
      return <div key={line.ruleId} className="flex flex-wrap items-center justify-between gap-2 border-t pt-2 text-sm">
        <span><b>{line.name}</b><small className="block text-muted-foreground">{adjustment ? adjustment.action === "Add" ? "Agregada manualmente" : "Ajustada manualmente" : "Automática"} · Base {money.format(line.taxableBase)} · {line.rate}%</small></span>
        <div className="flex items-center gap-1"><b>{money.format(line.amount)}</b>
          <Button type="button" size="icon" variant="ghost" aria-label={`Editar ${line.name}`} disabled={disabled} onClick={() => begin(line)}><Pencil className="h-4 w-4"/></Button>
          {adjustment ? <Button type="button" size="icon" variant="ghost" aria-label={`Restaurar ${line.name}`} disabled={disabled} onClick={() => restore(line.ruleId)}><RotateCcw className="h-4 w-4"/></Button> :
            <Button type="button" size="icon" variant="ghost" aria-label={`Excluir ${line.name}`} disabled={disabled} onClick={() => setDraft({ruleId:line.ruleId,action:"Exclude",taxableBase:null,amount:null,reason:""})}><Trash2 className="h-4 w-4"/></Button>}
        </div>
      </div>;
    })}
    {adjustments.filter(item => item.action === "Exclude").map(item => <div key={item.ruleId} className="flex items-center justify-between border-t pt-2 text-sm text-muted-foreground">
      <span>{rules.find(rule => rule.ruleId === item.ruleId)?.name ?? "Retención"} · Excluida: {item.reason}</span>
      <Button type="button" size="sm" variant="ghost" disabled={disabled} onClick={() => restore(item.ruleId)}>Restaurar</Button>
    </div>)}
    {adjustments.filter(item => item.action !== "Exclude" && !calculation?.lines.some(line => line.ruleId === item.ruleId)).map(item =>
      <div key={item.ruleId} className="flex items-center justify-between border-t pt-2 text-sm text-muted-foreground">
        <span>{rules.find(rule => rule.ruleId === item.ruleId)?.name ?? "Retención ajustada"} · Requiere revisar el cálculo</span>
        <Button type="button" size="sm" variant="ghost" disabled={disabled} onClick={() => restore(item.ruleId)}>Quitar ajuste</Button>
      </div>)}
    <Dialog open={!!draft} onOpenChange={open => {if (!open) setDraft(null)}}>
      <DialogContent className="sm:max-w-lg"><DialogHeader><DialogTitle>{draft?.action === "Add" ? "Agregar retención" : draft?.action === "Exclude" ? "Excluir retención" : "Ajustar retención"}</DialogTitle>
        <DialogDescription>El motivo y el cálculo final quedarán registrados con el documento.</DialogDescription></DialogHeader>
        {draft && <div className="space-y-4">
          {draft.action === "Add" && <div className="space-y-1"><Label>Regla configurada</Label><Select value={draft.ruleId} onValueChange={ruleId => setDraft({...draft,ruleId})}><SelectTrigger><SelectValue placeholder="Seleccionar retención"/></SelectTrigger><SelectContent>{available.map(rule => <SelectItem key={rule.ruleId} value={rule.ruleId}>{rule.code} · {rule.name} ({rule.rate}%)</SelectItem>)}</SelectContent></Select></div>}
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
