"use client";

import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { type CalculatePayrollRun, type PayrollCatalogOption, type PayrollNovelty, type PayrollRun, type PayrollRunEmployee, type PayrollRunSummary } from "@/services/api/payroll";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PayrollRunNovelties({ runs, selectedId, run, loading, error, canCalculate, calculating, natures, canManage, novelties, voiding, onAddNovelty, onEditNovelty, onVoidNovelty, onSelect, onRetry, onCalculate }: {
    runs: PayrollRunSummary[];
    selectedId: string | null;
    run: PayrollRun | null;
    loading: boolean;
    error: boolean;
    canCalculate: boolean;
    calculating: boolean;
    natures: PayrollCatalogOption[];
    canManage:boolean;
    novelties:PayrollNovelty[];
    voiding:boolean;
    onAddNovelty:(worker:PayrollRunEmployee)=>void;
    onEditNovelty:(novelty:PayrollNovelty)=>void;
    onVoidNovelty:(novelty:PayrollNovelty)=>Promise<void>;
    onSelect: (id: string) => void;
    onRetry: () => void;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
}) {
    const [employmentId, setEmploymentId] = useState("");
    const worker = run?.employees.find(employee => employee.employmentId === employmentId);
    const manualNovelties = new Map(novelties.map(novelty=>[novelty.noveltyId,novelty]));
    return <Card className="rounded-3xl"><CardHeader><CardTitle>Novedades de la liquidación</CardTitle>
        <CardDescription>Primero liquida los trabajadores. Después revisa sus conceptos y días. Al cambiar una novedad, recalcula el trabajador antes de contabilizar.</CardDescription>
    </CardHeader><CardContent className="space-y-4">
        <div className="space-y-2"><label className="text-sm font-medium" id="novelty-run-label">Liquidación</label>
            <Select value={selectedId ?? ""} onValueChange={onSelect} disabled={loading || calculating}><SelectTrigger aria-labelledby="novelty-run-label"><SelectValue placeholder="Selecciona una liquidación"/></SelectTrigger>
                <SelectContent>{runs.map(item => <SelectItem key={item.payrollRunId} value={item.payrollRunId}>{item.periodStart} — {item.periodEnd}</SelectItem>)}</SelectContent>
            </Select>
        </div>
        {loading && <p role="status">Cargando liquidación…</p>}
        {error ? <div role="alert" className="space-y-2"><p>No fue posible cargar la liquidación.</p><Button variant="outline" onClick={onRetry}>Reintentar liquidación</Button></div>
            : run && <>
                {run.status === "Draft" && <p>Calcula primero esta liquidación desde Liquidaciones.</p>}
                <div className="grid gap-4 2xl:grid-cols-[minmax(280px,.7fr)_minmax(0,1.6fr)]">
                    <div className="max-h-[32rem] overflow-auto rounded-xl border"><table className="w-full text-sm">
                        <thead className="sticky top-0 bg-card"><tr className="text-left"><th className="p-3">Trabajador</th><th>Días</th><th className="pr-3 text-right">Neto</th></tr></thead>
                        <tbody>{run.employees.map(employee => <tr key={employee.employmentId} className={`border-t ${employmentId === employee.employmentId ? "bg-primary/5" : ""}`}>
                            <td className="p-3"><Button variant="link" className="h-auto whitespace-normal p-0 text-left" disabled={loading || calculating}
                                onClick={() => setEmploymentId(employee.employmentId)}>{employee.employeeName}</Button>
                                <span className="block text-xs text-muted-foreground">{[employee.identification,employee.contractNumber].filter(Boolean).join(" · ")}</span>
                            </td><td>{employee.workedDays}</td><td className="pr-3 text-right">{money.format(employee.netPayable)}</td>
                        </tr>)}</tbody>
                    </table>{run.employees.length === 0 && <p className="p-3 text-sm text-muted-foreground">No hay trabajadores liquidados.</p>}</div>
                    {worker ? <WorkerDays key={`${run.payrollRunId}:${worker.employmentId}`} run={run} worker={worker} natures={natures}
                        disabled={loading || !canCalculate || run.status !== "Calculated"} busy={calculating} onCalculate={onCalculate}
                        canManage={canManage && run.status==="Calculated"} novelties={manualNovelties} voiding={voiding}
                        onAddNovelty={onAddNovelty} onEditNovelty={onEditNovelty} onVoidNovelty={onVoidNovelty}/>
                        : <p className="rounded-xl border p-4 text-sm text-muted-foreground">Selecciona un trabajador para revisar su liquidación.</p>}
                </div>
            </>}
    </CardContent></Card>;
}

