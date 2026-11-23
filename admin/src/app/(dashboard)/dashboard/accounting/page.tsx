"use client";

import {Fragment,Suspense,memo,useCallback,useEffect,useLayoutEffect,useMemo,useRef,useState,type ComponentProps,type FormEvent,type ReactNode} from "react";
import {useMutation,useQuery,useQueryClient} from "@tanstack/react-query";
import {defaultRangeExtractor,useVirtualizer} from "@tanstack/react-virtual";
import {useSearchParams} from "next/navigation";
import {AlertTriangle,BarChart3,BookOpen,Building2,CalendarRange,CheckCircle2,ChevronRight,Landmark,Link2,Loader2,Plus,ReceiptText,RefreshCw,Settings2,TableProperties,Trash2} from "lucide-react";
import {toast} from "sonner";
import {accountingApi,type AccountingAccount,type AccountingCostCenter,type AccountingOpenPosition,type AccountingOpeningInvoice,type BankAccount,type AccountMovementQuery} from "@/services/api/accounting";
import {emptyOpeningLine,type OpeningDraftLine} from "@/lib/accounting-opening-recovery";
import {useOpeningBalanceRecovery,useOpeningRecoveryScope,type OpeningRecoveryScope} from "@/hooks/use-opening-balance-recovery";
import {OpeningInvoiceFields,OpeningAdvanceFields} from "@/components/accounting/opening-invoice-fields";
import {FormattedNumberInput} from "@/components/ui/formatted-number-input";
import {PagedEntitySelect} from "@/components/forms/paged-entity-select";
import {CostCentersSection} from "@/components/accounting/cost-centers-section";
import {CostCenterSelect,useCostCenterLabels} from "@/components/accounting/cost-center-select";
import {PartySelect} from "@/components/parties/party-role-select";
import {AccountsSection} from "@/components/accounting/accounts-section";
import {AccountingDocumentDialog} from "@/components/accounting/accounting-document-dialog";
import {useTenantContextStore} from "@/stores/tenant-context-store";
import {useBusinessContextStore} from "@/stores/business-context-store";
import {useAuthStore} from "@/stores/auth-store";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Card,CardContent,CardHeader,CardTitle} from "@/components/ui/card";
import {Input as BaseInput} from "@/components/ui/input";
import {DatePicker} from "@/components/ui/date-picker";
import {Checkbox} from "@/components/ui/checkbox";
import {Label} from "@/components/ui/label";
import {Select,SelectContent,SelectItem,SelectTrigger,SelectValue} from "@/components/ui/select";
import {Dialog,DialogContent,DialogDescription,DialogHeader,DialogTitle} from "@/components/ui/dialog";
import {useReferenceOptions} from "@/hooks/use-reference-options";
import {WithholdingRulesWorkspace} from "./withholdings/withholding-rules-workspace";
import {BankReconciliationSection} from "./bank-reconciliation-section";
import {accountingDocumentTypeLabel,accountingStatusLabel} from "@/lib/accounting-labels";
import {ReportViewer} from "@/components/reports/report-viewer";
import {auxiliaryReportColumns,auxiliaryReportRows} from "@/lib/accounting-auxiliary-report";

type Section="setup"|"puc"|"banks"|"reconciliation"|"centers"|"periods"|"opening"|"withholdings"|"balance"|"reports"|"compliance";
type OpeningMode="ZeroDeclared"|"ImportedAndApproved";
const emptyOpeningInvoice=():AccountingOpeningInvoice=>({documentNumber:"",issuedOn:"",dueOn:"",partySiteId:""});
const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});
const auxiliaryMoney=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",minimumFractionDigits:2,maximumFractionDigits:4});
const year=new Date().getFullYear(),yearStart=`${year}-01-01`,yearEnd=`${year}-12-31`;

function Input(props:ComponentProps<typeof BaseInput>){
  if(props.type!=="date")return <BaseInput {...props}/>;
  const {value,onChange,disabled,className}=props;
  return <DatePicker value={String(value??"")} disabled={disabled} className={className}
    onChange={next=>onChange?.({target:{value:next},currentTarget:{value:next}} as never)}/>;
}

export default function AccountingConfigurationPage(){
  return <Suspense fallback={<p role="status">Cargando contabilidad…</p>}><AccountingConfigurationWorkspace/></Suspense>;
}

