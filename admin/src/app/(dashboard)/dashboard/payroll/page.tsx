"use client";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { BarChart3, Calculator, CheckCircle2, ExternalLink, FilePlus2, FileText, Landmark, ListChecks, Loader2, Pencil, RefreshCw, Settings2, Trash2, Users, Wallet } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { accountingStatusLabel, businessStatusLabel } from "@/lib/accounting-labels";
import { ReportViewer } from "@/components/reports/report-viewer";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { paymentMethodsApi, type PaymentMethod } from "@/services/api/payment-methods";
import { PayrollRunNovelties } from "@/components/payroll/payroll-run-novelties";
import { PayrollRunSelection } from "@/components/payroll/payroll-run-selection";
import { PayrollEmploymentSelect } from "@/components/payroll/payroll-employment-select";
import { accountingApi, type AccountingAccount, type AccountingCategoryDefinition, type AccountingMapping } from "@/services/api/accounting";
import { payrollApi, type CalculatePayrollRun, type PayrollRunEmploymentPage, type PayrollCatalogOption, type PayrollConcept, type PayrollDeductionAgreement, type PayrollEmployment, type PayrollEmploymentPage, type PayrollOptions, type PayrollNovelty, type PayrollReportDefinition, type PayrollRuleSet, type PayrollRun, type PayrollRunSummary, type PayrollPaymentBatch, type SaveConcept, type SaveDeductionAgreement, type SaveEmployment, type SavePayrollNovelty, type SaveRuleSet } from "@/services/api/payroll";
import { useTenantCommercialSubscription } from "@/hooks/use-tenants";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";
import { useTenantContextStore } from "@/stores/tenant-context-store";
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
type Section = "runs" | "employments" | "novelties" | "payments" | "electronic" | "reports" | "configuration";
export default function PayrollPage() {
    const businessId = useBusinessContextStore(state => state.selectedBusinessId);
    const tenantId = useTenantContextStore(state => state.selectedTenantId);
    const user = useAuthStore(state => state.user);
    const permissions = useMemo(() => new Set(user?.permissions ?? []), [user]);
    if (!tenantId || !businessId) return <Card className="rounded-3xl"><CardContent className="p-8 text-center text-muted-foreground">Selecciona una empresa y una sede para trabajar con nómina.</CardContent></Card>;
    if (!permissions.has("payroll.read")) return <Card className="rounded-3xl"><CardContent className="p-8 text-center text-muted-foreground">No tienes permiso para consultar nómina.</CardContent></Card>;
    return <PayrollWorkspace key={`${tenantId}:${businessId}:${user?.userId}`} tenantId={tenantId} businessId={businessId} permissions={permissions}/>;
}
function PayrollWorkspace({ tenantId, businessId, permissions }: { tenantId: string; businessId: string; permissions: Set<string> }) {
    const queryClient = useQueryClient();
    const [selectedId, setSelectedId] = useState<string | null>(null), [section, setSection] = useState<Section>("runs");
    const [creatingPayment, setCreatingPayment] = useState(false);
    const detailVisible = section === "runs" || section === "novelties" || creatingPayment;
    const workspace = useQuery({ queryKey: ["payroll-workspace", tenantId, businessId], queryFn: async () => {
        const [options, runs] = await Promise.all([payrollApi.options(), payrollApi.runs()]);
        return { options, runs };
    } });
    const detail = useQuery({ queryKey: ["payroll-run", tenantId, businessId, selectedId], queryFn: () => payrollApi.run(selectedId!), enabled: Boolean(selectedId) && detailVisible });
    const options = workspace.data?.options ?? null, runs = workspace.data?.runs ?? [], selected = detail.data ?? null, loading = workspace.isFetching;
    const [creatingRun, setCreatingRun] = useState(false), [creatingEmployment, setCreatingEmployment] = useState(false), [editingEmployment, setEditingEmployment] = useState<PayrollEmployment | null>(null), [preselectedPartyId, setPreselectedPartyId] = useState<string | null>(null), [creatingConcept, setCreatingConcept] = useState(false), [editingConcept, setEditingConcept] = useState<PayrollConcept | null>(null), [creatingRules, setCreatingRules] = useState(false), [editingRules, setEditingRules] = useState<PayrollRuleSet | null>(null), [creatingElectronic, setCreatingElectronic] = useState(false), [creatingAgreement, setCreatingAgreement] = useState(false), [editingAgreement, setEditingAgreement] = useState<PayrollDeductionAgreement | null>(null), [creatingNovelty, setCreatingNovelty] = useState(false);
    const [editingNovelty,setEditingNovelty]=useState<PayrollNovelty|null>(null);
    const [voidingNovelty,setVoidingNovelty]=useState(false);
    const [noveltyPreset,setNoveltyPreset]=useState<{employmentId:string;employeeName:string;startDate:string;endDate:string}|null>(null);
    const calculationKey=["payroll-calculation",tenantId,businessId];
    const calculationPending=useIsMutating({mutationKey:calculationKey})>0;
    const calculation=useMutation({mutationKey:calculationKey,
      mutationFn:({id,request}:{id:string;request:CalculatePayrollRun})=>payrollApi.calculateRun(id,request),onSuccess:runCalculated});
    async function calculateRun(id:string,request:CalculatePayrollRun){
      if(queryClient.isMutating({mutationKey:calculationKey})>0)throw new Error("Espera a que termine el cálculo en curso.");
      return calculation.mutateAsync({id,request});
    }
    const subscription = useTenantCommercialSubscription(permissions.has("payroll.fiscal"));
    const dianRemaining = subscription.data ? Math.max(0, subscription.data.dianDocumentMonthlyLimit - subscription.data.dianDocumentsUsed) : null;
    const openElectronic = () => {
        setCreatingElectronic(true);
    };
    async function reloadWorkspaceAndRun() {
      await Promise.all([workspace.refetch(), ...(selectedId ? [detailVisible ? detail.refetch()
        : queryClient.invalidateQueries({queryKey:["payroll-run",tenantId,businessId,selectedId],refetchType:"none"})] : [])]);
    }
    async function noveltySaved(result:PayrollNovelty) {
      queryClient.setQueryData<{options:PayrollOptions;runs:PayrollRunSummary[]}>(["payroll-workspace",tenantId,businessId],current=>{
        if(!current)return current;
        const previous=current.options.novelties;
        const novelties=previous.some(item=>item.noveltyId===result.noveltyId)
          ? previous.map(item=>item.noveltyId===result.noveltyId?result:item) : [result,...previous];
        return {...current,options:{...current.options,novelties}};
      });
      // The accepted capture belongs to this scope. A second command must not
      // inherit headers from a different workspace selected while it was saving.
      if(useTenantContextStore.getState().selectedTenantId!==tenantId ||
          useBusinessContextStore.getState().selectedBusinessId!==businessId)return;
      const savedLabel=result.status==="Voided"?"Novedad retirada":"Novedad guardada";
      const current=selectedId?queryClient.getQueryData<PayrollRun>(["payroll-run",tenantId,businessId,selectedId]):null;
      const worker=current?.employees.find(employee=>employee.employmentId===result.employmentId);
      const affected=current && worker && (result.startDate<=current.periodEnd && result.endDate>=current.periodStart
        || worker.lines.some(line=>line.noveltyId===result.noveltyId));
      if(current?.status!=="Calculated" || !affected || !permissions.has("payroll.calculate")) {
        toast.success(`${savedLabel}. Recalcula el trabajador antes de contabilizar.`);return;
      }
      toast.success(savedLabel);
      try {
        await calculateRun(current.payrollRunId,{rowVersion:current.rowVersion,recalculateEmploymentIds:[result.employmentId]});
      } catch(error) {
        toast.error(`${savedLabel}. No fue posible actualizar el cálculo: ${message(error,"Usa Recalcular trabajador antes de contabilizar.")}`);
      }
    }
    async function voidNovelty(item:PayrollNovelty) {
      if(!item.rowVersion || voidingNovelty)return;
      setVoidingNovelty(true);
      try {await noveltySaved(await payrollApi.voidNovelty(item.noveltyId,item.rowVersion));}
      catch(error){toast.error(message(error,"No fue posible retirar la novedad."));}
      finally {setVoidingNovelty(false);}
    }
    function agreementSaved(result:PayrollDeductionAgreement) {
      queryClient.setQueryData<{options:PayrollOptions;runs:PayrollRunSummary[]}>(["payroll-workspace",tenantId,businessId],current=>{
        if(!current)return current;
        const previous=current.options.deductionAgreements;
        const deductionAgreements=previous.some(item=>item.deductionAgreementId===result.deductionAgreementId)
          ? previous.map(item=>item.deductionAgreementId===result.deductionAgreementId?result:item) : [result,...previous];
        return {...current,options:{...current.options,deductionAgreements}};
      });
    }
    async function load() { await Promise.all([reloadWorkspaceAndRun(), queryClient.invalidateQueries({ queryKey: ["payroll-employments", tenantId, businessId] }), queryClient.invalidateQueries({ queryKey: ["payroll-run-employments", tenantId, businessId] })]); }
    async function selectRun(id: string) { setSelectedId(id); }
    async function employmentSaved(result:PayrollEmployment,previous:PayrollEmployment|null){
      const tableKey=["payroll-employments",tenantId,businessId];
      if(previous&&previous.isActive===result.isActive&&previous.employeeName===result.employeeName){
        queryClient.setQueriesData<PayrollEmploymentPage>({queryKey:tableKey},current=>current?{...current,items:current.items.map(item=>item.employmentId===result.employmentId?result:item)}:current);
      }else{
        // Creation/activation can move rows across the server's ordered pages.
        await queryClient.invalidateQueries({queryKey:tableKey});
      }
      queryClient.setQueryData(["payroll-employment-select-value",tenantId,businessId,result.employmentId],result);
      await queryClient.invalidateQueries({queryKey:["payroll-employment-select",tenantId,businessId],refetchType:"none"});
      // Contract dates, frequency and activation can change eligibility in any draft run.
      if(!previous || previous.isActive!==result.isActive || previous.employeeName!==result.employeeName ||
          previous.contractNumber!==result.contractNumber || previous.startDate!==result.startDate || previous.endDate!==result.endDate ||
          previous.payFrequencyOptionId!==result.payFrequencyOptionId || previous.partyId!==result.partyId)
        await queryClient.invalidateQueries({queryKey:["payroll-run-employments",tenantId,businessId],refetchType:"none"});
    }
    function runCalculated(result:PayrollRun){
      const key=["payroll-run",tenantId,businessId,result.payrollRunId];
      let updated=result;
      if(result.isPartialCalculation){
        const previous=queryClient.getQueryData<PayrollRun>(key);
        const changed=new Map(result.employees.map(employee=>[employee.employmentId,employee]));
        const previousIds=new Set(previous?.employees.map(employee=>employee.employmentId));
        if(!previous || previous.isPartialCalculation || previous.businessId!==result.businessId ||
            previous.employees.length!==result.employeeCount || changed.size!==result.employees.length ||
            [...changed.keys()].some(id=>!previousIds.has(id)))
          throw new Error("El detalle cambió durante el cálculo. Actualiza la liquidación antes de continuar.");
        updated={...result,isPartialCalculation:false,employees:previous.employees.map(employee=>changed.get(employee.employmentId)??employee)};
      }
      queryClient.setQueryData(key,updated);
      const selected=new Set(updated.employees.map(employee=>employee.employmentId));
      queryClient.setQueriesData<PayrollRunEmploymentPage>({queryKey:["payroll-run-employments",tenantId,businessId,result.payrollRunId]},current=>current?{
        ...current,rowVersion:result.rowVersion,selectedCount:selected.size,
        items:result.isPartialCalculation?current.items:current.items.map(item=>({...item,isSelected:selected.has(item.employmentId)})),
      }:current);
      queryClient.setQueryData<{options:PayrollOptions;runs:PayrollRunSummary[]}>(["payroll-workspace",tenantId,businessId],current=>current?{
        ...current,
        runs:current.runs.map(run=>run.payrollRunId===updated.payrollRunId?{
          ...run,status:updated.status,employeeCount:updated.employees.length,
          totalEarnings:updated.totalEarnings,totalDeductions:updated.totalDeductions,
          netPayable:updated.netPayable,rowVersion:updated.rowVersion,
        }:run),
      }:current);
    }
    useEffect(() => { const query = new URLSearchParams(window.location.search), requested = query.get("section") as Section | null, partyId = query.get("partyId"); if (requested && ["runs", "employments", "novelties", "payments", "electronic", "reports", "configuration"].includes(requested))
        setSection(requested); if (partyId) {
        setPreselectedPartyId(partyId);
        setSection("employments");
        setCreatingEmployment(permissions.has("payroll.manage"));
    } }, [permissions]);
    return <div className="space-y-5 [&_table]:tabular-nums">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between"><div><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">Nómina Auraly</p><h1 className="mt-2 text-3xl font-black">Nómina clara y conectada</h1><p className="mt-2 max-w-2xl text-sm text-cyan-50/80">Liquida, registra los pagos y envía la nómina electrónica. Todo conectado con tu contabilidad.</p></div>
    {section === "runs" && permissions.has("payroll.calculate") && <Button className="h-12 bg-white text-slate-950 hover:bg-cyan-50" disabled={!options || workspace.isError} onClick={() => setCreatingRun(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Nueva liquidación</Button>}
    {section === "employments" && permissions.has("payroll.manage") && <Button className="h-12 bg-white text-slate-950 hover:bg-cyan-50" disabled={!options || workspace.isError} onClick={() => setCreatingEmployment(true)}><Users className="mr-2 h-4 w-4"/>Nuevo contrato</Button>}</div></header>
    <Tabs value={section} onValueChange={value => { setSection(value as Section); const url = new URL(window.location.href); url.searchParams.set("section", value); url.searchParams.delete("partyId"); window.history.replaceState(null, "", url); }} className="space-y-5">
    <div className="flex min-w-0 gap-2"><div className="min-w-0 flex-1 overflow-x-auto rounded-2xl border bg-card p-1"><TabsList aria-label="Secciones de nómina" className="h-auto w-max justify-start gap-1 bg-transparent p-0">{([
      ["runs", "Liquidaciones", Calculator], ["employments", "Trabajadores", Users], ["novelties", "Novedades", ListChecks], ["payments", "Pagos", Wallet], ["electronic", "Electrónica DIAN", FileText], ["reports", "Reportes", BarChart3], ["configuration", "Configuración", Settings2]
    ] as const).map(([value, label, Icon]) => <TabsTrigger key={value} value={value} className="shrink-0 rounded-xl px-4 py-2.5 font-semibold data-[state=active]:bg-slate-950 data-[state=active]:text-white data-[state=active]:shadow"><Icon className="mr-2 h-4 w-4"/>{label}</TabsTrigger>)}</TabsList></div><Button aria-label="Actualizar nómina" variant="outline" className="h-12 rounded-2xl" disabled={loading || calculationPending} onClick={() => void load()}><RefreshCw className={loading ? "h-4 w-4 animate-spin" : "h-4 w-4"}/></Button></div>
    {workspace.isPending ? <div aria-label="Cargando nómina" className="grid gap-5 lg:grid-cols-2"><Skeleton className="h-72 rounded-3xl"/><Skeleton className="h-72 rounded-3xl"/></div> : workspace.isError ? <Card className="rounded-3xl"><CardContent className="space-y-3 p-6" role="alert"><p>{message(workspace.error, "No fue posible cargar la nómina.")}</p><Button variant="outline" onClick={() => void load()}>Reintentar</Button></CardContent></Card> : <TabsContent value={section} className="mt-0">
    {detail.isError && section === "runs" && <div role="alert" className="mb-4 rounded-2xl border border-destructive/30 p-4 text-sm">{message(detail.error, "No fue posible abrir la liquidación.")} <Button variant="outline" size="sm" onClick={() => void detail.refetch()}>Reintentar detalle</Button></div>}
    {detail.isFetching && section === "runs" && <p role="status" className="mb-3 text-sm text-muted-foreground">Cargando detalle…</p>}
    {section === "runs" && (
      <RunsSection options={options} runs={runs} selected={selected} canCalculate={permissions.has("payroll.calculate")} calculationDisabled={detail.isFetching || detail.isError} calculating={calculationPending} canApprove={permissions.has("payroll.approve") && !detail.isFetching && !detail.isError} onSelect={selectRun} onCalculate={calculateRun} onChanged={reloadWorkspaceAndRun}/>
    )}
    {section === "employments" && (
      <EmploymentsSection canManage={permissions.has("payroll.manage")} onEdit={setEditingEmployment} onSaved={employmentSaved}/>
    )}
    {section === "novelties" && <div className="space-y-5">
      <PayrollRunNovelties key={selectedId??"none"} runs={runs} selectedId={selectedId} run={selected} loading={detail.isFetching}
        error={detail.isError} canCalculate={permissions.has("payroll.calculate")} calculating={calculationPending}
        natures={options?catalog(options,"payroll-concept-nature"):[]} onSelect={setSelectedId} onRetry={()=>void detail.refetch()} onCalculate={calculateRun}
        canManage={permissions.has("payroll.manage")} novelties={options?.novelties??[]} voiding={voidingNovelty}
        onEditNovelty={setEditingNovelty} onVoidNovelty={voidNovelty} onAddNovelty={worker=>{
          if(!selected)return;
          setNoveltyPreset({employmentId:worker.employmentId,employeeName:worker.employeeName,startDate:selected.periodStart,endDate:selected.periodEnd});
          setCreatingNovelty(true);
        }}/>

      <NoveltiesSection options={options} canManage={permissions.has("payroll.manage")} onAgreement={() => setCreatingAgreement(true)} onEditAgreement={setEditingAgreement} onAgreementSaved={agreementSaved} voidingNovelty={voidingNovelty} onNovelty={() => {setNoveltyPreset(null);setCreatingNovelty(true);}} onEditNovelty={setEditingNovelty} onVoidNovelty={voidNovelty}/>
    </div>}
    {section === "payments" && (
      <PaymentsSection options={options} canPay={permissions.has("payroll.pay")} onCreate={() => setCreatingPayment(true)}/>
    )}
    {section === "electronic" && (
      <ElectronicSection options={options} canGenerate={permissions.has("payroll.fiscal")} onGenerate={openElectronic}/>
    )}
    {section === "reports" && (
      <PayrollReports/>
    )}
    {section === "configuration" && (
      <ConfigurationSection options={options} canConfigure={permissions.has("payroll.configure")} canReadAccounting={permissions.has("accounting.read")} canConfigureAccounting={permissions.has("accounting.configure")} onConcept={() => setCreatingConcept(true)} onEditConcept={setEditingConcept} onRules={() => setCreatingRules(true)} onEditRules={setEditingRules} onChanged={load}/>
    )}
    </TabsContent>}
    </Tabs>
    <Dialog open={creatingRun} onOpenChange={setCreatingRun}><DialogContent><DialogHeader><DialogTitle>Nueva liquidación</DialogTitle><DialogDescription>Selecciona el período, la periodicidad y las reglas vigentes.</DialogDescription></DialogHeader>{options && <RunForm businessId={businessId} options={options} runs={runs} onSaved={async () => { setCreatingRun(false); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingEmployment || editingEmployment !== null} onOpenChange={open => { if (!open) {
        setCreatingEmployment(false);
        setEditingEmployment(null);
        setPreselectedPartyId(null);
    } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingEmployment ? "Editar relación laboral" : "Nueva relación laboral"}</DialogTitle><DialogDescription>Selecciona un empleado y completa sus datos laborales y de pago.</DialogDescription></DialogHeader>{options && <EmploymentForm businessId={businessId} options={options} initial={editingEmployment} partyId={preselectedPartyId} onSaved={async result => { const previous=editingEmployment; setCreatingEmployment(false); setEditingEmployment(null); setPreselectedPartyId(null); await employmentSaved(result,previous); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingConcept || editingConcept !== null} onOpenChange={open => { if (!open) { setCreatingConcept(false); setEditingConcept(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingConcept ? "Editar concepto" : "Nuevo concepto"}</DialogTitle><DialogDescription>Define cómo se calcula, contabiliza y reporta este concepto.</DialogDescription></DialogHeader>{options && <ConceptForm options={options} initial={editingConcept} onSaved={async () => { setCreatingConcept(false); setEditingConcept(null); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingRules || editingRules !== null} onOpenChange={open => { if (!open) { setCreatingRules(false); setEditingRules(null); } }}><DialogContent className="max-h-[92dvh] max-w-4xl overflow-y-auto"><DialogHeader><DialogTitle>{editingRules ? "Editar reglas en borrador" : "Nuevo conjunto de reglas"}</DialogTitle><DialogDescription>Los parámetros y unidades se cargan desde tabla. Verifica la fuente normativa antes de aprobar.</DialogDescription></DialogHeader>{options && <RuleSetForm options={options} initial={editingRules} onSaved={async () => { setCreatingRules(false); setEditingRules(null); await load(); }}/>}</DialogContent></Dialog>
    <Dialog open={creatingElectronic} onOpenChange={setCreatingElectronic}><DialogContent><DialogHeader><DialogTitle>Consolidar nómina electrónica</DialogTitle><DialogDescription>Reserva la serie, crea el XML por trabajador y lo envía al motor fiscal para validar, firmar y transmitir a la DIAN.</DialogDescription></DialogHeader><p className="rounded-xl bg-muted p-3 text-sm">{dianRemaining === null ? "El cupo se verificará al preparar los documentos." : <>Cupo disponible: <b>{dianRemaining.toLocaleString("es-CO")} documentos DIAN</b>. Se requiere uno por trabajador.</>}{dianRemaining === 0 && " Para generar documentos nuevos necesitas ampliar el cupo; puedes recuperar un mes ya preparado."}</p><ElectronicPeriodForm businessId={businessId} onSaved={async () => { setCreatingElectronic(false); await load(); }}/></DialogContent></Dialog>
    <Dialog open={creatingAgreement || editingAgreement !== null} onOpenChange={open => { if (!open) { setCreatingAgreement(false); setEditingAgreement(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingAgreement ? "Editar descuento autorizado" : "Nuevo descuento autorizado"}</DialogTitle><DialogDescription>Registra autoridad, evidencia, prioridad, vigencia y límites antes de descontar.</DialogDescription></DialogHeader>{options && <DeductionAgreementForm options={options} initial={editingAgreement} onSaved={result=>{agreementSaved(result);setCreatingAgreement(false);setEditingAgreement(null);}}/>}</DialogContent></Dialog>
    <Dialog open={creatingNovelty || editingNovelty !== null} onOpenChange={open=>{if(!open){setCreatingNovelty(false);setEditingNovelty(null);setNoveltyPreset(null);}}}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingNovelty ? "Editar novedad" : "Nueva novedad"}</DialogTitle><DialogDescription>Registra el ingreso, ausencia o descuento que se incluirá al liquidar.</DialogDescription></DialogHeader>{options && <NoveltyForm key={editingNovelty?.noveltyId??"new"} options={options} initial={editingNovelty} preset={noveltyPreset} onSaved={result=>{void noveltySaved(result);setCreatingNovelty(false);setEditingNovelty(null);}}/>}</DialogContent></Dialog>
    <Dialog open={creatingPayment} onOpenChange={setCreatingPayment}><DialogContent className="max-h-[92dvh] overflow-y-auto"><DialogHeader><DialogTitle>Confirmar lote de pago</DialogTitle><DialogDescription>Consulta una liquidación y paga su grupo pendiente cuando la causación esté contabilizada.</DialogDescription></DialogHeader>{options && <PaymentForm options={options} runs={runs} selectedRun={selected} loading={detail.isFetching} error={detail.isError} onRunChange={setSelectedId} onRetry={()=>void detail.refetch()} onSaved={result=>{
      queryClient.setQueryData<{options:PayrollOptions;runs:PayrollRunSummary[]}>(["payroll-workspace",tenantId,businessId],current=>{
        if(!current)return current;
        const batches=current.options.paymentBatches;
        const updated=batches.some(batch=>batch.paymentBatchId===result.paymentBatchId)
          ? batches.map(batch=>batch.paymentBatchId===result.paymentBatchId?result:batch)
          : [result,...batches].sort((left,right)=>right.paymentDate.localeCompare(left.paymentDate));
        return {...current,options:{...current.options,paymentBatches:updated}};
      });
      queryClient.setQueryData<PayrollRun>(["payroll-run",tenantId,businessId,result.payrollRunId],current=>current?{...current,payableEmployees:result.remainingEmployees}:current);
      setCreatingPayment(false);
    }}/>}</DialogContent></Dialog>
  </div>;
}
function RunsSection({ options, runs, selected, canCalculate, calculationDisabled, calculating, canApprove, onSelect, onCalculate, onChanged }: {
    options: PayrollOptions | null;
    runs: PayrollRunSummary[];
    selected: PayrollRun | null;
    canCalculate: boolean;
    calculationDisabled: boolean;
    calculating: boolean;
    canApprove: boolean;
    onSelect: (id: string) => Promise<void>;
    onCalculate:(id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
    onChanged: () => Promise<void>;
}) {
    const [busy, setBusy] = useState<string | null>(null);
    const [selectionBlocksApproval, setSelectionBlocksApproval] = useState(false);
    async function approve() { if (!selected || calculating || selectionBlocksApproval)
        return; setBusy("approve"); try {
        await payrollApi.approveRun(selected.payrollRunId, selected.rowVersion, `payroll-approve-${selected.payrollRunId}`);
        toast.success("Nómina aprobada y enviada al motor contable.");
        await onSelect(selected.payrollRunId);
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible aprobar."));
    }
    finally {
        setBusy(null);
    } }
    return <div className="grid gap-5 2xl:grid-cols-[minmax(260px,.65fr)_minmax(0,1.8fr)]"><Card className="rounded-3xl"><CardHeader><CardTitle>Liquidaciones</CardTitle><CardDescription>Selecciona un período para revisar, calcular o aprobar su nómina.</CardDescription></CardHeader><CardContent className="max-h-96 space-y-2 overflow-y-auto">{runs.map(run => <button type="button" key={run.payrollRunId} onClick={() => void onSelect(run.payrollRunId)} className={`w-full rounded-2xl border p-4 text-left transition-colors ${selected?.payrollRunId === run.payrollRunId ? "border-primary bg-primary/5" : "hover:bg-muted/40"}`}><span className="flex items-center justify-between gap-3"><b>{run.periodStart} — {run.periodEnd}</b><small>{statusLabel(options, run.status)}</small></span><span className="mt-2 grid grid-cols-3 text-sm text-muted-foreground"><span>{run.employeeCount} trabajadores</span><span className="text-right">Deducciones {money.format(run.totalDeductions)}</span><strong className="text-right text-foreground">Neto {money.format(run.netPayable)}</strong></span></button>)}{runs.length === 0 && <Empty text="Todavía no hay liquidaciones."/>}</CardContent></Card><Card className="rounded-3xl"><CardHeader><CardTitle>Detalle</CardTitle><CardDescription>{selected ? `${selected.periodStart} — ${selected.periodEnd}` : "Selecciona una liquidación"}</CardDescription></CardHeader><CardContent>{!selected ? <Empty text="El detalle mostrará trabajadores, conceptos y totales."/> : <div className="space-y-4"><div className="grid gap-3 sm:grid-cols-3"><Metric label="Devengado" value={selected.totalEarnings}/><Metric label="Deducciones" value={selected.totalDeductions}/><Metric label="Neto" value={selected.netPayable}/></div><div className="flex flex-wrap gap-2">{canApprove && selected.status === "Calculated" && <Button disabled={busy !== null || calculating || selectionBlocksApproval} onClick={() => void approve()}><CheckCircle2 className="mr-2 h-4 w-4"/>{busy === "approve" ? "Aprobando…" : "Aprobar y contabilizar"}</Button>}</div>{canCalculate && ["Draft", "Calculated"].includes(selected.status) && <PayrollRunSelection key={selected.payrollRunId} run={selected} disabled={busy !== null || calculationDisabled} calculating={calculating} onCalculate={onCalculate} onBlocksApproval={setSelectionBlocksApproval}/>} {selected.employees.map(employee => <details key={employee.payrollRunEmployeeId} className="rounded-2xl border p-4"><summary className="cursor-pointer font-semibold">{employee.employeeName}<span className="float-right">{money.format(employee.netPayable)}</span></summary><div className="mt-3 overflow-x-auto"><table className="w-full min-w-[620px] text-sm"><thead><tr className="text-left text-muted-foreground"><th>Concepto</th><th>Naturaleza</th><th className="text-right">Base</th><th className="text-right">Valor</th></tr></thead><tbody>{employee.lines.map(line => <tr key={line.lineNumber} className="border-t"><td className="py-2">{line.conceptCode} · {line.conceptName}</td><td>{catalogLabel(options, "payroll-concept-nature", line.natureCode)}</td><td className="text-right">{line.baseAmount === null ? "—" : money.format(line.baseAmount)}</td><td className="text-right font-medium">{money.format(line.amount)}</td></tr>)}</tbody></table></div></details>)}</div>}</CardContent></Card></div>;
}
function EmploymentsSection({ canManage, onEdit, onSaved }: {
    canManage: boolean;
    onEdit: (value: PayrollEmployment) => void;
    onSaved:(result:PayrollEmployment,previous:PayrollEmployment)=>Promise<void>;
}) {
    const tenantId = useTenantContextStore(state => state.selectedTenantId), businessId = useBusinessContextStore(state => state.selectedBusinessId);
    const [page, setPage] = useState(1);
    const query = useQuery({ queryKey: ["payroll-employments", tenantId, businessId, page], queryFn: () => payrollApi.employments({ page, pageSize: 25 }) });
    const items = query.data?.items ?? [];
    async function deactivate(item: PayrollEmployment) { if (!window.confirm(`¿Desactivar el contrato ${item.contractNumber}?`)) return; try { const result=await payrollApi.saveEmployment({ ...item, isActive: false }); toast.success("Contrato desactivado."); await onSaved(result,item); } catch (error) { toast.error(message(error, "No fue posible desactivar el contrato.")); } }
    return <Card className="rounded-3xl"><CardHeader><CardTitle>Relaciones laborales</CardTitle><CardDescription>Administra los contratos y datos de pago de tus trabajadores.</CardDescription></CardHeader><CardContent className="space-y-3"><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[820px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Trabajador</th><th className="text-left">Contrato</th><th className="text-left">Vigencia</th><th className="text-left">Cuenta de pago</th><th className="text-right">Salario</th><th className="pr-3 text-right">Acciones</th></tr></thead><tbody>{items.map(item => <tr key={item.employmentId} className="border-t"><td className="p-3 font-medium">{item.employeeName}</td><td>{item.contractNumber}</td><td>{item.startDate} — {item.endDate ?? "vigente"}</td><td>{item.bankAccountNumber ? `•••• ${item.bankAccountNumber.slice(-4)}` : "Sin configurar"}</td><td className="text-right">{money.format(item.monthlySalary)}</td><td className="pr-3 text-right"><span className="inline-flex">{canManage && <Button size="sm" variant="ghost" onClick={() => onEdit(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</span></td></tr>)}</tbody></table>{query.isLoading && <p className="p-4 text-sm text-muted-foreground">Cargando trabajadores…</p>}{query.isError && <p className="p-4 text-sm text-destructive">No fue posible cargar los contratos. <Button size="sm" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></p>}{!query.isLoading && !query.isError && !items.length && <Empty text="Crea el primer contrato laboral."/>}</div><div className="flex items-center justify-between text-sm"><span className="text-muted-foreground">{query.data?.totalCount ?? 0} contratos</span><span className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1 || query.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button size="sm" variant="outline" disabled={!query.data || page * query.data.pageSize >= query.data.totalCount || query.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></span></div></CardContent></Card>;
}
function NoveltiesSection({ options, canManage, onAgreement, onEditAgreement, onAgreementSaved, voidingNovelty, onNovelty, onEditNovelty, onVoidNovelty }: {
    options: PayrollOptions | null;
    canManage: boolean;
    onAgreement: () => void;
    onEditAgreement: (value: PayrollDeductionAgreement) => void;
    onAgreementSaved:(item:PayrollDeductionAgreement)=>void;
    voidingNovelty:boolean;
    onNovelty: () => void;
    onEditNovelty:(item:PayrollNovelty)=>void;
    onVoidNovelty:(item:PayrollNovelty)=>Promise<void>;
}) { async function deactivate(item: PayrollDeductionAgreement) { try { onAgreementSaved(await payrollApi.saveDeductionAgreement({ ...item, isActive: false })); toast.success("Descuento desactivado."); } catch (error) { toast.error(message(error, "No fue posible desactivar el descuento.")); } } function evidence(url: string) { try { const parsed = new URL(url); if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error(); window.open(parsed.toString(), '_blank', 'noopener,noreferrer'); } catch { toast.error("La evidencia no tiene una URL válida."); } } return <div className="grid gap-5 xl:grid-cols-2"><Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Autorizaciones de descuento</CardTitle><CardDescription>Acuerdos, libranzas, embargos y otros conceptos con soporte.</CardDescription></span>{canManage && <Button size="sm" onClick={onAgreement}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.deductionAgreements.map(item => <div key={item.deductionAgreementId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.employeeName}</b><span>{item.isActive ? "Activo" : "Inactivo"}</span></span><p>{item.conceptName} · {item.authorityName}</p><p className="text-muted-foreground">Ref. {item.referenceNumber} · descontado {money.format(item.deductedToDate)}{item.authorizedTotal !== null ? ` de ${money.format(item.authorizedTotal)}` : ""}</p><div className="mt-2 flex flex-wrap gap-2"><Button size="sm" variant="outline" onClick={() => evidence(item.evidenceUrl)}><ExternalLink className="mr-2 h-4 w-4"/>Ver evidencia</Button>{canManage && <Button size="sm" variant="ghost" onClick={() => onEditAgreement(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div></div>)}{!options?.deductionAgreements.length && <Empty text="No hay descuentos autorizados."/>}</CardContent></Card><Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Novedades</CardTitle><CardDescription>Ingresos, ausencias, deducciones y ajustes aplicables a una liquidación.</CardDescription></span>{canManage && <Button size="sm" onClick={onNovelty}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.novelties.map(item => <div key={item.noveltyId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.employeeName}</b><span>{statusLabel(options, item.status)}</span></span><p>{item.noveltyTypeName} · {item.conceptName}</p><p className="text-muted-foreground">{item.startDate} — {item.endDate} · {money.format(item.totalAmount)}</p>{item.notes && <p>{item.notes}</p>}{canManage && item.rowVersion && ["Draft","Approved"].includes(item.status) && <div className="mt-2 flex gap-2"><Button size="sm" variant="outline" disabled={voidingNovelty} onClick={()=>onEditNovelty(item)}>Editar novedad</Button><Button size="sm" variant="ghost" className="text-destructive" disabled={voidingNovelty} onClick={()=>void onVoidNovelty(item)}>Retirar novedad</Button></div>}</div>)}{!options?.novelties.length && <Empty text="No hay novedades registradas."/>}</CardContent></Card></div>; }
function PaymentsSection({ options, canPay, onCreate }: {
    options: PayrollOptions | null;
    canPay: boolean;
    onCreate: () => void;
}) { return <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Pagos de nómina</CardTitle><CardDescription>Confirma un grupo de pago y consulta su resultado contable en Trazabilidad financiera.</CardDescription></span>{canPay && <Button onClick={onCreate}>Confirmar pago</Button>}</span></CardHeader><CardContent><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[680px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Referencia</th><th className="text-left">Medio</th><th className="text-right">Trabajadores</th><th className="text-right">Total</th><th className="pr-3 text-right">Estado</th></tr></thead><tbody>{options?.paymentBatches.map(item => <tr className="border-t" key={item.paymentBatchId}><td className="p-3">{item.paymentDate}</td><td>{item.referenceNumber}</td><td>{item.paymentMethodName}</td><td className="text-right">{item.employeeCount}</td><td className="text-right font-medium">{money.format(item.totalAmount)}</td><td className="pr-3 text-right">{businessStatusLabel(item.isPaid?"Paid":item.status)}<span className="block text-xs text-muted-foreground">{accountingStatusLabel(item.accountingStatus??"MissingAccountingJob")}</span></td></tr>)}</tbody></table>{!options?.paymentBatches.length && <Empty text="No hay pagos confirmados."/>}</div></CardContent></Card>; }
function ElectronicSection({ options, canGenerate, onGenerate }: {
    options: PayrollOptions | null;
    canGenerate: boolean;
    onGenerate: () => void;
}) { return <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Documentos electrónicos DIAN</CardTitle><CardDescription>Consolidación mensual por trabajador. La presentación se confirma cuando la DIAN acepta el documento.</CardDescription></span>{canGenerate && <Button onClick={onGenerate}>Consolidar mes</Button>}</span></CardHeader><CardContent className="space-y-3"><div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-muted/20 p-4 text-sm"><p>{options?.settings?.electronicPayrollEnabled ? "Nómina electrónica habilitada." : "Activa la nómina electrónica en Configuración."} {options?.electronicConfiguration?.isActive ? `Serie ${options.electronicConfiguration.prefix} configurada.` : "La serie y las credenciales DIAN requieren configuración."}</p><Button variant="outline" asChild><Link href="/dashboard/settings/fiscal">Configuración DIAN <ExternalLink className="ml-2 h-3 w-3"/></Link></Button></div>{options?.electronicPeriods.map(period => <details key={period.electronicPeriodId} className="rounded-xl border p-4"><summary className="cursor-pointer font-semibold">{period.year}-{String(period.month).padStart(2, "0")} · {statusLabel(options, period.status)}<span className="float-right">{period.documents.length} documentos</span></summary><div className="mt-3 space-y-2">{period.documents.map(document => <div key={document.electronicPayrollDocumentId} className="flex flex-wrap justify-between gap-2 rounded-lg bg-muted/30 p-3 text-sm"><span>{document.employeeName}</span><span>{statusLabel(options, document.status)}</span></div>)}</div></details>)}{!options?.electronicPeriods.length && <Empty text="No se han consolidado períodos electrónicos."/>}</CardContent></Card>; }
function PayrollReports() { const today = useBusinessDates().today(), [from, setFrom] = useState(`${today.slice(0, 4)}-01-01`), [to, setTo] = useState(today), [partyId, setPartyId] = useState("_all"), [definitions, setDefinitions] = useState<PayrollReportDefinition[]>([]), [reportCode, setReportCode] = useState(""), [selected, setSelected] = useState<PayrollReportDefinition | null>(null), [rows, setRows] = useState<Array<Record<string, string | number | null>>>([]), [loading, setLoading] = useState(false); useEffect(() => { payrollApi.reportDefinitions().then(items => { setDefinitions(items); setReportCode(items[0]?.code ?? ""); }).catch(error => toast.error(message(error, "No fue posible cargar el catálogo de reportes."))); }, []); async function open(definition: PayrollReportDefinition) { setSelected(definition); setLoading(true); try {
    const result = await payrollApi.report(definition.code, from, to, partyId === "_all" ? undefined : partyId);
    setRows(result.rows);
}
catch (error) {
    toast.error(message(error, "No fue posible generar el reporte."));
    setSelected(null);
}
finally {
    setLoading(false);
} } if (selected && !loading) {
    const columns = selected.columns.map(column => ({ key: column.key, label: column.label, align: column.align, format: (value: unknown) => column.format === "currency" ? money.format(Number(value ?? 0)) : column.format === "number" ? Number(value ?? 0).toLocaleString("es-CO") : column.format === "datetime" && value ? new Date(String(value)).toLocaleString("es-CO") : String(value ?? "") }));
    return <ReportViewer onClose={() => setSelected(null)} title={selected.name} description={`${selected.description} · ${from} a ${to}`} rows={rows} columns={columns} fileName={`nomina-${selected.code}-${from}-${to}`}/>;
} ; const selectedDefinition = definitions.find(item => item.code === reportCode); return <Card className="rounded-3xl"><CardHeader><CardTitle>Reportes de nómina</CardTitle><CardDescription>Consulta liquidaciones, comprobantes, pagos y documentos electrónicos.</CardDescription></CardHeader><CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-4"><Field label="Reporte"><Select value={reportCode} onValueChange={setReportCode}><SelectTrigger aria-label="Reporte"><SelectValue placeholder="Selecciona un reporte" /></SelectTrigger><SelectContent>{definitions.map(definition => <SelectItem key={definition.code} value={definition.code}>{definition.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Desde"><DatePicker today={today} value={from} onChange={setFrom}/></Field><Field label="Hasta"><DatePicker today={today} value={to} onChange={setTo}/></Field><Field label="Trabajador"><PartyRoleSelect role="Employee" includePartyId value={partyId} leadingOptions={[{value:"_all",label:"Todos los trabajadores"}]} placeholder="Buscar trabajador" onChange={setPartyId}/></Field><div className="rounded-xl border bg-muted/20 p-3 text-sm md:col-span-2 xl:col-span-3"><b>{selectedDefinition?.name ?? "Catálogo de reportes"}</b><p className="text-muted-foreground">{selectedDefinition?.description ?? "No hay reportes activos."}</p></div><Button className="self-end" disabled={loading || !selectedDefinition || !from || !to || to < from} onClick={() => selectedDefinition && void open(selectedDefinition)}>{loading && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar reporte</Button></CardContent></Card>; }
function ConfigurationSection({ options, canConfigure, canReadAccounting, canConfigureAccounting, onConcept, onEditConcept, onRules, onEditRules, onChanged }: {
    options: PayrollOptions | null;
    canConfigure: boolean;
    canReadAccounting: boolean;
    canConfigureAccounting: boolean;
    onConcept: () => void;
    onEditConcept: (value: PayrollConcept) => void;
    onRules: () => void;
    onEditRules: (value: PayrollRuleSet) => void;
    onChanged: () => Promise<void>;
}) {
    async function approve(id: string, version: string) { try {
        await payrollApi.approveRuleSet(id, version);
        toast.success("Conjunto de reglas aprobado.");
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible aprobar las reglas."));
    } }
    async function retire(item: PayrollRuleSet) { if (!window.confirm(`¿Retirar las reglas ${item.code}?`)) return; try { await payrollApi.retireRuleSet(item.ruleSetId, item.rowVersion); toast.success("Conjunto de reglas retirado."); await onChanged(); } catch (error) { toast.error(message(error, "No fue posible retirar las reglas.")); } }
    async function deactivateConcept(item: PayrollConcept) { if (!options) return; const option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(value => value.code === code)?.optionId ?? null : null; try { await payrollApi.saveConcept({ conceptId: item.conceptId, code: item.code, name: item.name, natureOptionId: option("payroll-concept-nature", item.natureCode) ?? "", calculationMethodOptionId: option("payroll-calculation-method", item.calculationMethodCode) ?? "", treatmentOptionId: option("payroll-concept-treatment", item.treatmentCode) ?? "", dianConceptOptionId: option("payroll-dian-concept", item.dianConceptCode), accountingCategoryOptionId: option("payroll-accounting-category", item.accountingCategoryCode) ?? "", systemRoleOptionId: option("payroll-system-concept-role", item.systemRoleCode), isSalaryBase: item.isSalaryBase, isSocialSecurityBase: item.isSocialSecurityBase, isBenefitsBase: item.isBenefitsBase, isTaxWithholdingBase: item.isTaxWithholdingBase, requiresDeductionAgreement: item.requiresDeductionAgreement, effectiveFrom: item.effectiveFrom, effectiveTo: item.effectiveTo, isActive: false, rowVersion: item.rowVersion }); toast.success("Concepto desactivado."); await onChanged(); } catch (error) { toast.error(message(error, "No fue posible desactivar el concepto.")); } }
    async function saveSettings(field: "electronicPayrollEnabled" | "isEmployerExemptFromHealthSenaIcbf", value: boolean) { try {
        await payrollApi.saveSettings({ isEmployerExemptFromHealthSenaIcbf: field === "isEmployerExemptFromHealthSenaIcbf" ? value : options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false, electronicPayrollEnabled: field === "electronicPayrollEnabled" ? value : options?.settings?.electronicPayrollEnabled ?? false, rowVersion: options?.settings?.rowVersion ?? null });
        toast.success("Configuración de nómina actualizada.");
        await onChanged();
    }
    catch (error) {
        toast.error(message(error, "No fue posible actualizar la configuración."));
    } }
    return <div className="grid gap-5 lg:grid-cols-2">
    <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Conceptos</CardTitle><CardDescription>Devengados, descuentos, aportes y provisiones.</CardDescription></span>{canConfigure && <Button size="sm" onClick={onConcept}><Settings2 className="mr-2 h-4 w-4"/>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.concepts.map(item => <div key={item.conceptId} className="rounded-xl border p-3 text-sm"><b>{item.code} · {item.name}</b><span className="block text-muted-foreground">{catalogLabel(options, "payroll-concept-nature", item.natureCode)} · {catalogLabel(options, "payroll-accounting-category", item.accountingCategoryCode)} · {item.isActive ? "Activo" : "Inactivo"}</span>{canConfigure && <div className="mt-2 flex gap-2"><Button size="sm" variant="ghost" onClick={() => onEditConcept(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>{item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivateConcept(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div>}</div>)}{!options?.concepts.length && <Empty text="Configura los conceptos requeridos por el calculador."/>}</CardContent></Card>
    <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Reglas versionadas</CardTitle><CardDescription>Tarifas y topes con vigencia y fuente.</CardDescription></span>{canConfigure && <Button size="sm" onClick={onRules}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.ruleSets.map(item => <div key={item.ruleSetId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.code} · {item.name}</b><span>{statusLabel(options, item.status)}</span></span><p className="text-muted-foreground">Desde {item.effectiveFrom} · {item.parameters.length} parámetros</p>{canConfigure && item.status !== "Retired" && <div className="mt-2 flex flex-wrap gap-2">{item.status === "Draft" && <Button size="sm" variant="ghost" onClick={() => onEditRules(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{item.status === "Draft" && <Button size="sm" variant="outline" onClick={() => void approve(item.ruleSetId, item.rowVersion)}>Aprobar</Button>}<Button size="sm" variant="ghost" className="text-destructive" onClick={() => void retire(item)}><Trash2 className="mr-2 h-4 w-4"/>Retirar</Button></div>}</div>)}{!options?.ruleSets.length && <Empty text="Crea un conjunto de reglas antes de liquidar."/>}</CardContent></Card>
    <Card className="rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Parámetros del empleador</CardTitle><CardDescription>Activa únicamente condiciones verificadas para la entidad legal.</CardDescription></CardHeader><CardContent className="grid gap-3 sm:grid-cols-2"><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.electronicPayrollEnabled ?? false} onCheckedChange={checked => void saveSettings("electronicPayrollEnabled", checked === true)}/><span><b className="block">Nómina electrónica</b><span className="text-muted-foreground">Habilita generación, firma y envío mensual.</span></span></label><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false} onCheckedChange={checked => void saveSettings("isEmployerExemptFromHealthSenaIcbf", checked === true)}/><span><b className="block">Exoneración verificada</b><span className="text-muted-foreground">Salud patronal, SENA e ICBF según condición legal.</span></span></label></CardContent></Card>
    {options && canReadAccounting && (
      <PayrollAccountingMappings options={options} canConfigure={canConfigureAccounting}/>
    )}
  </div>;
}
function RunForm({ businessId, options, runs, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    runs: PayrollRunSummary[];
    onSaved: () => Promise<void>;
}) { const today = useBusinessDates().today(), monthEnd = new Date(Date.UTC(Number(today.slice(0,4)),Number(today.slice(5,7)),0)).toISOString().slice(0,10), [ruleSetId, setRuleSetId] = useState(""), [frequency, setFrequency] = useState(""), [kind, setKind] = useState<"Regular" | "Adjustment">("Regular"), [originalId, setOriginalId] = useState(""), [start, setStart] = useState(today.slice(0, 8) + "01"), [end, setEnd] = useState(monthEnd), [payment, setPayment] = useState(today), [busy, setBusy] = useState(false); async function save() { setBusy(true); try {
    await payrollApi.createRun({ payrollRunId: crypto.randomUUID(), businessId, ruleSetId, payFrequencyOptionId: frequency, runKind: kind, originalPayrollRunId: kind === "Adjustment" ? originalId : null, periodStart: start, periodEnd: end, paymentDate: payment });
    toast.success(kind === "Adjustment" ? "Ajuste creado en borrador." : "Liquidación creada en borrador.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible crear la liquidación."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Tipo"><Select value={kind} onValueChange={value => { setKind(value as "Regular" | "Adjustment"); if (value === "Regular")
    setOriginalId(""); }}><SelectTrigger aria-label="Tipo"><SelectValue /></SelectTrigger><SelectContent>{catalog(options, "payroll-run-kind").filter(item => item.isActive).map(item => <SelectItem key={item.optionId} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>{kind === "Adjustment" && <Field label="Liquidación original"><Select value={originalId} onValueChange={value => { setOriginalId(value); const original = runs.find(run => run.payrollRunId === value); if (original) {
    setStart(original.periodStart);
    setEnd(original.periodEnd);
    setPayment(original.paymentDate);
} }}><SelectTrigger aria-label="Liquidación original"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{runs.filter(run => run.status === "Approved" && run.runKind === "Regular").map(run => <SelectItem key={run.payrollRunId} value={run.payrollRunId}>{run.periodStart} — {run.periodEnd}</SelectItem>)}</SelectContent></Select></Field>}<CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={frequency} onChange={setFrequency}/><Field label="Reglas aprobadas"><Select value={ruleSetId} onValueChange={setRuleSetId}><SelectTrigger aria-label="Reglas aprobadas"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.ruleSets.filter(item => item.status === "Approved" && item.effectiveFrom <= start && (!item.effectiveTo || item.effectiveTo >= end)).map(item => <SelectItem key={item.ruleSetId} value={item.ruleSetId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Inicio"><DatePicker today={today} value={start} onChange={setStart}/></Field><Field label="Fin"><DatePicker today={today} value={end} onChange={setEnd}/></Field>{kind === "Regular" && <p className="sm:col-span-2 text-sm text-muted-foreground">Mensual: mes completo. Quincenal: del 1 al 15 o del 16 al fin de mes. Los días a liquidar se ajustan después en Novedades.</p>}<Field label="Fecha de pago"><DatePicker today={today} value={payment} onChange={setPayment}/></Field>{kind === "Adjustment" && <p className="self-end rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">El ajuste liquida únicamente novedades nuevas como diferencias; no repite el salario ya aprobado.</p>}<DialogFooter className="sm:col-span-2"><Button disabled={busy || !frequency || !ruleSetId || !start || !end || !payment || end < start || payment < start || (kind === "Adjustment" && !originalId)} onClick={() => void save()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Crear borrador</Button></DialogFooter></div>; }
function EmploymentForm({ businessId, options, initial, partyId, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    initial: PayrollEmployment | null;
    partyId: string | null;
    onSaved: (result:PayrollEmployment) => Promise<void>;
}) { const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveEmployment>(initial ? { ...initial, appliesTransportAllowance:initial.appliesTransportAllowance??true, rowVersion: initial.rowVersion } : { employmentId: crypto.randomUUID(), partyId: partyId ?? "", businessId, employeeId: null, contractTypeOptionId: "", salaryTypeOptionId: "", payFrequencyOptionId: "", riskClassOptionId: "", workerTypeOptionId: "", workerSubtypeOptionId: null, paymentMethodOptionId: "", contractNumber: "", startDate: today, endDate: null, monthlySalary: 0, integralSalaryPercentage: null, bankAccountReference: null, bankOptionId: null, bankAccountTypeOptionId: null, bankAccountNumber: null, isActive: true, appliesTransportAllowance:true, rowVersion: null }); const set = <K extends keyof SaveEmployment>(key: K, value: SaveEmployment[K]) => setForm(current => ({ ...current, [key]: value })); const bankTransfer = catalog(options, "payroll-payment-method").find(item => item.optionId === form.paymentMethodOptionId)?.code === "Transfer"; async function save(event: React.FormEvent) { event.preventDefault(); setBusy(true); try {
    const result=await payrollApi.saveEmployment(form);
    toast.success("Relación laboral guardada.");
    await onSaved(result);
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el contrato."));
}
finally {
    setBusy(false);
} } return <form className="grid gap-4 sm:grid-cols-2" onSubmit={save}><Field label="Empleado"><PartyRoleSelect role="Employee" includePartyId disabled={Boolean(initial)} value={form.partyId} placeholder="Buscar empleado por nombre o identificación" selectedOption={initial?{value:initial.partyId,label:initial.employeeName,description:initial.contractNumber}:null} onChange={(value,party)=>setForm(current=>({...current,partyId:value,employeeId:party?.employeeId??null}))}/></Field><Field label="Número de contrato"><Input required value={form.contractNumber} onChange={event => set("contractNumber", event.target.value)}/></Field><CatalogField label="Tipo de contrato" options={catalog(options, "payroll-contract-type")} value={form.contractTypeOptionId} onChange={value => set("contractTypeOptionId", value)}/><CatalogField label="Tipo de salario" options={catalog(options, "payroll-salary-type")} value={form.salaryTypeOptionId} onChange={value => set("salaryTypeOptionId", value)}/><CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={form.payFrequencyOptionId} onChange={value => set("payFrequencyOptionId", value)}/><CatalogField label="Clase de riesgo" options={catalog(options, "payroll-risk-class")} value={form.riskClassOptionId} onChange={value => set("riskClassOptionId", value)}/><CatalogField label="Tipo de trabajador" options={catalog(options, "payroll-worker-type")} value={form.workerTypeOptionId} onChange={value => set("workerTypeOptionId", value)}/><CatalogField label="Subtipo" options={catalog(options, "payroll-worker-subtype")} value={form.workerSubtypeOptionId ?? ""} onChange={value => set("workerSubtypeOptionId", value || null)}/><CatalogField label="Medio de pago" options={catalog(options, "payroll-payment-method")} value={form.paymentMethodOptionId} onChange={value => set("paymentMethodOptionId", value)}/><Field label="Salario mensual"><Input required min={1} type="number" value={form.monthlySalary || ""} onChange={event => set("monthlySalary", event.currentTarget.valueAsNumber || 0)}/></Field><Field label="Porcentaje de salario integral"><Input min={0} step="any" type="number" value={form.integralSalaryPercentage ?? ""} onChange={event => set("integralSalaryPercentage", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Inicio"><DatePicker today={today} value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fin (opcional)"><DatePicker today={today} value={form.endDate ?? ""} onChange={value => set("endDate", value || null)}/></Field>{bankTransfer && <><CatalogField label="Banco" options={catalog(options, "payroll-bank")} value={form.bankOptionId ?? ""} onChange={value => set("bankOptionId", value || null)}/><CatalogField label="Tipo de cuenta" options={catalog(options, "payroll-bank-account-type")} value={form.bankAccountTypeOptionId ?? ""} onChange={value => set("bankAccountTypeOptionId", value || null)}/><Field label="Número de cuenta"><Input required maxLength={64} inputMode="numeric" autoComplete="off" value={form.bankAccountNumber ?? ""} onChange={event => set("bankAccountNumber", event.target.value.replace(/\s/g, "") || null)}/></Field></>}<label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox aria-label="Aplica auxilio de transporte" disabled={busy} checked={form.appliesTransportAllowance??true} onCheckedChange={checked=>set("appliesTransportAllowance",checked===true)}/>Aplica auxilio de transporte</label><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.isActive} onCheckedChange={checked => set("isActive", checked === true)}/>Relación activa</label><DialogFooter className="sm:col-span-2"><Button type="submit" disabled={busy || !form.employeeId || (bankTransfer && (!form.bankOptionId || !form.bankAccountTypeOptionId || !form.bankAccountNumber)) || Object.entries(form).some(([key, value]) => ["partyId", "contractTypeOptionId", "salaryTypeOptionId", "payFrequencyOptionId", "riskClassOptionId", "workerTypeOptionId", "paymentMethodOptionId"].includes(key) && !value)}>Guardar contrato</Button></DialogFooter></form>; }
function ConceptForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollConcept | null;
    onSaved: () => Promise<void>;
}) { const today = useBusinessDates().today(), option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(item => item.code === code)?.optionId ?? "" : "", [busy, setBusy] = useState(false), [form, setForm] = useState<SaveConcept>(initial ? { conceptId: initial.conceptId, code: initial.code, name: initial.name, natureOptionId: option("payroll-concept-nature", initial.natureCode), calculationMethodOptionId: option("payroll-calculation-method", initial.calculationMethodCode), treatmentOptionId: option("payroll-concept-treatment", initial.treatmentCode), dianConceptOptionId: option("payroll-dian-concept", initial.dianConceptCode) || null, accountingCategoryOptionId: option("payroll-accounting-category", initial.accountingCategoryCode), systemRoleOptionId: option("payroll-system-concept-role", initial.systemRoleCode) || null, isSalaryBase: initial.isSalaryBase, isSocialSecurityBase: initial.isSocialSecurityBase, isBenefitsBase: initial.isBenefitsBase, isTaxWithholdingBase: initial.isTaxWithholdingBase, requiresDeductionAgreement: initial.requiresDeductionAgreement, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, isActive: initial.isActive, rowVersion: initial.rowVersion } : { conceptId: crypto.randomUUID(), code: "", name: "", natureOptionId: "", calculationMethodOptionId: "", treatmentOptionId: "", dianConceptOptionId: null, accountingCategoryOptionId: "", systemRoleOptionId: null, isSalaryBase: false, isSocialSecurityBase: false, isBenefitsBase: false, isTaxWithholdingBase: false, requiresDeductionAgreement: false, effectiveFrom: today, effectiveTo: null, isActive: true, rowVersion: null }); const set = <K extends keyof SaveConcept>(key: K, value: SaveConcept[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    await payrollApi.saveConcept(form);
    toast.success("Concepto guardado.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el concepto."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Código"><Input value={form.code} onChange={event => set("code", event.target.value.toUpperCase())}/></Field><Field label="Nombre"><Input value={form.name} onChange={event => set("name", event.target.value)}/></Field><CatalogField label="Naturaleza" options={catalog(options, "payroll-concept-nature")} value={form.natureOptionId} onChange={value => set("natureOptionId", value)}/><CatalogField label="Método" options={catalog(options, "payroll-calculation-method")} value={form.calculationMethodOptionId} onChange={value => set("calculationMethodOptionId", value)}/><CatalogField label="Tratamiento" options={catalog(options, "payroll-concept-treatment")} value={form.treatmentOptionId} onChange={value => set("treatmentOptionId", value)}/><CatalogField label="Categoría contable" options={catalog(options, "payroll-accounting-category")} value={form.accountingCategoryOptionId} onChange={value => set("accountingCategoryOptionId", value)}/><CatalogField label="Concepto DIAN" options={catalog(options, "payroll-dian-concept")} value={form.dianConceptOptionId ?? ""} onChange={value => set("dianConceptOptionId", value)}/><CatalogField label="Rol del calculador" options={catalog(options, "payroll-system-concept-role")} value={form.systemRoleOptionId ?? ""} onChange={value => set("systemRoleOptionId", value)}/><div className="sm:col-span-2 grid gap-2 sm:grid-cols-2">{([['isSalaryBase', 'Base salarial'], ['isSocialSecurityBase', 'Base de seguridad social'], ['isBenefitsBase', 'Base de prestaciones'], ['isTaxWithholdingBase', 'Base de retención'], ['requiresDeductionAgreement', 'Exige acuerdo de deducción']] as Array<[
    keyof SaveConcept,
    string
]>).map(([key, label]) => <label key={key} className="flex items-center gap-2 rounded-xl border p-3 text-sm"><Checkbox checked={Boolean(form[key])} onCheckedChange={checked => set(key, (checked === true) as never)}/>{label}</label>)}</div><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.code || !form.name || !form.natureOptionId || !form.calculationMethodOptionId || !form.treatmentOptionId || !form.accountingCategoryOptionId} onClick={() => void save()}>Guardar concepto</Button></DialogFooter></div>; }
function RuleSetForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollRuleSet | null;
    onSaved: () => Promise<void>;
}) { const today = useBusinessDates().today(), definitions = catalog(options, "payroll-rule-parameter"), [busy, setBusy] = useState(false), [code, setCode] = useState(initial?.code ?? ""), [name, setName] = useState(initial?.name ?? ""), [source, setSource] = useState(initial?.sourceReference ?? ""), [effectiveFrom, setEffectiveFrom] = useState(initial?.effectiveFrom ?? today), [values, setValues] = useState<Record<string, string>>(Object.fromEntries((initial?.parameters ?? []).map(item => [item.code, String(item.numericValue)]))); async function save() { const request: SaveRuleSet = { ruleSetId: initial?.ruleSetId ?? crypto.randomUUID(), countryCode: initial?.countryCode ?? "CO", code, name, effectiveFrom, effectiveTo: initial?.effectiveTo ?? null, sourceReference: source, parameters: definitions.map(item => ({ code: item.code, numericValue: Number(values[item.code]), unitCode: item.metadataCode ?? "Value", description: item.description })), rowVersion: initial?.rowVersion ?? null }; setBusy(true); try {
    await payrollApi.saveRuleSet(request);
    toast.success("Reglas guardadas en borrador; revísalas y apruébalas.");
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible guardar las reglas."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Código"><Input value={code} onChange={event => setCode(event.target.value.toUpperCase())}/></Field><Field label="Nombre"><Input value={name} onChange={event => setName(event.target.value)}/></Field><Field label="Vigente desde"><DatePicker today={today} value={effectiveFrom} onChange={setEffectiveFrom}/></Field><Field label="Fuente normativa"><Input value={source} onChange={event => setSource(event.target.value)} placeholder="Resolución, decreto o URL verificada"/></Field><div className="sm:col-span-2 grid gap-4 sm:grid-cols-2">{definitions.map(item => <Field key={item.optionId} label={`${item.label} · ${item.metadataCode ?? "Valor"}`}><Input type="number" step="any" value={values[item.code] ?? ""} onChange={event => setValues(current => ({ ...current, [item.code]: event.target.value }))}/></Field>)}</div><DialogFooter className="sm:col-span-2"><Button disabled={busy || !code || !name || !source || definitions.some(item => values[item.code] === undefined || values[item.code] === "")} onClick={() => void save()}>Guardar borrador</Button></DialogFooter></div>; }
function DeductionAgreementForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollDeductionAgreement | null;
    onSaved:(result:PayrollDeductionAgreement)=>void;
}) { const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveDeductionAgreement>(initial ? { deductionAgreementId: initial.deductionAgreementId, employmentId: initial.employmentId, conceptId: initial.conceptId, authorityOptionId: initial.authorityOptionId, beneficiaryPartyId: initial.beneficiaryPartyId, referenceNumber: initial.referenceNumber, evidenceUrl: initial.evidenceUrl, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, authorizedTotal: initial.authorizedTotal, installmentAmount: initial.installmentAmount, priority: initial.priority, mustProtectMinimumNetPay: initial.mustProtectMinimumNetPay, isActive: initial.isActive, rowVersion: initial.rowVersion } : { deductionAgreementId: crypto.randomUUID(), employmentId: "", conceptId: "", authorityOptionId: "", beneficiaryPartyId: null, referenceNumber: "", evidenceUrl: "", effectiveFrom: today, effectiveTo: null, authorizedTotal: null, installmentAmount: null, priority: 100, mustProtectMinimumNetPay: true, isActive: true, rowVersion: null }); const set = <K extends keyof SaveDeductionAgreement>(key: K, value: SaveDeductionAgreement[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    onSaved(await payrollApi.saveDeductionAgreement(form));
    toast.success("Descuento autorizado guardado.");
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el descuento."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador"><PayrollEmploymentSelect value={form.employmentId} onChange={value=>set("employmentId",value)}/></Field><Field label="Concepto"><Select value={form.conceptId} onValueChange={value => set("conceptId", value)}><SelectTrigger aria-label="Concepto"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive && item.requiresDeductionAgreement).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Autoridad" options={catalog(options, "payroll-deduction-authority")} value={form.authorityOptionId} onChange={value => set("authorityOptionId", value)}/><Field label="Referencia"><Input maxLength={100} value={form.referenceNumber} onChange={event => set("referenceNumber", event.target.value)}/></Field><Field label="Evidencia (URL)"><Input type="url" maxLength={1000} value={form.evidenceUrl} onChange={event => set("evidenceUrl", event.target.value)}/></Field><Field label="Vigente desde"><DatePicker today={today} value={form.effectiveFrom} onChange={value => set("effectiveFrom", value)}/></Field><Field label="Vigente hasta"><DatePicker today={today} value={form.effectiveTo ?? ""} onChange={value => set("effectiveTo", value || null)}/></Field><Field label="Total autorizado"><Input type="number" min={0.01} step="any" value={form.authorizedTotal ?? ""} onChange={event => set("authorizedTotal", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Cuota"><Input type="number" min={0.01} step="any" value={form.installmentAmount ?? ""} onChange={event => set("installmentAmount", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Prioridad"><Input type="number" min={1} max={999} value={form.priority} onChange={event => set("priority", event.currentTarget.valueAsNumber || 1)}/></Field><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.mustProtectMinimumNetPay} onCheckedChange={checked => set("mustProtectMinimumNetPay", checked === true)}/>Proteger mínimo neto</label><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.employmentId || !form.conceptId || !form.authorityOptionId || !form.referenceNumber || !form.evidenceUrl} onClick={() => void save()}>Guardar autorización</Button></DialogFooter></div>; }
function NoveltyForm({ options, initial, preset, onSaved }: {
    options: PayrollOptions;
    initial:PayrollNovelty|null;
    preset:{employmentId:string;employeeName:string;startDate:string;endDate:string}|null;
    onSaved: (result:PayrollNovelty) => void;
}) { const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SavePayrollNovelty>(initial ? {...initial,reasonId:initial.reasonId??null,rowVersion:initial.rowVersion} : { noveltyId: crypto.randomUUID(), employmentId: preset?.employmentId??"", conceptId: "", noveltyTypeOptionId: "", reasonId: null, deductionAgreementId: null, startDate: preset?.startDate??today, endDate: preset?.endDate??today, quantity: 0, unitAmount: null, totalAmount: 0, notes: null, evidenceUrl: null }); const set = <K extends keyof SavePayrollNovelty>(key: K, value: SavePayrollNovelty[K]) => setForm(current => ({ ...current, [key]: value })); const agreements = options.deductionAgreements.filter(item => item.employmentId === form.employmentId && item.conceptId === form.conceptId && item.isActive), selectedConcept = options.concepts.find(item => item.conceptId === form.conceptId), method = selectedConcept?.calculationMethodCode, needsRate = method === "QuantityByRate" || method === "PercentageOfBase", zeroQuantityAllowed = method === "FixedAmount" && catalog(options, "payroll-novelty-type").find(option => option.optionId === form.noveltyTypeOptionId)?.code === "Amount"; async function save() { setBusy(true); try {
    onSaved(await payrollApi.saveNovelty(form));
}
catch (error) {
    toast.error(message(error, "No fue posible guardar la novedad."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador">{initial || preset ? <Input value={initial?.employeeName??preset?.employeeName??""} readOnly/> : <PayrollEmploymentSelect value={form.employmentId} onChange={value=>setForm(current => ({ ...current, employmentId: value, deductionAgreementId: null }))}/>}</Field><Field label="Concepto"><Select value={form.conceptId} onValueChange={value => setForm(current => ({ ...current, conceptId: value, deductionAgreementId: null, unitAmount: null, totalAmount: 0 }))}><SelectTrigger aria-label="Concepto"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Tipo de novedad" options={catalog(options, "payroll-novelty-type")} value={form.noveltyTypeOptionId} onChange={value => set("noveltyTypeOptionId", value)}/><Field label="Acuerdo de descuento (si aplica)"><Select value={form.deductionAgreementId ?? "_none"} onValueChange={value => set("deductionAgreementId", value === "_none" ? null : value)}><SelectTrigger aria-label="Acuerdo de descuento (si aplica)"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="_none">No aplica</SelectItem>{agreements.map(item => <SelectItem key={item.deductionAgreementId} value={item.deductionAgreementId}>{item.referenceNumber}</SelectItem>)}</SelectContent></Select></Field><Field label="Inicio"><DatePicker today={today} value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fin"><DatePicker today={today} value={form.endDate} onChange={value => set("endDate", value)}/></Field><Field label="Cantidad"><Input type="number" min={zeroQuantityAllowed ? 0 : 0.000001} step="any" value={form.quantity} onChange={event => set("quantity", event.currentTarget.valueAsNumber || 0)}/></Field><Field label={method === "PercentageOfBase" ? "Tarifa decimal" : "Valor unitario"}><Input type="number" min={0} step="any" value={form.unitAmount ?? ""} onChange={event => set("unitAmount", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label={needsRate ? "Total calculado por el motor" : "Valor total"}><Input type="number" min={0} step="any" disabled={needsRate} value={form.totalAmount} onChange={event => set("totalAmount", event.currentTarget.valueAsNumber || 0)}/></Field><Field label="Evidencia (URL)"><Input type="url" value={form.evidenceUrl ?? ""} onChange={event => set("evidenceUrl", event.target.value || null)}/></Field><label className="space-y-2 sm:col-span-2"><span className="block text-sm font-medium">Notas</span><Input maxLength={500} value={form.notes ?? ""} onChange={event => set("notes", event.target.value || null)}/></label><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.employmentId || !form.conceptId || !form.noveltyTypeOptionId || (form.quantity < 0 || (form.quantity === 0 && !zeroQuantityAllowed)) || form.endDate < form.startDate || (selectedConcept?.requiresDeductionAgreement && !form.deductionAgreementId) || (needsRate && form.unitAmount === null)} onClick={() => void save()}>{busy ? "Guardando novedad…" : initial ? "Guardar novedad" : "Registrar novedad"}</Button></DialogFooter></div>; }
function PaymentForm({ options, runs, selectedRun, loading, error, onRunChange, onRetry, onSaved }: {
    options: PayrollOptions; runs: PayrollRunSummary[]; selectedRun: PayrollRun|null;
    loading: boolean; error: boolean; onRunChange:(id:string)=>void; onRetry:()=>void;
    onSaved:(result:PayrollPaymentBatch)=>void;
}) {
    const tenantId=useTenantContextStore(state=>state.selectedTenantId), businessId=useBusinessContextStore(state=>state.selectedBusinessId);
    const today = useBusinessDates(businessId).today();
    const [runId,setRunId]=useState(""),[form,setForm]=useState<PaymentMethod|null>(null),[date,setDate]=useState(""),
      [reference,setReference]=useState(""),[busy,setBusy]=useState(false),[paymentBatchId]=useState(()=>crypto.randomUUID());
    const methodId=catalog(options,"payroll-payment-method").find(option=>option.code===form?.methodCode)?.optionId??"";
    const current=selectedRun?.payrollRunId===runId?selectedRun:null;
    const employees=(current?.payableEmployees??[]).filter(employee=>employee.paymentMethodOptionId===methodId);
    const total=employees.reduce((sum,employee)=>sum+employee.amount,0);
    const invalidDestination=employees.some(employee=>!employee.bankDestinationComplete);
    async function save(){
      if(busy||loading||error||!current||!employees.length||invalidDestination||!form||!methodId)return;
      setBusy(true);
      try{
        const result=await payrollApi.createPayment({paymentBatchId,payrollRunId:runId,paymentMethodOptionId:methodId,paymentMethodId:form.paymentMethodId,paymentDate:date,referenceNumber:reference});
        onSaved(result);toast.success("Grupo de pago confirmado y enviado a contabilidad.");
      }catch(error){toast.error(message(error,"No fue posible confirmar el pago."));}
      finally{setBusy(false);}
    }
    return <div className="grid gap-4">
      <Field label="Liquidación"><Select value={runId} disabled={busy} onValueChange={id=>{setRunId(id);onRunChange(id);}}><SelectTrigger aria-label="Liquidación"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{runs.filter(run=>run.status==="Approved").map(run=><SelectItem key={run.payrollRunId} value={run.payrollRunId}>{run.periodStart} — {run.periodEnd} · Neto liquidado {money.format(run.netPayable)}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Forma de pago"><PagedEntitySelect<PaymentMethod> ariaLabel="Forma de pago" value={form?.paymentMethodId??""}
        queryKey={["payment-method-choices",tenantId,businessId,"Payroll"]} disabled={busy}
        selectedOption={form?{value:form.paymentMethodId,label:form.name,description:`${form.accountCode} · ${form.accountName}`}:null}
        searchPlaceholder="Buscar forma de pago…" emptyMessage="No hay formas habilitadas para nómina."
        loadPage={async(search,page,pageSize)=>{const result=await paymentMethodsApi.list({usage:"Payroll",search,page,pageSize});
          return {...result,totalPages:Math.ceil(result.totalCount/result.pageSize)};}}
        getOption={item=>({value:item.paymentMethodId,label:item.name,description:`${item.code} · ${item.accountCode} · ${item.accountName}`})}
        onChange={(_id,_option,item)=>{if(item)setForm(item);}}/></Field>
      {form&&<p className="text-sm">Cuenta: {form.accountCode} · {form.accountName}. Se usará la cuenta vigente de esta forma al contabilizar.</p>}
      {runId&&loading?<p role="status">Consultando trabajadores pendientes…</p>:runId&&error?<p role="alert">No fue posible consultar la liquidación. <Button variant="link" onClick={onRetry}>Reintentar</Button></p>:runId&&current&&<section aria-label="Trabajadores del grupo de pago" className="space-y-2 rounded-xl border p-3">
        <div className="flex items-center justify-between gap-3"><p className="font-medium">{employees.length} {employees.length===1?"trabajador":"trabajadores"} · {money.format(total)}</p><Button type="button" variant="outline" size="sm" disabled={busy||loading} onClick={onRetry}>Actualizar pendientes</Button></div>
        {!methodId?<p className="text-sm text-muted-foreground">Selecciona la forma de pago para consultar su grupo.</p>:employees.length===0?<p className="text-sm text-muted-foreground">No hay trabajadores pendientes compatibles. La causación debe estar contabilizada para pagar.</p>:<ul className="max-h-56 space-y-2 overflow-y-auto text-sm">{employees.map(employee=><li key={employee.payrollRunEmployeeId} className="flex justify-between gap-3"><span>{employee.employeeName}</span><b>{money.format(employee.amount)}</b></li>)}</ul>}
        {invalidDestination&&<p role="alert" className="text-sm text-destructive">Un trabajador del grupo no tiene destino bancario completo en la liquidación.</p>}
        <p className="text-xs text-muted-foreground">Se confirma el neto completo de cada integrante. Los demás grupos conservan sus valores pendientes. El grupo se envía a contabilidad; no envía dinero al banco.</p>
      </section>}
      <Field label="Fecha de pago"><DatePicker today={today} disabled={busy} value={date} onChange={setDate}/></Field>
      <Field label="Referencia bancaria o de caja"><Input disabled={busy} maxLength={100} value={reference} onChange={event=>setReference(event.target.value)}/></Field>
      <DialogFooter><Button disabled={busy||loading||error||!current||!employees.length||invalidDestination||!methodId||!date||!reference.trim()} onClick={()=>void save()}>Confirmar pago del grupo</Button></DialogFooter>
    </div>;
}
function PayrollAccountingMappings({ options, canConfigure }: {
    options: PayrollOptions;
    canConfigure: boolean;
}) { const tenantId = useTenantContextStore(state => state.selectedTenantId ?? ""), businessId = useBusinessContextStore(state => state.selectedBusinessId), [accounts, setAccounts] = useState<AccountingAccount[]>([]), [definitions, setDefinitions] = useState<AccountingCategoryDefinition[]>([]), [mappings, setMappings] = useState<AccountingMapping[]>([]), [busy, setBusy] = useState<string | null>(null), [error, setError] = useState(""); const loadAccounting = useCallback(async () => { try {
    const [nextAccounts, nextDefinitions, nextMappings] = await Promise.all([accountingApi.accounts(), accountingApi.categoryDefinitions(), accountingApi.mappings()]);
    setAccounts(nextAccounts);
    setDefinitions(nextDefinitions);
    setMappings(nextMappings);
    setError("");
}
catch (cause) {
    setError(message(cause, "No fue posible consultar la configuración contable."));
} }, []); useEffect(() => { void loadAccounting(); }, [loadAccounting, tenantId, businessId]); const categories = catalog(options, "payroll-accounting-category").map(option => ({ option, definition: definitions.find(item => item.category === option.code) })); async function save(category: string, accountId: string) { if (!tenantId || !businessId)
    return; setBusy(category); try {
    await accountingApi.setMapping({ tenantId, businessId, category, accountId, effectiveFrom: "2000-01-01", effectiveTo: null });
    toast.success("Cuenta de nómina actualizada.");
    await loadAccounting();
}
catch (cause) {
    toast.error(message(cause, "No fue posible guardar la cuenta."));
}
finally {
    setBusy(null);
} } return <Card className="rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Integración contable de nómina</CardTitle><CardDescription>Las categorías nacen del catálogo de nómina y las cuentas del plan contable. La liquidación aprobada y el pago usan estas asignaciones.</CardDescription></CardHeader><CardContent className="space-y-3"><div className="rounded-2xl border bg-muted/30 p-4 text-sm"><Landmark className="mb-2 h-5 w-5 text-teal-700"/><b>Centros de costo</b><p className="mt-1 text-muted-foreground">La nómina y sus pagos usan la regla de todas las operaciones o el centro predeterminado de esta sede. El centro queda guardado en cada asiento y se conserva al reintentar. Consulta el gasto y sus aportes en el auxiliar contable, filtrando por centro.</p><Button asChild variant="link" className="mt-2 h-auto p-0"><Link href="/dashboard/accounting?section=centers">Ver centros y asignaciones <ExternalLink className="ml-2 h-3 w-3"/></Link></Button></div>{error && <p className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">{error} <Link className="underline" href="/dashboard/accounting">Abrir Contabilidad</Link></p>}{categories.map(({ option, definition }) => { const mapping = mappings.find(item => item.category === option.code && item.businessId === businessId) ?? mappings.find(item => item.category === option.code && item.businessId === null); return <div key={option.optionId} className="grid gap-2 rounded-xl border p-3 md:grid-cols-[minmax(220px,1fr)_minmax(260px,1.4fr)] md:items-center"><div><b className="text-sm">{option.label}</b><p className="text-xs text-muted-foreground">{option.code}</p></div><Select disabled={!canConfigure || busy !== null || Boolean(error)} value={mapping?.accountId ?? ""} onValueChange={value => void save(option.code, value)}><SelectTrigger aria-label={`Cuenta para ${option.label}`}><SelectValue placeholder="Selecciona una cuenta"/></SelectTrigger><SelectContent>{accounts.filter(account => account.isActive && account.allowsPosting && (!definition || account.accountType === definition.accountType)).map(account => <SelectItem key={account.accountId} value={account.accountId}>{account.code} · {account.name}</SelectItem>)}</SelectContent></Select></div>; })}{!categories.length && <Empty text="No hay categorías contables de nómina activas."/>}</CardContent></Card>; }
function ElectronicPeriodForm({ businessId, onSaved }: {
    businessId: string;
    onSaved: () => Promise<void>;
}) { const today = useBusinessDates(businessId).today(); const [period, setPeriod] = useState(today.slice(0, 7)), [busy, setBusy] = useState(false); async function save() { const [year, month] = period.split("-").map(Number); setBusy(true); try {
    const result = await payrollApi.generateElectronicPeriod({ electronicPeriodId: crypto.randomUUID(), businessId, year, month });
    toast.success(`${result.documents.length} documentos enviados a procesamiento fiscal.`);
    await onSaved();
}
catch (error) {
    toast.error(message(error, "No fue posible generar la nómina electrónica."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4"><Field label="Mes a reportar"><DatePicker today={today} value={period ? `${period}-01` : ""} onChange={value => setPeriod(value.slice(0, 7))}/></Field><DialogFooter><Button disabled={busy || !period} onClick={() => void save()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar y enviar</Button></DialogFooter></div>; }
function CatalogField({ label, options, value, onChange }: {
    label: string;
    options: PayrollCatalogOption[];
    value: string;
    onChange: (value: string) => void;
}) { return <Field label={label}><Select value={value} onValueChange={onChange}><SelectTrigger aria-label={label}><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.filter(item => item.isActive).map(item => <SelectItem key={item.optionId} value={item.optionId}>{item.label}</SelectItem>)}</SelectContent></Select></Field>; }
function catalogLabel(options: PayrollOptions | null, catalogCode: string, code: string) { return options?.catalogs[catalogCode]?.find(item => item.code === code)?.label ?? code; }
function statusLabel(options: PayrollOptions | null, code: string) { return catalogLabel(options, "payroll-workflow-status", code); }
function catalog(options: PayrollOptions, code: string) { return options.catalogs[code] ?? []; }
function Field({ label, children }: {
    label: string;
    children: React.ReactNode;
}) { return <label className="block space-y-2"><span className="block text-sm font-medium">{label}</span>{children}</label>; }
function Metric({ label, value }: {
    label: string;
    value: number;
}) { return <div className="rounded-xl border bg-muted/20 p-3"><p className="text-xs text-muted-foreground">{label}</p><b>{money.format(value)}</b></div>; }
function Empty({ text }: {
    text: string;
}) { return <p className="p-6 text-center text-sm text-muted-foreground">{text}</p>; }
function message(error: unknown, fallback: string) { return error && typeof error === "object" && "message" in error ? String(error.message) : fallback; }
