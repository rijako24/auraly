"use client";

import { useId, useRef, useState } from "react";
import { Loader2, Pencil, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { AccountSelect } from "@/components/accounting/account-select";
import { PartyRoleSelect, type PartyRoleSelection } from "@/components/parties/party-role-select";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { partiesApi, type PartySiteRoleOption } from "@/services/api/parties";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { allowedPurchaseEvidenceTypes } from "@/lib/purchase-evidence-policy";
import { expensesApi, type ConfirmExpense, type ExpenseLineInput, type ExpenseOptions, type ExpensePreview } from "@/services/api/expenses";
import type { PurchaseEvidenceType } from "@/services/api/goods-receipts";
import { ExpenseBreakdown } from "./expense-breakdown";

type EditableLine = ExpenseLineInput & { key: string; accountCode: string; accountName: string };
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
const issuedAt = (date: string) => date ? `${date}T12:00:00-05:00` : "";
const today = () => new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
function newLine(options: ExpenseOptions): EditableLine {
  return { key: crypto.randomUUID(), expenseAccountId: "", accountCode: "", accountName: "", conceptId: null,
    costCenterId: options.costCenters.find(center => center.isDefault)?.costCenterId ?? null,
    description: "", taxExclusiveAmount: 0, taxProfileId: null,
    taxTreatment: options.taxTreatments?.[0]?.code ?? "", withholdingConceptCode: null };
}

export function ExpenseForm({ businessId, options, onSaved, onBusyChange }: {
  businessId: string; options: ExpenseOptions; onSaved: () => Promise<void>; onBusyChange: (busy: boolean) => void;
}) {
  const [party, setParty] = useState<PartyRoleSelection | null>(null);
  const [siteOption, setSiteOption] = useState<PagedEntityOption | null>(null);
  const [lines, setLines] = useState<EditableLine[]>([]);
  const [editingLine, setEditingLine] = useState<EditableLine | null>(null);
  const [form, setForm] = useState<ConfirmExpense>(() => ({ expenseId: crypto.randomUUID(), businessId,
    supplierId: "", partySiteId: null, conceptId: null, costCenterId: null, supplierDocumentNumber: "", issuedAt: issuedAt(today()),
    dueDate: issuedAt(today()), currencyCode: "COP", description: "", taxExclusiveAmount: 0, vatAmount: 0,
    withholdingJurisdictionCode: null, evidenceUrl: null, purchaseEvidenceType: options.purchaseEvidenceTypes[0]?.code ?? "SupplierElectronicInvoice" }));
  const [preview, setPreview] = useState<ExpensePreview | null>(null);
  const [phase, setPhase] = useState<"calculate" | "confirm" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const calculationVersion = useRef(0);
  const pendingCalculations = useRef(0);
  const evidence = options.purchaseEvidenceTypes.filter(option => allowedPurchaseEvidenceTypes(party?.supplierPurchaseEvidencePolicy ?? null).includes(option.code));
  const supplierInvoice = form.purchaseEvidenceType === "SupplierElectronicInvoice";
  const support = form.purchaseEvidenceType === "BuyerElectronicSupportDocument";
  const knownAccounts = [...options.concepts.map(concept => ({ accountId: concept.expenseAccountId, code: concept.expenseAccountCode, name: concept.expenseAccountName })),
    ...lines.map(line => ({ accountId: line.expenseAccountId, code: line.accountCode, name: line.accountName }))];
  function invalidate() { calculationVersion.current++; setPreview(null); setError(null); }
  function change(patch: Partial<ConfirmExpense>) {
    const next = { ...form, ...patch };
    setForm(next);
    if ("supplierId" in patch || "issuedAt" in patch || "dueDate" in patch || "purchaseEvidenceType" in patch) {
      invalidate();
      if (canPreview(lines, next)) void calculate(lines, next);
    } else setError(null);
  }
  function changeLine(patch: Partial<EditableLine>) { setEditingLine(current => current ? { ...current, ...patch } : null); }
  const validLine = (line: EditableLine) => !!line.expenseAccountId && !!line.description.trim() &&
    line.taxExclusiveAmount > 0 && line.taxExclusiveAmount <= 999999999999 && !!line.taxTreatment;
  const existingLine = !!editingLine && lines.some(line => line.key === editingLine.key);
  function saveLine(event: React.FormEvent) {
    event.preventDefault();
    if (!editingLine || !validLine(editingLine) || phase || (!existingLine && lines.length >= 100)) return;
    const nextLines = existingLine ? lines.map(line => line.key === editingLine.key ? editingLine : line) : [...lines, editingLine];
    invalidate();
    setLines(nextLines);
    setEditingLine(null);
    if (canPreview(nextLines)) void calculate(nextLines);
  }
  function canPreview(value: EditableLine[], header: ConfirmExpense = form) {
    return !!header.supplierId && !!header.issuedAt && !!header.dueDate && header.dueDate >= header.issuedAt &&
    value.length > 0 && value.every(validLine); }
  const valid = canPreview(lines) && !!form.partySiteId && evidence.some(option => option.code === form.purchaseEvidenceType) &&
    (!supplierInvoice || !!form.supplierDocumentNumber?.trim()) && !editingLine;
  function request(value: EditableLine[], hash: string | null, header: ConfirmExpense = form): ConfirmExpense {
    return { ...header, lines: value.map(line => ({ expenseAccountId:line.expenseAccountId,conceptId:line.conceptId,costCenterId:line.costCenterId,description:line.description,taxExclusiveAmount:line.taxExclusiveAmount,taxProfileId:line.taxProfileId,taxTreatment:line.taxTreatment,withholdingConceptCode:line.withholdingConceptCode })), calculationHash: hash };
  }
  async function calculate(value: EditableLine[], header: ConfirmExpense = form) {
    const version = ++calculationVersion.current;
    pendingCalculations.current++;
    setPhase("calculate"); onBusyChange(true); setError(null); setPreview(null);
    try {
      const result = await expensesApi.preview(request(value, null, header));
      if (version === calculationVersion.current) { setPreview(result); return result; }
      return null;
    } catch (cause) {
      if (version === calculationVersion.current)
        setError(cause instanceof Error ? cause.message : "No fue posible calcular el gasto.");
      return null;
    } finally {
      pendingCalculations.current--;
      if (pendingCalculations.current === 0) { setPhase(null); onBusyChange(false); }
    }
  }
  async function confirm(event: React.FormEvent) {
    event.preventDefault();
    if (!valid || phase) return;
    const calculation = preview ?? await calculate(lines);
    if (!calculation?.canConfirm) return;
    setPhase("confirm"); onBusyChange(true); setError(null);
    try {
      await expensesApi.confirm(request(lines, calculation.calculationHash));
      toast.success("Gasto aceptado. Su comprobante y estado están en Trazabilidad financiera.");
      await onSaved();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "No fue posible confirmar el gasto.");
      if ((cause as { statusCode?: number }).statusCode === 409) setPreview(null);
    } finally { setPhase(null); onBusyChange(false); }
  }
  return <><form onSubmit={confirm} className="space-y-5">
    <fieldset disabled={!!phase} className="space-y-5 disabled:opacity-75">
      <section className="grid gap-4 rounded-xl border p-4 sm:grid-cols-2">
        <Field label="Proveedor o beneficiario">{id => <div id={id}><PartyRoleSelect role="Supplier" value={form.supplierId}
          selectedOption={party ? { value: party.roleId, label: party.displayName } : null}
          placeholder="Buscar proveedor o beneficiario" emptyMessage="No hay proveedores activos para esta búsqueda. Registra el tercero con rol Proveedor para usarlo en Gastos." onChange={(supplierId, selected) => {
            setParty(selected ?? null);
            setSiteOption(null);
            const allowed = options.purchaseEvidenceTypes.filter(option => allowedPurchaseEvidenceTypes(selected?.supplierPurchaseEvidencePolicy ?? null).includes(option.code));
            const due = new Date(form.issuedAt); due.setUTCDate(due.getUTCDate() + (selected?.supplierDefaultPaymentDueDays ?? 0));
            change({ supplierId, partySiteId: null, dueDate: Number.isNaN(due.getTime()) ? form.dueDate : issuedAt(due.toISOString().slice(0, 10)),
              purchaseEvidenceType: allowed.some(option => option.code === form.purchaseEvidenceType) ? form.purchaseEvidenceType : allowed[0]?.code ?? form.purchaseEvidenceType });
          }}/></div>}</Field>
        <Field label="Sede del proveedor">{() => <PagedEntitySelect<PartySiteRoleOption>
          queryKey={["expense-supplier-sites",businessId,form.supplierId]} value={form.partySiteId??""}
          selectedOption={siteOption} disabled={!form.supplierId} preload
          loadPage={(term,page,pageSize)=>partiesApi.portfolioSiteOptions({role:"Supplier",roleId:form.supplierId,search:term||undefined,page,pageSize})}
          getOption={item=>({value:item.partySiteId,label:item.siteName,description:`${item.displayName} · ${item.identification}`})}
          onChange={(value,option)=>{setSiteOption(option);change({partySiteId:value})}}
          onClear={()=>{setSiteOption(null);change({partySiteId:null})}}
          placeholder="Seleccionar sede" ariaLabel="Sede del proveedor" />}</Field>
        <Field label="Respaldo del gasto">{id => <Select value={form.purchaseEvidenceType} onValueChange={(value: PurchaseEvidenceType) => change({ purchaseEvidenceType: value })}><SelectTrigger id={id}><SelectValue/></SelectTrigger><SelectContent>{evidence.map(option => <SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select>}</Field>
        <Field label={supplierInvoice ? "Número de factura del proveedor" : "Referencia (opcional)"}>{id => <Input id={id} maxLength={80} required={supplierInvoice} value={form.supplierDocumentNumber ?? ""} onChange={event => change({ supplierDocumentNumber: event.target.value })}/>}</Field>
        <Field label="Descripción general (opcional)">{id => <Input id={id} maxLength={300} value={form.description} onChange={event => change({ description: event.target.value })}/>}</Field>
        <Field label="Fecha de emisión">{id => <DatePicker id={id} value={form.issuedAt.slice(0, 10)} onChange={date => change({ issuedAt: issuedAt(date) })}/>}</Field>
        <Field label="Fecha de vencimiento">{id => <DatePicker id={id} value={form.dueDate.slice(0, 10)} min={form.issuedAt.slice(0, 10)} onChange={date => change({ dueDate: issuedAt(date) })}/>}</Field>
        <p className="text-sm text-muted-foreground sm:col-span-2">{support ? "Este gasto generará un documento soporte electrónico. El proveedor debe tener su identificación y código postal de seis dígitos completos." : "Este respaldo registra el gasto y la cuenta por pagar sin enviar un documento a la DIAN."}</p>
      </section>
      <section className="space-y-3" aria-label="Líneas del gasto">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div><h3 className="font-semibold">Detalle del gasto</h3><p className="text-sm text-muted-foreground">Agrega cada gasto y edita sus datos desde la grilla.</p></div>
          <Button type="button" variant="outline" disabled={lines.length >= 100} onClick={() => setEditingLine(newLine(options))}><Plus className="mr-2 h-4 w-4"/>Agregar gasto</Button>
        </div>
        <div className="overflow-x-auto rounded-xl border">
          <table className="w-full min-w-[760px] text-sm">
            <caption className="sr-only">Gastos agregados</caption>
            <thead className="bg-muted/50 text-left"><tr><th className="p-3">Cuenta / descripción</th><th className="p-3">Centro de costo</th><th className="p-3 text-right">Base</th><th className="p-3">IVA</th><th className="p-3">Concepto tributario</th><th className="p-3 text-right">Acciones</th></tr></thead>
            <tbody>{lines.map((line, index) => {
              const tax = options.taxes?.find(item => item.taxProfileId === line.taxProfileId);
              const calculated = preview?.lines[index];
              return <tr key={line.key} className="border-t">
                <td className="p-3"><b>{line.accountCode} · {line.accountName}</b><p>{line.description}</p></td>
                <td className="p-3">{options.costCenters.find(center => center.costCenterId === line.costCenterId)?.name ?? "Predeterminado"}</td>
                <td className="whitespace-nowrap p-3 text-right font-medium">{money.format(line.taxExclusiveAmount)}</td>
                <td className="p-3">{tax ? `${tax.name} · ${tax.rate}%` : "Sin IVA"}{tax && <small className="block text-muted-foreground">{options.taxTreatments?.find(item => item.code === line.taxTreatment)?.label}</small>}{calculated && <span className="block whitespace-nowrap">{money.format(calculated.vatAmount)}</span>}</td>
                <td className="p-3">{line.withholdingConceptCode ?? "Sin clasificación específica"}</td>
                <td className="p-3"><div className="flex justify-end gap-1"><Button type="button" size="icon" variant="ghost" aria-label={`Editar gasto ${index + 1}`} onClick={() => setEditingLine({ ...line })}><Pencil className="h-4 w-4"/></Button><Button type="button" size="icon" variant="ghost" aria-label={`Quitar gasto ${index + 1}`} onClick={() => { const remaining = lines.filter(item => item.key !== line.key); invalidate(); setLines(remaining); if (canPreview(remaining)) void calculate(remaining); }}><Trash2 className="h-4 w-4"/></Button></div></td>
              </tr>;
            })}</tbody>
          </table>
          {lines.length === 0 && <p className="p-8 text-center text-sm text-muted-foreground">Agrega el primer gasto para continuar.</p>}
        </div>
        <p className="text-right text-xs text-muted-foreground">{lines.length} de 100 líneas</p>
      </section>
      <p className="text-sm text-muted-foreground">Las retenciones se calculan con las reglas de la empresa y el perfil tributario del proveedor, incluida su jurisdicción. Las bases compatibles se acumulan antes de evaluar el mínimo.</p>
    </fieldset>
    {error && <p role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">{error}</p>}
    {lines.length > 0 && <section className="space-y-4 rounded-xl border p-4" aria-label="Cálculo del gasto">
      {phase === "calculate" && <p role="status" className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin"/>Calculando retenciones…</p>}
      {preview ? <><ExpenseBreakdown withholding={preview.withholding}/>{preview.diagnostics.map((message, index) => <p key={index} className={`rounded-lg p-3 text-sm ${preview.canConfirm ? "bg-muted text-muted-foreground" : "bg-destructive/5 text-destructive"}`}>{message}</p>)}</> :
        phase !== "calculate" && !error && <p className="text-sm text-muted-foreground">{!form.supplierId ? "Selecciona el proveedor para calcular las retenciones de estos gastos." : "Completa las fechas válidas para calcular las retenciones."}</p>}
    </section>}
    <DialogFooter className="sticky bottom-0 border-t bg-background py-3">
      <Button type="submit" disabled={!!phase || !valid || preview?.canConfirm === false}>{phase === "confirm" && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}{phase === "confirm" ? "Confirmando gasto…" : "Confirmar gasto"}</Button>
    </DialogFooter>
  </form>
    <Dialog open={!!editingLine} onOpenChange={open => { if (!open) setEditingLine(null); }}>
      <DialogContent className="max-h-[90dvh] overflow-visible sm:max-w-3xl">
        <DialogHeader><DialogTitle>{existingLine ? "Editar gasto" : "Agregar gasto"}</DialogTitle><DialogDescription>Elige un gasto frecuente o una cuenta directa. La línea se incorpora al documento al guardar.</DialogDescription></DialogHeader>
        {editingLine && <form onSubmit={saveLine} className="max-h-[calc(90dvh-8rem)] space-y-5 overflow-y-auto pr-1">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-2">
            <Field label="Gasto frecuente (opcional)">{id => <div className="relative"><Select value={editingLine.conceptId ?? "none"} onValueChange={value => {
              const concept = options.concepts.find(item => item.conceptId === value);
              if (concept) changeLine({ conceptId: concept.conceptId, expenseAccountId: concept.expenseAccountId, accountCode: concept.expenseAccountCode, accountName: concept.expenseAccountName,
                description: concept.name, costCenterId: concept.defaultCostCenterId, withholdingConceptCode: concept.withholdingConceptCode });
            }}><SelectTrigger id={id} className={editingLine.conceptId ? "pr-16" : undefined}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Seleccionar gasto frecuente</SelectItem>{options.concepts.filter(item => item.isActive).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.name}</SelectItem>)}</SelectContent></Select>{editingLine.conceptId && <button type="button" aria-label="Quitar selección de gasto frecuente" className="absolute right-9 top-1/2 -translate-y-1/2 rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" onClick={() => changeLine({ conceptId: null, expenseAccountId: "", accountCode: "", accountName: "", description: "", costCenterId: options.costCenters.find(center => center.isDefault)?.costCenterId ?? null, withholdingConceptCode: null })}><X className="h-4 w-4"/></button>}</div>}</Field>
            <Field label="Cuenta de gasto">{id => <div id={id}><AccountSelect expenseOnly accounts={knownAccounts} value={editingLine.expenseAccountId} onChange={(expenseAccountId, account) => changeLine({ expenseAccountId, accountCode: account?.code ?? "", accountName: account?.name ?? "", conceptId: null })}/></div>}</Field>
            <Field label="Centro de costo">{id => <Select value={editingLine.costCenterId ?? "none"} onValueChange={value => changeLine({ costCenterId: value === "none" ? null : value })}><SelectTrigger id={id}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Predeterminado de contabilidad</SelectItem>{options.costCenters.map(item => <SelectItem key={item.costCenterId} value={item.costCenterId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select>}</Field>
            <Field label="Descripción de la línea">{id => <Input id={id} required maxLength={300} value={editingLine.description} onChange={event => changeLine({ description: event.target.value })}/>}</Field>
            <Field label="Base antes de IVA">{id => <FormattedNumberInput id={id} kind="currency" value={editingLine.taxExclusiveAmount} onValueChange={value => changeLine({ taxExclusiveAmount: value ?? 0 })}/>}</Field>
            <Field label="Impuesto">{id => <Select value={editingLine.taxProfileId ?? "none"} onValueChange={value => changeLine({ taxProfileId: value === "none" ? null : value })}><SelectTrigger id={id}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin IVA</SelectItem>{options.taxes?.map(tax => <SelectItem key={tax.taxProfileId} value={tax.taxProfileId}>{tax.name} · {tax.rate}%</SelectItem>)}</SelectContent></Select>}</Field>
            <Field label="Tratamiento del IVA">{id => <Select value={editingLine.taxTreatment} onValueChange={taxTreatment => changeLine({ taxTreatment })}><SelectTrigger id={id}><SelectValue placeholder="Selecciona el tratamiento"/></SelectTrigger><SelectContent>{options.taxTreatments?.map(item => <SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select>}</Field>
            <Field label="Concepto tributario">{id => <><Select value={editingLine.withholdingConceptCode ?? "none"} onValueChange={value => changeLine({ withholdingConceptCode: value === "none" ? null : value })}><SelectTrigger id={id}><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin clasificación específica</SelectItem>{options.withholdingConceptCodes?.map(code => <SelectItem key={code} value={code}>{code}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">Clasifica la línea para aplicar reglas de retención. Se configura en Contabilidad → Retenciones; también puede venir del gasto frecuente.</p></>}</Field>
          </div>
          <DialogFooter><Button type="button" variant="outline" onClick={() => setEditingLine(null)}>Cancelar</Button><Button type="submit" disabled={!validLine(editingLine)}>{existingLine ? "Guardar cambios" : "Agregar a la grilla"}</Button></DialogFooter>
        </form>}
      </DialogContent>
    </Dialog>
  </>;
}

function Field({ label, children }: { label: string; children: (id: string) => React.ReactNode }) {
  const id = useId();
  return <div className="space-y-2"><Label htmlFor={id}>{label}</Label>{children(id)}</div>;
}
