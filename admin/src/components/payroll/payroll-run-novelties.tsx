"use client";

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "sonner";
import { Calculator, Check, Eye, FileText, Pencil, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription } from "@/components/ui/dialog";
import { ReportViewer } from "@/components/reports/report-viewer";
import { payrollReceiptColumns,payrollReceiptRows } from "@/lib/payroll-receipt";
import type { ReportRow } from "@/lib/report-viewer";
import { payrollApi, type CalculatePayrollRun, type PayrollCatalogOption, type PayrollNovelty, type PayrollCalculatedEmployeeRow, type PayrollRun, type PayrollRunEmployee } from "@/services/api/payroll";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PayrollRunNovelties({ run, employmentId, onOpen, onBack, loading, error, canCalculate, calculating, calculationErrors, natures, canManage, voiding, noveltyEditor, editorEmploymentId, onCloseEditor, onAddNovelty, onEditNovelty, onVoidNovelty, onRetry, onCalculate }: {
    employmentId:string|null;
    onOpen:(runId:string,employmentId:string)=>void;
    onBack:()=>void;
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
    onRetry: () => void;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
}) {
    const alive=useRef(true),receiptRequest=useRef(false);
    const [receipt,setReceipt]=useState<{rows:ReportRow[];description:string}|null>(null),[receiptLoading,setReceiptLoading]=useState(false);
    useEffect(()=>{alive.current=true;return()=>{alive.current=false;};},[]);
    async function openReceipt(employee:PayrollCalculatedEmployeeRow){
      if(receiptRequest.current||calculating||voiding)return;
      receiptRequest.current=true;setReceiptLoading(true);
      try{
        const result=await payrollApi.report("payroll-receipt",employee.periodStart,employee.periodEnd,employee.partyId,employee.payrollRunEmployeeId);
        if(!alive.current)return;
        if(!result.rows.length||result.rows.some(row=>row.payrollRunEmployeeId!==employee.payrollRunEmployeeId)||!result.rows[0].employeeName)
          throw new Error("El resultado cambió. Actualiza la bandeja antes de abrir el desprendible.");
        setReceipt({rows:payrollReceiptRows(result.rows,false),description:`${result.rows[0].runStatus==="Calculated"?"Preliminar · ":""}${result.rows[0].employeeName} · ${result.from} a ${result.to}`});
      }catch(error){if(alive.current)toast.error(error instanceof Error?error.message:"No fue posible abrir el desprendible.");}
      finally{receiptRequest.current=false;if(alive.current)setReceiptLoading(false);}
    }
    async function recalculate(employee:PayrollCalculatedEmployeeRow){
      if(calculating||voiding||receiptLoading||!canCalculate)return;
      try{await onCalculate(employee.payrollRunId,{rowVersion:employee.rowVersion,recalculateEmploymentIds:[employee.employmentId]});if(alive.current)toast.success("Trabajador recalculado.");}
      catch(error){if(alive.current)toast.error(error instanceof Error?error.message:"No fue posible recalcular el trabajador.");}
    }
    const today=useBusinessDates().today();
    const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
    const [page,setPage]=useState(1),[search,setSearch]=useState(""),[draft,setDraft]=useState("");
    const [from,setFrom]=useState(`${today.slice(0,4)}-01-01`),[to,setTo]=useState(`${today.slice(0,4)}-12-31`);
    const list=useQuery({queryKey:["payroll-calculated-employees",tenant,business,page,from,to,search],
        queryFn:()=>payrollApi.calculatedEmployees({page,pageSize:20,from,to,search:search||undefined}),
        enabled:!receipt&&!receiptLoading&&!employmentId&&Boolean(from&&to&&to>=from),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
    const selectedRun = run?.status !== "Draft" ? run : null;
    const worker = selectedRun?.employees.find(employee => employee.employmentId === employmentId);
    return <Card className="rounded-3xl"><CardHeader><CardTitle>Novedades de la liquidación</CardTitle>
        <CardDescription>Primero liquida los trabajadores. Después revisa sus conceptos y días. Guardar, editar o retirar una novedad actualiza automáticamente su cálculo.</CardDescription>
    </CardHeader><CardContent className="space-y-4">
        {employmentId&&<Button variant="outline" disabled={calculating||voiding} onClick={()=>{onCloseEditor();onBack();}}>Volver a trabajadores</Button>}
        {loading && <p role="status">Cargando liquidación…</p>}
        {error ? <div role="alert" className="space-y-2"><p>No fue posible cargar la liquidación.</p><Button variant="outline" onClick={onRetry}>Reintentar liquidación</Button></div>
            : selectedRun && worker && <>
                    <WorkerDays key={`${selectedRun.payrollRunId}:${worker.employmentId}`} run={selectedRun} worker={worker} natures={natures}
                        calculationError={calculationErrors[worker.employmentId]??null}
                        disabled={loading || !canCalculate || selectedRun.status !== "Calculated"} busy={calculating} onCalculate={onCalculate}
                        canManage={canManage && selectedRun.status==="Calculated"} novelties={new Map((worker.novelties??[]).map(novelty=>[novelty.noveltyId,novelty]))} voiding={voiding}
                        noveltyEditor={editorEmploymentId===worker.employmentId?noveltyEditor:null}
                        onAddNovelty={onAddNovelty} onEditNovelty={onEditNovelty} onVoidNovelty={onVoidNovelty}/>
            </>}
        {!employmentId&&<section aria-label="Trabajadores liquidados" className="space-y-4">
            <div className="grid min-w-0 gap-3 sm:grid-cols-2"><label className="space-y-1 text-sm">Desde<DatePicker today={today} value={from} onChange={value=>{setFrom(value);setPage(1);}}/></label>
                <label className="space-y-1 text-sm">Hasta<DatePicker today={today} value={to} onChange={value=>{setTo(value);setPage(1);}}/></label></div>
            <form className="flex min-w-0 gap-2" onSubmit={event=>{event.preventDefault();setSearch(draft.trim());setPage(1);}}><Input aria-label="Buscar trabajador liquidado" className="min-w-0" value={draft} maxLength={160} placeholder="Trabajador, identificación o contrato" onChange={event=>setDraft(event.target.value)}/><Button variant="outline">Buscar</Button></form>
            {to<from&&<p role="alert">La fecha final debe ser igual o posterior a la inicial.</p>}
            {list.isFetching?<p role="status">Cargando trabajadores liquidados…</p>:list.isError?<div role="alert"><p>No fue posible consultar los trabajadores.</p><Button onClick={()=>void list.refetch()}>Reintentar trabajadores</Button></div>:<>
                <div className="space-y-3">{list.data?.items.map(employee=><article key={`${employee.payrollRunId}:${employee.employmentId}`} className="grid min-w-0 gap-3 rounded-xl border p-4 sm:grid-cols-2 lg:grid-cols-4">
                    <div className="min-w-0 sm:col-span-2">{employee.employeeName?<Button variant="link" className="h-auto max-w-full whitespace-normal p-0 text-left" disabled={calculating||voiding||receiptLoading} onClick={()=>onOpen(employee.payrollRunId,employee.employmentId)}>{employee.employeeName}</Button>:<p className="text-sm font-semibold">Identidad histórica no disponible</p>}<p className="break-words text-sm text-muted-foreground">{employee.identification??"—"} · Contrato {employee.contractNumber??"—"}</p>
                      <div className="mt-2 flex gap-2">
                        <Button size="icon" variant="outline" className="size-8" disabled={!employee.employeeName||calculating||voiding||receiptLoading} aria-label={`${employee.status==="Calculated"&&(canManage||canCalculate)?"Modificar":"Ver"} novedades de ${employee.employeeName??"identidad histórica no disponible"}`} title={employee.status==="Calculated"&&(canManage||canCalculate)?"Modificar novedades":"Ver novedades"} onClick={()=>onOpen(employee.payrollRunId,employee.employmentId)}>{employee.status==="Calculated"&&(canManage||canCalculate)?<Pencil className="size-4"/>:<Eye className="size-4"/>}</Button>
                        {canCalculate&&employee.status==="Calculated"&&<Button size="icon" variant="outline" className="size-8" disabled={!employee.employeeName||calculating||voiding||receiptLoading} aria-label={`Recalcular a ${employee.employeeName??"identidad histórica no disponible"}`} title="Recalcular trabajador" onClick={()=>void recalculate(employee)}><Calculator className="size-4"/></Button>}
                        <Button size="icon" variant="outline" className="size-8" disabled={!employee.employeeName||calculating||voiding||receiptLoading} aria-label={`Desprendible de ${employee.employeeName??"identidad histórica no disponible"}`} title={employee.status==="Calculated"?"Desprendible preliminar":"Desprendible"} onClick={()=>void openReceipt(employee)}><FileText className="size-4"/></Button>
                      </div></div>
                    <div className="text-sm"><p>{employee.periodStart} — {employee.periodEnd}</p><p className="text-muted-foreground">{employee.status==="Approved"?"Aprobada · Solo consulta":"Calculada"} · {employee.workedDays} días</p></div>
                    <dl className="space-y-1 text-sm"><div className="flex justify-between gap-3"><dt>Devengado</dt><dd>{money.format(employee.earnings)}</dd></div><div className="flex justify-between gap-3"><dt>Deducido</dt><dd>{money.format(employee.deductions)}</dd></div><div className="flex justify-between gap-3 font-semibold"><dt>Neto</dt><dd>{money.format(employee.netPayable)}</dd></div></dl>
                </article>)}</div>
                {list.data?.items.length===0&&<p>No hay trabajadores liquidados en este período.</p>}
                <div className="flex flex-wrap items-center justify-between gap-3 text-sm"><span>{list.data?.totalCount??0} resultados · Página {page}</span><div className="flex gap-2"><Button variant="outline" disabled={page===1||calculating} onClick={()=>setPage(value=>value-1)}>Anterior trabajadores</Button><Button variant="outline" disabled={page*20>=(list.data?.totalCount??0)||calculating} onClick={()=>setPage(value=>value+1)}>Siguiente trabajadores</Button></div></div>
            </>}
        </section>}
        {receiptLoading&&<p role="status">Preparando desprendible…</p>}
        <Dialog open={Boolean(receipt)} onOpenChange={open=>{if(!open)setReceipt(null);}}><DialogContent showClose={false} className="flex h-[95dvh] max-w-6xl flex-col overflow-hidden p-0"><DialogHeader className="sr-only"><DialogTitle>Desprendible laboral</DialogTitle><DialogDescription>Resultado congelado del trabajador y su período.</DialogDescription></DialogHeader><div className="min-h-0 flex-1 overflow-y-auto">{receipt&&<ReportViewer title="Desprendible laboral" description={receipt.description} rows={receipt.rows} columns={payrollReceiptColumns} fileName="desprendible-nomina" documentDownloads searchable={false} groupedHeaders onClose={()=>setReceipt(null)}/>}</div></DialogContent></Dialog>
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
        {run.status==="Approved"?<p className="rounded-lg border bg-muted/30 p-3 text-sm">Liquidación aprobada: solo consulta. Las modificaciones se registran mediante un ajuste.</p>:!canManage&&disabled&&<p className="rounded-lg border bg-muted/30 p-3 text-sm">Solo consulta: no tienes permiso para modificar novedades ni recalcular.</p>}
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
                {line.notes && <span className="block text-xs text-muted-foreground">{line.notes}</span>}
                {line.costCenterName&&<span className="block text-xs text-muted-foreground">Centro: {line.costCenterName}</span>}
                {line.evidenceUrl&&/^https?:\/\//i.test(line.evidenceUrl)&&<a href={line.evidenceUrl} target="_blank" rel="noopener noreferrer" className="block text-xs text-primary underline">Ver soporte</a>}
                {novelty?.notes&&novelty.notes!==line.notes&&<span className="block text-xs text-amber-700">Observación guardada pendiente de cálculo: {novelty.notes}</span>}</td>
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