function AccountingConfigurationWorkspace(){
  const requested=useSearchParams().get("section");
  const tenantId=useTenantContextStore(state=>state.selectedTenantId),businessId=useBusinessContextStore(state=>state.selectedBusinessId),queryClient=useQueryClient();
  const key=["accounting",tenantId,businessId],enabled=Boolean(tenantId&&businessId);
  const [section,setSection]=useState<Section>(requested==="centers"||requested==="withholdings"?requested:"setup"),[scope,setScope]=useState<"tenant"|"business">("tenant"),[from,setFrom]=useState(yearStart),[to,setTo]=useState(yearEnd),[openingMode,setOpeningMode]=useState<OpeningMode>("ZeroDeclared");
  const user=useAuthStore(state=>state.user);
  const openingRecovery=useOpeningRecoveryScope(tenantId,user?.userId,businessId,yearStart,section!=="centers"&&(user?.permissions?.includes("accounting.configure")??false));
  const {effectiveOn:effectiveFrom,setEffectiveOn:setEffectiveFrom}=openingRecovery;
  useEffect(()=>{if(requested==="withholdings"||requested==="centers")setSection(requested)},[requested]);
  const openingModes=useReferenceOptions("accounting-opening-balance-mode",enabled&&section!=="centers");
  const accounts=useQuery({queryKey:[...key,"accounts"],queryFn:accountingApi.accounts,enabled:enabled&&section!=="puc"&&section!=="centers"});
  const banks=useQuery({queryKey:[...key,"bank-accounts"],queryFn:()=>accountingApi.bankAccounts(true),enabled:enabled&&(section==="banks"||section==="reconciliation")});
  const centers=useQuery({queryKey:["accounting-default-cost-center",tenantId],queryFn:()=>accountingApi.costCenterPage({isDefault:true,isActive:true,pageSize:1}),enabled:enabled&&section==="setup"});
  const periods=useQuery({queryKey:[...key,"periods"],queryFn:accountingApi.periods,enabled:enabled&&section!=="centers"});
  const mappings=useQuery({queryKey:[...key,"mappings"],queryFn:accountingApi.mappings,enabled:enabled&&section!=="centers"});
  const definitions=useQuery({queryKey:[...key,"category-definitions"],queryFn:accountingApi.categoryDefinitions,enabled:enabled&&section!=="centers"});
  const readiness=useQuery({queryKey:[...key,"readiness",effectiveFrom,openingMode],queryFn:()=>accountingApi.readiness(effectiveFrom,openingMode),enabled:enabled&&openingRecovery.ready&&section!=="centers"});
  const balance=useQuery({queryKey:[...key,"trial-balance",from,to],queryFn:()=>accountingApi.trialBalance(from,to),enabled:enabled&&section==="balance"});
  const refresh=async()=>queryClient.invalidateQueries({queryKey:key});
  const defaults=useMutation({mutationFn:accountingApi.ensureDefaults,onSuccess:async result=>{await refresh();toast.success(result.isReady?"Contabilidad Auraly configurada":"Configuración aplicada; revisa los pendientes")},onError:showError});
  const activate=useMutation({mutationFn:()=>accountingApi.activate({effectiveFrom,functionalCurrencyCode:"COP",openingBalanceMode:openingMode}),onSuccess:async result=>{
    queryClient.setQueryData([...key,"readiness",result.effectiveFrom,result.openingBalanceMode],result);
    // Activation returns readiness, not the posted opening batch. Refresh only
    // that visible aggregate to obtain its authoritative status and row version.
    if(result.openingBalanceMode==="ImportedAndApproved")await queryClient.invalidateQueries({queryKey:["accounting-opening-balance",businessId,result.effectiveFrom]});
    if(result.status==="Ready")toast.success(result.openingBalanceMode==="ZeroDeclared"?"Motor contable activado desde cero":"Motor contable activado con su asiento de apertura");
    else toast.warning("La apertura aún no está contabilizada. Revisa su estado y los motivos en Trazabilidad financiera.");
  },onError:showError});
  const mapping=useMutation({mutationFn:({category,accountId}:{category:string;accountId:string})=>accountingApi.setMapping({tenantId:tenantId!,businessId:scope==="business"?businessId:null,category,accountId,effectiveFrom:"2000-01-01",effectiveTo:null}),onSuccess:async()=>{await refresh();toast.success("Cuenta automática actualizada")},onError:showError});
  const accountRows=accounts.data??[],postingAccounts=accountRows.filter(item=>item.isActive&&item.allowsPosting);
  const activeMappings=useMemo(()=>{const result=new Map<string,string>();for(const item of [...(mappings.data??[])].sort((a,b)=>Number(Boolean(a.businessId))-Number(Boolean(b.businessId))))result.set(item.category,item.accountId);return result},[mappings.data]);
  const categoryRows=definitions.data??[],requiredCategories=categoryRows.filter(item=>item.isRequired),today=new Date().toISOString().slice(0,10),mappedCount=requiredCategories.filter(item=>activeMappings.has(item.category)).length,hasCenter=(centers.data?.items??[]).some(item=>item.isActive&&item.isDefault),hasPeriod=(periods.data??[]).some(item=>item.status==="Open"&&item.startsOn<=today&&item.endsOn>=today);
  const configurationReady=categoryRows.length>0&&mappedCount===requiredCategories.length&&hasCenter&&hasPeriod;
  const accountingReady=readiness.data?.status==="Ready";
  const canEditOpeningBalances=readiness.data?.canEditOpeningBalances;
  const openingEffectiveOn=canEditOpeningBalances===false&&readiness.data?.effectiveFrom?readiness.data.effectiveFrom:effectiveFrom;
  if(!tenantId||!businessId)return <EmptyContext/>;
  return <div className="space-y-5">
    <header className="overflow-hidden rounded-3xl bg-gradient-to-r from-slate-950 via-cyan-950 to-teal-700 p-6 text-white shadow-lg"><div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between"><div><p className="text-xs font-bold uppercase tracking-[.2em] text-cyan-200">Motor contable Auraly</p><h1 className="mt-2 text-3xl font-black">Contabilidad clara y automática</h1><p className="mt-2 max-w-2xl text-sm text-cyan-50/80">Configura una vez el PUC y revisa cómo ventas, compras, inventario, devoluciones y caja acumulan sus movimientos.</p></div>{section!=="centers"&&<Button className="h-12 bg-white text-slate-950 hover:bg-cyan-50" disabled={defaults.isPending||accountingReady} onClick={()=>defaults.mutate()}>{defaults.isPending?<Loader2 className="mr-2 h-4 w-4 animate-spin"/>:<Settings2 className="mr-2 h-4 w-4"/>}{configurationReady?"Verificar configuración":"Configurar Auraly"}</Button>}</div></header>
    {section!=="puc"&&section!=="centers"&&<div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4"><StatusCard label="Motor contable" value={accountingReady?"Activo y contabilizando":readiness.data?.status==="Configuring"?"En configuración":"Desactivado"} ok={accountingReady}/><StatusCard label="PUC activo" value={`${accountRows.filter(x=>x.isActive).length} cuentas`} ok={accountRows.length>=requiredCategories.length}/><StatusCard label="Cuentas automáticas" value={`${mappedCount}/${requiredCategories.length} conceptos`} ok={requiredCategories.length>0&&mappedCount===requiredCategories.length}/><StatusCard label="Periodo actual" value={hasPeriod?"Abierto":"Sin periodo abierto"} ok={hasPeriod}/></div>}
    {section!=="centers"&&!accountingReady&&<Card className="rounded-3xl border-amber-300 bg-amber-50/40"><CardHeader><CardTitle>Activación controlada</CardTitle><p className="text-sm text-muted-foreground">Auraly valida la configuración antes de activar. Los documentos anteriores a la fecha efectiva no se contabilizan automáticamente.</p></CardHeader><CardContent className="grid gap-4 lg:grid-cols-[14rem_minmax(18rem,1fr)_auto] lg:items-start"><Field label="Fecha efectiva"><Input type="date" value={effectiveFrom} disabled={activate.isPending} onChange={event=>setEffectiveFrom(event.target.value)}/></Field><Field label="Cómo iniciar"><Select value={openingMode} onValueChange={value=>setOpeningMode(value as OpeningMode)} disabled={openingModes.isLoading||activate.isPending}><SelectTrigger><SelectValue placeholder="Seleccionar inicio contable"/></SelectTrigger><SelectContent>{(openingModes.data??[]).map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select><small className="text-muted-foreground">{openingModes.data?.find(option=>option.code===openingMode)?.description}</small></Field><Button className="lg:mt-6" disabled={!openingRecovery.ready||!readiness.data||readiness.isFetching||readiness.data.blockingIssues.length>0||activate.isPending||openingModes.isLoading} onClick={()=>activate.mutate()}>{(activate.isPending||readiness.isFetching)&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Activar contabilidad</Button>{Boolean(readiness.data?.blockingIssues.length)&&<div className="lg:col-span-3 rounded-xl border border-amber-300 bg-amber-100/60 p-3 text-sm"><strong>Falta resolver:</strong><ul className="mt-2 list-disc pl-5">{readiness.data?.blockingIssues.map(issue=><li key={issue}>{issue}</li>)}</ul>{openingMode==="ImportedAndApproved"&&<Button type="button" variant="link" className="mt-1 h-auto p-0" onClick={()=>setSection("opening")}>Ir a Saldos iniciales</Button>}</div>}{readiness.data&&readiness.data.blockingIssues.length===0&&<div className="lg:col-span-3 rounded-xl border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-900"><strong>Configuración validada.</strong> {openingMode==="ImportedAndApproved"?"Los saldos aprobados se convertirán en el asiento de apertura.":"La contabilidad iniciará en cero."}</div>}</CardContent></Card>}
    <nav className="flex gap-1 overflow-x-auto rounded-2xl border bg-card p-1"><Nav active={section==="setup"} icon={Settings2} onClick={()=>setSection("setup")}>Automatización</Nav><Nav active={section==="puc"} icon={BookOpen} onClick={()=>setSection("puc")}>Plan de cuentas</Nav><Nav active={section==="banks"} icon={Landmark} onClick={()=>setSection("banks")}>Cuentas bancarias</Nav><Nav active={section==="reconciliation"} icon={Link2} onClick={()=>setSection("reconciliation")}>Conciliación bancaria</Nav><Nav active={section==="centers"} icon={Building2} onClick={()=>setSection("centers")}>Centros de costo</Nav><Nav active={section==="periods"} icon={CalendarRange} onClick={()=>setSection("periods")}>Periodos</Nav><Nav active={section==="opening"} icon={Landmark} onClick={()=>setSection("opening")}>Saldos iniciales</Nav><Nav active={section==="withholdings"} icon={ReceiptText} onClick={()=>setSection("withholdings")}>Retenciones</Nav><Nav active={section==="balance"} icon={TableProperties} onClick={()=>setSection("balance")}>Balance de prueba</Nav><Nav active={section==="reports"} icon={BarChart3} onClick={()=>setSection("reports")}>Libros y estados</Nav><Nav active={section==="compliance"} icon={TableProperties} onClick={()=>setSection("compliance")}>Fiscal y exógena</Nav></nav>
    {section==="setup"&&(
      <AutomationSection categories={categoryRows} accounts={postingAccounts} mapped={activeMappings} mappedCount={mappedCount} scope={scope} setScope={setScope} pending={mapping.isPending} onMap={(category,accountId)=>mapping.mutate({category,accountId})}/>
    )}
    {section==="puc"&&(
      <AccountsSection key={tenantId} tenantId={tenantId}/>
    )}
    {section==="banks"&&(
      <BankAccountsSection rows={banks.data??[]} onSaved={async saved=>{
        const changesAnotherPrimary=saved.isPrimary&&(banks.data??[]).some(row=>row.isPrimary&&row.bankAccountId!==saved.bankAccountId);
        queryClient.setQueryData<BankAccount[]>([...key,"bank-accounts"],previous=>[
          ...(previous??[]).filter(row=>row.bankAccountId!==saved.bankAccountId),saved
        ].sort((a,b)=>Number(b.isPrimary)-Number(a.isPrimary)||a.displayName.localeCompare(b.displayName)));
        // Selecting a new primary also changes the previous bank's row version.
        // Reload only this catalog when that second authoritative row changed.
        if(changesAnotherPrimary)await queryClient.invalidateQueries({queryKey:[...key,"bank-accounts"],exact:true});
      }}/>
    )}
    {section==="reconciliation"&&<BankReconciliationSection key={`${tenantId}:${businessId}`} contextKey={`${tenantId}:${businessId}`} banks={banks.data??[]}/>}
    {section==="centers"&&(
      <CostCentersSection key={`${tenantId}:${businessId}`} tenantId={tenantId} businessId={businessId}/>
    )}
    {section==="periods"&&(
      <PeriodsSection tenantId={tenantId} rows={periods.data??[]} refresh={refresh}/>
    )}
    {section==="opening"&&(
      openingRecovery.ready?<OpeningBalancesSection key={`${tenantId}:${businessId}:${openingEffectiveOn}`} recovery={openingRecovery} businessId={businessId} effectiveOn={openingEffectiveOn} setEffectiveOn={setEffectiveFrom} accounts={postingAccounts} canEditOpeningBalances={readiness.isPending?undefined:!!canEditOpeningBalances&&!activate.isPending} refresh={async()=>{
        // An in-flight validation may have read the batch before approval.
        // Discard it before requesting the authoritative post-approval readiness.
        const queryKey=[...key,"readiness"];
        await queryClient.cancelQueries({queryKey});
        await queryClient.invalidateQueries({queryKey});
      }}/>:<p role="status">Recuperando la fecha de apertura de este equipo…</p>
    )}
    {section==="withholdings"&&<WithholdingRulesWorkspace embedded/>}
    {section==="balance"&&(
      <BalanceSection key={`${tenantId}:${businessId}`} contextKey={`${tenantId}:${businessId}`} from={from} to={to} setFrom={setFrom} setTo={setTo} rows={balance.data??[]} loading={balance.isFetching} refresh={()=>void balance.refetch()}/>
    )}
    {section==="reports"&&(
      <ReportsSection contextKey={`${tenantId}:${businessId}`} from={from} to={to} setFrom={setFrom} setTo={setTo}/>
    )}
    {section==="compliance"&&(
      <ComplianceSection contextKey={`${tenantId}:${businessId}`} businessId={businessId} accounts={postingAccounts}/>
    )}
  </div>
}

function OpeningBalancesSection({recovery,businessId,effectiveOn,setEffectiveOn,accounts,canEditOpeningBalances,refresh}:{recovery:OpeningRecoveryScope;businessId:string;effectiveOn:string;setEffectiveOn:(value:string)=>void;accounts:AccountingAccount[];canEditOpeningBalances:boolean|undefined;refresh:()=>Promise<unknown>}){
  const client=useQueryClient();
  const permissions=useAuthStore(state=>state.user?.permissions);
  const canConfigure=permissions?.includes("accounting.configure")??false;
  const canActivate=permissions?.includes("accounting.activate")??false;
  const queryKey=["accounting-opening-balance",businessId,effectiveOn];
  const opening=useQuery({queryKey,queryFn:()=>accountingApi.openingBalance(effectiveOn),enabled:Boolean(businessId&&effectiveOn&&canEditOpeningBalances!==undefined)});
  const local=useOpeningBalanceRecovery(recovery,effectiveOn,opening.data,canConfigure?canEditOpeningBalances:false);
  const {batchId,rowVersion,description,setDescription,lines,setLines}=local;
  const scrollRef=useRef<HTMLDivElement>(null),pendingScroll=useRef<number|null>(null);
  const [focusedLine,setFocusedLine]=useState<string|null>(null);
  const focusedIndex=lines.findIndex(line=>line.lineId===focusedLine);
  const virtualized=lines.length>=100;
  // This mutable observer must stay outside React Compiler memoization.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer=useVirtualizer({count:lines.length,enabled:local.ready&&virtualized,getScrollElement:()=>scrollRef.current,
    getItemKey:index=>lines[index].lineId,estimateSize:index=>lines[index].newInvoice||lines[index].newAdvance?320:100,overscan:4,
    rangeExtractor:range=>[...new Set([...defaultRangeExtractor(range),...(focusedIndex<0?[]:[focusedIndex-1,focusedIndex,focusedIndex+1].filter(index=>index>=0&&index<lines.length))])].sort((a,b)=>a-b)});
  const items=virtualizer.getVirtualItems();
  const visibleIndices=virtualized?items.map(item=>item.index):lines.map((_,index)=>index);
  // Resolve only the visual buffer, in stable 20-line batches. Closed selectors
  // never resolve an identity individually or read the rest of the document.
  const labelBlocks=new Set(visibleIndices.map(index=>Math.floor(index/20)));
  const centerLabels=useCostCenterLabels([...labelBlocks].flatMap(block=>lines.slice(block*20,block*20+20).map(line=>line.costCenterId)));
  useLayoutEffect(()=>{
    if(pendingScroll.current===null)return;
    const index=pendingScroll.current;pendingScroll.current=null;
    if(virtualized)virtualizer.scrollToIndex(index,{align:"end"});
    else scrollRef.current?.querySelector(`[data-index="${index}"]`)?.scrollIntoView({block:"nearest"});
  },[lines,virtualized,virtualizer]);
  const status=opening.data?.status??"Draft",editable=canEditOpeningBalances&&status==="Draft"&&canConfigure;
  const update=useCallback((index:number,change:Partial<OpeningDraftLine>)=>setLines(current=>current.map((line,position)=>position===index?{...line,...change}:line)),[setLines]);
  const removeLine=useCallback((index:number)=>setLines(current=>current.filter((_,position)=>position!==index)),[setLines]);
  const normalized=lines.map(line=>({...line,debit:Number(line.debit||0),credit:Number(line.credit||0),newInvoice:line.newInvoice?{...line.newInvoice,documentNumber:line.newInvoice.documentNumber.trim()}:null,newAdvance:line.newAdvance?{...line.newAdvance,reference:line.newAdvance.reference.trim()}:null}));
  const dirty=!opening.data||local.hasChanges;
  const debit=normalized.reduce((total,line)=>total+line.debit,0),credit=normalized.reduce((total,line)=>total+line.credit,0),balanced=debit>0&&Math.abs(debit-credit)<0.0001;
  const valid=Boolean(description.trim())&&normalized.length>0&&normalized.length<=500&&normalized.every(line=>{
    const account=accounts.find(item=>item.accountId===line.accountId),invoice=line.newInvoice,advance=line.newAdvance;
    const kind=account?.semantics?.financialFunction;
    return Boolean(account&&line.description.trim()&&(line.debit>0)!==(line.credit>0)&&(!account.requiresParty||line.partyId)&&
      (!(kind==="Payable"||kind==="Receivable")||line.existingPosition||invoice)&&
      (!(kind==="SupplierAdvance"||kind==="CustomerAdvance")||line.existingPosition||advance)&&
      (!invoice||(line.partyId&&invoice.documentNumber&&invoice.partySiteId&&invoice.issuedOn&&invoice.dueOn&&invoice.issuedOn<effectiveOn&&invoice.dueOn>=invoice.issuedOn&&
        (kind==="Payable"&&line.credit>0&&line.debit===0||kind==="Receivable"&&line.debit>0&&line.credit===0)))&&
      (!advance||(line.partyId&&advance.reference&&advance.partySiteId&&advance.issuedOn&&advance.issuedOn<effectiveOn&&
        (kind==="CustomerAdvance"&&line.credit>0&&line.debit===0||kind==="SupplierAdvance"&&line.debit>0&&line.credit===0))));
  });
  const save=useMutation({mutationFn:()=>accountingApi.saveOpeningBalance({batchId,businessId,effectiveOn,currencyCode:"COP",description:description.trim(),rowVersion,lines:normalized.map(line=>({lineId:line.lineId,existingPosition:line.existingPosition,newInvoice:line.newInvoice,newAdvance:line.newAdvance,accountId:line.accountId,partyId:line.partyId,costCenterId:line.costCenterId,description:line.description.trim(),debit:line.debit,credit:line.credit}))}),onSuccess:value=>{local.acceptServer(value);client.setQueryData(queryKey,value);toast.success("Borrador de saldos iniciales guardado");},onError:showError});
  const approve=useMutation({mutationFn:()=>accountingApi.approveOpeningBalance(batchId),onSuccess:async value=>{local.acceptServer(value);client.setQueryData(queryKey,value);await refresh();toast.success("Saldos iniciales aprobados y listos para activar");},onError:showError});
  const accountingReady=canEditOpeningBalances===false;
  const captureEnabled=editable&&local.ready&&!local.conflict&&!opening.isPending&&!opening.isError&&!save.isPending&&!approve.isPending;
  const canApprove=canActivate&&canEditOpeningBalances&&status==="Draft"&&local.ready&&!local.conflict&&!opening.isPending&&!opening.isError;
  return <Card role="region" aria-label="Saldos iniciales de la empresa" className="overflow-hidden rounded-3xl">
    <CardHeader className="border-b bg-muted/30"><div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div><CardTitle>Saldos iniciales</CardTitle><p className="mt-1 text-sm text-muted-foreground">Registra el balance de apertura. Para cartera, detalla cada factura; para anticipos, registra la referencia y el saldo disponible.</p></div>
      <Field label="Fecha efectiva"><Input className="w-full sm:w-48" type="date" value={effectiveOn} disabled={accountingReady||save.isPending||approve.isPending} onChange={event=>setEffectiveOn(event.target.value)}/></Field>
    </div></CardHeader>
    <CardContent className="space-y-4 p-0">
      {canConfigure&&<div className="space-y-2 px-5 pt-4 text-sm">
        <p className="text-muted-foreground">{status!=="Draft"?"La apertura aprobada se conserva en la empresa.":local.restored?"Recuperamos la captura de este equipo. Guarda el borrador para conservarla en la empresa.":"Tu captura se conserva automáticamente en este equipo. Guardar borrador la conserva en la empresa; Aprobar saldos la deja lista para activar."}</p>
        {recovery.error&&<p role="alert" className="text-destructive">{recovery.error}</p>}
        {local.conflict&&<div role="alert" className="space-y-2 rounded-xl border border-amber-300 bg-amber-50 p-3"><p>El borrador guardado cambió. Elige si continúas con él o recuperas la captura de este equipo antes de volver a guardar.</p><div className="flex flex-wrap gap-2"><Button variant="outline" onClick={()=>local.resolveConflict(false)}>Usar borrador guardado</Button><Button onClick={()=>local.resolveConflict(true)}>Recuperar mi captura</Button></div></div>}
      </div>}
      <div className="grid gap-4 p-5 lg:grid-cols-[1fr_auto]">
        <Field label="Descripción del asiento"><Input value={description} disabled={!captureEnabled} maxLength={300} onChange={event=>setDescription(event.target.value)}/></Field>
        <div className="flex flex-wrap items-end gap-2">
          <Button type="button" variant="outline" disabled={!captureEnabled||lines.length>=500} onClick={()=>{pendingScroll.current=lines.length;setLines(current=>[...current,emptyOpeningLine()])}}><Plus className="mr-2 h-4 w-4"/>Agregar línea</Button>
          <Button type="button" disabled={!captureEnabled||lines.length>=500} onClick={()=>{
            const empty=lines.findIndex(line=>!line.accountId&&!line.partyId&&!line.debit&&!line.credit&&!line.newInvoice);
            const invoice={...emptyOpeningLine(),description:"Factura inicial",newInvoice:emptyOpeningInvoice()};
            pendingScroll.current=empty<0?lines.length:empty;
            setLines(current=>empty<0?[...current,invoice]:current.map((line,index)=>index===empty?{...invoice,lineId:line.lineId}:line));
          }}><ReceiptText className="mr-2 h-4 w-4"/>Agregar factura inicial</Button>
        </div>
      </div>
      <div ref={scrollRef} role="region" aria-label="Partidas de saldos iniciales" className="max-h-[32rem] overflow-auto border-y" onFocusCapture={event=>{
        const row=(event.target as HTMLElement).closest<HTMLElement>("[data-line-id]");
        if(row)setFocusedLine(row.dataset.lineId??null);
      }}><table className="block w-full text-sm lg:table lg:table-fixed">
        <colgroup className="hidden lg:table-column-group"><col className="w-[20%]"/><col className="w-[17%]"/><col className="w-[13%]"/><col/><col className="w-[12%]"/><col className="w-[12%]"/><col className="w-14"/></colgroup>
        <thead className="sticky top-0 z-10 hidden bg-muted shadow-sm lg:table-header-group"><tr><th className="p-2 text-left">Cuenta PUC</th><th className="p-2 text-left">Tercero</th><th className="p-2 text-left">Centro de costo</th><th className="p-2 text-left">Descripción / factura</th><th className="p-2 text-right">Débito</th><th className="p-2 text-right">Crédito</th><th><span className="sr-only">Acciones</span></th></tr></thead>
        {local.ready&&visibleIndices.map((index,position)=>{
          const line=lines[index],item=items[position],previous=items[position-1];
          const gap=virtualized?item.start-(previous?.end??0):0;
          return <Fragment key={line.lineId}>
            {gap>0&&<tbody aria-hidden className="block lg:table-row-group"><tr className="block lg:table-row"><td colSpan={7} className="block p-0 lg:table-cell" style={{height:gap}}/></tr></tbody>}
            <tbody data-index={index} data-line-id={line.lineId} ref={virtualized?virtualizer.measureElement:undefined} className="block lg:table-row-group">
              <OpeningBalanceRow line={line} index={index} accounts={accounts} businessId={businessId} effectiveOn={effectiveOn}
                captureEnabled={!!captureEnabled} centerLabel={line.costCenterId?centerLabels.data?.get(line.costCenterId):undefined} canRemove={lines.length>1} update={update} removeLine={removeLine}/>
            </tbody>
          </Fragment>;
        })}
        {virtualized&&items.length>0&&<tbody aria-hidden className="block lg:table-row-group"><tr className="block lg:table-row"><td colSpan={7} className="block p-0 lg:table-cell" style={{height:Math.max(0,virtualizer.getTotalSize()-items[items.length-1].end)}}/></tr></tbody>}
        <tfoot className="sticky bottom-0 z-10 block border-t-2 bg-background font-bold lg:table-footer-group"><tr className="grid grid-cols-2 gap-2 p-3 lg:table-row"><td className="col-span-2 lg:p-3" colSpan={4}>Totales {balanced&&<span className="ml-2 text-emerald-700">· Cuadrado</span>}</td><td className="text-right lg:p-2">{auxiliaryMoney.format(debit)}</td><td className="text-right lg:p-2">{auxiliaryMoney.format(credit)}</td><td/></tr></tfoot>
      </table></div>
      {local.ready&&<p className="px-5 text-xs text-muted-foreground">{lines.length} partidas · Desplázate para revisar el documento completo.</p>}
      {opening.isError&&<p role="alert" className="p-5 text-destructive">No se pudieron cargar los saldos iniciales. <Button variant="link" onClick={()=>void opening.refetch()}>Reintentar</Button></p>}
      {opening.isLoading&&<p className="p-6 text-center text-sm text-muted-foreground">Cargando saldos iniciales…</p>}
      {!opening.isPending&&!opening.isError&&!local.ready&&<p role="status" className="p-6 text-center text-sm text-muted-foreground">Recuperando la captura de saldos iniciales…</p>}
      {accountingReady&&<p className="px-5 text-sm text-muted-foreground">La apertura contable ya está cerrada. Sus saldos se conservan para consulta.</p>}
      <div className="flex flex-col gap-3 p-5 pt-0 sm:flex-row sm:items-center sm:justify-between"><p className="text-sm text-muted-foreground">Una partida por factura. La cuenta acumula sus saldos. Si la deuda ya existe, vincúlala para incorporarla sin crear otra. Guarda los cambios antes de aprobar.</p><div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" disabled={!captureEnabled||!valid||save.isPending||approve.isPending} onClick={()=>save.mutate()}>{save.isPending&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Guardar borrador</Button>
        <Button type="button" disabled={!canApprove||!opening.data||dirty||!valid||!balanced||save.isPending||approve.isPending} onClick={()=>approve.mutate()}>{approve.isPending&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Aprobar saldos</Button>
      </div></div>
    </CardContent>
  </Card>;
}

// Unchanged lines retain their controls while a single capture field changes.
const OpeningBalanceRow=memo(function OpeningBalanceRow({line,index,accounts,businessId,effectiveOn,captureEnabled,centerLabel,canRemove,update,removeLine}:{
  line:OpeningDraftLine;index:number;accounts:AccountingAccount[];businessId:string;effectiveOn:string;captureEnabled:boolean;
  centerLabel:AccountingCostCenter|undefined;canRemove:boolean;update:(index:number,change:Partial<OpeningDraftLine>)=>void;removeLine:(index:number)=>void;
}){
          const account=accounts.find(item=>item.accountId===line.accountId),kind=account?.semantics?.financialFunction;
          const portfolio=kind==="Payable"||kind==="Receivable";
          const advanceKind=kind==="SupplierAdvance"?"SupplierCredit":kind==="CustomerAdvance"?"CustomerCredit":null;
          const positionKind=portfolio?kind:advanceKind;
          const debitPosition=positionKind==="Receivable"||positionKind==="SupplierCredit";
          return <Fragment>
            <tr aria-label={`Partida de apertura ${index+1}`} className="grid min-w-0 grid-cols-2 gap-3 border-t p-3 lg:table-row lg:p-0 lg:[&>td]:align-top">
              <td className="col-span-2 min-w-0 lg:p-2"><span className="mb-1 block text-xs text-muted-foreground lg:hidden">Cuenta PUC</span>
                <Select value={line.accountId} disabled={!captureEnabled} onValueChange={accountId=>{const selected=accounts.find(item=>item.accountId===accountId);const functionCode=selected?.semantics?.financialFunction;update(index,{accountId,partyId:selected?.requiresParty?line.partyId:null,partyName:selected?.requiresParty?line.partyName:null,partyIdentification:selected?.requiresParty?line.partyIdentification:null,existingPosition:null,existingPositionDocumentNumber:null,newInvoice:functionCode==="Payable"||functionCode==="Receivable"?emptyOpeningInvoice():null,newInvoicePartySiteName:null,newAdvance:functionCode==="SupplierAdvance"||functionCode==="CustomerAdvance"?{reference:"",issuedOn:"",partySiteId:""}:null,newAdvancePartySiteName:null});}}>
                  <SelectTrigger aria-label={`Cuenta de apertura ${index+1}`}><SelectValue placeholder="Seleccionar cuenta"/></SelectTrigger>
                  <SelectContent>{accounts.filter(item=>!line.newInvoice||!!line.accountId||item.semantics?.financialFunction==="Payable"||item.semantics?.financialFunction==="Receivable").map(item=><SelectItem key={item.accountId} value={item.accountId}>{item.code} · {item.name}</SelectItem>)}</SelectContent>
                </Select>{account?.requiresParty&&!line.partyId&&<small className="text-amber-700">Esta cuenta exige tercero.</small>}
              </td>
              <td className="col-span-2 min-w-0 lg:p-2"><span className="mb-1 block text-xs text-muted-foreground lg:hidden">Tercero</span>
                <PartySelect value={line.partyId??"none"} disabled={!captureEnabled} leadingOptions={[{value:"none",label:"Sin tercero"}]}
                  selectedOption={line.partyId?{value:line.partyId,label:line.partyName??"Tercero seleccionado",description:line.partyIdentification}:null}
                  ariaLabel={`Tercero de apertura ${index+1}`} placeholder="Buscar cualquier tercero"
                  onChange={(value,party)=>update(index,{partyId:value==="none"?null:value,partyName:party?.displayName,partyIdentification:party?.identification,existingPosition:null,existingPositionDocumentNumber:null,newInvoice:line.newInvoice?{...line.newInvoice,partySiteId:""}:null,newInvoicePartySiteName:null,newAdvance:line.newAdvance?{...line.newAdvance,partySiteId:""}:null,newAdvancePartySiteName:null})}/>
              </td>
              <td className="col-span-2 min-w-0 lg:p-2"><span className="mb-1 block text-xs text-muted-foreground lg:hidden">Centro de costo</span><CostCenterSelect value={line.costCenterId} onChange={value=>update(index,{costCenterId:value})} selected={centerLabel} disabled={!captureEnabled} ariaLabel={`Centro de costo de apertura ${index+1}`}/></td>
              <td className="col-span-2 min-w-0 space-y-2 lg:p-2"><Input aria-label={`Descripción de apertura ${index+1}`} value={line.description} disabled={!captureEnabled} maxLength={300} onChange={event=>update(index,{description:event.target.value})}/>
                {positionKind&&<><Button type="button" size="sm" variant="ghost" className="h-auto min-h-8 w-full justify-start whitespace-normal px-0 text-left text-xs" disabled={!captureEnabled}
                  onClick={()=>update(index,{newInvoice:portfolio&&!line.newInvoice?emptyOpeningInvoice():null,
                    newAdvance:advanceKind&&!line.newAdvance?{reference:"",issuedOn:"",partySiteId:""}:null,
                    newInvoicePartySiteName:null,newAdvancePartySiteName:null,existingPosition:null,existingPositionDocumentNumber:null})}>
                  {line.newInvoice||line.newAdvance?`Vincular ${advanceKind?"anticipo":"factura"} existente`:`Registrar ${advanceKind?"anticipo":"factura"} inicial ${advanceKind?"nuevo":"nueva"}`}</Button>
                {!line.newInvoice&&!line.newAdvance&&<PagedEntitySelect<AccountingOpenPosition>
                  queryKey={["opening-position-options",businessId,positionKind,line.partyId]} value={line.existingPosition?.subledgerId??""}
                  selectedOption={line.existingPosition?{value:line.existingPosition.subledgerId,label:line.existingPositionDocumentNumber??(advanceKind?"Anticipo seleccionado":"Obligación seleccionada")}:null}
                  disabled={!captureEnabled||!line.partyId} placeholder={advanceKind?"Seleccionar anticipo existente":"Seleccionar factura existente"}
                  ariaLabel={`Vincular ${advanceKind?"anticipo":"obligación"} anterior en partida ${index+1}`} searchPlaceholder="Buscar referencia…"
                  emptyMessage={advanceKind?"No hay anticipos disponibles para este tercero.":"No hay obligaciones pendientes para este tercero."}
                  loadPage={async(search,page,pageSize)=>{const result=await accountingApi.openPositions({subledgerKind:positionKind,partyId:line.partyId!,advancesOnly:!!advanceKind,search,page,pageSize});return {...result,totalPages:Math.ceil(result.totalCount/result.pageSize)};}}
                  getOption={item=>({value:item.subledgerId,label:item.documentNumber,description:`${item.partyName} · Saldo ${auxiliaryMoney.format(item.outstandingAmount)}${item.openingEntryId?" · Ya tiene apertura contable":""}`})}
                  onChange={(_id,_option,item)=>{if(item)update(index,{existingPosition:{subledgerKind:positionKind,subledgerId:item.subledgerId},existingPositionDocumentNumber:item.documentNumber,
                    description:`Saldo ${item.documentNumber}`,debit:debitPosition?String(item.outstandingAmount):"",credit:!debitPosition?String(item.outstandingAmount):""});}}
                  onClear={line.existingPosition?()=>update(index,{existingPosition:null,existingPositionDocumentNumber:null}):undefined}/>}</>}
                {line.newInvoice&&!portfolio&&<p className="text-xs text-muted-foreground">Selecciona una cuenta que maneje cuentas por cobrar o por pagar.</p>}
              </td>
              <td className="min-w-0 lg:p-2"><span className="mb-1 block text-xs text-muted-foreground lg:hidden">Débito</span><FormattedNumberInput ariaLabel={`Débito de apertura ${index+1}`} className="text-right" kind="currency" value={line.debit} disabled={!captureEnabled||!!line.newInvoice||!!line.newAdvance} onValueChange={value=>update(index,{debit:value===null?"":String(value),credit:value===null?line.credit:""})}/></td>
              <td className="min-w-0 lg:p-2"><span className="mb-1 block text-xs text-muted-foreground lg:hidden">Crédito</span><FormattedNumberInput ariaLabel={`Crédito de apertura ${index+1}`} className="text-right" kind="currency" value={line.credit} disabled={!captureEnabled||!!line.newInvoice||!!line.newAdvance} onValueChange={value=>update(index,{credit:value===null?"":String(value),debit:value===null?line.debit:""})}/></td>
              <td className="col-span-2 lg:p-2"><Button type="button" size="icon" variant="ghost" disabled={!captureEnabled||!canRemove} aria-label="Eliminar línea" onClick={()=>removeLine(index)}><Trash2 className="h-4 w-4"/></Button></td>
            </tr>
            {line.newInvoice&&portfolio&&<tr className="block lg:table-row"><td colSpan={7} className="block p-3 lg:table-cell"><OpeningInvoiceFields businessId={businessId} partyId={line.partyId} kind={kind} lineNumber={index+1}
              invoice={line.newInvoice} siteName={line.newInvoicePartySiteName} effectiveOn={effectiveOn} amount={Number((kind==="Receivable"?line.debit:line.credit)||0)} disabled={!captureEnabled}
              onInvoice={(newInvoice,newInvoicePartySiteName)=>update(index,{newInvoice,newInvoicePartySiteName})}
              onAmount={amount=>update(index,{debit:kind==="Receivable"&&amount>0?String(amount):"",credit:kind==="Payable"&&amount>0?String(amount):""})}/></td></tr>}
            {line.newAdvance&&advanceKind&&<tr className="block lg:table-row"><td colSpan={7} className="block p-3 lg:table-cell"><OpeningAdvanceFields businessId={businessId} partyId={line.partyId}
              kind={advanceKind==="CustomerCredit"?"Receivable":"Payable"} lineNumber={index+1} advance={line.newAdvance} siteName={line.newAdvancePartySiteName}
              effectiveOn={effectiveOn} amount={Number((debitPosition?line.debit:line.credit)||0)} disabled={!captureEnabled}
              onAdvance={(newAdvance,newAdvancePartySiteName)=>update(index,{newAdvance,newAdvancePartySiteName})}
              onAmount={amount=>update(index,{debit:debitPosition&&amount>0?String(amount):"",credit:!debitPosition&&amount>0?String(amount):""})}/></td></tr>}

          </Fragment>;
});

function AutomationSection({categories,accounts,mapped,mappedCount,scope,setScope,pending,onMap}:{categories:Array<{category:string;displayName:string;isRequired:boolean}>;accounts:Array<{accountId:string;code:string;name:string}>;mapped:Map<string,string>;mappedCount:number;scope:"tenant"|"business";setScope:(value:"tenant"|"business")=>void;pending:boolean;onMap:(category:string,accountId:string)=>void}){const requiredCount=categories.filter(item=>item.isRequired).length;return <Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b bg-muted/30"><div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"><div><CardTitle>Cuentas automáticas</CardTitle><p className="mt-1 text-sm text-muted-foreground">Cada concepto debe apuntar a una cuenta contabilizable. {mappedCount}/{requiredCount} completos.</p></div><Select value={scope} onValueChange={value=>setScope(value as "tenant"|"business")}><SelectTrigger className="w-56"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="tenant">Regla general de la empresa</SelectItem><SelectItem value="business">Excepción para esta sede</SelectItem></SelectContent></Select></div></CardHeader><CardContent className="p-0"><div className="grid lg:grid-cols-2">{categories.map(item=><div key={item.category} className="grid grid-cols-[minmax(0,1fr)_minmax(13rem,1.2fr)] items-center gap-3 border-b p-4 lg:odd:border-r"><strong className="text-sm">{item.displayName}</strong><Select value={mapped.get(item.category)??""} disabled={pending||!accounts.length} onValueChange={accountId=>onMap(item.category,accountId)}><SelectTrigger className={mapped.has(item.category)?"":"border-amber-400 bg-amber-50"}><SelectValue placeholder="Seleccionar cuenta"/></SelectTrigger><SelectContent>{accounts.map(account=><SelectItem key={account.accountId} value={account.accountId}>{account.code} · {account.name}</SelectItem>)}</SelectContent></Select></div>)}</div></CardContent></Card>}

function BankAccountsSection({rows,onSaved}:{rows:BankAccount[];onSaved:(saved:BankAccount)=>Promise<void>}){
  const types=useReferenceOptions("bank-account-type");
  const [editing,setEditing]=useState<BankAccount|null>(null),[bankName,setBankName]=useState(""),[accountNumber,setAccountNumber]=useState(""),[displayName,setDisplayName]=useState(""),[accountTypeOptionId,setAccountTypeOptionId]=useState(""),[isPrimary,setPrimary]=useState(false),[isActive,setActive]=useState(true);
  const reset=()=>{setEditing(null);setBankName("");setAccountNumber("");setDisplayName("");setAccountTypeOptionId("");setPrimary(false);setActive(true)};
  const edit=(row:BankAccount)=>{setEditing(row);setBankName(row.bankName);setAccountNumber(row.accountNumber);setDisplayName(row.displayName);setAccountTypeOptionId(row.accountTypeOptionId);setPrimary(row.isPrimary);setActive(row.isActive)};
  const save=useMutation({mutationFn:()=>accountingApi.saveBankAccount({bankAccountId:editing?.bankAccountId??crypto.randomUUID(),accountTypeOptionId,bankName,accountNumber,displayName,isPrimary,isActive,rowVersion:editing?.rowVersion??null}),onSuccess:async saved=>{await onSaved(saved);reset();toast.success("Cuenta bancaria guardada. Sus auxiliares se configuran en el PUC.")},onError:showError});
  return <div className="grid gap-5 xl:grid-cols-[25rem_1fr]">
    <Card className="h-fit rounded-3xl"><CardHeader><CardTitle>{editing?"Editar cuenta bancaria":"Nueva cuenta bancaria"}</CardTitle><p className="text-sm text-muted-foreground">Registra una vez cada cuenta bancaria real. En el PUC puedes vincular sus auxiliares por medio de pago, con la numeración que necesites.</p></CardHeader><CardContent>
      <form className="space-y-4" onSubmit={event=>submit(event,save.mutate)}>
        <Field label="Banco"><Input value={bankName} onChange={event=>setBankName(event.target.value)} required/></Field>
        <Field label="Nombre para mostrar"><Input value={displayName} onChange={event=>setDisplayName(event.target.value)} placeholder="Ej. Bancolombia principal" required/></Field>
        <Field label="Tipo de cuenta"><Select value={accountTypeOptionId} onValueChange={setAccountTypeOptionId}><SelectTrigger><SelectValue placeholder="Seleccionar tipo"/></SelectTrigger><SelectContent>{(types.data??[]).map(item=><SelectItem key={item.id} value={item.id}>{item.label}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Número de cuenta"><Input value={accountNumber} onChange={event=>setAccountNumber(event.target.value)} required/></Field>
        <label className="flex items-center gap-2 text-sm"><Checkbox checked={isPrimary} onCheckedChange={checked=>setPrimary(checked===true)}/> Cuenta principal para transferencias</label>
        {editing&&<label className="flex items-center gap-2 text-sm"><Checkbox checked={isActive} onCheckedChange={checked=>setActive(checked===true)}/> Cuenta activa</label>}
        <div className="flex gap-2"><Button className="flex-1" disabled={save.isPending||!accountTypeOptionId}>Guardar cuenta bancaria</Button>{editing&&<Button type="button" variant="outline" onClick={reset}>Cancelar</Button>}</div>
      </form>
    </CardContent></Card>
    <Card className="overflow-hidden rounded-3xl"><CardHeader><CardTitle>Cuentas bancarias</CardTitle></CardHeader><CardContent className="p-0"><div className="overflow-x-auto">
      <table className="w-full text-sm"><thead className="bg-muted/50 text-left"><tr><th className="p-3">Cuenta</th><th>Banco</th><th>Auxiliares del PUC</th><th>Estado</th><th/></tr></thead><tbody>{rows.map(row=><tr key={row.bankAccountId} className="border-t"><td className="p-3"><strong>{row.displayName}</strong><small className="block text-muted-foreground">{row.accountTypeName} · {row.accountNumber}</small></td><td>{row.bankName}{row.isPrimary&&<Badge className="ml-2" variant="secondary">Principal</Badge>}</td><td>{row.auxiliaryCount===1?`${row.accountingAccountCode} · ${row.accountingAccountName}`:`${row.auxiliaryCount} auxiliares vinculados`}</td><td><Badge variant={row.isActive?"secondary":"outline"}>{row.isActive?"Activa":"Inactiva"}</Badge></td><td className="pr-3"><Button size="sm" variant="ghost" onClick={()=>edit(row)}>Editar</Button></td></tr>)}</tbody></table>
      {!rows.length&&<p className="p-8 text-center text-sm text-muted-foreground">No hay cuentas bancarias configuradas.</p>}
    </div></CardContent></Card>
  </div>
}
function PeriodsSection({tenantId,rows,refresh}:{tenantId:string;rows:Array<{periodId:string;name:string;startsOn:string;endsOn:string;status:string}>;refresh:()=>Promise<unknown>}){const [name,setName]=useState(String(year)),[starts,setStarts]=useState(yearStart),[ends,setEnds]=useState(yearEnd);const create=useMutation({mutationFn:()=>accountingApi.createPeriod({periodId:crypto.randomUUID(),tenantId,startsOn:starts,endsOn:ends,name}),onSuccess:async()=>{await refresh();toast.success("Periodo creado")},onError:showError});const close=useMutation({mutationFn:accountingApi.closePeriod,onSuccess:async()=>{await refresh();toast.success("Periodo cerrado")},onError:showError});return <div className="grid gap-5 lg:grid-cols-[24rem_1fr]"><Card className="h-fit rounded-3xl"><CardHeader><CardTitle>Nuevo periodo</CardTitle></CardHeader><CardContent><form className="space-y-4" onSubmit={event=>submit(event,create.mutate)}><Field label="Nombre"><Input value={name} onChange={event=>setName(event.target.value)} required/></Field><Field label="Desde"><Input type="date" value={starts} onChange={event=>setStarts(event.target.value)} required/></Field><Field label="Hasta"><Input type="date" value={ends} onChange={event=>setEnds(event.target.value)} required/></Field><Button className="w-full" disabled={create.isPending}>Crear periodo</Button></form></CardContent></Card><Card className="rounded-3xl"><CardHeader><CardTitle>Periodos contables</CardTitle></CardHeader><CardContent className="space-y-2">{rows.map(row=><div key={row.periodId} className="flex items-center gap-3 rounded-2xl border p-4"><CalendarRange className="h-5 w-5 text-teal-700"/><div className="min-w-0 flex-1"><strong>{row.name}</strong><small className="block text-muted-foreground">{row.startsOn} a {row.endsOn}</small></div><Badge variant={row.status==="Open"?"secondary":"outline"}>{row.status==="Open"?"Abierto":"Cerrado"}</Badge>{row.status==="Open"&&<Button size="sm" variant="outline" disabled={close.isPending} onClick={()=>close.mutate(row.periodId)}>Cerrar</Button>}</div>)}{!rows.length&&<p className="text-sm text-muted-foreground">No hay periodos.</p>}</CardContent></Card></div>}

function AccountSelect({value,accounts,onChange}:{value:string;accounts:Array<{accountId:string;code:string;name:string}>;onChange:(value:string)=>void}){return <Select value={value} onValueChange={onChange}><SelectTrigger><SelectValue placeholder="Seleccionar cuenta"/></SelectTrigger><SelectContent>{accounts.map(item=><SelectItem key={item.accountId} value={item.accountId}>{item.code} · {item.name}</SelectItem>)}</SelectContent></Select>}

function ComplianceSection({contextKey,businessId,accounts}:{contextKey:string;businessId:string;accounts:Array<{accountId:string;code:string;name:string}>}){
  const queryClient=useQueryClient(),[taxYear,setTaxYear]=useState(2025),[selected,setSelected]=useState(""),[accountId,setAccountId]=useState(""),[concept,setConcept]=useState(""),[targetField,setTargetField]=useState("value"),[businessScope,setBusinessScope]=useState(true);
  const definitions=useQuery({queryKey:["compliance-definitions",contextKey,taxYear],queryFn:()=>accountingApi.complianceDefinitions(taxYear)});
  const current=definitions.data?.find(item=>`${item.formatCode}:${item.formatVersion}`===selected)??definitions.data?.[0];
  const mappings=useQuery({queryKey:["compliance-mappings",contextKey,taxYear,current?.formatCode],queryFn:()=>accountingApi.complianceMappings(taxYear,current?.formatCode),enabled:Boolean(current)});
  const runs=useQuery({queryKey:["compliance-runs",contextKey,taxYear],queryFn:()=>accountingApi.complianceRuns(taxYear)});
  const save=useMutation({mutationFn:()=>accountingApi.setComplianceMapping({businessId:businessScope?businessId:null,authorityCode:current!.authorityCode,taxYear:current!.taxYear,formatCode:current!.formatCode,formatVersion:current!.formatVersion,accountId,conceptCode:concept,targetField}),onSuccess:async()=>{await queryClient.invalidateQueries({queryKey:["compliance-mappings",contextKey]});setConcept("");toast.success("Mapeo fiscal guardado")},onError:showError});
  const generate=useMutation({mutationFn:()=>accountingApi.generateComplianceReport({authorityCode:current!.authorityCode,taxYear:current!.taxYear,formatCode:current!.formatCode,formatVersion:current!.formatVersion,periodFrom:`${taxYear}-01-01`,periodTo:`${taxYear}-12-31`}),onSuccess:async result=>{await queryClient.invalidateQueries({queryKey:["compliance-runs",contextKey]});toast[result.status==="Ready"?"success":"warning"](result.status==="Ready"?"Informe reproducible generado":"Informe bloqueado: revisa las validaciones")},onError:showError});
  const definitionValue=current?`${current.formatCode}:${current.formatVersion}`:"";
  return <div className="space-y-5"><Card className="rounded-3xl"><CardHeader><CardTitle>Informes fiscales e información exógena</CardTitle><p className="text-sm text-muted-foreground">Catálogo versionado por autoridad, año, formato y resolución. Cada ejecución congela mapeos, datos de tercero, totales y huella SHA-256; el CSV es una base auditable y no reemplaza el prevalidador ni la presentación ante DIAN.</p></CardHeader><CardContent className="grid gap-4 lg:grid-cols-[10rem_1fr_auto] lg:items-end"><Field label="Año gravable"><Input type="number" min={2000} max={2200} value={taxYear} onChange={event=>{setTaxYear(Number(event.target.value));setSelected("")}}/></Field><Field label="Informe"><Select value={selected||definitionValue} onValueChange={setSelected}><SelectTrigger><SelectValue placeholder="Seleccionar definición"/></SelectTrigger><SelectContent>{(definitions.data??[]).map(item=><SelectItem key={`${item.formatCode}-${item.formatVersion}`} value={`${item.formatCode}:${item.formatVersion}`}>{item.reportKind==="Exogenous"?"Exógena":"Fiscal"} · {item.formatCode} v{item.formatVersion} · {item.name}</SelectItem>)}</SelectContent></Select></Field><Button disabled={!current||generate.isPending} onClick={()=>generate.mutate()}>{generate.isPending&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Generar corte anual</Button>{current&&<div className="lg:col-span-3 grid gap-2 rounded-2xl border bg-muted/30 p-4 text-sm md:grid-cols-3"><span><strong>Autoridad:</strong> {current.authorityCode}</span><span><strong>Resolución:</strong> {current.resolutionNumber}</span><span><strong>Anexo:</strong> {current.technicalAnnex}</span><span className="md:col-span-3 break-all font-mono text-xs"><strong>SHA-256 fuente:</strong> {current.sourceSha256}</span></div>}</CardContent></Card>
  {current&&<div className="grid gap-5 xl:grid-cols-[25rem_1fr]"><Card className="h-fit rounded-3xl"><CardHeader><CardTitle>Mapeo de concepto</CardTitle><p className="text-sm text-muted-foreground">La clasificación tributaria requiere decisión contable explícita; no se infiere silenciosamente.</p></CardHeader><CardContent><form className="space-y-4" onSubmit={event=>submit(event,save.mutate)}><Field label="Cuenta contable"><AccountSelect value={accountId} accounts={accounts} onChange={setAccountId}/></Field><Field label="Concepto DIAN"><Input value={concept} maxLength={24} onChange={event=>setConcept(event.target.value)} placeholder="Ej. 5001" required/></Field><Field label="Campo de valor"><Input value={targetField} maxLength={64} onChange={event=>setTargetField(event.target.value)} placeholder="Ej. deductiblePayment" required/></Field><label className="flex items-center gap-2 text-sm"><Checkbox checked={businessScope} onCheckedChange={checked=>setBusinessScope(checked===true)}/> Aplicar solo a esta sede</label><Button className="w-full" disabled={!accountId||!concept||save.isPending}>Guardar mapeo</Button></form></CardContent></Card><Card className="overflow-hidden rounded-3xl"><CardHeader><CardTitle>Mapeos activos · {current.formatCode} v{current.formatVersion}</CardTitle></CardHeader><CardContent className="p-0"><div className="overflow-x-auto"><table className="w-full text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Cuenta</th><th className="text-left">Concepto</th><th className="text-left">Campo</th><th className="pr-3 text-left">Alcance</th></tr></thead><tbody>{(mappings.data??[]).map(item=><tr key={item.mappingId} className="border-t"><td className="p-3">{item.accountCode} · {item.accountName}</td><td className="font-mono">{item.conceptCode}</td><td className="font-mono">{item.targetField}</td><td className="pr-3">{item.businessId?"Sede":"Empresa"}</td></tr>)}</tbody></table>{!mappings.data?.length&&<p className="p-8 text-center text-sm text-muted-foreground">Sin mapeos. La ejecución quedará bloqueada hasta configurar al menos uno.</p>}</div></CardContent></Card></div>}
  <Card className="overflow-hidden rounded-3xl"><CardHeader><CardTitle>Ejecuciones reproducibles</CardTitle></CardHeader><CardContent className="p-0"><div className="overflow-x-auto"><table className="w-full min-w-[850px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Creado</th><th className="text-left">Informe</th><th className="text-left">Estado</th><th className="text-right">Filas</th><th className="text-right">Control</th><th className="pr-3 text-left">Salida</th></tr></thead><tbody>{(runs.data??[]).map(run=><tr key={run.runId} className="border-t align-top"><td className="p-3 whitespace-nowrap">{new Date(run.createdAt).toLocaleString("es-CO")}</td><td>{run.formatCode} v{run.formatVersion}<small className="block text-muted-foreground">{run.name}</small></td><td><Badge variant={run.status==="Ready"?"secondary":"destructive"}>{run.status==="Ready"?"Listo":"Bloqueado"}</Badge>{run.validations.map(value=><small key={`${value.code}-${value.partyId}`} className="mt-1 block max-w-md text-destructive">{value.code}: {value.message}</small>)}</td><td className="text-right">{run.rowCount}</td><td className="text-right">{money.format(run.controlTotal)}</td><td className="pr-3">{run.status==="Ready"?<a className="font-semibold text-teal-700 underline" href={accountingApi.complianceArtifactUrl(run.runId)}>Descargar CSV auditable</a>:"Corrige y vuelve a generar"}</td></tr>)}</tbody></table>{!runs.data?.length&&<p className="p-8 text-center text-sm text-muted-foreground">Todavía no hay ejecuciones para este año.</p>}</div></CardContent></Card></div>
}

function ReportsSection({contextKey,from,to,setFrom,setTo}:{contextKey:string;from:string;to:string;setFrom:(value:string)=>void;setTo:(value:string)=>void}){
  const reportTypes=useReferenceOptions("accounting-report-type");
  const [report,setReport]=useState<"ledger"|"journal"|"balance"|"income"|"exceptions">("ledger");
  const [accountingDocumentId,setAccountingDocumentId]=useState<string>();
  const ledger=useQuery({queryKey:["accounting-report",contextKey,"ledger",from,to],queryFn:()=>accountingApi.generalLedger(from,to),enabled:report==="ledger"});
  const journal=useQuery({queryKey:["accounting-report",contextKey,"journal",from,to],queryFn:()=>accountingApi.journal(from,to),enabled:report==="journal"});
  const balance=useQuery({queryKey:["accounting-report",contextKey,"balance",to],queryFn:()=>accountingApi.balanceSheet(to),enabled:report==="balance"});
  const income=useQuery({queryKey:["accounting-report",contextKey,"income",from,to],queryFn:()=>accountingApi.incomeStatement(from,to),enabled:report==="income"});
  const exceptions=useQuery({queryKey:["accounting-report",contextKey,"exceptions",from,to],queryFn:()=>accountingApi.exceptions(from,to),enabled:report==="exceptions"});
  return <><Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b"><div className="grid gap-3 lg:grid-cols-[1fr_15rem_11rem_11rem]"><div><CardTitle>Libros y estados básicos</CardTitle><p className="mt-1 text-sm text-muted-foreground">Información producida exclusivamente desde asientos contabilizados; las excepciones muestran lo pendiente.</p></div><Field label="Informe"><Select value={report} onValueChange={value=>setReport(value as typeof report)} disabled={reportTypes.isLoading}><SelectTrigger><SelectValue placeholder="Seleccionar informe"/></SelectTrigger><SelectContent>{(reportTypes.data??[]).map(item=><SelectItem key={item.code} value={item.code}>{item.label}</SelectItem>)}</SelectContent></Select></Field><Field label="Desde"><Input type="date" value={from} disabled={report==="balance"} onChange={event=>setFrom(event.target.value)}/></Field><Field label="Hasta"><Input type="date" value={to} onChange={event=>setTo(event.target.value)}/></Field></div></CardHeader><CardContent className="p-0"><div className="overflow-x-auto">
    {report==="ledger"&&<table className="w-full text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Cuenta</th><th className="text-left">Nombre</th><th className="text-right">Inicial</th><th className="text-right">Débito</th><th className="text-right">Crédito</th><th className="pr-3 text-right">Final</th></tr></thead><tbody>{(ledger.data??[]).map(row=><tr key={row.accountCode} className="border-t"><td className="p-3 font-mono">{row.accountCode}</td><td>{row.accountName}</td><td className="text-right">{money.format(row.openingBalance)}</td><td className="text-right">{money.format(row.debit)}</td><td className="text-right">{money.format(row.credit)}</td><td className="pr-3 text-right font-semibold">{money.format(row.closingBalance)}</td></tr>)}</tbody></table>}
    {report==="journal"&&<table className="w-full min-w-[900px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha / asiento</th><th className="text-left">Documento</th><th className="text-left">Cuenta</th><th className="text-left">Descripción</th><th className="text-left">Centro</th><th className="text-right">Débito</th><th className="pr-3 text-right">Crédito</th></tr></thead><tbody>{(journal.data??[]).map(row=><tr onClick={()=>setAccountingDocumentId(row.sourceDocumentId)} key={`${row.entryId}-${row.lineNumber}`} className="cursor-pointer border-t transition hover:bg-muted/40"><td className="p-3">{new Date(row.occurredAt).toLocaleDateString("es-CO")}<small className="block font-mono">{row.entryNumber}</small></td><td>{accountingDocumentTypeLabel(row.sourceDocumentType)}<small className="block text-muted-foreground">{row.sourceDocumentId.slice(0,8)}</small></td><td>{row.accountCode} · {row.accountName}</td><td>{row.description}</td><td>{row.costCenterCode?`${row.costCenterCode} · ${row.costCenterName}`:"Sin centro"}</td><td className="text-right">{money.format(row.debit)}</td><td className="pr-3 text-right">{money.format(row.credit)}</td></tr>)}</tbody></table>}
    {(report==="balance"||report==="income")&&<StatementTable rows={report==="balance"?(balance.data??[]):(income.data??[])}/>}
    {report==="exceptions"&&<table className="w-full text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Documento</th><th className="text-left">Estado</th><th className="text-left">Código</th><th className="pr-3 text-left">Detalle</th></tr></thead><tbody>{(exceptions.data??[]).map(row=><tr key={row.sourceDocumentId} className="border-t"><td className="p-3">{new Date(row.occurredAt).toLocaleDateString("es-CO")}</td><td>{accountingDocumentTypeLabel(row.sourceDocumentType)} · {row.sourceDocumentId.slice(0,8)}</td><td>{accountingStatusLabel(row.status)}</td><td>{row.errorCode??"—"}</td><td className="pr-3">{row.errorMessage??"Pendiente de procesamiento"}</td></tr>)}</tbody></table>}
  </div></CardContent></Card><AccountingDocumentDialog documentId={accountingDocumentId} onClose={()=>setAccountingDocumentId(undefined)}/></>
}

function StatementTable({rows}:{rows:Array<{section:string;accountCode:string;accountName:string;amount:number}>}){const types=useReferenceOptions("accounting-account-type"),labels=new Map((types.data??[]).map(item=>[item.code,item.label])),totals=rows.reduce((result,row)=>result.set(row.section,(result.get(row.section)??0)+row.amount),new Map<string,number>());return <table className="w-full text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Sección</th><th className="text-left">Cuenta</th><th className="text-left">Nombre</th><th className="pr-3 text-right">Valor</th></tr></thead><tbody>{rows.map(row=><tr key={`${row.section}-${row.accountCode}`} className="border-t"><td className="p-3">{labels.get(row.section)??row.section}</td><td className="font-mono">{row.accountCode}</td><td>{row.accountName}</td><td className="pr-3 text-right font-semibold">{money.format(row.amount)}</td></tr>)}{[...totals].map(([section,total])=><tr key={`total-${section}`} className="border-t bg-muted/30 font-bold"><td className="p-3" colSpan={3}>Total {labels.get(section)??section}</td><td className="pr-3 text-right">{money.format(total)}</td></tr>)}</tbody></table>}

function BalanceSection({contextKey,from,to,setFrom,setTo,rows,loading,refresh}:{contextKey:string;from:string;to:string;setFrom:(v:string)=>void;setTo:(v:string)=>void;rows:Array<{accountCode:string;accountName:string;level:"Class"|"Group"|"Account"|"Subaccount"|"Auxiliary";openingDebit:number;openingCredit:number;debit:number;credit:number;closingDebit:number;closingCredit:number}>;loading:boolean;refresh:()=>void}){
  const [selected,setSelected]=useState<{title:string;accountFrom?:string;accountTo?:string;accountRootCode?:string}|null>(null);
  const [movementAccountFrom,setMovementAccountFrom]=useState("");
  const [movementAccountTo,setMovementAccountTo]=useState("");
  const [movementCenter,setMovementCenter]=useState("all");
  const [movementPage,setMovementPage]=useState(1);
  const [movementParty,setMovementParty]=useState("");
  const report=useMutation({mutationFn:async(filters:Omit<AccountMovementQuery,"page"|"pageSize">)=>{
    const data=await accountingApi.accountMovementReport(filters);
    return {data,filters,rows:auxiliaryReportRows(data)};
  },onError:showError});
  const movementCenterLabels=useCostCenterLabels([movementCenter==="all"?null:movementCenter]);
  const movements=useQuery({queryKey:["account-movements",contextKey,selected?.accountFrom,selected?.accountTo,selected?.accountRootCode,from,to,movementCenter,movementParty,movementPage],queryFn:()=>accountingApi.accountMovements({accountFrom:selected?.accountFrom,accountTo:selected?.accountTo,accountRootCode:selected?.accountRootCode,from,to,page:movementPage,pageSize:50,costCenterId:movementCenter==="all"?undefined:movementCenter,partyId:movementParty||undefined}),enabled:Boolean(selected)&&!report.data,retry:false,staleTime:Infinity});
  const selectedCode=selected?.accountRootCode??(selected?.accountFrom===selected?.accountTo?selected?.accountFrom:undefined);
  const singleAccount=Boolean(selectedCode&&movements.data?.accounts.length===1&&movements.data.accounts[0].accountCode===selectedCode);
  const pendingRange=movementAccountFrom.trim()!==(selected?.accountFrom??selected?.accountRootCode??"")||movementAccountTo.trim()!==(selected?.accountTo??selected?.accountRootCode??"");
  const selectedTotal=singleAccount?movements.data?.accounts[0]:undefined;
  const openAccount=(row:(typeof rows)[number])=>{setMovementAccountFrom(row.accountCode);setMovementAccountTo(row.accountCode);setMovementPage(1);setSelected({title:`${row.accountCode} · ${row.accountName}`,accountRootCode:row.accountCode})};
  const openRange=()=>{setMovementAccountFrom("");setMovementAccountTo("");setMovementPage(1);setSelected({title:"Libro auxiliar por cuenta"})};
  const applyRange=(event:FormEvent)=>{event.preventDefault();setMovementPage(1);setSelected({title:"Libro auxiliar por cuenta",accountFrom:movementAccountFrom.trim()||undefined,accountTo:movementAccountTo.trim()||undefined})};
  const classRows=rows.filter(row=>row.level==="Class"),totals=classRows.reduce((value,row)=>({openingDebit:value.openingDebit+row.openingDebit,openingCredit:value.openingCredit+row.openingCredit,debit:value.debit+row.debit,credit:value.credit+row.credit,closingDebit:value.closingDebit+row.closingDebit,closingCredit:value.closingCredit+row.closingCredit}),{openingDebit:0,openingCredit:0,debit:0,credit:0,closingDebit:0,closingCredit:0});
  const levelLabels={Class:"Clase",Group:"Grupo",Account:"Cuenta",Subaccount:"Subcuenta",Auxiliary:"Auxiliar"};
  return <>
    <div className="flex justify-end"><Button variant="outline" onClick={openRange}><BookOpen className="mr-2 h-4 w-4"/>Libro auxiliar por cuentas</Button></div>
    <Card className="overflow-hidden rounded-3xl"><CardHeader className="border-b"><div className="flex flex-col gap-3 lg:flex-row lg:items-end"><div className="flex-1"><CardTitle>Balance de prueba</CardTitle><p className="mt-1 text-sm text-muted-foreground">Saldo inicial, movimientos y saldo final agrupados por clase, grupo, cuenta, subcuenta y auxiliar. Abre una subcuenta o auxiliar para ver su detalle.</p></div><Field label="Desde"><Input type="date" value={from} onChange={event=>setFrom(event.target.value)}/></Field><Field label="Hasta"><Input type="date" value={to} onChange={event=>setTo(event.target.value)}/></Field><Button variant="outline" onClick={refresh} disabled={loading}><RefreshCw className={`mr-2 h-4 w-4 ${loading?"animate-spin":""}`}/>Actualizar</Button></div></CardHeader><CardContent className="p-0"><div className="overflow-x-auto"><table className="w-full min-w-[1050px] text-sm"><thead className="bg-muted/50"><tr><th className="p-3 text-left">Código / nivel</th><th className="text-left">Nombre</th><th className="text-right">Inicial débito</th><th className="text-right">Inicial crédito</th><th className="text-right">Movimiento débito</th><th className="text-right">Movimiento crédito</th><th className="text-right">Final débito</th><th className="pr-4 text-right">Final crédito</th><th className="w-10"/></tr></thead><tbody>{rows.map(row=>{const drill=row.level==="Subaccount"||row.level==="Auxiliary";return <tr key={`${row.level}-${row.accountCode}`} tabIndex={drill?0:undefined} role={drill?"button":undefined} aria-label={drill?`${row.accountCode} · ${row.accountName}`:undefined} onClick={()=>drill&&openAccount(row)} onKeyDown={event=>{if(drill&&(event.key==="Enter"||event.key===" ")){event.preventDefault();openAccount(row)}}} className={`${drill?"cursor-pointer hover:bg-muted/50 focus-visible:bg-muted/50":"bg-muted/10"} border-t outline-none transition-colors`}><td className="p-3"><span className="font-mono font-semibold">{row.accountCode}</span><small className="ml-2 text-muted-foreground">{levelLabels[row.level]}</small></td><td className={row.level==="Class"?"font-bold":row.level==="Group"?"font-semibold":""}>{row.accountName}</td><td className="text-right">{money.format(row.openingDebit)}</td><td className="text-right">{money.format(row.openingCredit)}</td><td className="text-right">{money.format(row.debit)}</td><td className="text-right">{money.format(row.credit)}</td><td className="text-right font-semibold">{money.format(row.closingDebit)}</td><td className="pr-4 text-right font-semibold">{money.format(row.closingCredit)}</td><td>{drill&&<ChevronRight className="h-4 w-4 text-muted-foreground"/>}</td></tr>})}</tbody><tfoot className="border-t-2 bg-muted/30 font-bold"><tr><td className="p-3" colSpan={2}>Totales por clases</td><td className="text-right">{money.format(totals.openingDebit)}</td><td className="text-right">{money.format(totals.openingCredit)}</td><td className="text-right">{money.format(totals.debit)}</td><td className="text-right">{money.format(totals.credit)}</td><td className="text-right">{money.format(totals.closingDebit)}</td><td className="pr-4 text-right">{money.format(totals.closingCredit)}</td><td/></tr></tfoot></table>{!loading&&!rows.length&&<p className="p-10 text-center text-muted-foreground">No hay movimientos contabilizados en este rango.</p>}</div></CardContent></Card>
    <Dialog open={Boolean(selected)} onOpenChange={open=>{if(!open){report.reset();setSelected(null)}}}>
      <DialogContent className="max-h-[95dvh] w-[98vw] max-w-[1600px] overflow-y-auto">
        {report.data?<>
          <DialogHeader className="sr-only"><DialogTitle>Libro auxiliar para imprimir y exportar</DialogTitle><DialogDescription>Informe completo del rango seleccionado.</DialogDescription></DialogHeader>
          <ReportViewer onClose={()=>report.reset()} title="Libro auxiliar por cuenta" documentDownloads searchable={false}
            description={`${report.data.filters.from} al ${report.data.filters.to} · ${report.data.data.totalCount} movimientos · ${report.data.data.accounts.length} cuentas${report.data.filters.partyId?" · Filtro de tercero aplicado":""}${report.data.filters.costCenterId?" · Filtro de centro aplicado":""}`}
            fileName={`libro-auxiliar-${report.data.filters.from}-${report.data.filters.to}`}
            rows={report.data.rows} columns={auxiliaryReportColumns}/>
        </>:<>
        <DialogHeader><DialogTitle>{selected?.title}</DialogTitle><DialogDescription>Libro auxiliar del {from} al {to}. El saldo incluye movimientos anteriores al rango, con los mismos filtros.</DialogDescription></DialogHeader>
        <form className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end" onSubmit={applyRange}>
          <Field label="Cuenta desde"><Input aria-label="Cuenta desde" inputMode="numeric" pattern="[0-9]*" maxLength={32} value={movementAccountFrom} onChange={event=>setMovementAccountFrom(event.target.value)} placeholder="Sin límite inferior"/></Field>
          <Field label="Cuenta hasta"><Input aria-label="Cuenta hasta" inputMode="numeric" pattern="[0-9]*" maxLength={32} value={movementAccountTo} onChange={event=>setMovementAccountTo(event.target.value)} placeholder="Sin límite superior"/></Field>
          <Button type="submit" disabled={movements.isFetching||!pendingRange}>Aplicar rango de cuentas</Button>
          {selected?.accountRootCode&&!pendingRange&&<p className="text-sm text-muted-foreground sm:col-span-3">Incluye los auxiliares de la cuenta seleccionada, según la jerarquía del balance.</p>}
          {pendingRange&&<p className="text-sm text-muted-foreground sm:col-span-3">Aplica el rango para actualizar el libro.</p>}
        </form>
        <div className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <Field label="Tercero"><PartySelect value={movementParty} onChange={value=>{setMovementParty(value);setMovementPage(1)}} placeholder="Todos los terceros" ariaLabel="Filtrar auxiliar por tercero"/></Field>
          <Field label="Centro de costo"><CostCenterSelect value={movementCenter==="all"?null:movementCenter} onChange={value=>{setMovementCenter(value??"all");setMovementPage(1)}} selected={movementCenterLabels.data?.get(movementCenter)} ariaLabel="Filtrar auxiliar por centro" includeInactive emptyLabel="Todos los centros"/></Field>
          <Button variant="outline" disabled={movements.isFetching} onClick={()=>void movements.refetch()}>Actualizar auxiliar</Button>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button variant="outline" disabled={report.isPending||movements.isFetching||movements.isError||pendingRange} onClick={()=>report.mutate({
            from,to,accountFrom:selected?.accountFrom,accountTo:selected?.accountTo,accountRootCode:selected?.accountRootCode,
            partyId:movementParty||undefined,costCenterId:movementCenter==="all"?undefined:movementCenter,
          })}>{report.isPending?<><Loader2 className="mr-2 h-4 w-4 animate-spin"/>Preparando libro auxiliar…</>:"Imprimir / exportar libro completo"}</Button>
        </div>
        {movements.isError&&<p role="alert" className="text-destructive">No se pudo cargar el auxiliar. Conservamos los filtros. {movements.error instanceof Error&&<span>{movements.error.message} </span>}<Button variant="link" disabled={movements.isFetching} onClick={()=>void movements.refetch()}>Reintentar auxiliar</Button></p>}
        {selectedTotal&&<dl className="grid grid-cols-2 gap-3 rounded-xl border bg-muted/30 p-3 text-sm sm:grid-cols-4">
          <div><dt>Saldo anterior</dt><dd className="font-semibold">{auxiliaryMoney.format(selectedTotal.openingBalance)}</dd></div>
          <div><dt>Débitos del período</dt><dd className="font-semibold">{auxiliaryMoney.format(selectedTotal.debit)}</dd></div>
          <div><dt>Créditos del período</dt><dd className="font-semibold">{auxiliaryMoney.format(selectedTotal.credit)}</dd></div>
          <div><dt>Nuevo saldo</dt><dd className="font-semibold">{auxiliaryMoney.format(selectedTotal.closingBalance)}</dd></div>
        </dl>}
        {!singleAccount&&Boolean(movements.data?.accounts.length)&&<div className="overflow-x-auto rounded-xl border">
          <table aria-label="Resumen por cuenta de todo el período" className="w-full min-w-[700px] text-sm">
            <caption className="p-3 text-left font-semibold">Resumen por cuenta de todo el período</caption>
            <thead className="bg-muted"><tr><th className="p-3 text-left">Cuenta</th><th className="text-right">Saldo anterior</th><th className="text-right">Débitos</th><th className="text-right">Créditos</th><th className="pr-3 text-right">Nuevo saldo</th></tr></thead>
            <tbody>{movements.data?.accounts.map(account=><tr key={account.accountCode} className="border-t"><td className="p-3">{account.accountCode} · {account.accountName}</td><td className="text-right">{auxiliaryMoney.format(account.openingBalance)}</td><td className="text-right">{auxiliaryMoney.format(account.debit)}</td><td className="text-right">{auxiliaryMoney.format(account.credit)}</td><td className="pr-3 text-right font-semibold">{auxiliaryMoney.format(account.closingBalance)}</td></tr>)}</tbody>
          </table>
        </div>}
        <div className="max-h-[55vh] overflow-auto rounded-xl border">
          <table className="w-full min-w-[1120px] text-xs [&_th]:whitespace-nowrap [&_th]:px-2 [&_td]:px-2 [&_td]:py-2 [&_td]:align-top [&_td:nth-child(n+8)]:whitespace-nowrap"><thead className="sticky top-0 bg-muted"><tr><th className="p-3 text-left">Fecha</th><th className="text-left">Número</th><th className="text-left">Documento</th><th className="text-left">Identificación</th><th className="text-left">Tercero</th><th className="text-left">Observación</th><th className="text-left">Centro</th><th className="text-right">Base</th><th className="text-right">Débito</th><th className="text-right">Crédito</th><th className="pr-3 text-right">Saldo</th></tr></thead>
            <tbody>{(movements.data?.items??[]).flatMap((item,index,items)=>{
              const row=<tr key={`${item.entryId}-${item.lineNumber}`} className="border-t"><td className="whitespace-nowrap p-3">{new Date(item.occurredAt).toLocaleDateString("es-CO")}</td><td className="font-mono">{item.entryNumber}</td><td><span className="block">{item.sourceDocumentNumber??accountingDocumentTypeLabel(item.sourceDocumentType)}</span>{item.reference&&<small className="text-muted-foreground">{item.reference}</small>}</td><td>{item.partyIdentification??"—"}</td><td>{item.partyName??"Sin tercero histórico"}</td><td>{item.description}</td><td>{item.costCenterCode?`${item.costCenterCode} · ${item.costCenterName}`:"Sin clasificación histórica"}</td><td className="text-right">{item.baseAmount==null?"—":auxiliaryMoney.format(item.baseAmount)}</td><td className="text-right">{auxiliaryMoney.format(item.debit)}</td><td className="text-right">{auxiliaryMoney.format(item.credit)}</td><td className="pr-3 text-right font-semibold">{auxiliaryMoney.format(item.balance)}</td></tr>;
              return !singleAccount&&(index===0||items[index-1].accountCode!==item.accountCode)
                ? [<tr key={`account-${item.entryId}-${item.lineNumber}`} className="border-t bg-muted/50"><th colSpan={11} className="p-3 text-left">{item.accountCode} · {item.accountName}<span className="ml-3 font-normal text-muted-foreground">Movimientos de esta página</span></th></tr>,row]
                : [row];
            })}</tbody>
          </table>
          {movements.isFetching&&<p role="status" className="p-10 text-center text-muted-foreground">Cargando movimientos…</p>}
          {!movements.isFetching&&!movements.isError&&!movements.data?.items.length&&<p className="p-10 text-center text-muted-foreground">No hay movimientos para estos filtros en el rango.</p>}
        </div>
        <div className="flex items-center justify-between gap-3 text-sm">
          <span>{movements.data?`${movements.data.totalCount} movimientos`:movements.isError?"Resultado no disponible":"Consultando…"}</span>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" disabled={movementPage<=1||movements.isFetching} onClick={()=>setMovementPage(page=>page-1)}>Anterior</Button>
            <span>Página {movementPage} de {Math.max(1,movements.data?.totalPages??1)}</span>
            <Button variant="outline" size="sm" disabled={movements.isFetching||movements.isError||movementPage>=(movements.data?.totalPages??0)} onClick={()=>setMovementPage(page=>page+1)}>Siguiente</Button>
          </div>
        </div>
        </>}
      </DialogContent>
    </Dialog>
  </>
}

function StatusCard({label,value,ok}:{label:string;value:string;ok:boolean}){return <Card className="rounded-2xl"><CardContent className="flex items-center gap-3 p-4">{ok?<CheckCircle2 className="h-8 w-8 text-emerald-600"/>:<AlertTriangle className="h-8 w-8 text-amber-600"/>}<div><small className="text-muted-foreground">{label}</small><strong className="block text-sm">{value}</strong></div></CardContent></Card>}
function Nav({active,icon:Icon,onClick,children}:{active:boolean;icon:typeof Landmark;onClick:()=>void;children:ReactNode}){return <button type="button" onClick={onClick} className={`flex shrink-0 items-center rounded-xl px-4 py-2.5 text-sm font-semibold ${active?"bg-slate-950 text-white shadow":"text-muted-foreground hover:bg-muted"}`}><Icon className="mr-2 h-4 w-4"/>{children}</button>}
function EmptyContext(){return <div className="rounded-3xl border border-dashed p-12 text-center"><Landmark className="mx-auto h-12 w-12 text-teal-700"/><h1 className="mt-4 text-xl font-bold">Selecciona una empresa y una sede</h1><p className="mt-2 text-sm text-muted-foreground">La configuración contable se aplica dentro de ese contexto.</p></div>}
function submit(event:FormEvent,mutate:()=>void){event.preventDefault();mutate()}
function showError(error:unknown){toast.error(error instanceof Error?error.message:"No fue posible completar la operación")}
function Field({label,children}:{label:string;children:ReactNode}){return <div className="space-y-2"><Label>{label}</Label>{children}</div>}
