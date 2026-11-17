"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { toast } from "sonner";
import { Check, Pencil, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { type CalculatePayrollRun, type PayrollCatalogOption, type PayrollNovelty, type PayrollRun, type PayrollRunEmployee, type PayrollRunSummary } from "@/services/api/payroll";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PayrollRunNovelties({ runs, selectedId, run, loading, error, canCalculate, calculating, calculationErrors, natures, canManage, voiding, noveltyEditor, editorEmploymentId, onCloseEditor, onAddNovelty, onEditNovelty, onVoidNovelty, onSelect, onRetry, onCalculate }: {
    runs: PayrollRunSummary[];
    selectedId: string | null;
    run: PayrollRun | null;
    loading: boolean;
    error: boolean;
    canCalculate: boolean;
    calculating: boolean;
    calculationErrors:Record<string,string>;
    natures: PayrollCatalogOption[];
    canManage:boolean;
    voiding:boolean;
    noveltyEditor:ReactNode;
    editorEmploymentId:string|null;
    onCloseEditor:()=>void;
    onAddNovelty:(worker:PayrollRunEmployee)=>void;
    onEditNovelty:(novelty:PayrollNovelty)=>void;
    onVoidNovelty:(novelty:PayrollNovelty)=>Promise<void>;
    onSelect: (id: string) => void;
    onRetry: () => void;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
}) {
    const [employmentId, setEmploymentId] = useState("");
    const completedRuns = runs.filter(item => item.status !== "Draft");
    const selectedRun = run?.status !== "Draft" ? run : null;
    const worker = selectedRun?.employees.find(employee => employee.employmentId === employmentId);
    return <Card className="rounded-3xl"><CardHeader><CardTitle>Novedades de la liquidación</CardTitle>
        <CardDescription>Primero liquida los trabajadores. Después revisa sus conceptos y días. Guardar, editar o retirar una novedad actualiza automáticamente su cálculo.</CardDescription>
    </CardHeader><CardContent className="space-y-4">
        <div className="space-y-2"><label className="text-sm font-medium" id="novelty-run-label">Liquidación</label>
            <Select value={selectedRun ? selectedId ?? "" : ""} onValueChange={id => { setEmploymentId(""); onSelect(id); }} disabled={loading || calculating || voiding || completedRuns.length === 0}><SelectTrigger aria-labelledby="novelty-run-label"><SelectValue placeholder="Selecciona una liquidación calculada"/></SelectTrigger>
                <SelectContent>{completedRuns.map(item => <SelectItem key={item.payrollRunId} value={item.payrollRunId}>{item.periodStart} — {item.periodEnd}</SelectItem>)}</SelectContent>
            </Select>
            {completedRuns.length === 0 && <p className="text-sm text-muted-foreground">Primero calcula una liquidación desde Liquidaciones para revisar aquí a sus trabajadores.</p>}
        </div>
        {loading && <p role="status">Cargando liquidación…</p>}
        {error ? <div role="alert" className="space-y-2"><p>No fue posible cargar la liquidación.</p><Button variant="outline" onClick={onRetry}>Reintentar liquidación</Button></div>
            : selectedRun && <>
                {worker ? <>
                    <Button variant="outline" disabled={calculating || voiding} onClick={()=>{onCloseEditor();setEmploymentId("");}}>Volver a trabajadores</Button>
                    <WorkerDays key={`${selectedRun.payrollRunId}:${worker.employmentId}`} run={selectedRun} worker={worker} natures={natures}
                        calculationError={calculationErrors[worker.employmentId]??null}
                        disabled={loading || !canCalculate || selectedRun.status !== "Calculated"} busy={calculating} onCalculate={onCalculate}
                        canManage={canManage && selectedRun.status==="Calculated"} novelties={new Map((worker.novelties??[]).map(novelty=>[novelty.noveltyId,novelty]))} voiding={voiding}
                        noveltyEditor={editorEmploymentId===worker.employmentId?noveltyEditor:null}
                        onAddNovelty={onAddNovelty} onEditNovelty={onEditNovelty} onVoidNovelty={onVoidNovelty}/>
                </> : <section aria-label="Trabajadores liquidados" className="space-y-3">
                    <p className="text-sm font-medium">{selectedRun.employees.length} trabajadores liquidados · Selecciona uno para ver y modificar sus novedades.</p>
                    <div className="max-h-[36rem] overflow-auto rounded-xl border"><table className="w-full min-w-[760px] text-sm">
                        <thead className="sticky top-0 bg-card"><tr className="text-left"><th className="p-3">Identificación</th><th>Contrato</th><th>Trabajador</th><th className="text-right">Días</th><th className="text-right">Devengado</th><th className="text-right">Deducido</th><th className="pr-3 text-right">Neto</th></tr></thead>
                        <tbody>{selectedRun.employees.map(employee => <tr key={employee.employmentId} className="border-t">
                            <td className="p-3">{employee.identification ?? "—"}</td><td>{employee.contractNumber ?? "—"}</td><td className="min-w-48 py-3"><Button variant="link" className="h-auto whitespace-normal p-0 text-left" disabled={loading || calculating}
                                onClick={() => setEmploymentId(employee.employmentId)}>{employee.employeeName}</Button>
                            </td><td className="text-right">{employee.workedDays}</td><td className="text-right">{money.format(employee.earnings)}</td><td className="text-right">{money.format(employee.deductions)}</td><td className="pr-3 text-right">{money.format(employee.netPayable)}</td>
                        </tr>)}</tbody>
                    </table>{selectedRun.employees.length === 0 && <p className="p-3 text-sm text-muted-foreground">No hay trabajadores liquidados.</p>}</div>
                </section>}
            </>}
    </CardContent></Card>;
}

function WorkerDays({ run, worker, natures, disabled, busy, calculationError, onCalculate, canManage, novelties, voiding, noveltyEditor, onAddNovelty, onEditNovelty, onVoidNovelty }: {
    run: PayrollRun;
    worker: PayrollRunEmployee;
    natures: PayrollCatalogOption[];
    disabled: boolean;
    busy: boolean;
    calculationError:string|null;
    canManage:boolean;
    novelties:Map<string,PayrollNovelty>;
    voiding:boolean;
    noveltyEditor:ReactNode;
    onAddNovelty:(worker:PayrollRunEmployee)=>void;
    onEditNovelty:(novelty:PayrollNovelty)=>void;
    onVoidNovelty:(novelty:PayrollNovelty)=>Promise<void>;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
}) {
    const [draft,setDraft]=useState({sourceId:worker.payrollRunEmployeeId,conceptId:null as string|null,days:"",error:null as string|null});
    const current=draft.sourceId===worker.payrollRunEmployeeId ? draft
        : {sourceId:worker.payrollRunEmployeeId,conceptId:null,days:"",error:null};
    const {days,error}=current;
    const setDays=(days:string)=>setDraft({...current,days});
    const setError=(error:string|null)=>setDraft({...current,error});
    const hasInputs = worker.baseWorkedDays != null && worker.maximumContractDays != null && worker.periodDays != null && worker.periodDays > 0;
    const absences = hasInputs ? Number((worker.baseWorkedDays! - worker.workedDays).toFixed(4)) : 0;
    const maximum = hasInputs ? Number(Math.max(0, Math.min(worker.periodDays!, worker.maximumContractDays!) - absences).toFixed(4)) : 0;
    const locked = disabled || busy || !hasInputs;
    const dayInputs=new Map((worker.conceptWorkedDays??[]).map(input=>[input.conceptId,input]));
    const includedNovelties=new Set(worker.lines.map(line=>line.noveltyId).filter(Boolean));
    const pendingNovelties=canManage?[...novelties.values()].filter(novelty=>
        novelty.employmentId===worker.employmentId && novelty.startDate<=run.periodEnd && novelty.endDate>=run.periodStart
        && ["Draft","Approved"].includes(novelty.status) && !includedNovelties.has(novelty.noveltyId)):[];
    function noveltyActions(novelty:PayrollNovelty|null|undefined) {
        return canManage && novelty?.rowVersion && ["Draft","Approved"].includes(novelty.status) ? <div className="flex gap-1">
            <Button size="icon" variant="ghost" className="h-8 w-8" aria-label="Editar novedad" title={`Editar ${novelty.conceptName}`} disabled={busy || voiding} onClick={()=>onEditNovelty(novelty)}><Pencil className="h-4 w-4"/></Button>
            <Button size="icon" variant="ghost" className="h-8 w-8 text-destructive" aria-label="Retirar novedad" title={`Retirar ${novelty.conceptName}`} disabled={busy || voiding} onClick={()=>void onVoidNovelty(novelty)}><Trash2 className="h-4 w-4"/></Button>
        </div>:null;
    }
    async function save(event: FormEvent) {
        event.preventDefault();
        if (locked || !current.conceptId || !dayInputs.has(current.conceptId)) return;
        const value = Number(days);
        if (!days.trim() || !Number.isFinite(value) || value < 0 || value > maximum) { setError(`Los días trabajados deben estar entre 0 y ${maximum}.`); return; }
        await calculate({rowVersion:run.rowVersion,workedDaysChanges:[{employmentId:worker.employmentId,conceptId:current.conceptId,workedDays:value}]},"Días del concepto guardados y trabajador recalculado.");
    }
    async function calculate(request:CalculatePayrollRun,success:string) {
        setError(null);
        try {
            const result = await onCalculate(run.payrollRunId, request);
            const updated = result.employees[0];
            if (!result.isPartialCalculation || result.employees.length !== 1 || updated?.employmentId !== worker.employmentId)
                throw new Error("La respuesta no corresponde al trabajador. Actualiza la liquidación antes de continuar.");
            const changed=current.conceptId?updated.conceptWorkedDays?.find(input=>input.conceptId===current.conceptId):null;
            setDraft({sourceId:updated.payrollRunEmployeeId,conceptId:changed?.conceptId??null,days:changed?String(changed.workedDays):"",error:null});toast.success(success);
        } catch (failure) { setError(failure instanceof Error ? failure.message : "No fue posible recalcular el trabajador."); }
    }
    return <section aria-label={`Liquidación de ${worker.employeeName}`} className="space-y-4 rounded-xl border p-4" aria-busy={busy}>
        <h3 className="font-semibold">{worker.employeeName}</h3>
        <dl className="flex flex-wrap gap-x-8 gap-y-2 text-sm">
            <div><dt className="text-muted-foreground">Contrato</dt><dd>{worker.contractNumber??"—"}</dd></div>
            <div><dt className="text-muted-foreground">Identificación</dt><dd>{worker.identification??"—"}</dd></div>
            <div><dt className="text-muted-foreground">Período</dt><dd>{run.periodStart} — {run.periodEnd}</dd></div>
            <div><dt className="text-muted-foreground">Salario base mensual</dt><dd>{worker.monthlySalary==null?"—":money.format(worker.monthlySalary)}</dd></div>
        </dl>
        <div className="flex flex-wrap gap-2">
            {canManage && <Button variant="outline" disabled={busy || voiding} onClick={()=>onAddNovelty(worker)}>Agregar novedad al trabajador</Button>}
            {!disabled && calculationError && <Button variant="outline" disabled={busy || voiding} onClick={()=>void calculate(
                {rowVersion:run.rowVersion,recalculateEmploymentIds:[worker.employmentId]},"Cálculo del trabajador actualizado.")}>Reintentar cálculo</Button>}
        </div>
        {!hasInputs && <p className="text-sm text-muted-foreground">{run.runKind === "Adjustment" ? "Los días de una liquidación de ajuste se conservan en lectura." : run.status === "Calculated" ? "Recalcula la liquidación completa desde Liquidaciones para habilitar la edición de días." : "Los días de este cálculo se conservan en lectura."}</p>}
        {hasInputs && <p className="text-sm text-muted-foreground">Período: {worker.periodDays} días · Ausencias registradas: {absences} · Máximo por concepto: {maximum}</p>}
        {hasInputs && !disabled && dayInputs.size===0 && <p className="text-sm text-muted-foreground">Recalcula desde Liquidaciones para habilitar los días por concepto.</p>}
        {hasInputs && (worker.baseWorkedDays! > worker.maximumContractDays! || [...dayInputs.values()].some(input=>input.baseDays>worker.maximumContractDays!)) && <p role="alert" className="text-sm text-destructive">Hay días fuera de la vigencia contractual. Ajusta los conceptos antes de contabilizar.</p>}
        {(error||calculationError) && <p role="alert" className="text-sm text-destructive">{error??calculationError}</p>}
        {noveltyEditor}
        <div className="max-h-96 overflow-auto"><table className="w-full min-w-[640px] text-sm"><thead><tr className="text-left"><th>Concepto</th><th>Naturaleza</th><th className="px-3 text-right">Cantidad</th><th className="px-3 text-right">Base</th><th className="px-3 text-right">Valor</th><th className="px-2">Acciones</th></tr></thead>
            <tbody>{worker.lines.map(line => {const novelty=line.noveltyId?novelties.get(line.noveltyId):null;const dayInput=!line.noveltyId?dayInputs.get(line.conceptId):null;
                const editing=dayInput && current.conceptId===line.conceptId;return <tr key={line.lineNumber} className="border-t"><td className="py-2">{line.conceptCode} · {line.conceptName}
                {novelty?.notes && <span className="block text-xs text-muted-foreground">{novelty.notes}</span>}</td>
                <td>{natures.find(nature => nature.code === line.natureCode)?.label ?? line.natureCode}</td><td className="px-3 text-right">{editing?<form className="flex items-center justify-end gap-1" onSubmit={event=>void save(event)}>
                    <Input aria-label={`Días trabajados de ${line.conceptName}`} type="number" required min={0} max={maximum} step="0.0001" value={days} disabled={locked || voiding} onChange={event=>setDays(event.target.value)} className="h-8 w-20"/>
                    <Button type="submit" size="icon" variant="ghost" className="h-8 w-8" aria-label={`Guardar días de ${line.conceptName}`} title="Guardar días del concepto" disabled={locked || voiding}><Check className="h-4 w-4"/></Button>
                    <Button type="button" size="icon" variant="ghost" className="h-8 w-8" aria-label="Cancelar edición de días" title="Cancelar edición" disabled={busy || voiding} onClick={()=>setDraft({...current,conceptId:null,error:null})}><X className="h-4 w-4"/></Button>
                </form>:<>{line.quantity}{dayInput && <span className="ml-1 text-xs text-muted-foreground">días</span>}</>}</td>
                <td className="px-3 text-right">{line.baseAmount == null ? "—" : money.format(line.baseAmount)}</td><td className="px-3 text-right">{money.format(line.amount)}</td>
                <td className="px-2">{dayInput && !disabled && <Button size="icon" variant="ghost" className="h-8 w-8" aria-label={`Editar días de ${line.conceptName}`} title={`Editar días de ${line.conceptName}`} disabled={locked || voiding} onClick={()=>setDraft({sourceId:worker.payrollRunEmployeeId,conceptId:line.conceptId,days:String(dayInput.workedDays),error:null})}><Pencil className="h-4 w-4"/></Button>}{noveltyActions(novelty)}</td></tr>})}
                {pendingNovelties.map(novelty=><tr key={novelty.noveltyId} className="border-t bg-amber-50/60 dark:bg-amber-950/20">
                    <td className="py-2">{novelty.conceptName}<span className="block text-xs text-muted-foreground">{novelty.notes}</span></td>
                    <td>Pendiente de recalcular</td><td className="text-right">{novelty.quantity}</td><td className="text-right">—</td>
                    <td className="text-right">{money.format(novelty.totalAmount)}</td><td>{noveltyActions(novelty)}</td>
                </tr>)}</tbody>
        </table></div>
        <p className="text-sm font-semibold">Devengado: {money.format(worker.earnings)} · Deducido: {money.format(worker.deductions)} · Neto: {money.format(worker.netPayable)}</p>
    </section>;
}
