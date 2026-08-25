"use client";

import {useMemo} from "react";
import {useQuery} from "@tanstack/react-query";
import {Area,AreaChart,Bar,BarChart,CartesianGrid,Cell,Pie,PieChart,ResponsiveContainer,Tooltip,XAxis,YAxis} from "recharts";
import {Banknote,Boxes,Clock3,CreditCard,Landmark,PackageSearch,Percent,ReceiptText,RefreshCw,RotateCcw,ShoppingCart,TrendingUp,Users} from "lucide-react";
import {salesReportingApi,type SalesBreakdownRow,type SalesDimension,type SalesReportFilter} from "@/services/api/sales-reporting";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Card,CardContent,CardDescription,CardHeader,CardTitle} from "@/components/ui/card";
import {PageError} from "@/components/ui/page-error";
import {PageLoading} from "@/components/ui/page-loading";
import {useReferenceOptions} from "@/hooks/use-reference-options";
import {completePaymentBreakdown} from "@/lib/sales-payment-breakdown";

const money=new Intl.NumberFormat("es-CO",{style:"currency",currency:"COP",maximumFractionDigits:0});
const number=new Intl.NumberFormat("es-CO",{maximumFractionDigits:2});
const palette=["#0f766e","#0891b2","#2563eb","#7c3aed","#d97706","#475569"];

