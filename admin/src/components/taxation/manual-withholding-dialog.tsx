"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AccountSelect } from "@/components/accounting/account-select";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { taxationApi, type WithholdingAdjustment, type WithholdingKind, type WithholdingRule } from "@/services/api/taxation";

type Draft = {kind:WithholdingKind|"";name:string;taxableBase:number|null;rate:number|null;
  amount:number|null;accountId:string;jurisdictionCode:string;reason:string;saveRule:boolean;code:string};
const empty: Draft = {kind:"",name:"",taxableBase:null,rate:null,amount:null,accountId:"",
  jurisdictionCode:"",reason:"",saveRule:false,code:""};
const calculatedAmount = (base:number|null, rate:number|null) =>
  base && rate ? Math.round(base * rate * 100) / 10000 : null;

export function ManualWithholdingDialog({businessId,occurredAt,grossAmount,existingLines,rules,initial,onClose,onApply}: {
  businessId:string;occurredAt:string;grossAmount:number;
  existingLines:Array<{kind:WithholdingKind;taxableBase:number;jurisdictionCode:string|null}>;
  rules:WithholdingRule[];initial?:WithholdingAdjustment;
  onClose:()=>void;onApply:(value:WithholdingAdjustment)=>void;
}) {
  const queryClient = useQueryClient();
  const kinds = useReferenceOptions("accounting-withholding-kind");
  const [draft,setDraft] = useState<Draft>(() => initial ? {kind:initial.kind ?? "",name:initial.name ?? "",
    taxableBase:initial.taxableBase,rate:initial.rate ?? null,amount:initial.amount,
    accountId:initial.accountId ?? "",jurisdictionCode:initial.jurisdictionCode ?? "",
    reason:initial.reason,saveRule:false,code:""} : empty);
  const [saving,setSaving] = useState(false);
  const [error,setError] = useState<string|null>(null);
  const duplicateKind = !!draft.kind && existingLines.some(line => line.kind === draft.kind &&
    line.taxableBase === draft.taxableBase &&
    (line.jurisdictionCode ?? "") === (draft.kind === "IndustryCommerce" ? draft.jurisdictionCode.trim().toUpperCase() : ""));
  const validationMessage = duplicateKind
    ? "Ya existe una retención de este tipo y base. Ajusta o excluye la existente antes de agregar otra."
    : !kinds.data?.some(item => item.code === draft.kind) ? "Selecciona un tipo de retención válido."
    : !draft.name.trim() ? "Indica el concepto o nombre de la retención."
    : !draft.taxableBase || draft.taxableBase <= 0 ? "Indica una base de retención mayor que cero."
    : draft.taxableBase > grossAmount ? "La base no puede superar el total del documento en COP."
    : !draft.rate || draft.rate <= 0 || draft.rate > 100 ? "Indica una tarifa mayor que cero y hasta 100 %."
    : !draft.amount || draft.amount <= 0 || draft.amount > draft.taxableBase ? "El valor retenido debe ser mayor que cero y no superar la base."
    : !draft.accountId ? "Selecciona la cuenta contable de retención."
    : !draft.reason.trim() ? "Indica el motivo de la retención."
    : draft.kind === "IndustryCommerce" && !draft.jurisdictionCode.trim() ? "Indica la jurisdicción de ReteICA."
    : draft.saveRule && !draft.code.trim() ? "Indica el código de la regla que deseas guardar."
    : null;
  const valid = validationMessage === null;
  async function apply() {
    if (!valid || saving) return;
    setSaving(true);setError(null);
    try {
      if (draft.saveRule) {
        const saved = await taxationApi.createRule({businessId,code:draft.code.trim(),
          name:draft.name.trim(),kind:draft.kind as WithholdingKind,direction:"Purchase",moment:"Accrual",
          baseKind:draft.kind === "Vat" ? "VatAmount" : "TaxExclusiveAmount",
          conceptCode:null,jurisdictionCode:draft.kind === "IndustryCommerce" ? draft.jurisdictionCode.trim() : null,
          rate:draft.rate!,minimumBase:0,requiredResponsibilities:[],defaultAccountId:draft.accountId,
          effectiveFrom:occurredAt.slice(0,10),effectiveTo:null,isActive:true,appliesAutomatically:false});
        queryClient.setQueryData<WithholdingRule[]>(["withholding-rules",businessId,"purchase-adjustments"],
          current => [...(current ?? []),saved]);
      }
      onApply({ruleId:"00000000-0000-0000-0000-000000000000",action:"Manual",
        manualLineId:initial?.manualLineId ?? crypto.randomUUID(),kind:draft.kind as WithholdingKind,
        name:draft.name.trim(),taxableBase:draft.taxableBase,rate:draft.rate,amount:draft.amount,
        accountId:draft.accountId,jurisdictionCode:draft.kind === "IndustryCommerce" ? draft.jurisdictionCode.trim() : null,
        reason:draft.reason.trim()});
      onClose();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "No fue posible guardar la regla de retención.");
    } finally {setSaving(false)}
  }
  return <Dialog open onOpenChange={open => {if (!open && !saving) onClose()}}>
    <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-xl">
      <DialogHeader><DialogTitle>{initial ? "Editar retención manual" : "Agregar retención manual"}</DialogTitle>
        <DialogDescription>Esta retención se aplica al documento actual. La cuenta seleccionada recibirá el crédito contable.</DialogDescription></DialogHeader>
      <div className="space-y-4">
        {rules.some(rule => rule.isActive && !rule.appliesAutomatically && rule.direction === "Purchase") &&
          <div className="space-y-1"><Label>Regla manual guardada (opcional)</Label><Select onValueChange={ruleId => {
            const rule = rules.find(item => item.ruleId === ruleId);
            if (rule) setDraft({...draft,kind:rule.kind,name:rule.name,rate:rule.rate,
              amount:calculatedAmount(draft.taxableBase,rule.rate),accountId:rule.defaultAccountId ?? "",
              jurisdictionCode:rule.jurisdictionCode ?? "",code:"",saveRule:false});
          }}><SelectTrigger><SelectValue placeholder="Elegir regla guardada"/></SelectTrigger><SelectContent>{rules.filter(rule => rule.isActive && !rule.appliesAutomatically && rule.direction === "Purchase").map(rule =>
            <SelectItem key={rule.ruleId} value={rule.ruleId}>{rule.code} · {rule.name} ({rule.rate}%)</SelectItem>)}</SelectContent></Select></div>}
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1"><Label>Tipo</Label><Select value={draft.kind} onValueChange={(kind) => setDraft({...draft,kind:kind as WithholdingKind,jurisdictionCode:""})} disabled={kinds.isLoading || kinds.isError}><SelectTrigger aria-label="Tipo de retención"><SelectValue placeholder="Seleccionar tipo"/></SelectTrigger><SelectContent>{(kinds.data ?? []).map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select>{kinds.isError && <p role="alert" className="text-xs text-destructive">No fue posible cargar los tipos de retención.</p>}</div>
          <div className="space-y-1"><Label>Concepto o nombre</Label><Input aria-label="Concepto o nombre" maxLength={120} value={draft.name} onChange={event => setDraft({...draft,name:event.target.value})}/></div>
          <div className="space-y-1"><Label>Base de retención</Label><FormattedNumberInput ariaLabel="Base de retención" kind="currency" value={draft.taxableBase ?? ""} onValueChange={taxableBase => setDraft({...draft,taxableBase,amount:calculatedAmount(taxableBase,draft.rate)})}/></div>
          <div className="space-y-1"><Label>Tarifa %</Label><FormattedNumberInput ariaLabel="Tarifa %" kind="percent" value={draft.rate ?? ""} onValueChange={rate => setDraft({...draft,rate,amount:calculatedAmount(draft.taxableBase,rate)})}/></div>
          <div className="space-y-1"><Label>Valor retenido</Label><FormattedNumberInput ariaLabel="Valor retenido" kind="currency" value={draft.amount ?? ""} onValueChange={amount => setDraft({...draft,amount})}/></div>
          {draft.kind === "IndustryCommerce" && <div className="space-y-1"><Label>Jurisdicción</Label><Input aria-label="Jurisdicción" maxLength={16} value={draft.jurisdictionCode} onChange={event => setDraft({...draft,jurisdictionCode:event.target.value})}/></div>}
        </div>
        <div className="space-y-1"><Label>Cuenta contable de retención</Label><AccountSelect liabilityOnly value={draft.accountId} onChange={accountId => setDraft({...draft,accountId})}/><p className="text-xs text-muted-foreground">Busca una cuenta de pasivo por código o nombre.</p></div>
        <div className="space-y-1"><Label>Motivo</Label><Textarea aria-label="Motivo" maxLength={500} value={draft.reason} onChange={event => setDraft({...draft,reason:event.target.value})} placeholder="Explica por qué se aplica esta retención"/></div>
        <div className="space-y-3 rounded-lg border p-3"><label className="flex items-center gap-2 text-sm"><Checkbox checked={draft.saveRule} onCheckedChange={value => setDraft({...draft,saveRule:value === true})}/>Guardar como regla para usarla después</label>
          {draft.saveRule && <><div className="space-y-1"><Label>Código de la regla</Label><Input aria-label="Código de la regla" maxLength={32} value={draft.code} onChange={event => setDraft({...draft,code:event.target.value})} placeholder="Ej. RET-SERVICIOS"/></div><p className="text-xs text-muted-foreground">La regla permanecerá aunque cierres este documento. Podrás aplicarla manualmente; no se calculará automáticamente.</p></>}
        </div>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        {!error && validationMessage && <p role="status" className="text-sm text-muted-foreground">{validationMessage}</p>}
        <DialogFooter><Button type="button" variant="outline" disabled={saving} onClick={onClose}>Cerrar</Button><Button type="button" disabled={saving || !valid} onClick={() => void apply()}>{saving ? "Aplicando…" : "Aplicar retención"}</Button></DialogFooter>
      </div>
    </DialogContent>
  </Dialog>;
}