function WorkerDays({ run, worker, natures, disabled, busy, onCalculate, canManage, novelties, voiding, onAddNovelty, onEditNovelty, onVoidNovelty }: {
    run: PayrollRun;
    worker: PayrollRunEmployee;
    natures: PayrollCatalogOption[];
    disabled: boolean;
    busy: boolean;
    canManage:boolean;
    novelties:Map<string,PayrollNovelty>;
    voiding:boolean;
    onAddNovelty:(worker:PayrollRunEmployee)=>void;
    onEditNovelty:(novelty:PayrollNovelty)=>void;
    onVoidNovelty:(novelty:PayrollNovelty)=>Promise<void>;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
}) {
    const [draft,setDraft]=useState({sourceId:worker.payrollRunEmployeeId,days:String(worker.workedDays),error:null as string|null});
    const current=draft.sourceId===worker.payrollRunEmployeeId ? draft
        : {sourceId:worker.payrollRunEmployeeId,days:String(worker.workedDays),error:null};
    const {days,error}=current;
    const setDays=(days:string)=>setDraft({...current,days});
    const setError=(error:string|null)=>setDraft({...current,error});
    const hasInputs = worker.baseWorkedDays != null && worker.maximumContractDays != null && worker.periodDays != null && worker.periodDays > 0;
    const absences = hasInputs ? Number((worker.baseWorkedDays! - worker.workedDays).toFixed(4)) : 0;
    const maximum = hasInputs ? Number(Math.max(0, Math.min(worker.periodDays!, worker.maximumContractDays!) - absences).toFixed(4)) : 0;
    const locked = disabled || busy || !hasInputs;
    async function save(event: FormEvent) {
        event.preventDefault();
        if (locked) return;
        const value = Number(days);
        if (!days.trim() || !Number.isFinite(value) || value < 0 || value > maximum) { setError(`Los días trabajados deben estar entre 0 y ${maximum}.`); return; }
        await calculate({rowVersion:run.rowVersion,workedDaysChanges:[{employmentId:worker.employmentId,workedDays:value}]},"Días guardados y trabajador recalculado.");
    }
    async function calculate(request:CalculatePayrollRun,success:string) {
        setError(null);
        try {
            const result = await onCalculate(run.payrollRunId, request);
            const updated = result.employees[0];
            if (!result.isPartialCalculation || result.employees.length !== 1 || updated?.employmentId !== worker.employmentId)
                throw new Error("La respuesta no corresponde al trabajador. Actualiza la liquidación antes de continuar.");
            setDraft({sourceId:updated.payrollRunEmployeeId,days:String(updated.workedDays),error:null});toast.success(success);
        } catch (failure) { setError(failure instanceof Error ? failure.message : "No fue posible recalcular el trabajador."); }
    }
    return <section aria-label={`Liquidación de ${worker.employeeName}`} className="space-y-4 rounded-xl border p-4" aria-busy={busy}>
        <h3 className="font-semibold">{worker.employeeName}</h3>
        <div className="flex flex-wrap gap-2">
            {canManage && <Button variant="outline" disabled={busy || voiding} onClick={()=>onAddNovelty(worker)}>Agregar novedad al trabajador</Button>}
            {!disabled && hasInputs && <Button variant="outline" disabled={locked || voiding} onClick={()=>void calculate(
                {rowVersion:run.rowVersion,recalculateEmploymentIds:[worker.employmentId]},"Trabajador recalculado con sus novedades vigentes.")}>Recalcular trabajador</Button>}
        </div>
        {!hasInputs && <p className="text-sm text-muted-foreground">{run.runKind === "Adjustment" ? "Los días de una liquidación de ajuste se conservan en lectura." : run.status === "Calculated" ? "Recalcula la liquidación completa desde Liquidaciones para habilitar la edición de días." : "Los días de este cálculo se conservan en lectura."}</p>}
        <form onSubmit={event => void save(event)} className="space-y-3">
            <label className="block space-y-2 text-sm font-medium"><span>Días trabajados</span><Input type="number" required min={0} max={maximum} step="0.0001"
                value={days} disabled={locked} onChange={event => setDays(event.target.value)} className="max-w-48"/></label>
            {hasInputs && <p className="text-sm text-muted-foreground">Días base: {worker.baseWorkedDays} · Ausencias registradas: {absences} · Máximo permitido: {maximum}</p>}
            {hasInputs && worker.baseWorkedDays! > worker.maximumContractDays! && <p role="alert" className="text-sm text-destructive">Hay días fuera de la vigencia contractual. Ajusta los días antes de contabilizar.</p>}
            {!disabled && hasInputs && <Button type="submit" disabled={locked}>{busy ? "Recalculando trabajador…" : "Guardar y recalcular trabajador"}</Button>}
        </form>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        <div className="max-h-96 overflow-auto"><table className="w-full min-w-[640px] text-sm"><thead><tr className="text-left"><th>Concepto</th><th>Naturaleza</th><th className="text-right">Cantidad</th><th className="text-right">Base</th><th className="text-right">Valor</th><th>Acciones</th></tr></thead>
            <tbody>{worker.lines.map(line => {const novelty=line.noveltyId?novelties.get(line.noveltyId):null;return <tr key={line.lineNumber} className="border-t"><td className="py-2">{line.conceptCode} · {line.conceptName}</td>
                <td>{natures.find(nature => nature.code === line.natureCode)?.label ?? line.natureCode}</td><td className="text-right">{line.quantity}</td>
                <td className="text-right">{line.baseAmount == null ? "—" : money.format(line.baseAmount)}</td><td className="text-right">{money.format(line.amount)}</td>
                <td>{canManage && novelty?.rowVersion && ["Draft","Approved"].includes(novelty.status) && <div className="flex gap-1">
                    <Button size="sm" variant="ghost" disabled={busy || voiding} onClick={()=>onEditNovelty(novelty)}>Editar</Button>
                    <Button size="sm" variant="ghost" disabled={busy || voiding} onClick={()=>void onVoidNovelty(novelty)}>Retirar</Button>
                </div>}</td></tr>})}</tbody>
        </table></div>
        <p className="text-sm font-semibold">Devengado: {money.format(worker.earnings)} · Deducido: {money.format(worker.deductions)} · Neto: {money.format(worker.netPayable)}</p>
    </section>;
}
