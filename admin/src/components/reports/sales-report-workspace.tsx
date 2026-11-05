"use client";

import {useState,useMemo,useEffect,type FormEvent,type ReactNode} from "react";
import {usePathname,useRouter,useSearchParams} from "next/navigation";
import {useQuery} from "@tanstack/react-query";
import {ArrowLeft,ChevronRight,Download,RefreshCw,Search} from "lucide-react";
import {salesReportingApi,type SalesReportExplorerPage,type SalesReportExplorerRequest,type SalesReportExplorerRow,type SalesReportOption} from "@/services/api/sales-reporting";
import {useBusinessContextStore} from "@/stores/business-context-store";
import {useTenantContextStore} from "@/stores/tenant-context-store";
import {useAuthStore} from "@/stores/auth-store";
import {AccountingDocumentDialog} from "@/components/accounting/accounting-document-dialog";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Card,CardContent} from "@/components/ui/card";
import {PageError} from "@/components/ui/page-error";
import {PageLoading} from "@/components/ui/page-loading";
import {Select,SelectContent,SelectItem,SelectTrigger,SelectValue} from "@/components/ui/select";
import {SalesReportFilters,reportFilterFromQuery,reportFilterQueryChanges} from "@/components/reports/sales-report-filters";
import {toReportCsv} from "@/lib/report-viewer";

const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:4});
const number=new Intl.NumberFormat("es-CO",{maximumFractionDigits:6});
const amount=(value:number|null)=>value===null?"No modelado":money.format(value);
type ReportPathStep={dimension:string;key:string;label:string;page:number;comparison:string};

export function SalesReportWorkspace({module="sales"}:{module?:"sales"|"products"|"impact"|"supplier"}){
  const tenant=useTenantContextStore(state=>state.selectedTenantId),business=useBusinessContextStore(state=>state.selectedBusinessId);
  return <SalesReportContent key={`${tenant}/${business}/${module}`} scope={`${tenant}/${business}`} ready={Boolean(tenant&&business)} module={module}/>;
}