export default function TodayReportPage(){
  const today=useQuery({queryKey:["sales-report-today"],queryFn:salesReportingApi.today,staleTime:30_000});
  const date=today.data?.businessDate;
  const filter=useMemo<SalesReportFilter|undefined>(()=>date?{from:date,to:date}:undefined,[date]);
  const hourly=useBreakdown(filter,"hour",24);
  const payments=useBreakdown(filter,"payment-method",20);
  const paymentMethods=useReferenceOptions("payment-method");
  const sellers=useBreakdown(filter,"seller",8);
  const products=useBreakdown(filter,"product",8);
  const suppliers=useBreakdown(filter,"supplier",8);
  const warehouses=useBreakdown(filter,"warehouse",8);
  const queries=[today,hourly,payments,sellers,products,suppliers,warehouses];
  const refreshing=queries.some(query=>query.isFetching);
  const refresh=async()=>{await Promise.all(queries.map(query=>query.refetch()))};
  if(today.isLoading)return <PageLoading/>;
  if(today.isError||!today.data)return <PageError onRetry={()=>void today.refetch()}/>;
  const value=today.data,totals=value.totals;
  const paymentRows=completePaymentBreakdown(paymentMethods.data??[],payments.data??[]);
  const hasPayments=paymentRows.some(row=>row.netSales!==0);
  const noProjection=!value.projectedThrough;
  return <div className="space-y-6">
    <header className="relative overflow-hidden rounded-3xl bg-slate-950 p-6 text-white shadow-xl lg:p-8">
      <div className="absolute -right-24 -top-24 h-72 w-72 rounded-full bg-teal-500/25 blur-3xl"/>
      <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
        <div><p className="text-xs font-bold uppercase tracking-[.24em] text-teal-300">Pulso comercial · {formatDate(value.businessDate)}</p><h1 className="mt-2 text-3xl font-black tracking-tight lg:text-4xl">Así va la empresa hoy</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">Lectura macro del día comercial. Todas las cifras provienen del motor de reportes y conservan ventas, devoluciones, costo, utilidad y recaudo.</p></div>
        <div className="flex flex-wrap items-center gap-2"><Badge variant="outline" className="border-white/20 bg-white/10 text-white"><Clock3 className="mr-1.5 h-3.5 w-3.5"/>{value.projectedThrough?`Corte ${new Date(value.projectedThrough).toLocaleTimeString("es-CO",{hour:"2-digit",minute:"2-digit",second:"2-digit"})}`:"Sin movimientos proyectados"}</Badge><Button className="bg-white text-slate-950 hover:bg-slate-100" onClick={()=>void refresh()} disabled={refreshing}><RefreshCw className={`mr-2 h-4 w-4 ${refreshing?"animate-spin":""}`}/>{refreshing?"Actualizando":"Actualizar"}</Button></div>
      </div>
    </header>

    {noProjection&&<Card className="border-dashed"><CardContent className="p-6 text-sm text-muted-foreground">Todavía no hay ventas proyectadas para el día comercial actual. El tablero se llenará a medida que el motor confirme y proyecte movimientos.</CardContent></Card>}

    <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      <Metric title="Venta bruta" value={money.format(totals.grossSales)} detail={`${totals.documentCount.toLocaleString("es-CO")} comprobantes`} icon={TrendingUp} tone="dark"/>
      <Metric title="Venta neta" value={money.format(totals.netTotalSales)} detail={`Descuentos ${money.format(totals.discounts)}`} icon={Banknote} tone="teal"/>
      <Metric title="Devoluciones" value={money.format(totals.returns)} detail={`${number.format(value.returnRatePercent)}% sobre venta`} icon={RotateCcw} tone="amber"/>
      <Metric title="Utilidad bruta" value={money.format(totals.grossProfit)} detail={`${number.format(totals.grossMarginPercent)}% de margen`} icon={Landmark} tone="blue"/>
      <Metric title="Costo reconocido" value={money.format(totals.netRecognizedCost)} detail="Neto de devoluciones" icon={Boxes}/>
      <Metric title="Recaudado" value={money.format(totals.collected)} detail={`Crédito ${money.format(totals.creditSales)}`} icon={CreditCard}/>
      <Metric title="Ticket promedio" value={money.format(value.averageTicket)} detail="Venta neta por comprobante" icon={ReceiptText}/>
      <Metric title="Clientes identificados" value={value.customerCount.toLocaleString("es-CO")} detail={`${number.format(totals.unitsSold)} unidades vendidas`} icon={Users}/>
    </section>

    <section className="grid gap-4 xl:grid-cols-[1.55fr_1fr]">
      <Card className="rounded-3xl"><CardHeader><CardTitle>Ritmo de venta por hora</CardTitle><CardDescription>Venta neta proyectada en cada hora del día comercial.</CardDescription></CardHeader><CardContent><div className="h-80"><ResponsiveContainer width="100%" height="100%"><AreaChart data={hourly.data??[]}><defs><linearGradient id="todaySales" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#0f766e" stopOpacity={.45}/><stop offset="95%" stopColor="#0f766e" stopOpacity={.03}/></linearGradient></defs><CartesianGrid strokeDasharray="3 3" vertical={false}/><XAxis dataKey="label"/><YAxis tickFormatter={compact}/><Tooltip formatter={value=>money.format(Number(value))}/><Area type="monotone" dataKey="netSales" name="Venta neta" stroke="#0f766e" fill="url(#todaySales)" strokeWidth={3}/></AreaChart></ResponsiveContainer></div></CardContent></Card>
      <Card className="rounded-3xl"><CardHeader><CardTitle>Cómo pagaron</CardTitle><CardDescription>Recaudo y crédito registrados en los comprobantes.</CardDescription></CardHeader><CardContent>{paymentMethods.isLoading?<p className="py-20 text-center text-sm text-muted-foreground">Cargando medios de pago…</p>:paymentMethods.isError?<p className="py-20 text-center text-sm text-destructive">No fue posible cargar los medios de pago.</p>:<><div className="h-56">{hasPayments?<ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={paymentRows} dataKey="netSales" nameKey="label" innerRadius={55} outerRadius={90} paddingAngle={3}>{paymentRows.map((row,index)=><Cell key={row.key} fill={palette[index%palette.length]}/>)}</Pie><Tooltip formatter={value=>money.format(Number(value))}/></PieChart></ResponsiveContainer>:<div className="grid h-full place-items-center rounded-2xl bg-muted/30 px-6 text-center text-sm text-muted-foreground">Los medios de pago están disponibles; todavía no registran recaudo hoy.</div>}</div><Ranking rows={paymentRows} value="netSales"/></>}</CardContent></Card>
    </section>

    <section className="grid gap-4 lg:grid-cols-2">
      <RankingCard title="Vendedores" description="Quién explica la venta neta de hoy." rows={sellers.data??[]} icon={Users}/>
      <RankingCard title="Productos" description="Productos con mayor venta neta." rows={products.data??[]} icon={PackageSearch}/>
      <RankingCard title="Proveedores impactados" description="Venta atribuida al proveedor proyectado de cada producto." rows={suppliers.data??[]} icon={ShoppingCart}/>
      <RankingCard title="Sedes" description="Distribución macro por bodega o sede de venta." rows={warehouses.data??[]} icon={Percent}/>
    </section>
  </div>;
}

