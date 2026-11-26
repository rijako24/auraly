"use client";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useMediaQuery } from "@/hooks/use-media-query";
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
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { accountingStatusLabel, businessStatusLabel } from "@/lib/accounting-labels";
import { ReportViewer } from "@/components/reports/report-viewer";
import { payrollReceiptColumns, payrollReceiptRows } from "@/lib/payroll-receipt";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { paymentMethodsApi, type PaymentMethod } from "@/services/api/payment-methods";
import { PayrollRunNovelties } from "@/components/payroll/payroll-run-novelties";
import { PayrollEmploymentConfigurationFields, PayrollEmploymentDetailsFields, emptyPayrollConfiguration } from "@/components/payroll/payroll-employment-configuration";
import { AccountSelect } from "@/components/accounting/account-select";
import { CostCenterSelect } from "@/components/accounting/cost-center-select";
import { PayrollProfiles } from "@/components/payroll/payroll-profiles";
import { PayrollRunSelection } from "@/components/payroll/payroll-run-selection";
import { PayrollEmploymentSelect } from "@/components/payroll/payroll-employment-select";
import { accountingApi, type AccountingCategoryDefinition, type AccountingMapping } from "@/services/api/accounting";
import { payrollApi, type CalculatePayrollRun, type PayrollRunEmploymentPage, type PayrollCatalogOption, type PayrollConcept, type PayrollDeductionAgreement, type PayrollEmployment, type PayrollEmploymentPage, type PayrollOptions, type PayrollNovelty, type PayrollReportDefinition, type PayrollRun, type PayrollRunSummary, type PayrollRunPage, type PayrollPaymentBatch, type PayrollHistoryPage, type ElectronicPayrollPeriod, type SaveConcept, type SaveDeductionAgreement, type SaveEmployment, type SavePayrollNovelty } from "@/services/api/payroll";
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
    const [navigationReady,setNavigationReady]=useState(false);
    const selectedTab=useRef<HTMLButtonElement>(null);
    const narrowTabs=useMediaQuery("(max-width: 1023px)");
    useEffect(()=>{selectedTab.current?.scrollIntoView({block:"nearest",inline:"nearest"});},[section,narrowTabs]);
    const [selectedWorkerId,setSelectedWorkerId]=useState<string|null>(null);
    const [creatingPayment, setCreatingPayment] = useState(false),[paying,setPaying]=useState(false);
    const detailVisible = section === "runs" || section === "novelties" && Boolean(selectedWorkerId) || creatingPayment;
    const workspace = useQuery({ queryKey: ["payroll-workspace", tenantId, businessId], queryFn: async () => {
        return {options:await payrollApi.options()};
    },enabled:navigationReady,staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false });
    const workerScope=section==="novelties"?selectedWorkerId:null;
    const detailKey=["payroll-run",tenantId,businessId,selectedId,...(workerScope?[workerScope]:[])];
    const detail = useQuery({ queryKey: detailKey, queryFn: () => payrollApi.run(selectedId!,workerScope??undefined), enabled: Boolean(selectedId) && detailVisible,
      refetchOnWindowFocus:false,refetchOnReconnect:false });
    const options = workspace.data?.options ?? null, selected = detail.data ?? null, loading = workspace.isFetching;
    const [creatingRun, setCreatingRun] = useState(false), [creatingEmployment, setCreatingEmployment] = useState(false), [editingEmployment, setEditingEmployment] = useState<PayrollEmployment | null>(null), [preselectedPartyId, setPreselectedPartyId] = useState<string | null>(null), [creatingConcept, setCreatingConcept] = useState(false), [editingConcept, setEditingConcept] = useState<PayrollConcept | null>(null), [creatingElectronic, setCreatingElectronic] = useState(false), [creatingAgreement, setCreatingAgreement] = useState(false), [editingAgreement, setEditingAgreement] = useState<PayrollDeductionAgreement | null>(null);
    const [noveltyEditorState,setNoveltyEditorState]=useState<{
      initial:PayrollNovelty|null;
      preset:{employmentId:string;employeeName:string;startDate:string;endDate:string}|null;
    }|null>(null);
    const editingNovelty=noveltyEditorState?.initial??null,noveltyPreset=noveltyEditorState?.preset??null;
    const creatingNovelty=noveltyEditorState!==null && editingNovelty===null;
    const [voidingNovelty,setVoidingNovelty]=useState(false);
    const [calculationErrors,setCalculationErrors]=useState<Record<string,Record<string,string>>>({});
    function closeNoveltyEditor(){setNoveltyEditorState(null);}
    const calculationKey=["payroll-calculation",tenantId,businessId];
    const calculationPending=useIsMutating({mutationKey:calculationKey})>0;
    const calculation=useMutation({mutationKey:calculationKey,
      mutationFn:({id,request}:{id:string;request:CalculatePayrollRun})=>payrollApi.calculateRun(id,request),onSuccess:(result,variables)=>runCalculated(result,variables.request.rowVersion)});
    async function calculateRun(id:string,request:CalculatePayrollRun){
      if(queryClient.isMutating({mutationKey:calculationKey})>0)throw new Error("Espera a que termine el cálculo en curso.");
      return calculation.mutateAsync({id,request});
    }
    const subscription = useTenantCommercialSubscription(creatingElectronic && permissions.has("payroll.fiscal"));
    const dianRemaining = subscription.data ? Math.max(0, subscription.data.dianDocumentMonthlyLimit - subscription.data.dianDocumentsUsed) : null;
    const openElectronic = () => {
        setCreatingElectronic(true);
    };
    async function reloadWorkspaceAndRun() {
      await Promise.all([workspace.refetch(),queryClient.invalidateQueries({queryKey:["payroll-runs",tenantId,businessId]}),queryClient.invalidateQueries({queryKey:["payroll-run-select",tenantId,businessId],refetchType:"none"}), ...(selectedId ? [detailVisible ? detail.refetch()
        : queryClient.invalidateQueries({queryKey:["payroll-run",tenantId,businessId,selectedId],refetchType:"none"})] : [])]);
    }
    async function noveltySaved(result:PayrollNovelty) {
      queryClient.setQueryData<PayrollRun>(detailKey,current=>{
        if(!current)return current;
        return {...current,employees:current.employees.map(employee=>{
          if(employee.employmentId!==result.employmentId)return employee;
          const previous=employee.novelties??[];
          const novelties=previous.some(item=>item.noveltyId===result.noveltyId)
            ? previous.map(item=>item.noveltyId===result.noveltyId?result:item) : [result,...previous];
          return {...employee,novelties};
        })};
      });
      // The accepted capture belongs to this scope. A second command must not
      // inherit headers from a different workspace selected while it was saving.
      if(useTenantContextStore.getState().selectedTenantId!==tenantId ||
          useBusinessContextStore.getState().selectedBusinessId!==businessId)return;
      const savedLabel=result.status==="Voided"?"Novedad retirada":"Novedad guardada";
      const current=selectedId?queryClient.getQueryData<PayrollRun>(detailKey):null;
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
        setCalculationErrors(previous=>({...previous,[current.payrollRunId]:{
          ...previous[current.payrollRunId],[result.employmentId]:message(error,"No fue posible actualizar el cálculo del trabajador.")
        }}));
        toast.error(`${savedLabel}. No fue posible actualizar el cálculo: ${message(error,"Reintenta el cálculo del trabajador antes de contabilizar.")}`);
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
      queryClient.setQueriesData<PayrollHistoryPage<PayrollDeductionAgreement>>({queryKey:["payroll-agreements",tenantId,businessId]},current=>current?{...current,items:current.items.map(row=>row.deductionAgreementId===result.deductionAgreementId?result:row)}:current);
      void queryClient.invalidateQueries({queryKey:["payroll-agreements",tenantId,businessId]});
      void queryClient.invalidateQueries({queryKey:["payroll-agreement-select",tenantId,businessId],refetchType:"none"});
      queryClient.setQueryData(["payroll-agreement-value",tenantId,businessId,result.deductionAgreementId],result);
    }
    async function load() { await Promise.all([reloadWorkspaceAndRun(),...(["payroll-agreements","payroll-payments","payroll-electronic-periods","payroll-electronic-documents"].map(key=>queryClient.invalidateQueries({queryKey:[key,tenantId,businessId]}))), queryClient.invalidateQueries({ queryKey: ["payroll-employments", tenantId, businessId] }), queryClient.invalidateQueries({ queryKey: ["payroll-run-employments", tenantId, businessId] }),queryClient.invalidateQueries({queryKey:["payroll-calculated-employees",tenantId,businessId],refetchType:section==="novelties"&&!selectedWorkerId?"active":"none"})]); }
    async function selectRun(id: string) { closeNoveltyEditor(); setSelectedId(id); }
    function conceptSaved(result:PayrollConcept){
      queryClient.setQueryData<{options:PayrollOptions}>(["payroll-workspace",tenantId,businessId],current=>current?{
        ...current,options:{...current.options,concepts:[...current.options.concepts.filter(item=>item.conceptId!==result.conceptId),result]
          .sort((left,right)=>Number(right.isActive)-Number(left.isActive)||left.name.localeCompare(right.name,"es")||left.code.localeCompare(right.code,"es"))}
      }:current);
    }
    function settingsSaved(settings:PayrollOptions["settings"]){
      queryClient.setQueryData<{options:PayrollOptions}>(["payroll-workspace",tenantId,businessId],current=>current?{...current,options:{...current.options,settings}}:current);
    }
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
    function runCalculated(result:PayrollRun,baseVersion?:string){
      setCalculationErrors(previous=>{
        const errors=previous[result.payrollRunId];if(!errors)return previous;
        const calculatedIds=new Set(result.employees.map(employee=>employee.employmentId));
        const remaining=result.isPartialCalculation?Object.fromEntries(Object.entries(errors).filter(([id])=>!calculatedIds.has(id))):{};
        const next={...previous};
        if(Object.keys(remaining).length)next[result.payrollRunId]=remaining;else delete next[result.payrollRunId];
        return next;
      });
      const key=["payroll-run",tenantId,businessId,result.payrollRunId];
      let updated=result;
      if(result.isPartialCalculation){
        const previous=queryClient.getQueryData<PayrollRun>(key);
        const changed=new Map(result.employees.map(employee=>[employee.employmentId,employee]));
        const previousIds=new Set(previous?.employees.map(employee=>employee.employmentId));
        if(previous && (previous.isPartialCalculation || previous.businessId!==result.businessId ||
            previous.employees.length!==result.employeeCount || changed.size!==result.employees.length ||
            [...changed.keys()].some(id=>!previousIds.has(id))))
          throw new Error("El detalle cambió durante el cálculo. Actualiza la liquidación antes de continuar.");
        if(previous&&previous.rowVersion===baseVersion)updated={...result,isPartialCalculation:false,employees:previous.employees.map(employee=>changed.get(employee.employmentId)??employee)};
        else if(previous)void queryClient.invalidateQueries({queryKey:key,exact:true,refetchType:"none"});
      }
      if(!updated.isPartialCalculation)queryClient.setQueryData(key,updated);
      for(const worker of result.employees)queryClient.setQueryData([...key,worker.employmentId],{...result,isPartialCalculation:true,employees:[worker]});
      queryClient.setQueriesData<import("@/services/api/payroll").PayrollCalculatedEmployeePage>({queryKey:["payroll-calculated-employees",tenantId,businessId]},current=>current?{
        ...current,items:current.items.map(row=>{const worker=result.payrollRunId===row.payrollRunId?result.employees.find(item=>item.employmentId===row.employmentId):null;
          return result.payrollRunId===row.payrollRunId?{...row,rowVersion:result.rowVersion,status:result.status,...(worker?{payrollRunEmployeeId:worker.payrollRunEmployeeId,workedDays:worker.workedDays,earnings:worker.earnings,deductions:worker.deductions,netPayable:worker.netPayable}:{})}:row;})}:current);
      const selected=new Set(updated.employees.map(employee=>employee.employmentId));
      queryClient.setQueriesData<PayrollRunEmploymentPage>({queryKey:["payroll-run-employments",tenantId,businessId,result.payrollRunId]},current=>current?{
        ...current,rowVersion:result.rowVersion,selectedCount:selected.size,
        items:result.isPartialCalculation?current.items:current.items.map(item=>({...item,isSelected:selected.has(item.employmentId)})),
      }:current);
      queryClient.setQueriesData<PayrollRunPage>({queryKey:["payroll-runs",tenantId,businessId]},current=>current?{
        ...current,items:current.items.map(row=>row.payrollRunId===result.payrollRunId?{
          ...row,status:result.status,employeeCount:result.employeeCount,totalEarnings:result.totalEarnings,
          totalDeductions:result.totalDeductions,netPayable:result.netPayable,rowVersion:result.rowVersion}:row)
      }:current);
    }
    useEffect(() => { const query = new URLSearchParams(window.location.search), requested = query.get("section") as Section | null, partyId = query.get("partyId"); if (requested && ["runs", "employments", "novelties", "payments", "electronic", "reports", "configuration"].includes(requested))
        setSection(requested); if (partyId) {
        setPreselectedPartyId(partyId);
        setSection("employments");
        setCreatingEmployment(permissions.has("payroll.manage"));
    } setNavigationReady(true);}, [permissions]);
    return <div className="space-y-5 [&_table]:tabular-nums">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between"><div><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">Nómina Auraly</p><h1 className="mt-2 text-3xl font-black">Nómina clara y conectada</h1><p className="mt-2 max-w-2xl text-sm text-cyan-50/80">Liquida, registra los pagos y envía la nómina electrónica. Todo conectado con tu contabilidad.</p></div>
    {section === "runs" && permissions.has("payroll.calculate") && <Button className="h-12 bg-white text-slate-950 hover:bg-cyan-50" disabled={!options || workspace.isError} onClick={() => setCreatingRun(true)}><FilePlus2 className="mr-2 h-4 w-4"/>Nueva liquidación</Button>}
    {section === "employments" && permissions.has("payroll.manage") && <Button className="h-12 bg-white text-slate-950 hover:bg-cyan-50" disabled={!options || workspace.isError} onClick={() => setCreatingEmployment(true)}><Users className="mr-2 h-4 w-4"/>Nuevo contrato</Button>}</div></header>
    <Tabs value={section} onValueChange={value => { if(value!=="novelties"){setSelectedWorkerId(null);closeNoveltyEditor();} if(value === "novelties" && selected?.status === "Draft")setSelectedId(null); setSection(value as Section); const url = new URL(window.location.href); url.searchParams.set("section", value); url.searchParams.delete("partyId"); window.history.replaceState(null, "", url); }} className="space-y-5">
    <div className="flex min-w-0 gap-2"><div className="min-w-0 flex-1 overflow-x-auto rounded-2xl border bg-card p-1"><TabsList aria-label="Secciones de nómina" className="h-auto w-max justify-start gap-1 bg-transparent p-0">{([
      ["runs", "Liquidaciones", Calculator], ["employments", "Trabajadores", Users], ["novelties", "Novedades", ListChecks], ["payments", "Pagos", Wallet], ["electronic", "Electrónica DIAN", FileText], ["reports", "Reportes", BarChart3], ["configuration", "Configuración", Settings2]
    ] as const).map(([value, label, Icon]) => <TabsTrigger key={value} ref={value===section?selectedTab:undefined} value={value} className="shrink-0 rounded-xl px-4 py-2.5 font-semibold data-[state=active]:bg-slate-950 data-[state=active]:text-white data-[state=active]:shadow"><Icon className="mr-2 h-4 w-4"/>{label}</TabsTrigger>)}</TabsList></div><Button aria-label="Actualizar nómina" variant="outline" className="h-12 rounded-2xl" disabled={loading || calculationPending} onClick={() => void load()}><RefreshCw className={loading ? "h-4 w-4 animate-spin" : "h-4 w-4"}/></Button></div>
    {workspace.isPending ? <div aria-label="Cargando nómina" className="grid gap-5 lg:grid-cols-2"><Skeleton className="h-72 rounded-3xl"/><Skeleton className="h-72 rounded-3xl"/></div> : workspace.isError ? <Card className="rounded-3xl"><CardContent className="space-y-3 p-6" role="alert"><p>{message(workspace.error, "No fue posible cargar la nómina.")}</p><Button variant="outline" onClick={() => void load()}>Reintentar</Button></CardContent></Card> : <TabsContent value={section} className="mt-0">
    {detail.isError && section === "runs" && <div role="alert" className="mb-4 rounded-2xl border border-destructive/30 p-4 text-sm">{message(detail.error, "No fue posible abrir la liquidación.")} <Button variant="outline" size="sm" onClick={() => void detail.refetch()}>Reintentar detalle</Button></div>}
    {detail.isFetching && section === "runs" && <p role="status" className="mb-3 text-sm text-muted-foreground">Cargando detalle…</p>}
    {section === "runs" && (
      <RunsSection options={options} selected={selected} canCalculate={permissions.has("payroll.calculate")} calculationDisabled={detail.isFetching || detail.isError} calculating={calculationPending} canApprove={permissions.has("payroll.approve") && !detail.isFetching && !detail.isError} onSelect={selectRun} onCalculate={calculateRun} onChanged={async()=>{const result=await detail.refetch();if(result.isError)throw new Error("La aprobación se guardó. No fue posible actualizar su detalle; pulsa Actualizar nómina.");if(result.data)runCalculated(result.data);await queryClient.invalidateQueries({queryKey:["payroll-run-select",tenantId,businessId],refetchType:"none"});}}/>
    )}
    {section === "employments" && (
      <EmploymentsSection canManage={permissions.has("payroll.manage")} onEdit={setEditingEmployment} onSaved={employmentSaved}/>
    )}
    {section === "novelties" && <div className="space-y-5">
      <PayrollRunNovelties run={workerScope?selected:null} employmentId={selectedWorkerId} loading={detailVisible&&detail.isFetching}
        onOpen={(runId,employmentId)=>{closeNoveltyEditor();setSelectedId(runId);setSelectedWorkerId(employmentId);}}
        onBack={()=>{closeNoveltyEditor();setSelectedWorkerId(null);}}
        error={Boolean(workerScope)&&detail.isError} canCalculate={permissions.has("payroll.calculate")} calculating={calculationPending}
        calculationErrors={calculationErrors[selectedId??""]??{}}
        natures={options?catalog(options,"payroll-concept-nature"):[]} onRetry={()=>void detail.refetch()} onCalculate={calculateRun}
        canManage={permissions.has("payroll.manage")} voiding={voidingNovelty}
        editorEmploymentId={editingNovelty?.employmentId??noveltyPreset?.employmentId??null} onCloseEditor={closeNoveltyEditor}
        noveltyEditor={options && (creatingNovelty || editingNovelty) && <section aria-label="Edición de novedad" className="space-y-4 rounded-xl border bg-muted/30 p-4">
          <div className="flex items-center justify-between"><h4 className="font-semibold">{editingNovelty?"Editar novedad":"Agregar novedad"}</h4><Button variant="ghost" onClick={closeNoveltyEditor}>Cerrar edición</Button></div>
          <NoveltyForm onAgreementSaved={agreementSaved} key={editingNovelty?.noveltyId??"new"} options={options} initial={editingNovelty} preset={noveltyPreset} onSaved={result=>{void noveltySaved(result);setNoveltyEditorState(current=>current===noveltyEditorState?null:current);}}/>
        </section>}
        onEditNovelty={item=>setNoveltyEditorState({initial:item,preset:null})} onVoidNovelty={voidNovelty} onAddNovelty={worker=>{
          if(!selected)return;
          setNoveltyEditorState({initial:null,preset:{employmentId:worker.employmentId,employeeName:worker.employeeName,startDate:selected.periodStart,endDate:selected.periodEnd}});
        }}/>

      <DeductionAgreementsSection canManage={permissions.has("payroll.manage")} onAgreement={() => setCreatingAgreement(true)} onEditAgreement={setEditingAgreement} onAgreementSaved={agreementSaved}/>
    </div>}
    {section === "payments" && (
      <PaymentsSection canPay={permissions.has("payroll.pay")} onCreate={() => setCreatingPayment(true)}/>
    )}
    {section === "electronic" && (
      <ElectronicSection options={options} canGenerate={permissions.has("payroll.fiscal")} onGenerate={openElectronic}/>
    )}
    {section === "reports" && (
      <PayrollReports/>
    )}
    {section === "configuration" && (
      <>
      <ConfigurationSection options={options} canConfigure={permissions.has("payroll.configure")} canReadAccounting={permissions.has("accounting.read")} canConfigureAccounting={permissions.has("accounting.configure")} onConcept={() => setCreatingConcept(true)} onEditConcept={setEditingConcept} onConceptSaved={conceptSaved} onSettingsSaved={settingsSaved}/>
      </>
    )}
    </TabsContent>}
    </Tabs>
    <Dialog open={creatingRun} onOpenChange={setCreatingRun}><DialogContent><DialogHeader><DialogTitle>Nueva liquidación</DialogTitle><DialogDescription>Selecciona el período y la periodicidad. Los porcentajes se toman de la configuración de cada contrato.</DialogDescription></DialogHeader>{options && <RunForm businessId={businessId} options={options} onSaved={result => {
        queryClient.setQueryData(["payroll-run",tenantId,businessId,result.payrollRunId],result);
        void queryClient.invalidateQueries({queryKey:["payroll-runs",tenantId,businessId]});
        setCreatingRun(false);
      }}/>}</DialogContent></Dialog>
    <Dialog open={creatingEmployment || editingEmployment !== null} onOpenChange={open => { if (!open) {
        setCreatingEmployment(false);
        setEditingEmployment(null);
        setPreselectedPartyId(null);
    } }}><DialogContent className="flex h-[min(92dvh,52rem)] max-h-[92dvh] max-w-3xl flex-col gap-0 overflow-hidden p-0"><DialogHeader className="shrink-0 border-b px-6 py-5"><DialogTitle>{editingEmployment ? "Editar relación laboral" : "Nueva relación laboral"}</DialogTitle><DialogDescription>Selecciona un empleado y completa sus datos laborales y de pago.</DialogDescription></DialogHeader><div className="min-h-0 flex-1 overflow-y-auto p-6">{options && (creatingEmployment||editingEmployment!==null) && <EmploymentForm businessId={businessId} options={options} initial={editingEmployment} partyId={preselectedPartyId} onSaved={async result => { const previous=editingEmployment; setCreatingEmployment(false); setEditingEmployment(null); setPreselectedPartyId(null); await employmentSaved(result,previous); }}/>}</div></DialogContent></Dialog>
    <Dialog open={creatingConcept || editingConcept !== null} onOpenChange={open => { if (!open) { setCreatingConcept(false); setEditingConcept(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingConcept ? "Editar concepto" : "Nuevo concepto"}</DialogTitle><DialogDescription>Define cómo se calcula, contabiliza y reporta este concepto.</DialogDescription></DialogHeader>{options && <ConceptForm options={options} initial={editingConcept} onSaved={result => {conceptSaved(result);setCreatingConcept(false);setEditingConcept(null);}}/>}</DialogContent></Dialog>

    <Dialog open={creatingElectronic} onOpenChange={setCreatingElectronic}><DialogContent><DialogHeader><DialogTitle>Consolidar nómina electrónica</DialogTitle><DialogDescription>Reserva la serie, crea el XML por trabajador y lo envía al motor fiscal para validar, firmar y transmitir a la DIAN.</DialogDescription></DialogHeader><p className="rounded-xl bg-muted p-3 text-sm">{dianRemaining === null ? "El cupo se verificará al preparar los documentos." : <>Cupo disponible: <b>{dianRemaining.toLocaleString("es-CO")} documentos DIAN</b>. Se requiere uno por trabajador.</>}{dianRemaining === 0 && " Para generar documentos nuevos necesitas ampliar el cupo; puedes recuperar un mes ya preparado."}</p><ElectronicPeriodForm businessId={businessId} onSaved={async () => { setCreatingElectronic(false); await queryClient.invalidateQueries({queryKey:["payroll-electronic-periods",tenantId,businessId]}); }}/></DialogContent></Dialog>
    <Dialog open={creatingAgreement || editingAgreement !== null} onOpenChange={open => { if (!open) { setCreatingAgreement(false); setEditingAgreement(null); } }}><DialogContent className="max-h-[92dvh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>{editingAgreement ? "Editar descuento autorizado" : "Nuevo descuento autorizado"}</DialogTitle><DialogDescription>Registra autoridad, evidencia, prioridad, vigencia y límites antes de descontar.</DialogDescription></DialogHeader>{options && <DeductionAgreementForm options={options} initial={editingAgreement} onSaved={result=>{agreementSaved(result);setCreatingAgreement(false);setEditingAgreement(null);}}/>}</DialogContent></Dialog>
    <Dialog open={creatingPayment} onOpenChange={open=>{if(!paying)setCreatingPayment(open);}}><DialogContent showClose={!paying} className="max-h-[92dvh] max-w-2xl overflow-y-auto"><DialogHeader><DialogTitle>Confirmar lote de pago</DialogTitle><DialogDescription>Consulta una liquidación y paga su grupo pendiente cuando la causación esté contabilizada.</DialogDescription></DialogHeader>{options && <PaymentForm options={options} selectedRun={selected} loading={detail.isFetching} error={detail.isError} busy={paying} setBusy={setPaying} onRunChange={selectRun} onRetry={()=>void detail.refetch()} onSaved={result=>{
      void queryClient.invalidateQueries({queryKey:["payroll-payments",tenantId,businessId]});
      queryClient.setQueryData<PayrollRun>(["payroll-run",tenantId,businessId,result.payrollRunId],current=>current?{...current,payableEmployees:result.remainingEmployees}:current);
      if(result.remainingEmployees.length===0)setCreatingPayment(false);
    }}/>}</DialogContent></Dialog>
  </div>;
}
function RunsSection({ options, selected, canCalculate, calculationDisabled, calculating, canApprove, onSelect, onCalculate, onChanged }: {
    options: PayrollOptions | null;
    selected: PayrollRun | null;
    canCalculate: boolean;
    calculationDisabled: boolean;
    calculating: boolean;
    canApprove: boolean;
    onSelect: (id: string) => Promise<void>;
    onCalculate:(id:string,request:CalculatePayrollRun)=>Promise<PayrollRun>;
    onChanged: () => Promise<void>;
}) {
    const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
    const [page,setPage]=useState(1);
    const list=useQuery({queryKey:["payroll-runs",tenant,business,page],queryFn:()=>payrollApi.runs({page,pageSize:20}),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
    const runs=list.data?.items??[];
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
    return <div className="grid min-w-0 grid-cols-1 gap-5 2xl:grid-cols-[minmax(260px,.65fr)_minmax(0,1.8fr)]"><Card className="min-w-0 rounded-3xl"><CardHeader><CardTitle>Liquidaciones</CardTitle><CardDescription>Selecciona un período para revisar, calcular o aprobar su nómina.</CardDescription></CardHeader><CardContent className="max-h-96 space-y-2 overflow-y-auto">{list.isFetching&&<p role="status">Cargando liquidaciones…</p>}{list.isError&&<div role="alert">No fue posible consultar las liquidaciones. <Button onClick={()=>void list.refetch()}>Reintentar liquidaciones</Button></div>}{runs.map(run => <button type="button" key={run.payrollRunId} disabled={busy!==null||calculating} onClick={() => void onSelect(run.payrollRunId)} className={`w-full rounded-2xl border p-4 text-left transition-colors ${selected?.payrollRunId === run.payrollRunId ? "border-primary bg-primary/5" : "hover:bg-muted/40"}`}><span className="flex items-center justify-between gap-3"><b>{run.periodStart} — {run.periodEnd}</b><small>{statusLabel(options, run.status)}</small></span><span className="mt-2 grid grid-cols-3 text-sm text-muted-foreground"><span>{run.employeeCount} trabajadores</span><span className="text-right">Deducciones {money.format(run.totalDeductions)}</span><strong className="text-right text-foreground">Neto {money.format(run.netPayable)}</strong></span></button>)}{!list.isFetching&&!list.isError&&runs.length === 0 && <Empty text="Todavía no hay liquidaciones."/>}<div className="flex flex-wrap justify-between gap-2 text-sm"><span>{list.data?.totalCount??0} liquidaciones · Página {page}</span><Button variant="outline" disabled={page===1||list.isFetching} onClick={()=>setPage(value=>value-1)}>Anterior liquidaciones</Button><Button variant="outline" disabled={page>=(list.data?.totalPages??0)||list.isFetching} onClick={()=>setPage(value=>value+1)}>Siguiente liquidaciones</Button></div></CardContent></Card><Card className="min-w-0 rounded-3xl"><CardHeader><CardTitle>Detalle</CardTitle><CardDescription>{selected ? `${selected.periodStart} — ${selected.periodEnd}` : "Selecciona una liquidación"}</CardDescription></CardHeader><CardContent>{!selected ? <Empty text="El detalle mostrará trabajadores, conceptos y totales."/> : <div className="space-y-4"><div className="grid gap-3 sm:grid-cols-3"><Metric label="Devengado" value={selected.totalEarnings}/><Metric label="Deducciones" value={selected.totalDeductions}/><Metric label="Neto" value={selected.netPayable}/></div><div className="flex flex-wrap gap-2">{canApprove && selected.status === "Calculated" && <Button disabled={busy !== null || calculating || selectionBlocksApproval} onClick={() => void approve()}><CheckCircle2 className="mr-2 h-4 w-4"/>{busy === "approve" ? "Aprobando…" : "Aprobar y contabilizar"}</Button>}</div>{canCalculate && ["Draft", "Calculated"].includes(selected.status) && <PayrollRunSelection key={selected.payrollRunId} run={selected} disabled={busy !== null || calculationDisabled} calculating={calculating} onCalculate={onCalculate} onBlocksApproval={setSelectionBlocksApproval}/>} {selected.employees.map(employee => <details key={employee.payrollRunEmployeeId} className="rounded-2xl border p-4"><summary className="cursor-pointer font-semibold">{employee.employeeName}<span className="float-right">{money.format(employee.netPayable)}</span></summary><div className="mt-3 overflow-x-auto"><table className="w-full min-w-[620px] text-sm"><thead><tr className="text-left text-muted-foreground"><th>Concepto</th><th>Naturaleza</th><th className="text-right">Base</th><th className="text-right">Valor</th></tr></thead><tbody>{employee.lines.map(line => <tr key={line.lineNumber} className="border-t"><td className="py-2">{line.conceptCode} · {line.conceptName}</td><td>{catalogLabel(options, "payroll-concept-nature", line.natureCode)}</td><td className="text-right">{line.baseAmount === null ? "—" : money.format(line.baseAmount)}</td><td className="text-right font-medium">{money.format(line.amount)}</td></tr>)}</tbody></table></div></details>)}</div>}</CardContent></Card></div>;
}
function EmploymentsSection({ canManage, onEdit, onSaved }: {
    canManage: boolean;
    onEdit: (value: PayrollEmployment) => void;
    onSaved:(result:PayrollEmployment,previous:PayrollEmployment)=>Promise<void>;
}) {
    const tenantId = useTenantContextStore(state => state.selectedTenantId), businessId = useBusinessContextStore(state => state.selectedBusinessId);
    const [page, setPage] = useState(1);
    const query = useQuery({ queryKey: ["payroll-employments", tenantId, businessId, page], queryFn: () => payrollApi.employments({ page, pageSize: 25 }),
      staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false });
    const items = query.data?.items ?? [];
    async function deactivate(item: PayrollEmployment) { if (!window.confirm(`¿Desactivar el contrato ${item.contractNumber}?`)) return; try { const result=await payrollApi.saveEmployment({ ...item, configuration:item.configuration?.values, isActive: false }); toast.success("Contrato desactivado."); await onSaved(result,item); } catch (error) { toast.error(message(error, "No fue posible desactivar el contrato.")); } }
    return <Card className="rounded-3xl"><CardHeader><CardTitle>Relaciones laborales</CardTitle><CardDescription>Administra los contratos y datos de pago de tus trabajadores.</CardDescription></CardHeader><CardContent className="space-y-3"><div className="overflow-x-auto rounded-xl border"><table className="w-full min-w-[820px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Trabajador</th><th className="text-left">Contrato</th><th className="text-left">Vigencia</th><th className="text-left">Cuenta de pago</th><th className="text-right">Salario</th><th className="pr-3 text-right">Acciones</th></tr></thead><tbody>{items.map(item => <tr key={item.employmentId} className="border-t"><td className="p-3 font-medium">{item.employeeName}</td><td>{item.contractNumber}</td><td>{item.startDate} — {item.endDate ?? "vigente"}</td><td>{item.bankAccountNumber ? `•••• ${item.bankAccountNumber.slice(-4)}` : "Sin configurar"}</td><td className="text-right">{money.format(item.monthlySalary)}</td><td className="pr-3 text-right"><span className="inline-flex">{canManage && <Button size="sm" variant="ghost" onClick={() => onEdit(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</span></td></tr>)}</tbody></table>{query.isLoading && <p className="p-4 text-sm text-muted-foreground">Cargando trabajadores…</p>}{query.isError && <p className="p-4 text-sm text-destructive">No fue posible cargar los contratos. <Button size="sm" variant="outline" onClick={() => void query.refetch()}>Reintentar</Button></p>}{!query.isLoading && !query.isError && !items.length && <Empty text="Crea el primer contrato laboral."/>}</div><div className="flex items-center justify-between text-sm"><span className="text-muted-foreground">{query.data?.totalCount ?? 0} contratos</span><span className="flex gap-2"><Button size="sm" variant="outline" disabled={page <= 1 || query.isFetching} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button size="sm" variant="outline" disabled={!query.data || page * query.data.pageSize >= query.data.totalCount || query.isFetching} onClick={() => setPage(value => value + 1)}>Siguiente</Button></span></div></CardContent></Card>;
}
function DeductionAgreementsSection({ canManage, onAgreement, onEditAgreement, onAgreementSaved }: {
    canManage: boolean;
    onAgreement: () => void;
    onEditAgreement: (value: PayrollDeductionAgreement) => void;
    onAgreementSaved:(item:PayrollDeductionAgreement)=>void;
}) {
 const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId),[page,setPage]=useState(1);
 const list=useQuery({queryKey:["payroll-agreements",tenant,business,page],queryFn:()=>payrollApi.agreements({page,pageSize:20}),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
 async function deactivate(item: PayrollDeductionAgreement) { try { onAgreementSaved(await payrollApi.saveDeductionAgreement({ ...item, isActive: false })); toast.success("Descuento desactivado."); } catch (error) { toast.error(message(error, "No fue posible desactivar el descuento.")); } } function evidence(url: string) { try { const parsed = new URL(url); if (!['http:', 'https:'].includes(parsed.protocol)) throw new Error(); window.open(parsed.toString(), '_blank', 'noopener,noreferrer'); } catch { toast.error("La evidencia no tiene una URL válida."); } } return <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Autorizaciones de descuento</CardTitle><CardDescription>Acuerdos, libranzas, embargos y otros conceptos con soporte.</CardDescription></span>{canManage && <Button size="sm" onClick={onAgreement}>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{list.isFetching&&<p role="status">Cargando autorizaciones…</p>}{list.isError&&<p role="alert">No fue posible cargar las autorizaciones. <Button onClick={()=>void list.refetch()}>Reintentar autorizaciones</Button></p>}{list.data?.items.map(item => <div key={item.deductionAgreementId} className="rounded-xl border p-3 text-sm"><span className="flex justify-between gap-3"><b>{item.employeeName}</b><span>{item.isActive ? "Activo" : "Inactivo"}</span></span><p>{item.conceptName} · {item.authorityName}</p><p className="text-muted-foreground">Ref. {item.referenceNumber} · descontado {money.format(item.deductedToDate)}{item.authorizedTotal !== null ? ` de ${money.format(item.authorizedTotal)}` : ""}</p><div className="mt-2 flex flex-wrap gap-2"><Button size="sm" variant="outline" onClick={() => evidence(item.evidenceUrl)}><ExternalLink className="mr-2 h-4 w-4"/>Ver evidencia</Button>{canManage && <Button size="sm" variant="ghost" onClick={() => onEditAgreement(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>}{canManage && item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivate(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div></div>)}{!list.isFetching&&!list.isError&&!list.data?.items.length && <Empty text="No hay descuentos autorizados."/>}<PayrollHistoryNavigation name="autorizaciones" page={page} totalCount={list.data?.totalCount??0} busy={list.isFetching} onPage={setPage}/></CardContent></Card>; }
function PaymentsSection({ canPay, onCreate }: {
    canPay: boolean;
    onCreate: () => void;
}) {
 const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId),[page,setPage]=useState(1);
 const list=useQuery({queryKey:["payroll-payments",tenant,business,page],queryFn:()=>payrollApi.payments(page),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
 return <Card className="rounded-3xl">
    <CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Pagos de nómina</CardTitle><CardDescription>Confirma un grupo de pago y consulta su resultado contable en Trazabilidad financiera.</CardDescription></span>{canPay && <Button onClick={onCreate}>Confirmar pago</Button>}</span></CardHeader>
    <CardContent>{list.isFetching&&<p role="status">Cargando pagos…</p>}{list.isError&&<p role="alert">No fue posible cargar los pagos. <Button onClick={()=>void list.refetch()}>Reintentar pagos</Button></p>}<div className="overflow-x-auto rounded-xl border">
        <table className="w-full min-w-[800px] text-sm">
            <thead className="bg-muted/50"><tr>
                <th className="px-4 py-3 text-left">Fecha</th>
                <th className="px-4 py-3 text-left">Referencia</th>
                <th className="min-w-[9rem] px-4 py-3 text-left">Medio</th>
                <th className="whitespace-nowrap px-4 py-3 text-center">Trabajadores</th>
                <th className="min-w-[9rem] px-4 py-3 text-right">Total</th>
                <th className="min-w-[10rem] px-4 py-3 text-left">Estado</th>
            </tr></thead>
            <tbody>{list.data?.items.map(item => <tr className="border-t" key={item.paymentBatchId}>
                <td className="whitespace-nowrap px-4 py-3">{item.paymentDate}</td>
                <td className="max-w-xs px-4 py-3 [overflow-wrap:anywhere]">{item.referenceNumber}</td>
                <td className="px-4 py-3">{item.paymentMethodName}</td>
                <td className="px-4 py-3 text-center tabular-nums">{item.employeeCount}</td>
                <td className="whitespace-nowrap px-4 py-3 text-right font-medium tabular-nums">{money.format(item.totalAmount)}</td>
                <td className="px-4 py-3 text-left">{businessStatusLabel(item.isPaid?"Paid":item.status)}<span className="block text-xs text-muted-foreground">{accountingStatusLabel(item.accountingStatus??"MissingAccountingJob")}</span></td>
            </tr>)}</tbody>
        </table>
        {!list.isFetching&&!list.isError&&!list.data?.items.length && <Empty text="No hay pagos confirmados."/>}
    </div><PayrollHistoryNavigation name="pagos" page={page} totalCount={list.data?.totalCount??0} busy={list.isFetching} onPage={setPage}/></CardContent>
</Card>; }
function PayrollHistoryNavigation({name,page,totalCount,busy,onPage}:{name:string;page:number;totalCount:number;busy:boolean;onPage:(page:number)=>void}) {
 return <div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-sm"><span>{totalCount} resultados · Página {page}</span><div className="flex gap-2"><Button variant="outline" disabled={busy||page===1} onClick={()=>onPage(page-1)}>Anterior {name}</Button><Button variant="outline" disabled={busy||page*20>=totalCount} onClick={()=>onPage(page+1)}>Siguiente {name}</Button></div></div>;
}
function ElectronicSection({options,canGenerate,onGenerate}:{options:PayrollOptions|null;canGenerate:boolean;onGenerate:()=>void}) {
 const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId),[page,setPage]=useState(1);
 const list=useQuery({queryKey:["payroll-electronic-periods",tenant,business,page],queryFn:()=>payrollApi.electronicPeriods(page),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
 return <Card className="rounded-3xl"><CardHeader><div className="flex flex-wrap items-center justify-between gap-3"><div><CardTitle>Documentos electrónicos DIAN</CardTitle><CardDescription>Consolidación mensual por trabajador.</CardDescription></div>{canGenerate&&<Button onClick={onGenerate}>Consolidar mes</Button>}</div></CardHeader><CardContent className="space-y-3">
 <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-muted/20 p-4 text-sm"><p>{options?.settings?.electronicPayrollEnabled?"Nómina electrónica habilitada.":"Activa la nómina electrónica en Configuración."} {options?.electronicConfiguration?.isActive?`Serie ${options.electronicConfiguration.prefix} configurada.`:"La serie y las credenciales DIAN requieren configuración."}</p><Button variant="outline" asChild><Link href="/dashboard/settings/fiscal">Configuración DIAN <ExternalLink className="ml-2 size-3"/></Link></Button></div>
 {list.isFetching&&<p role="status">Cargando períodos…</p>}{list.isError&&<p role="alert">No fue posible cargar los períodos. <Button onClick={()=>void list.refetch()}>Reintentar períodos</Button></p>}
 {list.data?.items.map(period=><ElectronicPeriodDetail key={period.electronicPeriodId} period={period} options={options}/>)}
 {!list.isFetching&&!list.isError&&!list.data?.items.length&&<Empty text="No se han consolidado períodos electrónicos."/>}
 <PayrollHistoryNavigation name="períodos" page={page} totalCount={list.data?.totalCount??0} busy={list.isFetching} onPage={setPage}/>
 </CardContent></Card>;
}
function ElectronicPeriodDetail({period,options}:{period:ElectronicPayrollPeriod;options:PayrollOptions|null}) {
 const [open,setOpen]=useState(false);
 return <details className="rounded-xl border p-4" onToggle={event=>setOpen(event.currentTarget.open)}><summary className="cursor-pointer font-semibold">{period.year}-{String(period.month).padStart(2,"0")} · {statusLabel(options,period.status)} <span className="text-sm font-normal">{period.documentCount??0} documentos</span></summary>{open&&<ElectronicDocuments periodId={period.electronicPeriodId} options={options}/>}</details>;
}
function ElectronicDocuments({periodId,options}:{periodId:string;options:PayrollOptions|null}) {
 const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId),[page,setPage]=useState(1);
 const list=useQuery({queryKey:["payroll-electronic-documents",tenant,business,periodId,page],queryFn:()=>payrollApi.electronicDocuments(periodId,page),staleTime:Infinity,refetchOnWindowFocus:false,refetchOnReconnect:false});
 return <div className="mt-3 space-y-2">{list.isFetching&&<p role="status">Cargando documentos…</p>}{list.isError&&<p role="alert">No fue posible cargar los documentos. <Button onClick={()=>void list.refetch()}>Reintentar documentos</Button></p>}{list.data?.items.map(document=><div key={document.electronicPayrollDocumentId} className="flex flex-wrap justify-between gap-2 rounded-lg bg-muted/30 p-3 text-sm"><span>{document.employeeName}</span><span>{statusLabel(options,document.status)}</span></div>)}<PayrollHistoryNavigation name="documentos" page={page} totalCount={list.data?.totalCount??0} busy={list.isFetching} onPage={setPage}/></div>;
}
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
} } const [signature,setSignature]=useState(false); if (selected && !loading) {
    const columns = selected.columns.map(column => ({ key: column.key, label: column.label, align: column.align, format: (value: unknown) => column.format === "currency" ? money.format(Number(value ?? 0)) : column.format === "number" ? Number(value ?? 0).toLocaleString("es-CO") : column.format === "datetime" && value ? new Date(String(value)).toLocaleString("es-CO") : String(value ?? "") }));
    const receipt = selected.dataset === "PayrollReceipt";
    return <div className="space-y-3">{receipt&&<label className="flex items-center gap-2 text-sm"><Checkbox checked={signature} onCheckedChange={value=>setSignature(value===true)}/>Incluir espacio para firma</label>}<ReportViewer onClose={() => setSelected(null)} title={selected.name} description={`${selected.description} · ${from} a ${to}`} rows={receipt ? payrollReceiptRows(rows,signature) : rows} columns={receipt ? payrollReceiptColumns : columns} groupedHeaders={receipt} searchable={!receipt} documentDownloads fileName={`nomina-${selected.code}-${from}-${to}`}/></div>;
} ; const selectedDefinition = definitions.find(item => item.code === reportCode); return <Card className="rounded-3xl"><CardHeader><CardTitle>Reportes de nómina</CardTitle><CardDescription>Consulta liquidaciones, comprobantes, pagos y documentos electrónicos.</CardDescription></CardHeader><CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-4"><Field label="Reporte"><Select value={reportCode} onValueChange={setReportCode}><SelectTrigger aria-label="Reporte"><SelectValue placeholder="Selecciona un reporte" /></SelectTrigger><SelectContent>{definitions.map(definition => <SelectItem key={definition.code} value={definition.code}>{definition.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Desde"><DatePicker today={today} value={from} onChange={setFrom}/></Field><Field label="Hasta"><DatePicker today={today} value={to} onChange={setTo}/></Field><Field label="Trabajador"><PartyRoleSelect role="Employee" includePartyId value={partyId} leadingOptions={[{value:"_all",label:"Todos los trabajadores"}]} placeholder="Buscar trabajador" onChange={setPartyId}/></Field><div className="rounded-xl border bg-muted/20 p-3 text-sm md:col-span-2 xl:col-span-3"><b>{selectedDefinition?.name ?? "Catálogo de reportes"}</b><p className="text-muted-foreground">{selectedDefinition?.description ?? "No hay reportes activos."}</p></div><Button className="self-end" disabled={loading || !selectedDefinition || !from || !to || to < from} onClick={() => selectedDefinition && void open(selectedDefinition)}>{loading && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar reporte</Button></CardContent></Card>; }
function ConfigurationSection({ options, canConfigure, canReadAccounting, canConfigureAccounting, onConcept, onEditConcept, onConceptSaved, onSettingsSaved }: {
    options: PayrollOptions | null;
    canConfigure: boolean;
    canReadAccounting: boolean;
    canConfigureAccounting: boolean;
    onConcept: () => void;
    onEditConcept: (value: PayrollConcept) => void;
    onConceptSaved:(value:PayrollConcept)=>void;
    onSettingsSaved:(value:PayrollOptions["settings"])=>void;
}) {
    async function deactivateConcept(item: PayrollConcept) { if (!options) return; const option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(value => value.code === code)?.optionId ?? null : null; try { const result=await payrollApi.saveConcept({ conceptId: item.conceptId, code: item.code, name: item.name, natureOptionId: option("payroll-concept-nature", item.natureCode) ?? "", calculationMethodOptionId: option("payroll-calculation-method", item.calculationMethodCode) ?? "", treatmentOptionId: option("payroll-concept-treatment", item.treatmentCode) ?? "", dianConceptOptionId: option("payroll-dian-concept", item.dianConceptCode), accountingCategoryOptionId: option("payroll-accounting-category", item.accountingCategoryCode) ?? "", systemRoleOptionId: option("payroll-system-concept-role", item.systemRoleCode), isSalaryBase: item.isSalaryBase, isSocialSecurityBase: item.isSocialSecurityBase, isBenefitsBase: item.isBenefitsBase, isTaxWithholdingBase: item.isTaxWithholdingBase, requiresDeductionAgreement: item.requiresDeductionAgreement, effectiveFrom: item.effectiveFrom, effectiveTo: item.effectiveTo, isActive: false, rowVersion: item.rowVersion }); onConceptSaved(result);toast.success("Concepto desactivado."); } catch (error) { toast.error(message(error, "No fue posible desactivar el concepto.")); } }
    async function saveSettings(field: "electronicPayrollEnabled" | "isEmployerExemptFromHealthSenaIcbf", value: boolean) { try {
        const result=await payrollApi.saveSettings({ isEmployerExemptFromHealthSenaIcbf: field === "isEmployerExemptFromHealthSenaIcbf" ? value : options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false, electronicPayrollEnabled: field === "electronicPayrollEnabled" ? value : options?.settings?.electronicPayrollEnabled ?? false, rowVersion: options?.settings?.rowVersion ?? null });
        onSettingsSaved(result);toast.success("Configuración de nómina actualizada.");
    }
    catch (error) {
        toast.error(message(error, "No fue posible actualizar la configuración."));
    } }
    return <div className="grid gap-5 lg:grid-cols-2">
    {options&&<PayrollProfiles options={options} canConfigure={canConfigure} canReadAccounting={canReadAccounting}/> }
    <Card className="rounded-3xl"><CardHeader><span className="flex items-center justify-between gap-3"><span><CardTitle>Conceptos</CardTitle><CardDescription>Devengados, descuentos, aportes y provisiones.</CardDescription></span>{canConfigure && <Button size="sm" onClick={onConcept}><Settings2 className="mr-2 h-4 w-4"/>Agregar</Button>}</span></CardHeader><CardContent className="space-y-2">{options?.concepts.map(item => <div key={item.conceptId} className="rounded-xl border p-3 text-sm"><b>{item.code} · {item.name}</b><span className="block text-muted-foreground">{catalogLabel(options, "payroll-concept-nature", item.natureCode)} · {catalogLabel(options, "payroll-accounting-category", item.accountingCategoryCode)} · {item.isActive ? "Activo" : "Inactivo"}</span>{canConfigure && <div className="mt-2 flex gap-2"><Button size="sm" variant="ghost" onClick={() => onEditConcept(item)}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>{item.isActive && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => void deactivateConcept(item)}><Trash2 className="mr-2 h-4 w-4"/>Desactivar</Button>}</div>}</div>)}{!options?.concepts.length && <Empty text="Configura los conceptos requeridos por el calculador."/>}</CardContent></Card>

    <Card className="rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Parámetros del empleador</CardTitle><CardDescription>Activa únicamente condiciones verificadas para la entidad legal.</CardDescription></CardHeader><CardContent className="grid gap-3 sm:grid-cols-2"><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.electronicPayrollEnabled ?? false} onCheckedChange={checked => void saveSettings("electronicPayrollEnabled", checked === true)}/><span><b className="block">Nómina electrónica</b><span className="text-muted-foreground">Habilita generación, firma y envío mensual.</span></span></label><label className="flex items-center gap-3 rounded-xl border p-4 text-sm"><Checkbox disabled={!canConfigure} checked={options?.settings?.isEmployerExemptFromHealthSenaIcbf ?? false} onCheckedChange={checked => void saveSettings("isEmployerExemptFromHealthSenaIcbf", checked === true)}/><span><b className="block">Exoneración verificada</b><span className="text-muted-foreground">Salud patronal, SENA e ICBF según condición legal.</span></span></label></CardContent></Card>
    {options && canReadAccounting && (
      <PayrollAccountingMappings options={options} canConfigure={canConfigureAccounting}/>
    )}
  </div>;
}
function PayrollRunSelect({value,label,regularOnly=false,disabled=false,onChange}:{value:string;label:string;regularOnly?:boolean;disabled?:boolean;onChange:(run:PayrollRunSummary)=>void}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
  const [selected,setSelected]=useState<PayrollRunSummary|null>(null);
  const option=(row:PayrollRunSummary)=>({value:row.payrollRunId,label:`${row.periodStart} — ${row.periodEnd}`,description:`Neto liquidado ${money.format(row.netPayable)}`});
  return <PagedEntitySelect<PayrollRunSummary> queryKey={["payroll-run-select",tenant,business,regularOnly]} value={value} ariaLabel={label} disabled={disabled}
    selectedOption={selected?.payrollRunId===value?option(selected):null} searchPlaceholder="Buscar período (AAAA-MM-DD)…" pageSize={20}
    loadPage={(search,page,pageSize)=>payrollApi.runs({page,pageSize,search:search||undefined,status:"Approved",kind:regularOnly?"Regular":undefined})}
    getOption={option} onChange={(_id,_option,row)=>{if(row){setSelected(row);onChange(row);}}}/>;
}
function RunForm({ businessId, options, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    onSaved: (result:PayrollRun) => void;
}) { const today = useBusinessDates().today(), monthEnd = new Date(Date.UTC(Number(today.slice(0,4)),Number(today.slice(5,7)),0)).toISOString().slice(0,10), [frequency, setFrequency] = useState(""), [kind, setKind] = useState<"Regular" | "Adjustment">("Regular"), [originalId, setOriginalId] = useState(""), [start, setStart] = useState(today.slice(0, 8) + "01"), [end, setEnd] = useState(monthEnd), [payment, setPayment] = useState(today), [busy, setBusy] = useState(false); async function save() { setBusy(true); try {
    const result = await payrollApi.createRun({ payrollRunId: crypto.randomUUID(), businessId, payFrequencyOptionId: frequency, runKind: kind, originalPayrollRunId: kind === "Adjustment" ? originalId : null, periodStart: start, periodEnd: end, paymentDate: payment });
    toast.success(kind === "Adjustment" ? "Ajuste creado en borrador." : "Liquidación creada en borrador.");
    onSaved(result);
}
catch (error) {
    toast.error(message(error, "No fue posible crear la liquidación."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Tipo"><Select value={kind} onValueChange={value => { setKind(value as "Regular" | "Adjustment"); if (value === "Regular")
    setOriginalId(""); }}><SelectTrigger aria-label="Tipo"><SelectValue /></SelectTrigger><SelectContent>{catalog(options, "payroll-run-kind").filter(item => item.isActive).map(item => <SelectItem key={item.optionId} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field>{kind === "Adjustment" && <Field label="Liquidación original"><PayrollRunSelect value={originalId} label="Liquidación original" regularOnly onChange={original=>{setOriginalId(original.payrollRunId);setStart(original.periodStart);setEnd(original.periodEnd);setPayment(original.paymentDate);}}/></Field>}<CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={frequency} onChange={setFrequency}/><Field label="Inicio"><DatePicker today={today} value={start} onChange={setStart}/></Field><Field label="Fin"><DatePicker today={today} value={end} onChange={setEnd}/></Field>{kind === "Regular" && <p className="sm:col-span-2 text-sm text-muted-foreground">Mensual: mes completo. Quincenal: del 1 al 15 o del 16 al fin de mes. Los días a liquidar se ajustan después en Novedades.</p>}<Field label="Fecha de pago"><DatePicker today={today} value={payment} onChange={setPayment}/></Field>{kind === "Adjustment" && <p className="self-end rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">El ajuste liquida únicamente novedades nuevas como diferencias; no repite el salario ya aprobado.</p>}<DialogFooter className="sm:col-span-2"><Button disabled={busy || !frequency || !start || !end || !payment || end < start || payment < start || (kind === "Adjustment" && !originalId)} onClick={() => void save()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Crear borrador</Button></DialogFooter></div>; }
function EmploymentForm({ businessId, options, initial, partyId, onSaved }: {
    businessId: string;
    options: PayrollOptions;
    initial: PayrollEmployment | null;
    partyId: string | null;
    onSaved: (result:PayrollEmployment) => Promise<void>;
}) { const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveEmployment>(initial ? { ...initial, configuration:initial.configuration?.values??emptyPayrollConfiguration, appliesTransportAllowance:initial.appliesTransportAllowance??true, rowVersion: initial.rowVersion } : { configuration:emptyPayrollConfiguration, employmentId: crypto.randomUUID(), partyId: partyId ?? "", businessId, employeeId: null, contractTypeOptionId: "", salaryTypeOptionId: "", payFrequencyOptionId: "", riskClassOptionId: "", workerTypeOptionId: "", workerSubtypeOptionId: null, paymentMethodOptionId: "", contractNumber: "", startDate: today, endDate: null, monthlySalary: 0, integralSalaryPercentage: null, bankAccountReference: null, bankOptionId: null, bankAccountTypeOptionId: null, bankAccountNumber: null, isActive: true, appliesTransportAllowance:true, rowVersion: null }); const set = <K extends keyof SaveEmployment>(key: K, value: SaveEmployment[K]) => setForm(current => ({ ...current, [key]: value })); const bankTransfer = catalog(options, "payroll-payment-method").find(item => item.optionId === form.paymentMethodOptionId)?.code === "Transfer"; async function save(event: React.FormEvent) { event.preventDefault(); setBusy(true); try {
    const result=await payrollApi.saveEmployment(form);
    toast.success("Relación laboral guardada.");
    await onSaved(result);
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el contrato."));
}
finally {
    setBusy(false);
} } return <form className="grid gap-4 sm:grid-cols-2" onSubmit={save}><Field label="Empleado"><PartyRoleSelect role="Employee" includePartyId disabled={Boolean(initial)} value={form.partyId} placeholder="Buscar empleado por nombre o identificación" selectedOption={initial?{value:initial.partyId,label:initial.employeeName,description:initial.contractNumber}:null} onChange={(value,party)=>setForm(current=>({...current,partyId:value,employeeId:party?.employeeId??null}))}/></Field><Field label="Número de contrato"><Input required value={form.contractNumber} onChange={event => set("contractNumber", event.target.value)}/></Field><CatalogField label="Tipo de contrato" options={catalog(options, "payroll-contract-type")} value={form.contractTypeOptionId} onChange={value => set("contractTypeOptionId", value)}/><CatalogField label="Tipo de salario" options={catalog(options, "payroll-salary-type")} value={form.salaryTypeOptionId} onChange={value => set("salaryTypeOptionId", value)}/><CatalogField label="Periodicidad" options={catalog(options, "payroll-pay-frequency")} value={form.payFrequencyOptionId} onChange={value => set("payFrequencyOptionId", value)}/><CatalogField label="Clase de riesgo" options={catalog(options, "payroll-risk-class")} value={form.riskClassOptionId} onChange={value => set("riskClassOptionId", value)}/><CatalogField label="Tipo de trabajador" options={catalog(options, "payroll-worker-type")} value={form.workerTypeOptionId} onChange={value => set("workerTypeOptionId", value)}/><CatalogField label="Subtipo" options={catalog(options, "payroll-worker-subtype")} value={form.workerSubtypeOptionId ?? ""} onChange={value => set("workerSubtypeOptionId", value || null)}/><CatalogField label="Medio de pago" options={catalog(options, "payroll-payment-method")} value={form.paymentMethodOptionId} onChange={value => set("paymentMethodOptionId", value)}/><Field label="Salario mensual"><FormattedNumberInput kind="currency" ariaLabel="Salario mensual" value={form.monthlySalary||""} onValueChange={value=>set("monthlySalary",value??0)} disabled={busy}/></Field><Field label="Porcentaje de salario integral"><Input min={0} step="any" type="number" value={form.integralSalaryPercentage ?? ""} onChange={event => set("integralSalaryPercentage", event.target.value ? event.currentTarget.valueAsNumber : null)}/></Field><Field label="Inicio"><DatePicker today={today} value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fecha de retiro (opcional)"><DatePicker today={today} value={form.endDate ?? ""} onChange={value => set("endDate", value || null)}/></Field>{bankTransfer && <><CatalogField label="Banco" options={catalog(options, "payroll-bank")} value={form.bankOptionId ?? ""} onChange={value => set("bankOptionId", value || null)}/><CatalogField label="Tipo de cuenta" options={catalog(options, "payroll-bank-account-type")} value={form.bankAccountTypeOptionId ?? ""} onChange={value => set("bankAccountTypeOptionId", value || null)}/><Field label="Número de cuenta"><Input required maxLength={64} inputMode="numeric" autoComplete="off" value={form.bankAccountNumber ?? ""} onChange={event => set("bankAccountNumber", event.target.value.replace(/\s/g, "") || null)}/></Field></>}<label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox aria-label="Pensionado por vejez" disabled={busy || !catalog(options,"payroll-worker-subtype").some(item=>item.code==="01" && item.isActive)} checked={catalog(options,"payroll-worker-subtype").find(item=>item.optionId===form.workerSubtypeOptionId)?.code==="01"} onCheckedChange={checked=>set("workerSubtypeOptionId",catalog(options,"payroll-worker-subtype").find(item=>item.code===(checked===true?"01":"00") && item.isActive)?.optionId??null)}/>Pensionado por vejez</label><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox aria-label="Aplica auxilio de transporte" disabled={busy} checked={form.appliesTransportAllowance??true} onCheckedChange={checked=>set("appliesTransportAllowance",checked===true)}/>Aplica auxilio de transporte</label><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.isActive} onCheckedChange={checked => set("isActive", checked === true)}/>Relación activa</label><PayrollEmploymentDetailsFields value={form.configuration?.details} supervisorName={initial?.configuration?.supervisorName} disabled={busy} onChange={details=>set("configuration",{...(form.configuration??emptyPayrollConfiguration),details})}/><PayrollEmploymentConfigurationFields value={form.configuration??emptyPayrollConfiguration} initial={initial?.configuration} disabled={busy} onChange={value=>set("configuration",value)}/><DialogFooter className="sm:col-span-2"><Button type="submit" disabled={busy || !form.employeeId || (bankTransfer && (!form.bankOptionId || !form.bankAccountTypeOptionId || !form.bankAccountNumber)) || Object.entries(form).some(([key, value]) => ["partyId", "contractTypeOptionId", "salaryTypeOptionId", "payFrequencyOptionId", "riskClassOptionId", "workerTypeOptionId", "paymentMethodOptionId"].includes(key) && !value)}>Guardar contrato</Button></DialogFooter></form>; }
function ConceptForm({ options, initial, onSaved }: {
    options: PayrollOptions;
    initial: PayrollConcept | null;
    onSaved:(value:PayrollConcept)=>void;
}) { const today = useBusinessDates().today(), option = (catalogCode: string, code: string | null) => code ? catalog(options, catalogCode).find(item => item.code === code)?.optionId ?? "" : "", [busy, setBusy] = useState(false), [form, setForm] = useState<SaveConcept>(initial ? { conceptId: initial.conceptId, code: initial.code, name: initial.name, natureOptionId: option("payroll-concept-nature", initial.natureCode), calculationMethodOptionId: option("payroll-calculation-method", initial.calculationMethodCode), treatmentOptionId: option("payroll-concept-treatment", initial.treatmentCode), dianConceptOptionId: option("payroll-dian-concept", initial.dianConceptCode) || null, accountingCategoryOptionId: option("payroll-accounting-category", initial.accountingCategoryCode), systemRoleOptionId: option("payroll-system-concept-role", initial.systemRoleCode) || null, isSalaryBase: initial.isSalaryBase, isSocialSecurityBase: initial.isSocialSecurityBase, isBenefitsBase: initial.isBenefitsBase, isTaxWithholdingBase: initial.isTaxWithholdingBase, requiresDeductionAgreement: initial.requiresDeductionAgreement, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, isActive: initial.isActive, rowVersion: initial.rowVersion } : { conceptId: crypto.randomUUID(), code: "", name: "", natureOptionId: "", calculationMethodOptionId: "", treatmentOptionId: "", dianConceptOptionId: null, accountingCategoryOptionId: "", systemRoleOptionId: null, isSalaryBase: false, isSocialSecurityBase: false, isBenefitsBase: false, isTaxWithholdingBase: false, requiresDeductionAgreement: false, effectiveFrom: today, effectiveTo: null, isActive: true, rowVersion: null }); const set = <K extends keyof SaveConcept>(key: K, value: SaveConcept[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    const result=await payrollApi.saveConcept(form);
    toast.success("Concepto guardado.");
    onSaved(result);
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

function DeductionAgreementForm({ options, initial, onSaved, preset }: {
    preset?:{employmentId:string;employeeName:string;conceptId:string};
    options: PayrollOptions;
    initial: PayrollDeductionAgreement | null;
    onSaved:(result:PayrollDeductionAgreement)=>void;
}) { const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SaveDeductionAgreement>(initial ? { deductionAgreementId: initial.deductionAgreementId, employmentId: initial.employmentId, conceptId: initial.conceptId, authorityOptionId: initial.authorityOptionId, beneficiaryPartyId: initial.beneficiaryPartyId, referenceNumber: initial.referenceNumber, evidenceUrl: initial.evidenceUrl, effectiveFrom: initial.effectiveFrom, effectiveTo: initial.effectiveTo, authorizedTotal: initial.authorizedTotal, installmentAmount: initial.installmentAmount, priority: initial.priority, mustProtectMinimumNetPay: initial.mustProtectMinimumNetPay, isActive: initial.isActive, rowVersion: initial.rowVersion } : { deductionAgreementId: crypto.randomUUID(), employmentId: preset?.employmentId??"", conceptId: preset?.conceptId??"", authorityOptionId: "", beneficiaryPartyId: null, referenceNumber: "", evidenceUrl: "", effectiveFrom: today, effectiveTo: null, authorizedTotal: null, installmentAmount: null, priority: 100, mustProtectMinimumNetPay: true, isActive: true, rowVersion: null }); const set = <K extends keyof SaveDeductionAgreement>(key: K, value: SaveDeductionAgreement[K]) => setForm(current => ({ ...current, [key]: value })); async function save() { setBusy(true); try {
    onSaved(await payrollApi.saveDeductionAgreement(form));
    toast.success("Descuento autorizado guardado.");
}
catch (error) {
    toast.error(message(error, "No fue posible guardar el descuento."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador">{preset?<Input readOnly value={preset.employeeName}/>:<PayrollEmploymentSelect value={form.employmentId} onChange={value=>set("employmentId",value)}/>}</Field><Field label="Concepto"><Select disabled={Boolean(preset)} value={form.conceptId} onValueChange={value => set("conceptId", value)}><SelectTrigger aria-label="Concepto"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive && item.requiresDeductionAgreement).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Autoridad" options={catalog(options, "payroll-deduction-authority")} value={form.authorityOptionId} onChange={value => set("authorityOptionId", value)}/><Field label="Referencia"><Input maxLength={100} value={form.referenceNumber} onChange={event => set("referenceNumber", event.target.value)}/></Field><Field label="Evidencia (URL)"><Input type="url" maxLength={1000} value={form.evidenceUrl} onChange={event => set("evidenceUrl", event.target.value)}/></Field><Field label="Vigente desde"><DatePicker today={today} value={form.effectiveFrom} onChange={value => set("effectiveFrom", value)}/></Field><Field label="Vigente hasta"><DatePicker today={today} value={form.effectiveTo ?? ""} onChange={value => set("effectiveTo", value || null)}/></Field><Field label="Total autorizado"><FormattedNumberInput ariaLabel="Total autorizado" value={form.authorizedTotal??""} kind="currency" onValueChange={value=>set("authorizedTotal",value)}/></Field><Field label="Cuota"><FormattedNumberInput ariaLabel="Cuota" value={form.installmentAmount??""} kind="currency" onValueChange={value=>set("installmentAmount",value)}/></Field><Field label="Prioridad"><Input type="number" min={1} max={999} value={form.priority} onChange={event => set("priority", event.currentTarget.valueAsNumber || 1)}/></Field><label className="flex items-center gap-2 self-end rounded-xl border p-3 text-sm"><Checkbox checked={form.mustProtectMinimumNetPay} onCheckedChange={checked => set("mustProtectMinimumNetPay", checked === true)}/>Proteger mínimo neto</label><DialogFooter className="sm:col-span-2"><Button disabled={busy || !form.employmentId || !form.conceptId || !form.authorityOptionId || !form.referenceNumber || !form.evidenceUrl} onClick={() => void save()}>Guardar autorización</Button></DialogFooter></div>; }
function PayrollAgreementSelect({value,employmentId,conceptId,disabled,onChange}:{value:string|null;employmentId:string;conceptId:string;disabled:boolean;onChange:(id:string|null)=>void}) {
 const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
 const queryClient=useQueryClient();
 const [selected,setSelected]=useState<PayrollDeductionAgreement|null>(null);
 const saved=queryClient.getQueryData<PayrollDeductionAgreement>(["payroll-agreement-value",tenant,business,value]);
 return <PagedEntitySelect<PayrollDeductionAgreement> queryKey={["payroll-agreement-select",tenant,business,employmentId,conceptId]} value={value??""} ariaLabel="Acuerdo de descuento (si aplica)" disabled={disabled}
   placeholder="No aplica" selectedOption={value?{value,label:selected?.deductionAgreementId===value?selected.referenceNumber:saved?.referenceNumber??"Autorización seleccionada"}:null}
   searchPlaceholder="Buscar referencia…" onClear={()=>{setSelected(null);onChange(null);}} pageSize={20}
   loadPage={(search,page,pageSize)=>payrollApi.agreements({page,pageSize,employmentId,conceptId,active:true,search:search||undefined})}
   getOption={row=>({value:row.deductionAgreementId,label:row.referenceNumber,description:row.authorityName})}
   onChange={(id,_option,row)=>{setSelected(row??null);onChange(id);}}/>;
}
function NoveltyForm({ options, initial, preset, onSaved, onAgreementSaved }: {
    onAgreementSaved:(result:PayrollDeductionAgreement)=>void;
    options: PayrollOptions;
    initial:PayrollNovelty|null;
    preset:{employmentId:string;employeeName:string;startDate:string;endDate:string}|null;
    onSaved: (result:PayrollNovelty) => void;
}) { const [creatingAuthority,setCreatingAuthority]=useState<string|null>(null); const [centerName,setCenterName]=useState(initial?.costCenterName); const today = useBusinessDates().today(), [busy, setBusy] = useState(false), [form, setForm] = useState<SavePayrollNovelty>(initial ? {...initial,reasonId:initial.reasonId??null,rowVersion:initial.rowVersion} : { noveltyId: crypto.randomUUID(), employmentId: preset?.employmentId??"", conceptId: "", noveltyTypeOptionId: "", reasonId: null, deductionAgreementId: null, startDate: preset?.startDate??today, endDate: preset?.endDate??today, quantity: 0, unitAmount: null, totalAmount: 0, notes: null, evidenceUrl: null }); const set = <K extends keyof SavePayrollNovelty>(key: K, value: SavePayrollNovelty[K]) => setForm(current => ({ ...current, [key]: value })); const selectedConcept = options.concepts.find(item => item.conceptId === form.conceptId), method = selectedConcept?.calculationMethodCode, needsRate = method === "QuantityByRate" || method === "PercentageOfBase", zeroQuantityAllowed = method === "FixedAmount" && catalog(options, "payroll-novelty-type").find(option => option.optionId === form.noveltyTypeOptionId)?.code === "Amount"; async function save() { setBusy(true); try {
    onSaved(await payrollApi.saveNovelty(form));
}
catch (error) {
    toast.error(message(error, "No fue posible guardar la novedad."));
}
finally {
    setBusy(false);
} } return <div className="grid gap-4 sm:grid-cols-2"><Field label="Trabajador">{initial || preset ? <Input value={initial?.employeeName??preset?.employeeName??""} readOnly/> : <PayrollEmploymentSelect value={form.employmentId} onChange={value=>setForm(current => ({ ...current, employmentId: value, deductionAgreementId: null }))}/>}</Field><Field label="Concepto"><Select value={form.conceptId} onValueChange={value => setForm(current => ({ ...current, conceptId: value, deductionAgreementId: null, unitAmount: null, totalAmount: 0 }))}><SelectTrigger aria-label="Concepto"><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{options.concepts.filter(item => item.isActive).map(item => <SelectItem key={item.conceptId} value={item.conceptId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select></Field><CatalogField label="Tipo de novedad" options={catalog(options, "payroll-novelty-type")} value={form.noveltyTypeOptionId} onChange={value => set("noveltyTypeOptionId", value)}/><Field label="Acuerdo de descuento (si aplica)"><PayrollAgreementSelect value={form.deductionAgreementId} employmentId={form.employmentId} conceptId={form.conceptId} disabled={busy||!form.employmentId||!form.conceptId}
    onChange={value=>set("deductionAgreementId",value)}/></Field>{selectedConcept?.requiresDeductionAgreement&&<section aria-label="Autorización de esta novedad" className="space-y-3 rounded-xl border p-4 sm:col-span-2">{creatingAuthority?<><div className="flex items-center justify-between gap-3"><h4 className="font-medium">Autorización del descuento</h4><Button variant="ghost" onClick={()=>setCreatingAuthority(null)}>Cerrar autorización</Button></div><DeductionAgreementForm key={creatingAuthority} options={options} initial={null} preset={{employmentId:form.employmentId,employeeName:initial?.employeeName??preset?.employeeName??"Trabajador seleccionado",conceptId:form.conceptId}} onSaved={result=>{onAgreementSaved(result);setForm(current=>current.employmentId===result.employmentId&&current.conceptId===result.conceptId?{...current,deductionAgreementId:result.deductionAgreementId}:current);setCreatingAuthority(current=>current===creatingAuthority?null:current);}}/></>:<Button variant="outline" disabled={busy||!form.employmentId} onClick={()=>setCreatingAuthority(crypto.randomUUID())}>Registrar autorización aquí</Button>}</section>}<Field label="Inicio"><DatePicker today={today} value={form.startDate} onChange={value => set("startDate", value)}/></Field><Field label="Fin"><DatePicker today={today} value={form.endDate} onChange={value => set("endDate", value)}/></Field><Field label="Cantidad"><Input type="number" min={zeroQuantityAllowed ? 0 : 0.000001} step="any" value={form.quantity} onChange={event => set("quantity", event.currentTarget.valueAsNumber || 0)}/></Field><Field label={method === "PercentageOfBase" ? "Tarifa decimal" : "Valor unitario"}><FormattedNumberInput ariaLabel={method==="PercentageOfBase"?"Tarifa decimal":"Valor unitario"} value={form.unitAmount??""} kind={method==="PercentageOfBase"?"number":"currency"} onValueChange={value=>set("unitAmount",value)}/></Field><Field label={needsRate ? "Total calculado por el motor" : "Valor total"}><FormattedNumberInput ariaLabel={needsRate?"Total calculado por el motor":"Valor total"} value={form.totalAmount} kind="currency" disabled={needsRate} onValueChange={value=>set("totalAmount",value??0)}/></Field><Field label="Evidencia (URL)"><Input type="url" value={form.evidenceUrl ?? ""} onChange={event => set("evidenceUrl", event.target.value || null)}/></Field><Field label="Centro de costo de la novedad"><CostCenterSelect value={form.costCenterId??null} selectedName={centerName} ariaLabel="Centro de costo de la novedad" emptyLabel="Del contrato / Automático" disabled={busy} onChange={(id,row)=>{set("costCenterId",id);setCenterName(row?.name);}}/></Field><label className="space-y-2 sm:col-span-2"><span className="block text-sm font-medium">Notas</span><Input maxLength={500} value={form.notes ?? ""} onChange={event => set("notes", event.target.value || null)}/></label><DialogFooter className="sm:col-span-2"><Button disabled={busy || Boolean(creatingAuthority) || !form.employmentId || !form.conceptId || !form.noveltyTypeOptionId || (form.quantity < 0 || (form.quantity === 0 && !zeroQuantityAllowed)) || form.endDate < form.startDate || (selectedConcept?.requiresDeductionAgreement && !form.deductionAgreementId) || (needsRate && form.unitAmount === null)} onClick={() => void save()}>{busy ? "Guardando novedad…" : initial ? "Guardar novedad" : "Registrar novedad"}</Button></DialogFooter></div>; }
function PaymentForm({ options, selectedRun, loading, error, busy, setBusy, onRunChange, onRetry, onSaved }: {
    options: PayrollOptions; selectedRun: PayrollRun|null;
    loading: boolean; error: boolean; onRunChange:(id:string)=>void; onRetry:()=>void;
    busy:boolean;setBusy:(busy:boolean)=>void;
    onSaved:(result:PayrollPaymentBatch)=>void;
}) {
    const [runId,setRunId]=useState("");
    const businessId=useBusinessContextStore(state=>state.selectedBusinessId);
    const today=useBusinessDates(businessId).today();
    const current=selectedRun?.payrollRunId===runId?selectedRun:null;
    const methods=catalog(options,"payroll-payment-method");
    const pending=current?.payableEmployees??[];
    const missingMethod=pending.some(employee=>!methods.some(method=>method.optionId===employee.paymentMethodOptionId));
    async function save(method:PayrollCatalogOption,form:PaymentMethod,date:string,reference:string,paymentBatchId:string){
      const employees=pending.filter(employee=>employee.paymentMethodOptionId===method.optionId);
      if(busy||loading||error||!current||missingMethod||!employees.length||employees.some(employee=>!employee.bankDestinationComplete)
        ||form.methodCode!==method.code||!date||!reference.trim())return;
      setBusy(true);
      try{
        const result=await payrollApi.createPayment({paymentBatchId,payrollRunId:runId,paymentMethodOptionId:method.optionId,paymentMethodId:form.paymentMethodId,paymentDate:date,referenceNumber:reference});
        onSaved(result);toast.success("Grupo de pago confirmado y enviado a contabilidad.");
      }catch(error){toast.error(message(error,"No fue posible confirmar el pago."));}
      finally{setBusy(false);}
    }
    return <div className="grid gap-4">
      <Field label="Liquidación"><PayrollRunSelect value={runId} label="Liquidación" disabled={busy} onChange={run=>{setRunId(run.payrollRunId);onRunChange(run.payrollRunId);}}/></Field>
      {runId&&loading&&<p role="status">Consultando trabajadores pendientes…</p>}
      {runId&&error&&<p role="alert">No fue posible consultar la liquidación. <Button variant="link" onClick={onRetry}>Reintentar</Button></p>}
      {current&&<>
        <div className="flex flex-wrap items-center justify-between gap-2"><p className="text-sm font-medium">{pending.length} {pending.length===1?"trabajador pendiente":"trabajadores pendientes"} · {money.format(pending.reduce((sum,employee)=>sum+employee.amount,0))}</p><Button type="button" variant="outline" size="sm" disabled={busy||loading} onClick={onRetry}>Actualizar pendientes</Button></div>
        {missingMethod&&<p role="alert">Hay trabajadores cuyo medio contractual no está disponible en el catálogo. Revisa su configuración antes de pagar.</p>}
        {pending.length===0&&<p className="text-sm text-muted-foreground">No hay trabajadores pendientes. La causación debe estar contabilizada para pagar.</p>}
        {methods.map(method=><PayrollPaymentGroup key={`${runId}:${method.optionId}`} method={method}
          employees={pending.filter(employee=>employee.paymentMethodOptionId===method.optionId)} today={today} busy={busy||missingMethod||loading||error} onPay={save}/>)}
        <p className="text-xs text-muted-foreground">Se confirma el neto completo de cada integrante. Los demás grupos conservan sus valores pendientes. Registrar el pago no envía dinero al banco.</p>
      </>}
    </div>;
}

function PayrollPaymentGroup({method,employees,today,busy,onPay}:{
    method:PayrollCatalogOption;employees:PayrollRun["payableEmployees"];today:string;busy:boolean;
    onPay:(method:PayrollCatalogOption,form:PaymentMethod,date:string,reference:string,paymentBatchId:string)=>Promise<void>;
}) {
    const tenantId=useTenantContextStore(state=>state.selectedTenantId),businessId=useBusinessContextStore(state=>state.selectedBusinessId);
    const [expanded,setExpanded]=useState(false),[form,setForm]=useState<PaymentMethod|null>(null),[date,setDate]=useState(""),
      [reference,setReference]=useState(""),[paymentBatchId]=useState(()=>crypto.randomUUID());
    const total=employees.reduce((sum,employee)=>sum+employee.amount,0);
    const invalidDestination=employees.some(employee=>!employee.bankDestinationComplete);
    const contentId=`payroll-payment-${method.optionId}`;
    const open=expanded&&employees.length>0;
    return <section aria-label={`Grupo ${method.label}`} className="overflow-hidden rounded-xl border">
      <Button type="button" variant="ghost" className="h-auto w-full justify-between gap-3 whitespace-normal rounded-none p-4 text-left"
        aria-expanded={open} aria-controls={contentId} disabled={busy||employees.length===0} onClick={()=>setExpanded(!expanded)}>
        <span><span className="block font-semibold">{method.label}</span><span className="text-xs text-muted-foreground">{employees.length} {employees.length===1?"trabajador pendiente":"trabajadores pendientes"}</span></span>
        <span className="flex items-center gap-2"><b>{money.format(total)}</b><span aria-hidden="true">{open?"−":"+"}</span></span>
      </Button>
      {open&&<div id={contentId} className="grid gap-4 border-t p-4">
        <ul aria-label={`Trabajadores ${method.label}`} className="max-h-56 space-y-2 overflow-y-auto text-sm">{employees.map(employee=><li key={employee.payrollRunEmployeeId} className="flex justify-between gap-3"><span>{employee.employeeName}</span><b>{money.format(employee.amount)}</b></li>)}</ul>
        {invalidDestination&&<p role="alert" className="text-sm text-destructive">Un trabajador del grupo no tiene destino bancario completo en la liquidación.</p>}
        <Field label="Forma de pago"><PagedEntitySelect<PaymentMethod> ariaLabel="Forma de pago" value={form?.paymentMethodId??""}
          queryKey={["payment-method-choices",tenantId,businessId,"Payroll",method.code]} disabled={busy}
          selectedOption={form?{value:form.paymentMethodId,label:form.name,description:`${form.accountCode} · ${form.accountName}`}:null}
          searchPlaceholder="Buscar forma de pago…" emptyMessage="No hay formas habilitadas para nómina."
          loadPage={async(search,page,pageSize)=>{const result=await paymentMethodsApi.list({usage:"Payroll",methodCode:method.code,search,page,pageSize});
            return {...result,totalPages:Math.ceil(result.totalCount/result.pageSize)};}}
          getOption={item=>({value:item.paymentMethodId,label:item.name,description:`${item.code} · ${item.accountCode} · ${item.accountName}`})}
          onChange={(_id,_option,item)=>{if(item)setForm(item);}}/></Field>
        {form&&<p className="text-sm">Cuenta: {form.accountCode} · {form.accountName}. Se usará la cuenta vigente de esta forma al contabilizar.</p>}
        <Field label="Fecha de pago"><DatePicker today={today} disabled={busy} value={date} onChange={setDate}/></Field>
        <Field label="Referencia bancaria o de caja"><Input disabled={busy} maxLength={100} value={reference} onChange={event=>setReference(event.target.value)}/></Field>
        <Button disabled={busy||invalidDestination||!form||!date||!reference.trim()} onClick={()=>{if(form)void onPay(method,form,date,reference,paymentBatchId);}}>Confirmar pago del grupo</Button>
      </div>}
    </section>;
}
function PayrollAccountingMappings({ options, canConfigure }: {
    options: PayrollOptions;
    canConfigure: boolean;
}) { const today=useBusinessDates().today(),[effectiveFrom,setEffectiveFrom]=useState(today);const tenantId = useTenantContextStore(state => state.selectedTenantId ?? ""), businessId = useBusinessContextStore(state => state.selectedBusinessId), [definitions, setDefinitions] = useState<AccountingCategoryDefinition[]>([]), [mappings, setMappings] = useState<AccountingMapping[]>([]), [busy, setBusy] = useState<string | null>(null), [error, setError] = useState(""); const loadAccounting = useCallback(async () => { try {
    const [nextDefinitions, nextMappings] = await Promise.all([accountingApi.categoryDefinitions(), accountingApi.mappings()]);
    setDefinitions(nextDefinitions);
    setMappings(nextMappings);
    setError("");
}
catch (cause) {
    setError(message(cause, "No fue posible consultar la configuración contable."));
} }, []); useEffect(() => { void loadAccounting(); }, [loadAccounting, tenantId, businessId]); const categories = catalog(options, "payroll-accounting-category").map(option => ({ option, definition: definitions.find(item => item.category === option.code) })); async function save(category: string, accountId: string) { if (!tenantId || !businessId)
    return; setBusy(category); let saved=false; try {
    await accountingApi.setMapping({ tenantId, businessId, category, accountId, effectiveFrom, effectiveTo: null });
    saved=true;
    // This command has no mapping response; the one bounded configuration read
    // recovers the server-assigned identity and any adjusted effective ranges.
    setMappings(await accountingApi.mappings());
    setError("");toast.success("Cuenta de nómina actualizada.");
}
catch (cause) {
    if(saved){setError("La cuenta se guardó, pero no se pudo consultar su vigencia. Actualiza la configuración para continuar.");}
    else toast.error(message(cause, "No fue posible guardar la cuenta."));
}
finally {
    setBusy(null);
} } return <Card className="rounded-3xl lg:col-span-2"><CardHeader><CardTitle>Integración contable de nómina</CardTitle><CardDescription>Las categorías nacen del catálogo de nómina y las cuentas del plan contable. La liquidación aprobada y el pago usan estas asignaciones.</CardDescription></CardHeader><CardContent className="space-y-3"><Field label="Vigencia de las asignaciones generales"><DatePicker today={today} value={effectiveFrom} onChange={setEffectiveFrom} disabled={!canConfigure||busy!==null}/></Field><div className="rounded-2xl border bg-muted/30 p-4 text-sm"><Landmark className="mb-2 h-5 w-5 text-teal-700"/><b>Centros de costo</b><p className="mt-1 text-muted-foreground">El contrato puede asignar un centro de costo. Sin esa asignación, la nómina usa la regla general o el centro predeterminado de la sede. El pago cancela el auxiliar y centro de la causación original. El centro queda guardado en cada asiento y se conserva al reintentar. Consulta el gasto y sus aportes en el auxiliar contable, filtrando por centro.</p><Button asChild variant="link" className="mt-2 h-auto p-0"><Link href="/dashboard/accounting?section=centers">Ver centros y asignaciones <ExternalLink className="ml-2 h-3 w-3"/></Link></Button></div>{error && <p className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">{error} <Button variant="link" onClick={()=>void loadAccounting()}>Actualizar configuración</Button> <Link className="underline" href="/dashboard/accounting">Abrir Contabilidad</Link></p>}{categories.map(({ option, definition }) => { const active=mappings.filter(item=>item.category===option.code&&item.effectiveFrom<=effectiveFrom&&(!item.effectiveTo||item.effectiveTo>=effectiveFrom));const mapping = active.find(item => item.businessId === businessId) ?? active.find(item => item.businessId === null); return <div key={option.optionId} className="grid gap-2 rounded-xl border p-3 md:grid-cols-[minmax(220px,1fr)_minmax(260px,1.4fr)] md:items-center"><div><b className="text-sm">{option.label}</b><p className="text-xs text-muted-foreground">{option.code}</p></div><AccountSelect disabled={!canConfigure||busy!==null||Boolean(error)||!effectiveFrom} value={mapping?.accountId??""}
          ariaLabel={`Cuenta para ${option.label}`} resolveMissing={false} liabilityOnly={definition?.accountType==="Liability"}
          accounts={mappings.map(row=>({accountId:row.accountId,code:row.accountCode??"",name:row.accountName??"Cuenta asignada"}))}
          onChange={value=>void save(option.code,value)}/></div>; })}{!categories.length && <Empty text="No hay categorías contables de nómina activas."/>}</CardContent></Card>; }
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