function SalesReportContent({scope,ready,module}:{scope:string;ready:boolean;module:"sales"|"products"|"impact"|"supplier"}){
  const router=useRouter(),pathname=usePathname(),url=useSearchParams();
  useEffect(()=>{if(ready&&!url.has("scope")){const next=new URLSearchParams(url.toString());next.set("scope",scope);router.replace(`${pathname}?${next}`,{scroll:false});}},[ready,url,scope,router,pathname]);
  const activeUrl=!url.get("scope")||url.get("scope")===scope;
  const get=(name:string)=>activeUrl?url.get(name):null;
  const context=useQuery({queryKey:["sales-report-context",scope],queryFn:salesReportingApi.context,enabled:ready,staleTime:0,refetchOnMount:"always",refetchOnWindowFocus:false});
  const from=get("from")??context.data?.monthStart??"",to=get("to")??context.data?.businessDate??"";
  let path:ReportPathStep[]=[],pathError:string|undefined;
  try{
    if(get("path")){const parsed:unknown=JSON.parse(get("path")!);if(!Array.isArray(parsed)||parsed.length>8||parsed.some(step=>!step||typeof step.dimension!=="string"||typeof step.key!=="string"||typeof step.label!=="string"||!Number.isInteger(step.page)||step.page<1))throw new Error("invalid");path=parsed;}
    else if(get("drill"))path=[{dimension:get("drill")!,key:get("key")??"",label:get("groupLabel")??"Grupo",page:Number(get("groupReturnPage"))||1,comparison:get("groupReturnComparison")??"none"}];
  }catch{pathError="La ruta del reporte no es válida. Vuelve al inicio del informe.";}
  const isRoot=path.length===0&&!get("document");
  const requestedGroup=get("group")==="category"?"area":get("group");
  const groupBy=isRoot&&module==="impact"?"customer":isRoot&&module==="supplier"?"supplier":isRoot&&module==="products"?(["area","category-line","category-group","subgroup"].includes(requestedGroup??"")?requestedGroup!:"product"):requestedGroup??"document",documentId=get("document")??undefined;
  const page=Math.max(1,Number(get("page"))||1),orderBy=get("order")??(module==="products"?"profit-desc":"net-desc"),search=get("search")??"";
  const [originalOpen,setOriginalOpen]=useState(false);
  const canReadFinancial=useAuthStore(state=>state.user?.permissions.includes("accounting.read")??false);
  const filter=reportFilterFromQuery(get,{from,to});
  const productFilters=module==="products"?{marginFrom:get("marginFrom")?Number(get("marginFrom")):undefined,marginTo:get("marginTo")?Number(get("marginTo")):undefined,returnActivity:get("productReturns")??"all",compositionKind:get("composition")??undefined}:undefined;
  if(productFilters&&[productFilters.marginFrom,productFilters.marginTo].some(value=>value!==undefined&&!Number.isFinite(value)))pathError="El rango de margen del reporte no es válido.";
  const showParticipation=module==="products"&&!documentId&&["product","area","category-line","category-group","subgroup"].includes(groupBy);
  const request:SalesReportExplorerRequest={filter,groupBy,page,pageSize:25,orderBy,search:search||undefined,
    products:productFilters,drillPath:path.map(step=>({dimension:step.dimension,key:step.key})),documentId,module,customerActivity:get("activity")??"all",comparison:get("comparison")??"none"};
  const validDates=Boolean(from&&to&&from<=to);
  const report=useQuery({queryKey:["sales-report-explorer",scope,request],queryFn:()=>salesReportingApi.explore(request),
    enabled:ready&&Boolean(context.data)&&validDates&&!pathError,refetchOnWindowFocus:false,retry:false});
  const navigate=(changes:Record<string,string|null>)=>{
    setOriginalOpen(false);
    const next=new URLSearchParams(activeUrl?url.toString():"");next.set("scope",scope);next.set("from",from);next.set("to",to);next.delete("page");
    for(const [key,value] of Object.entries(changes)){if(value===null||value==="all"||value==="")next.delete(key);else next.set(key,value);}
    router.push(`${pathname}?${next}`,{scroll:false});
  };
  const refresh=async()=>{
    const fresh=await context.refetch();
    if(fresh.data&&((!get("from")&&fresh.data.monthStart!==from)||(!get("to")&&fresh.data.businessDate!==to)))return;
    await report.refetch();
  };
  const open=(row:SalesReportExplorerRow)=>{
    if(documentId)return;
    if(groupBy==="document"){navigate({document:row.documentId,documentLabel:row.label,documentReturnPage:String(page)});return;}
    const nextGroup=({area:"category-line","category-line":"category-group","category-group":"subgroup",subgroup:"product",product:"document"} as Record<string,string>)[groupBy]??"product";
    const nextPath=[...path,{dimension:groupBy,key:row.key,label:row.label,page,comparison:request.comparison??"none"}];
    navigate({group:nextGroup,path:JSON.stringify(nextPath),drill:null,key:null,groupLabel:null,...(groupBy==="day"||groupBy==="month"?{comparison:null}:{})});
  };
  const back=()=>{
    if(documentId){navigate({document:null,documentLabel:null,page:get("documentReturnPage")??"1",documentReturnPage:null});return;}
    const previous=path.at(-1);if(!previous)return;
    navigate({group:previous.dimension,path:path.length>1?JSON.stringify(path.slice(0,-1)):null,drill:null,key:null,page:String(previous.page),comparison:previous.comparison,groupLabel:null});
  };
  const searchSubmit=(event:FormEvent<HTMLFormElement>)=>{event.preventDefault();navigate({search:String(new FormData(event.currentTarget).get("query")??"").trim(),document:null});};
  if(context.isLoading||!ready)return <PageLoading/>;
  if(context.isError||!context.data)return <PageError message={context.error instanceof Error?context.error.message:undefined} onRetry={()=>void context.refetch()}/>;
  if(pathError)return <PageError message={pathError} onRetry={()=>router.replace(pathname)}/>;
  const data=report.data,totals=data?.totals,groupingLabel=context.data.groupings.find(option=>option.code===groupBy)?.label??groupBy;
  return <div className="space-y-5">
    <header className="rounded-3xl bg-slate-950 p-6 text-white"><p className="text-xs font-semibold uppercase tracking-widest text-teal-300">Reportes comerciales</p>
      <div className="mt-2 flex flex-wrap items-start justify-between gap-4"><div><h1 className="text-3xl font-bold">{module==="products"?"Productos y rentabilidad":module==="impact"?"Impacto comercial":module==="supplier"?"Proveedores: compras y ventas":"Ventas"}</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">{module==="supplier"?"Venta de productos frente a recepciones menos devoluciones de compra. La compra del período y el costo reconocido de lo vendido son cifras diferentes.":module==="impact"?"Clientes conocidos por ventas proyectadas: quién compró, quién no compró en el período y qué portafolio llegó a ellos.":module==="products"?"Productos físicos vendidos, cantidades, descuentos, devoluciones y rentabilidad sobre costo histórico. Servicios y cargos se consultan en Ventas.":"Venta sin impuestos, impuestos, costo y utilidad de los movimientos del período. Abre un grupo para llegar a sus comprobantes y líneas."}</p></div>
        <Button variant="secondary" onClick={()=>void refresh()} disabled={report.isFetching||context.isFetching||!validDates}><RefreshCw className={`mr-2 size-4 ${report.isFetching?"animate-spin":""}`}/>Actualizar</Button></div></header>
    {context.data.hasFailedSources&&<p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-950">Hay movimientos que no se pudieron proyectar. Las cifras del último corte están incompletas hasta resolver esos errores.</p>}
    {context.data.hasPendingSources&&<p role="status" className="rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950">Hay movimientos pendientes de proyectar. Consulta el corte indicado y usa Actualizar para revisar su avance.</p>}
    <SalesReportFilters key={JSON.stringify([filter,productFilters])} value={filter} context={context.data} scope={scope} supplier={module==="supplier"} products={productFilters}
      onApply={(next,products)=>navigate({...reportFilterQueryChanges(next),...(module==="products"?{marginFrom:products?.marginFrom===undefined?null:String(products.marginFrom),marginTo:products?.marginTo===undefined?null:String(products.marginTo),productReturns:products?.returnActivity??null,composition:products?.compositionKind??null}:{}),document:null,drill:null,key:null,path:null,groupLabel:null})}>
      {module==="supplier"?<Field label="Análisis"><p className="py-2 text-sm">Compras y ventas por proveedor</p></Field>:module==="impact"?<Field label="Actividad del cliente"><Options value={request.customerActivity??"all"} options={context.data.customerActivities} onChange={value=>navigate({activity:value,group:null,drill:null,key:null,path:null,document:null})}/></Field>:<Field label="Agrupar por"><Options value={groupBy} options={module==="products"&&isRoot?context.data.groupings.filter(option=>["product","area","category-line","category-group","subgroup"].includes(option.code)):context.data.groupings.filter(option=>option.code!=="category")} onChange={value=>navigate({group:value,document:null,drill:null,key:null,path:null,groupLabel:null})}/></Field>}
    </SalesReportFilters>
    <div className="space-y-3 rounded-xl border bg-card p-4">      <div className="flex flex-wrap items-end gap-3"><form onSubmit={searchSubmit} className="flex min-w-60 flex-1 gap-2"><Input aria-label="Buscar en los movimientos" placeholder="Número, cliente, producto o servicio" key={search} name="query" defaultValue={search}/><Button variant="outline" type="submit"><Search className="size-4"/><span className="sr-only">Buscar</span></Button></form>
        <div className="w-72"><Options value={request.comparison??"none"} options={context.data.comparisons} onChange={value=>navigate({comparison:value,drill:null,key:null,path:null,document:null})}/></div><div className="w-64"><Options value={orderBy} options={context.data.orders} onChange={value=>navigate({order:value})}/></div><Button variant="ghost" onClick={()=>{router.push(pathname,{scroll:false});}}>Limpiar filtros</Button></div>
      <p className="text-xs text-muted-foreground">Los filtros se aplican a la vez y se conservan al abrir el detalle. La agrupación define las filas. Importes en COP. Fechas del negocio: {context.data.timeZoneId}. Cada venta y devolución cuenta por su fecha de movimiento. La cantidad corresponde a productos físicos; los servicios conservan su unidad en el detalle. Las cantidades de unidades de medida diferentes no son comparables entre sí.</p></div>
    {!validDates?<PageError message="Selecciona un rango de fechas válido."/>:report.isError?<PageError message={report.error instanceof Error?report.error.message:"No fue posible consultar los movimientos."} onRetry={()=>void report.refetch()}/>:report.isLoading?<PageLoading/>:data&&totals&&<>
      {data.products&&<Card><CardContent className="space-y-2 p-4 text-sm"><h2 className="font-semibold">Aporte del portafolio filtrado · {number.format(data.products.products)} productos</h2><p>Venta neta sin impuestos: {amount(data.products.netUntaxed)} · Utilidad sin impuestos: {amount(data.products.profit)}</p><p className="text-xs text-muted-foreground">La participación usa este conjunto completo, con todas las páginas, y conserva su base al abrir el detalle. Los porcentajes se calculan cuando la base total es positiva y conocida. Productos con pérdida o devolución neta pueden aportar porcentajes negativos; el redondeo puede impedir que sumen exactamente 100%.</p></CardContent></Card>}
      {data.supplier&&<section className="space-y-3"><div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Measure label="Compra neta sin impuestos" value={amount(data.supplier.netPurchases)} detail={`Recepciones ${amount(data.supplier.purchaseReceipts)} − devoluciones ${amount(data.supplier.purchaseReturns)}`}/>
        <Measure label="Impuestos de compra" value={amount(data.supplier.purchaseTax)} detail="Tarifas históricas de las recepciones y devoluciones"/>
        <Measure label="Compra con impuestos" value={amount(data.supplier.purchasesWithTax)} detail={data.supplier.previousNetPurchases===null?"Sin comparación seleccionada":`Compra previa sin impuestos: ${amount(data.supplier.previousNetPurchases)}`}/>
        <Measure label="Clientes conocidos" value={number.format(data.supplier.knownCustomers)} detail="Universo autorizado por ventas proyectadas hasta el fin del período"/></div>
        <p className="text-xs text-muted-foreground">La atribución comercial del proveedor es histórica y no demuestra el lote físico vendido. Relación venta/compra no es rotación de inventario. Los clientes generales se cuentan una sola vez. Abre el proveedor para consultar sus ventas.</p></section>}
      {data.impact&&<section className="space-y-3"><div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Measure label="Clientes conocidos" value={number.format(data.impact.knownCustomers)} detail="Con venta proyectada positiva hasta el fin del período"/>
        <Measure label="Compradores" value={number.format(data.impact.buyers)} detail="Una venta positiva en el período; devolver no borra la compra"/>
        <Measure label="Sin compra" value={number.format(data.impact.withoutPurchase)} detail="Conocidos que no compraron el portafolio filtrado"/>
        <Measure label="Cobertura" value={`${number.format(data.impact.coveragePercent)}%`} detail="Compradores identificados / clientes conocidos"/></div>
        <p className="text-xs text-muted-foreground">Este universo no incluye prospectos sin ventas. Historia disponible desde {data.impact.firstHistoricalDate??"sin datos"}. {data.impact.anonymousDocuments} comprobantes con cliente no identificado se informan aparte. Los filtros de portafolio acotan compradores y conservan el universo autorizado.</p></section>}
      <div className="grid gap-3 sm:grid-cols-3"><Measure label="Ventas" value={amount(totals.salesBeforeReturns)} detail="Sin impuestos, después de descuentos y antes de devoluciones"/><Measure label="Devoluciones" value={amount(totals.returns)} detail="Sin impuestos, ocurridas en este período"/><Measure label="Total neto" value={amount(totals.netUntaxed)} detail="Ventas menos devoluciones, sin impuestos"/></div>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"><Measure label="Impuestos netos" value={amount(totals.tax)} detail={`Impuestos devueltos: ${amount(totals.returnedTax)}`}/>
        <Measure label="Subtotal neto con impuestos" value={amount(totals.netTotal)} detail={`Impuestos: ${amount(totals.tax)}`}/><Measure label="Costo reconocido" value={amount(totals.recognizedCost)} detail={totals.missingCostLines?`${totals.missingCostLines} líneas con costo no modelado o no disponible`:"Costo histórico neto de devoluciones"}/>
        <Measure label="Utilidad sin impuestos" value={amount(totals.profit)} detail={totals.marginPercent===null?"Margen no calculable":`Margen ${number.format(totals.marginPercent)}% sobre venta sin impuestos`}/></div>
      {data.document&&<Card><CardContent className="p-4 text-sm"><h2 className="font-semibold">{data.document.documentNumber} · {data.document.customerName}</h2><p className="mt-1 text-muted-foreground">Facturó: {data.document.billedByName??"Sin nombre histórico"} · Vendedor: {data.document.sellerName} · {data.document.warehouseName??"Sin bodega"}{data.document.fiscalNumber?` · DIAN: ${data.document.fiscalNumber}`:""}</p>{canReadFinancial&&!data.hasLineScope&&<Button variant="outline" size="sm" className="mt-3" onClick={()=>setOriginalOpen(true)}>Abrir trazabilidad financiera del original</Button>}</CardContent></Card>}
      {data.officialTotal!==null&&<Card><CardContent className="p-4 text-sm"><strong>Total oficial del período: {amount(data.officialTotal)}</strong><span className="ml-3 text-muted-foreground">Subtotal con impuestos {amount(totals.netTotal)} + redondeo {amount(data.roundingAdjustment)}. {context.data.isRestaurant?`Propinas registradas aparte: ${amount(data.restaurantTip)}.`:""}</span></CardContent></Card>}
      {data.comparison&&<Card><CardContent className="p-4 text-sm"><p className="font-semibold">Comparación seleccionada: {data.comparison.from} a {data.comparison.to}</p>
        <p className="mt-2">Venta sin impuestos: {amount(data.comparison.totals.netUntaxed)} · Compradores identificados: {number.format(data.comparison.totals.customers)} · Utilidad sin impuestos: {amount(data.comparison.totals.profit)}</p></CardContent></Card>}
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm"><div className="flex items-center gap-2">{(path.length>0||documentId)&&<Button variant="outline" size="sm" onClick={back}><ArrowLeft className="mr-1 size-4"/>Volver</Button>}
        <span>{documentId?get("documentLabel")??"Detalle del comprobante":path.length?path.map(step=>step.label).join(" › "):`Por ${groupingLabel.toLowerCase()}`}</span></div>
        <span className="text-xs text-muted-foreground">{data.projectedThrough?`Proyectado: ${new Date(data.projectedThrough).toLocaleString("es-CO",{timeZone:context.data.timeZoneId})}`:"Sin movimientos proyectados en el rango"}</span></div>
      {data.hasLineScope&&<p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-950">{module==="supplier"?"La comparación incluye productos físicos. Los importes de compra no se usan como costo de venta.":"Las cifras corresponden al subconjunto de líneas seleccionado. Los pagos, crédito, redondeo y propinas no se distribuyen entre líneas."}</p>}
      <Card className="overflow-hidden"><CardContent className="p-0"><div className="flex items-center justify-between gap-3 p-4"><span className="text-sm">{number.format(data.totalCount)} {documentId?"líneas":groupBy==="document"?"comprobantes":"grupos"} · {number.format(totals.customers)} clientes compradores identificados</span><Button variant="outline" size="sm" disabled={!data.items.length} onClick={()=>exportPage(data,request,scope,groupingLabel)}><Download className="mr-2 size-4"/>Exportar esta página</Button></div>
        <p className="px-4 pb-3 text-xs text-muted-foreground">Desplaza la tabla horizontalmente para ver todas las cifras. La identificación permanece visible.</p>
        <div className="overflow-x-auto focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring" role="region" aria-label="Tabla del reporte con desplazamiento horizontal" tabIndex={0}><table className="w-full table-fixed text-sm tabular-nums [&_th]:align-middle [&_th]:text-xs [&_th]:leading-snug" style={{minWidth:320+128*(11+(showParticipation?2:0)+(!documentId&&groupBy==="document"&&!data.hasLineScope?2:0)+(data.supplier?(data.comparison?9:7):0))+48}}>
          <colgroup><col className="w-56 sm:w-80"/><col span={11} className="w-32"/>{showParticipation&&<col span={2} className="w-32"/>}{!documentId&&groupBy==="document"&&!data.hasLineScope&&<col span={2} className="w-32"/>}{data.supplier&&<col span={data.comparison?9:7} className="w-32"/>}<col className="w-12"/></colgroup>
          <thead className="bg-muted text-left"><tr><th className="sticky left-0 z-20 bg-muted p-3 shadow-[1px_0_0_0_hsl(var(--border))]">{documentId?"Línea / movimiento":groupBy==="document"?"Comprobante":groupingLabel}</th>
          {[documentId?"Cantidad / unidad":"Cantidad neta","Bruto sin impuestos","Descuentos","Ventas sin impuestos","Devoluciones sin impuestos","Total neto sin impuestos","Impuestos","Con impuestos","Costo","Utilidad","Margen"].map(title=><th key={title} className="p-3 text-right">{title}</th>)}{showParticipation&&["Participación en venta neta","Participación en utilidad"].map(title=><th key={title} className="p-3 text-right">{title}</th>)}{!documentId&&groupBy==="document"&&!data.hasLineScope&&<><th className="p-3 text-right">Redondeo</th><th className="p-3 text-right">Total oficial del período</th></>}{data.supplier&&["Clientes compradores","Cobertura","Recepciones sin impuestos","Devoluciones de compra sin impuestos","Compra neta sin impuestos","Compra con impuestos","Relación venta/compra",...(data.comparison?["Venta previa sin impuestos","Compra previa sin impuestos"]:[])].map(title=><th key={title} className="p-3 text-right">{title}</th>)}<th/></tr></thead>
          <tbody>{data.items.map(row=><tr key={row.key} className={`group border-t align-top transition-colors ${documentId?"":"cursor-pointer hover:bg-muted focus-within:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"}`}
            tabIndex={documentId?undefined:0}
            onClick={event=>{if(!documentId&&!(event.target as HTMLElement).closest("button,a,input,select,textarea,summary,details"))open(row);}}
            onKeyDown={event=>{if(!documentId&&event.target===event.currentTarget&&(event.key==="Enter"||event.key===" ")){event.preventDefault();open(row);}}}>
            <td className="sticky left-0 z-10 bg-background p-3 shadow-[1px_0_0_0_hsl(var(--border))] group-hover:bg-muted group-focus-within:bg-muted"><div className="space-y-1 break-words leading-snug"><button className="text-left font-semibold hover:underline disabled:cursor-default" disabled={Boolean(documentId)} onClick={()=>open(row)}>{row.label}</button>{row.document&&<div className="space-y-0.5 text-xs text-muted-foreground"><p className="font-medium text-foreground">{row.document.customerName}</p><p>Facturó: {row.document.billedByName??"Sin nombre histórico"}</p><p>Vendedor: {row.document.sellerName} · {row.document.warehouseName??"Sin bodega"}</p>{row.document.fiscalNumber&&<p>DIAN: {row.document.fiscalNumber}</p>}</div>}{row.boughtInPeriod!==null&&<small className="block text-muted-foreground">{row.key==="unattributed"?"Cliente no identificado":row.boughtInPeriod?"Con compra en el período":"Sin compra en el período"}</small>}
            {documentId&&<><small className="block text-muted-foreground">{row.movementType==="Return"?"Devolución · ":"Venta · "}{row.itemKind==="Service"?"Servicio":row.itemKind==="Charge"?"Cargo sin atribución":"Producto"} {row.itemCode} · {row.unitCode??"Sin unidad histórica"}</small>{row.returnDisposition&&<small className="block text-muted-foreground">Disposición: {({Sellable:"Reintegro vendible",Inspection:"En revisión",Damaged:"Dañado",NotReturned:"Sin reintegro"} as Record<string,string>)[row.returnDisposition]??row.returnDisposition} · Motivo: {row.returnReasonDescription??row.returnReasonCode??"Sin motivo histórico"}</small>}<small className="block text-muted-foreground">{row.supplierName}{row.waiterName?` · Mesero: ${row.waiterName}`:""}</small><Composition row={row}/></>}
            {row.occurredAt&&<small className="block text-muted-foreground">{new Date(row.occurredAt).toLocaleDateString("es-CO",{timeZone:context.data.timeZoneId})} · {documentId?"Movimiento":"Último movimiento"}</small>}</div></td><td className="p-3 text-right">{number.format(row.measures.quantity)}</td>
            {[row.measures.grossSales,row.measures.discounts,row.measures.salesBeforeReturns,row.measures.returns,row.measures.netUntaxed,row.measures.tax,row.measures.netTotal,row.measures.recognizedCost,row.measures.profit].map((value,index)=><td key={index} className="whitespace-nowrap p-3 text-right">{amount(value)}</td>)}<td className="p-3 text-right">{row.measures.marginPercent===null?"—":`${number.format(row.measures.marginPercent)}%`}</td>
            {showParticipation&&[row.salesParticipationPercent,row.profitParticipationPercent].map((value,index)=><td key={index} className="p-3 text-right">{value===null?"—":`${number.format(value)}%`}</td>)}{!documentId&&groupBy==="document"&&!data.hasLineScope&&<><td className="p-3 text-right">{amount(row.roundingAdjustment)}</td><td className="p-3 text-right">{amount(row.officialTotal)}</td></>}{row.supplier&&<><td className="p-3 text-right">{number.format(row.measures.customers)}</td><td className="p-3 text-right">{number.format(row.supplier.penetrationPercent)}%</td><td className="p-3 text-right">{amount(row.supplier.purchaseReceipts)}</td><td className="p-3 text-right">{amount(row.supplier.purchaseReturns)}</td><td className="p-3 text-right">{amount(row.supplier.netPurchases)}</td><td className="p-3 text-right">{amount(row.supplier.purchasesWithTax)}</td><td className="p-3 text-right">{row.supplier.salesPurchaseRatio===null?"—":`${number.format(row.supplier.salesPurchaseRatio)} ×`}</td>{data.comparison&&<><td className="p-3 text-right">{amount(row.supplier.previousNetSales)}</td><td className="p-3 text-right">{amount(row.supplier.previousNetPurchases)}</td></>}</>}
            <td className="p-3">{!documentId&&<Button variant="ghost" size="icon" aria-label={`Ver ${row.label}`} onClick={()=>open(row)}><ChevronRight className="size-4"/></Button>}</td></tr>)}</tbody></table>{!data.items.length&&<p className="p-10 text-center text-muted-foreground">No hay movimientos que cumplan estos filtros.</p>}</div>
        <div className="flex items-center justify-between border-t p-4 text-sm"><span>Página {page} de {Math.max(1,Math.ceil(data.totalCount/25))}</span><div className="flex gap-2"><Button variant="outline" disabled={page<=1||report.isFetching} onClick={()=>navigate({page:String(page-1)})}>Anterior</Button><Button variant="outline" disabled={page*25>=data.totalCount||report.isFetching} onClick={()=>navigate({page:String(page+1)})}>Siguiente</Button></div></div></CardContent></Card>
      {documentId&&<div className="grid gap-4 md:grid-cols-2"><Card><CardContent className="p-5"><h2 className="mb-3 font-semibold">Impuestos del subconjunto completo</h2>{data.taxes.map(tax=><p key={`${tax.taxCode}/${tax.taxRate}`} className="mb-2 text-sm">{tax.taxCode} {number.format(tax.taxRate)}% · Base {amount(tax.base)} · Impuesto {amount(tax.tax)}</p>)}</CardContent></Card>
        <Card><CardContent className="p-5"><h2 className="mb-3 font-semibold">Pagos declarados en el período</h2>{data.hasLineScope?<p className="text-sm text-muted-foreground">No atribuibles al subconjunto de líneas.</p>:<>{data.payments.map(payment=><p key={`${payment.movementType}/${payment.methodCode}`} className="mb-2 text-sm">{payment.movementType==="Credit"?"Crédito":payment.movementType==="Refund"?"Reembolso":"Pago"} · {context.data.paymentMethods.find(method=>method.code===payment.methodCode)?.label??payment.methodCode}: {amount(payment.amount)}</p>)}{data.roundingAdjustment!==null&&<p className="text-sm">Redondeo oficial: {amount(data.roundingAdjustment)}</p>}{context.data.isRestaurant&&data.restaurantTip!==null&&<p className="text-sm">Propina: {amount(data.restaurantTip)}</p>}</>}</CardContent></Card></div>}
    </>}
    <AccountingDocumentDialog documentId={originalOpen?data?.document?.documentId:undefined} sourceLabel="Comprobante original completo · consulta financiera" onClose={()=>setOriginalOpen(false)}/>
  </div>;
}

function Field({label,children}:{label:string;children:ReactNode}){return <div className="space-y-1.5"><span className="text-xs font-semibold text-muted-foreground">{label}</span>{children}</div>;}
function Options({value,options,onChange,withAll=false}:{value:string;options:SalesReportOption[];onChange:(value:string)=>void;withAll?:boolean}){return <Select value={value} onValueChange={onChange}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent>{withAll&&<SelectItem value="all">Todos</SelectItem>}{options.map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select>;}
function Measure({label,value,detail}:{label:string;value:string;detail:string}){return <Card><CardContent className="p-4"><p className="text-xs font-medium text-muted-foreground">{label}</p><strong className="mt-2 block text-xl">{value}</strong><p className="mt-1 text-xs text-muted-foreground">{detail}</p></CardContent></Card>;}
function Composition({row}:{row:SalesReportExplorerRow}){
  const [open,setOpen]=useState(false);
  const components:Record<string,unknown>[]=useMemo(()=>open&&row.compositionDetailJson?JSON.parse(row.compositionDetailJson):[],[open,row.compositionDetailJson]);
  if(!row.compositionKind||row.compositionKind==="None")return null;
  return <details className="mt-2 text-xs" open={open} onToggle={event=>setOpen(event.currentTarget.open)}><summary className="cursor-pointer">{row.compositionKind==="Combo"?"Combo":"Ingredientes"} · Composición proporcional del movimiento</summary>{open&&(components.length?components.map((component,index)=><p key={index}>{String(component.ProductName??component.ProductCode??"Sin nombre histórico")} · {number.format(Number(component.Quantity))} {String(component.UnitCode??"")} · Costo {amount(Number(component.RecognizedCostAmount))}</p>):<p>{row.compositionDetailJson===null?"No hay detalle histórico de componentes disponible.":"Este movimiento no registra consumo de componentes."}</p>)}{open&&row.movementType==="Return"&&<p>La composición explica el costo proporcional original; la disposición indica si hubo reintegro.</p>}</details>;
}
function exportPage(data:SalesReportExplorerPage,request:SalesReportExplorerRequest,scope:string,grouping:string){
  const columns=[{key:"scope",label:"Tenant / sede"},{key:"from",label:"Desde"},{key:"to",label:"Hasta"},
    {key:"cut",label:"Corte de proyección"},{key:"grouping",label:"Agrupación"},{key:"filters",label:"Filtros aplicados"},
    {key:"page",label:"Página exportada"},{key:"lineScope",label:"Subconjunto de líneas"},{key:"label",label:"Grupo / línea"},{key:"quantity",label:"Cantidad neta"},{key:"salesBeforeReturns",label:"Ventas sin impuestos"},{key:"returns",label:"Devoluciones sin impuestos"},{key:"netUntaxed",label:"Venta sin impuestos"},
    {key:"tax",label:"Impuestos"},{key:"netTotal",label:"Venta con impuestos"},{key:"recognizedCost",label:"Costo"},
    {key:"profit",label:"Utilidad"},{key:"marginPercent",label:"Margen sin impuestos"}];
  columns.push({key:"rounding",label:"Redondeo documental"},{key:"official",label:"Total oficial documental"});
  if(data.products)columns.push({key:"salesParticipationPercent",label:"Participación en venta neta %"},{key:"profitParticipationPercent",label:"Participación en utilidad %"});
  if(data.supplier)columns.push({key:"purchaseReceipts",label:"Recepciones sin impuestos"},{key:"purchaseReturns",label:"Devoluciones de compra sin impuestos"},{key:"netPurchases",label:"Compra sin impuestos"},{key:"purchasesWithTax",label:"Compra con impuestos"},
    {key:"buyers",label:"Clientes compradores"},{key:"coverage",label:"Cobertura %"},{key:"previousNetSales",label:"Venta previa sin impuestos"},
    {key:"previousNetPurchases",label:"Compra previa sin impuestos"});
  const csv=toReportCsv(data.items.map(row=>({label:row.label,salesParticipationPercent:row.salesParticipationPercent,profitParticipationPercent:row.profitParticipationPercent,...row.measures,...row.supplier,
    scope,from:request.filter.from,to:request.filter.to,cut:data.projectedThrough,grouping,
    filters:JSON.stringify(request),page:data.page,lineScope:data.hasLineScope?"Sí":"No",rounding:row.roundingAdjustment,
    official:row.officialTotal,buyers:row.measures.customers,coverage:row.supplier?.penetrationPercent??null})),columns);
  const url=URL.createObjectURL(new Blob([csv],{type:"text/csv;charset=utf-8"}));const anchor=document.createElement("a");anchor.href=url;anchor.download="ventas-pagina.csv";anchor.click();URL.revokeObjectURL(url);
}