function useBreakdown(filter:SalesReportFilter|undefined,dimension:SalesDimension,limit:number){return useQuery({queryKey:["sales-report-today-breakdown",filter,dimension],queryFn:()=>salesReportingApi.breakdown(filter!,dimension,limit),enabled:Boolean(filter),staleTime:30_000})}
function Metric({title,value,detail,icon:Icon,tone="plain"}:{title:string;value:string;detail:string;icon:typeof TrendingUp;tone?:"plain"|"dark"|"teal"|"amber"|"blue"}){const styles={plain:"",dark:"border-slate-950 bg-slate-950 text-white",teal:"border-teal-200 bg-teal-50 text-teal-950",amber:"border-amber-200 bg-amber-50 text-amber-950",blue:"border-blue-200 bg-blue-50 text-blue-950"};return <Card className={`rounded-2xl ${styles[tone]}`}><CardContent className="p-5"><div className="flex items-center justify-between"><span className="text-xs font-bold uppercase tracking-wide opacity-65">{title}</span><Icon className="h-5 w-5 opacity-65"/></div><strong className="mt-3 block text-2xl tracking-tight">{value}</strong><small className="mt-1 block opacity-65">{detail}</small></CardContent></Card>}
function RankingCard({title,description,rows,icon:Icon}:{title:string;description:string;rows:SalesBreakdownRow[];icon:typeof Users}){return <Card className="rounded-3xl"><CardHeader><div className="flex items-start justify-between"><div><CardTitle>{title}</CardTitle><CardDescription>{description}</CardDescription></div><Icon className="h-5 w-5 text-muted-foreground"/></div></CardHeader><CardContent>{rows.length?<><div className="h-56"><ResponsiveContainer width="100%" height="100%"><BarChart data={rows} layout="vertical" margin={{left:25}}><CartesianGrid strokeDasharray="3 3" horizontal={false}/><XAxis type="number" tickFormatter={compact}/><YAxis type="category" dataKey="label" width={120} tick={{fontSize:11}}/><Tooltip formatter={value=>money.format(Number(value))}/><Bar dataKey="netSales" name="Venta neta" fill="#0f766e" radius={[0,7,7,0]}/></BarChart></ResponsiveContainer></div></>:<p className="py-16 text-center text-sm text-muted-foreground">Sin datos proyectados hoy.</p>}</CardContent></Card>}
function Ranking({rows,value}:{rows:SalesBreakdownRow[];value:"netSales"}){return <div className="space-y-2">{rows.slice(0,5).map((row,index)=><div key={row.key} className="flex items-center gap-3 text-sm"><span className="h-2.5 w-2.5 rounded-full" style={{background:palette[index%palette.length]}}/><span className="min-w-0 flex-1 truncate">{paymentLabel(row.label)}</span><strong>{money.format(row[value])}</strong></div>)}{!rows.length&&<p className="text-center text-sm text-muted-foreground">Sin pagos proyectados hoy.</p>}</div>}
function paymentLabel(value:string){return ({Cash:"Efectivo",Credit:"Crédito",Card:"Tarjeta",Transfer:"Transferencia"} as Record<string,string>)[value]??value}
function compact(value:number){return new Intl.NumberFormat("es-CO",{notation:"compact",maximumFractionDigits:1}).format(value)}
function formatDate(value:string){return new Date(`${value}T12:00:00`).toLocaleDateString("es-CO",{weekday:"long",day:"numeric",month:"long"})}
