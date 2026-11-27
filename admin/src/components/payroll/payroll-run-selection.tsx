"use client";

import { useEffect, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Calculator } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { CostCenterSelect } from "@/components/accounting/cost-center-select";
import { payrollApi, type PayrollRun, type CalculatePayrollRun } from "@/services/api/payroll";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function PayrollRunSelection({ run, disabled, calculating, onCalculate, onBlocksApproval, renderBeforeSelection, onOpenWorker }: {
    run: PayrollRun;
    disabled: boolean;
    calculating: boolean;
    onCalculate: (id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
    onBlocksApproval: (value: boolean) => void;
    renderBeforeSelection: (calculateButton: ReactNode) => ReactNode;
    onOpenWorker: (employmentId: string) => void;
}) {
    const tenantId = useTenantContextStore(state => state.selectedTenantId);
    const businessId = useBusinessContextStore(state => state.selectedBusinessId);
    const [page, setPage] = useState(1), [search, setSearch] = useState(""), [draftSearch, setDraftSearch] = useState("");
    const [contractNumber, setContractNumber] = useState(""), [draftContract, setDraftContract] = useState("");
    const [costCenterId,setCostCenterId]=useState<string|null>(null),[costCenterName,setCostCenterName]=useState<string|null>(null);
    const [workSite,setWorkSite]=useState(""),[draftWorkSite,setDraftWorkSite]=useState("");
    const [sorting, setSorting] = useState<{ by: string; direction: "asc" | "desc" }>({ by: "employeeName", direction: "asc" });
    const [selectAll, setSelectAll] = useState<boolean | null>(null);
    const [selectionVersion, setSelectionVersion] = useState<string | null>(null);
    const [changes, setChanges] = useState<Record<string, { selected: boolean; previous: boolean }>>({});
    const busy = calculating;
    const queryKey = ["payroll-run-employments", tenantId, businessId, run.payrollRunId];
    const query = useQuery({
        queryKey: [...queryKey, page, search, contractNumber, sorting,costCenterId,workSite],
        queryFn: () => payrollApi.runEmployments(run.payrollRunId, { page, pageSize: 50, search: search || undefined,
            contractNumber: contractNumber || undefined, sortBy: sorting.by, sortDirection: sorting.direction,
            costCenterId:costCenterId??undefined,workSite:workSite||undefined }),
        staleTime: Infinity, refetchOnWindowFocus: false, refetchOnReconnect: false,
    });
    const data = query.data;
    const dirty = selectAll !== null || Object.keys(changes).length > 0;
    const stale = Boolean(data && (data.rowVersion !== run.rowVersion || (dirty && selectionVersion && selectionVersion !== run.rowVersion)));
    const count = data ? (selectAll === null ? data.selectedCount : selectAll ? data.eligibleCount : 0)
        + Object.values(changes).reduce((sum, value) => sum + Number(value.selected) - Number(selectAll ?? value.previous), 0) : 0;
    useEffect(() => { onBlocksApproval(dirty || busy); }, [dirty, busy, onBlocksApproval]);
    useEffect(() => () => onBlocksApproval(false), [onBlocksApproval]);

    function toggle(id: string, selected: boolean, previous: boolean) {
        setSelectionVersion(dirty ? selectionVersion ?? data!.rowVersion : data!.rowVersion);
        setChanges(current => {
            const next = { ...current };
            if (selected === (selectAll ?? previous)) delete next[id];
            else next[id] = { selected, previous };
            return next;
        });
    }
    async function calculate() {
        if (!data || stale || busy || disabled) return;
        try {
            await onCalculate(run.payrollRunId, {
                rowVersion: selectionVersion ?? data.rowVersion,
                ...(selectAll === null ? {} : { selectAll }),
                selectionChanges: Object.entries(changes).map(([employmentId, value]) => ({ employmentId, isSelected: value.selected })),
            });
            setSelectAll(null); setChanges({}); setSelectionVersion(null);
            toast.success("Liquidación calculada con los trabajadores seleccionados.");
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "No fue posible calcular la liquidación.");
        }
    }
    const locked = disabled || busy;
    const calculatedWorkers = new Set(run.employees.map(worker => worker.employmentId));
    const canRefreshLegacyCalculation = run.employees.some(worker => worker.baseWorkedDays == null && run.runKind === "Regular");
    const sortHeader = (by: string, label: string) => <Button size="sm" variant="ghost" disabled={locked || query.isFetching}
        aria-label={`Ordenar por ${label}`} onClick={() => { setPage(1); setSorting(current => ({ by, direction: current.by === by && current.direction === "asc" ? "desc" : "asc" })); }}>
        {label}{sorting.by === by ? sorting.direction === "asc" ? " ↑" : " ↓" : ""}</Button>;
    const sortAria = (by: string) => sorting.by === by ? sorting.direction === "asc" ? "ascending" as const : "descending" as const : undefined;
    return <>
      {renderBeforeSelection(dirty || busy || run.status === "Draft" || canRefreshLegacyCalculation ?
        <Button type="button" variant="outline" disabled={locked || query.isFetching || query.isError || !data || stale || count < 1 || count > 500}
            onClick={() => void calculate()}><Calculator aria-hidden="true" className="size-4"/>{busy ? "Calculando…" : dirty ? "Guardar cambios" : run.status === "Draft" ? (run.runKind === "Adjustment" ? "Calcular ajuste" : "Reintentar cálculo") : "Actualizar cálculo"}</Button> : null
      )}
      <section aria-label="Trabajadores de la liquidación" className="min-w-0 max-w-full space-y-5 rounded-2xl border p-4 sm:p-5" aria-busy={busy}>
        <h3 className="font-semibold">Seleccionar trabajadores</h3>
        {dirty && <p role="status" className="text-sm text-muted-foreground">Guarda los cambios para actualizar la liquidación.</p>}
        <form className="grid min-w-0 gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)_auto]" onSubmit={event => { event.preventDefault(); setPage(1); setSearch(draftSearch.trim());setWorkSite(draftWorkSite.trim());setContractNumber(draftContract.trim()); }}>
            <Input aria-label="Buscar trabajador de la liquidación" placeholder="Nombre, identificación o contrato" value={draftSearch}
                className="min-w-0 flex-1"
                maxLength={160} disabled={locked} onChange={event => setDraftSearch(event.target.value)}/>
            <CostCenterSelect value={costCenterId} selectedName={costCenterName} ariaLabel="Filtrar trabajadores por centro de costo"
                emptyLabel="Todos los centros de costo" includeInactive disabled={locked} className="min-w-0 w-full"
                onChange={(id,row)=>{setPage(1);setCostCenterId(id);setCostCenterName(row?.name??null);}}/>
            <Input aria-label="Filtrar sitio de trabajo" placeholder="Sitio de trabajo" value={draftWorkSite} maxLength={200}
                className="min-w-0 w-full" disabled={locked} onChange={event=>setDraftWorkSite(event.target.value)}/>
            <Input aria-label="Filtrar contrato" placeholder="Contrato" value={draftContract} maxLength={64} disabled={locked}
                className="min-w-0 w-full" onChange={event=>setDraftContract(event.target.value)}/>
            <Button type="submit" variant="outline" disabled={locked || query.isFetching}>Buscar</Button>
        </form>
        {query.isPending ? <p role="status">Cargando trabajadores…</p> : query.isError ? <div role="alert" className="space-y-2">
            <p>No fue posible cargar los trabajadores de la liquidación.</p>
            <Button variant="outline" disabled={locked} onClick={() => void query.refetch()}>Reintentar trabajadores</Button>
        </div> : data && <>
            <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
                <span className="font-medium">{count} seleccionados de {data.eligibleCount} elegibles</span>
                {data.eligibleCount>500&&<Button size="sm" variant="outline" disabled={locked || stale} onClick={() => { setSelectionVersion(data.rowVersion); setSelectAll(false); setChanges({}); }}>Limpiar selección</Button>}
                {dirty && <Button size="sm" variant="ghost" disabled={locked} onClick={() => { setSelectionVersion(null); setSelectAll(null); setChanges({}); }}>Descartar selección</Button>}
            </div>
            {data.eligibleCount > 500 && <p className="text-sm">Selecciona hasta 500 trabajadores por liquidación.</p>}
            {stale && <p role="alert" className="text-sm text-destructive">{data.rowVersion !== run.rowVersion
                ? "La liquidación cambió. Actualiza la liquidación antes de continuar."
                : "La liquidación cambió. Descarta la selección anterior antes de continuar."}</p>}
            <div className="max-h-[32rem] max-w-full overflow-auto rounded-xl border"><table className="w-full min-w-[800px] whitespace-nowrap text-sm">
                <thead className="sticky top-0 z-10 bg-card [&_th]:px-3 [&_th]:py-3"><tr className="text-left"><th><label className="flex items-center gap-3"><Checkbox aria-label="Incluir todos los trabajadores elegibles" checked={count===data.eligibleCount&&count>0?true:count>0?"indeterminate":false}
                    disabled={locked||stale||query.isFetching||data.eligibleCount===0||data.eligibleCount>500}
                    onCheckedChange={value=>{setSelectionVersion(data.rowVersion);setSelectAll(value===true);setChanges({});}}/><span>Incluir todos</span></label></th>
                    <th aria-sort={sortAria("employeeName")}>{sortHeader("employeeName", "Trabajador")}</th>
                    <th aria-sort={sortAria("identification")}>{sortHeader("identification", "Identificación")}</th>
                    <th aria-sort={sortAria("contractNumber")}>{sortHeader("contractNumber", "Contrato")}</th>
                    <th aria-sort={sortAria("startDate")}>{sortHeader("startDate", "Inicio")}</th>
                    <th aria-sort={sortAria("endDate")}>{sortHeader("endDate", "Fin")}</th><th>Periodicidad</th></tr></thead>
                <tbody className="[&_td]:px-3 [&_td]:py-3">{data.items.map(item => {
                    const selected = changes[item.employmentId]?.selected ?? selectAll ?? item.isSelected;
                    return <tr key={item.employmentId} className="border-t">
                        <td className="py-3"><Checkbox aria-label={`Incluir ${item.employeeName}, contrato ${item.contractNumber}`} checked={selected}
                            disabled={locked || stale || (!selected && count >= 500) || (!changes[item.employmentId] && Object.keys(changes).length >= 500)}
                            onCheckedChange={value => toggle(item.employmentId, value === true, item.isSelected)}/></td>
                        <td className="min-w-40 max-w-64 whitespace-normal break-words">{calculatedWorkers.has(item.employmentId)
                            ? <button type="button" title="Ver novedades" disabled={locked || stale || dirty} className="rounded text-left font-medium text-primary hover:underline focus-visible:outline-2 disabled:text-muted-foreground disabled:no-underline" onClick={()=>onOpenWorker(item.employmentId)}>{item.employeeName}</button>
                            : item.employeeName}</td><td>{item.identification}</td><td>{item.contractNumber}</td>
                        <td>{item.startDate}</td><td>{item.endDate ?? "vigente"}</td><td>{item.payFrequencyName}</td>
                    </tr>;
                })}</tbody>
            </table></div>
            {data.items.length === 0 && <p className="text-sm">{data.eligibleCount === 0
                ? "No hay contratos activos y vigentes que coincidan con la periodicidad de esta liquidación. Revisa la periodicidad y las fechas de los contratos."
                : "No hay trabajadores que coincidan con la búsqueda."}</p>}
            <div className="flex flex-wrap items-center justify-between gap-2 text-sm"><span>Página {page} · {data.totalCount} resultados</span><div className="flex flex-wrap gap-2">
                <Button size="sm" variant="outline" disabled={locked || query.isFetching || page === 1} onClick={() => setPage(value => value - 1)}>Anterior trabajadores</Button>
                <Button size="sm" variant="outline" disabled={locked || query.isFetching || page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Siguiente trabajadores</Button>
            </div></div>
        </>}
      </section>
    </>;
}
