"use client";

import { useEffect, useMemo, useState } from "react";
import {
  ArrowLeft, CheckCircle2, ChevronLeft, ChevronRight, Eye, FileText,
  History, Loader2, Minus, Plus, Printer, ReceiptText, Search,
  ShieldCheck, Trash2, WifiOff,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { toast } from "@/components/ui/toast";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import {
  serviceInvoicesApi, type BillableServiceItem, type IssuedServiceInvoice,
  type ServiceInvoiceCustomerItem, type ServiceInvoiceDetail,
  type ServiceInvoiceHistoryItem,
} from "@/services/api/service-invoices";
import type { PosPrintTemplateFormat } from "@/services/pos/pos-edge-client";
import {
  openServiceInvoicePrintPreview,
  renderServiceInvoice,
} from "@/services/service-invoices/service-invoice-print";

type CartLine = BillableServiceItem & {
  quantity: number;
  discountKind: "Percentage" | "Value";
  discountValue: number;
};

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export default function ServiceInvoicesPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const canDiscount = permissions.includes("service-invoices.discount");
  const canIssue = permissions.includes("service-invoices.issue") && permissions.includes("service-invoices.create");
  const [online, setOnline] = useState(true);
  const [services, setServices] = useState<BillableServiceItem[]>([]);
  const [customers, setCustomers] = useState<ServiceInvoiceCustomerItem[]>([]);
  const [serviceQuery, setServiceQuery] = useState("");
  const [customerQuery, setCustomerQuery] = useState("");
  const [customer, setCustomer] = useState<ServiceInvoiceCustomerItem>();
  const [lines, setLines] = useState<CartLine[]>([]);
  const [paymentMethod, setPaymentMethod] = useState("Transfer");
  const [paymentReference, setPaymentReference] = useState("");
  const [loading, setLoading] = useState(false);
  const [issuing, setIssuing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [ticket, setTicket] = useState<IssuedServiceInvoice>();
  const [historyOpen, setHistoryOpen] = useState(false);
  const [history, setHistory] = useState<ServiceInvoiceHistoryItem[]>([]);
  const [historyTotal, setHistoryTotal] = useState(0);
  const [historyPage, setHistoryPage] = useState(1);
  const [historyQuery, setHistoryQuery] = useState("");
  const [historyLoading, setHistoryLoading] = useState(false);
  const [detail, setDetail] = useState<ServiceInvoiceDetail>();
  const [printFormat, setPrintFormat] = useState<PosPrintTemplateFormat>("HalfLetter");

  useEffect(() => {
    const update = () => setOnline(navigator.onLine);
    update();
    window.addEventListener("online", update);
    window.addEventListener("offline", update);
    return () => { window.removeEventListener("online", update); window.removeEventListener("offline", update); };
  }, []);

  useEffect(() => {
    if (!businessId || !online) return;
    let active = true;
    setLoading(true);
    Promise.all([
      serviceInvoicesApi.services(businessId, serviceQuery),
      serviceInvoicesApi.customers(businessId, customerQuery),
    ]).then(([servicePage, customerPage]) => {
      if (!active) return;
      setServices(servicePage.items);
      setCustomers(customerPage.items);
    }).catch((error: Error) => active && toast.error(error.message))
      .finally(() => active && setLoading(false));
    return () => { active = false; };
  }, [businessId, serviceQuery, customerQuery, online]);

  useEffect(() => {
    if (!historyOpen || !businessId || detail) return;
    let active = true;
    setHistoryLoading(true);
    serviceInvoicesApi.history(businessId, {
      query: historyQuery || undefined, page: historyPage, pageSize: 15,
    }).then((page) => {
      if (!active) return;
      setHistory(page.items);
      setHistoryTotal(page.total);
    }).catch((error: Error) => active && toast.error(error.message))
      .finally(() => active && setHistoryLoading(false));
    return () => { active = false; };
  }, [businessId, detail, historyOpen, historyPage, historyQuery]);

  const totals = useMemo(() => lines.reduce((value, line) => {
    const gross = line.unitPrice * line.quantity;
    const discount = line.discountKind === "Percentage"
      ? gross * Math.min(100, line.discountValue) / 100
      : Math.min(gross, line.discountValue);
    const base = Math.max(0, gross - discount);
    const tax = base * line.taxRate / 100;
    return { base: value.base + base, discount: value.discount + discount, tax: value.tax + tax, total: value.total + base + tax };
  }, { base: 0, discount: 0, tax: 0, total: 0 }), [lines]);

  const add = (service: BillableServiceItem) => setLines((current) => {
    const found = current.find((line) => line.billableServiceId === service.billableServiceId);
    return found
      ? current.map((line) => line === found ? { ...line, quantity: line.quantity + 1 } : line)
      : [...current, { ...service, quantity: 1, discountKind: "Percentage", discountValue: 0 }];
  });
  const update = (id: string, patch: Partial<CartLine>) => setLines((current) =>
    current.map((line) => line.billableServiceId === id ? { ...line, ...patch } : line));

  async function issue() {
    if (!businessId || !customer || !lines.length || !online || !canIssue) return;
    setIssuing(true);
    try {
      const result = await serviceInvoicesApi.issue(
        businessId, customer.customerId,
        lines.map((line) => ({
          billableServiceId: line.billableServiceId,
          quantity: line.quantity,
          description: line.description ?? line.name,
          discountKind: line.discountValue > 0 ? line.discountKind : undefined,
          discountValue: line.discountValue,
        })), paymentMethod, paymentReference || undefined, crypto.randomUUID());
      setTicket(result);
      setConfirming(false);
      setLines([]);
      setCustomer(undefined);
      setPaymentReference("");
      toast.success(`Factura ${result.documentNumber} emitida`);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible emitir la factura.");
    } finally { setIssuing(false); }
  }

  async function showDetail(documentId: string) {
    if (!businessId) return;
    setHistoryLoading(true);
    try { setDetail(await serviceInvoicesApi.detail(businessId, documentId)); }
    catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible consultar la factura."); }
    finally { setHistoryLoading(false); }
  }

  async function printInvoice(documentId: string) {
    if (!businessId) return;
    const preview = openServiceInvoicePrintPreview();
    try {
      const printable = await serviceInvoicesApi.printable(businessId, documentId);
      renderServiceInvoice(preview, printable, printFormat);
    } catch (error) {
      preview?.close();
      toast.error(error instanceof Error ? error.message : "No fue posible imprimir la factura.");
    }
  }

  if (!businessId) return <Card><CardContent className="p-10 text-center text-muted-foreground">Selecciona una sede en la barra superior.</CardContent></Card>;

  return <div className="space-y-6 pb-24">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div><p className="text-sm font-semibold text-teal-700">Facturación online</p><h1 className="text-3xl font-black tracking-tight">Servicios y facturación</h1><p className="mt-2 max-w-3xl text-muted-foreground">Emite servicios sin afectar bodegas, existencias, caja ni sincronización POS. Precios, descuentos, impuestos y cupo DIAN se validan nuevamente en el servidor.</p></div>
      <div className="flex flex-wrap gap-2"><Button variant="outline" onClick={() => { setHistoryOpen(true); setDetail(undefined); }}><History className="mr-2 h-4 w-4"/>Historial</Button><Badge variant="outline" className="w-fit rounded-full px-4 py-2"><ShieldCheck className="mr-2 h-4 w-4"/>Factura electrónica · FSV</Badge></div>
    </header>
    {!online && <div className="flex gap-3 rounded-2xl border border-amber-300 bg-amber-50 p-4 text-amber-950"><WifiOff className="mt-0.5 h-5 w-5 shrink-0"/><div><strong>Los servicios funcionan únicamente en línea</strong><p className="text-sm">El catálogo no se descarga a la caja y no se puede emitir hasta recuperar conexión.</p></div></div>}
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_390px]">
      <div className="space-y-5">
        <Card className="rounded-3xl"><CardHeader><CardTitle>1. Cliente</CardTitle></CardHeader><CardContent className="space-y-3">
          {customer ? <div className="flex items-center justify-between rounded-2xl border border-teal-200 bg-teal-50 p-4"><div><strong>{customer.displayName}</strong><p className="text-sm text-muted-foreground">{customer.identification}{customer.email ? ` · ${customer.email}` : " · Sin correo; no se programará envío"}</p></div><Button variant="ghost" onClick={() => setCustomer(undefined)}>Cambiar</Button></div> : <><div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={customerQuery} onChange={(event) => setCustomerQuery(event.target.value)} placeholder="Buscar por nombre o identificación"/></div><div className="grid max-h-52 gap-2 overflow-y-auto">{customers.map((value) => <button key={value.customerId} type="button" onClick={() => setCustomer(value)} className="rounded-2xl border p-3 text-left transition hover:border-teal-400 hover:bg-teal-50"><strong className="block">{value.displayName}</strong><small className="text-muted-foreground">{value.identification}{value.email ? ` · ${value.email}` : ""}</small></button>)}</div></>}
        </CardContent></Card>
        <Card className="rounded-3xl"><CardHeader><CardTitle>2. Agregar servicios</CardTitle></CardHeader><CardContent className="space-y-4"><div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={serviceQuery} onChange={(event) => setServiceQuery(event.target.value)} placeholder="Buscar por código o nombre"/></div>{loading ? <div className="flex justify-center p-10"><Loader2 className="h-6 w-6 animate-spin"/></div> : <div className="grid gap-3 sm:grid-cols-2">{services.map((service) => <button key={service.billableServiceId} type="button" onClick={() => add(service)} className="group rounded-2xl border p-4 text-left transition hover:-translate-y-0.5 hover:border-teal-400 hover:shadow-md"><div className="flex justify-between gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-teal-50 text-teal-700"><FileText className="h-5 w-5"/></span><Plus className="h-5 w-5 text-muted-foreground group-hover:text-teal-700"/></div><strong className="mt-3 block">{service.name}</strong><p className="line-clamp-2 text-xs text-muted-foreground">{service.description || service.code}</p><div className="mt-3 flex items-center justify-between"><span className="font-bold text-teal-800">{money.format(service.unitPrice)}</span><Badge variant="secondary">{service.taxName} {service.taxRate}%</Badge></div></button>)}</div>}</CardContent></Card>
        {!!lines.length && <Card className="rounded-3xl"><CardHeader><CardTitle>3. Detalle</CardTitle></CardHeader><CardContent className="space-y-3">{lines.map((line) => <div key={line.billableServiceId} className="rounded-2xl border p-4"><div className="flex items-start justify-between gap-3"><div><strong>{line.name}</strong><p className="text-xs text-muted-foreground">{line.code} · {money.format(line.unitPrice)} · {line.taxName} {line.taxRate}%</p></div><Button size="icon" variant="ghost" onClick={() => setLines((current) => current.filter((value) => value !== line))}><Trash2 className="h-4 w-4 text-red-600"/></Button></div><div className="mt-4 grid gap-3 sm:grid-cols-[150px_1fr_130px]"><div><Label>Cantidad</Label><div className="mt-1 flex items-center rounded-md border"><Button size="icon" variant="ghost" onClick={() => update(line.billableServiceId, { quantity: Math.max(1, line.quantity - 1) })}><Minus className="h-3 w-3"/></Button><Input className="border-0 text-center shadow-none" type="number" min={1} value={line.quantity} onChange={(event) => update(line.billableServiceId, { quantity: Math.max(1, Number(event.target.value)) })}/><Button size="icon" variant="ghost" onClick={() => update(line.billableServiceId, { quantity: line.quantity + 1 })}><Plus className="h-3 w-3"/></Button></div></div>{canDiscount ? <><div><Label>Tipo de descuento</Label><Select value={line.discountKind} onValueChange={(value: "Percentage" | "Value") => update(line.billableServiceId, { discountKind: value })}><SelectTrigger className="mt-1"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Percentage">Porcentaje</SelectItem><SelectItem value="Value">Valor</SelectItem></SelectContent></Select></div><div><Label>{line.discountKind === "Percentage" ? "%" : "Valor"}</Label><Input className="mt-1" type="number" min={0} value={line.discountValue} onChange={(event) => update(line.billableServiceId, { discountValue: Math.max(0, Number(event.target.value)) })}/></div></> : <div className="sm:col-span-2"/>}</div></div>)}</CardContent></Card>}
      </div>
      <aside className="xl:sticky xl:top-24 xl:h-fit"><Card className="overflow-hidden rounded-3xl border-slate-900 bg-slate-950 text-white shadow-xl"><CardHeader><CardTitle className="flex items-center gap-2"><ReceiptText className="h-5 w-5 text-teal-300"/>Resumen</CardTitle></CardHeader><CardContent className="space-y-5"><div className="space-y-2 text-sm"><Row label="Base" value={money.format(totals.base)}/><Row label="Descuentos" value={`− ${money.format(totals.discount)}`}/><Row label="Impuestos" value={money.format(totals.tax)}/><div className="my-3 border-t border-white/15"/><Row label="Total" value={money.format(totals.total)} strong/></div><div className="space-y-2"><Label className="text-white">Medio de pago</Label><Select value={paymentMethod} onValueChange={setPaymentMethod}><SelectTrigger className="border-white/20 bg-white/10"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Transfer">Transferencia</SelectItem><SelectItem value="Cash">Efectivo</SelectItem><SelectItem value="DebitCard">Tarjeta débito</SelectItem><SelectItem value="CreditCard">Tarjeta crédito</SelectItem></SelectContent></Select><Input className="border-white/20 bg-white/10 text-white placeholder:text-white/50" value={paymentReference} onChange={(event) => setPaymentReference(event.target.value)} placeholder="Referencia opcional"/></div><Button className="h-12 w-full bg-teal-400 font-bold text-slate-950 hover:bg-teal-300" disabled={!customer || !lines.length || !online || !canIssue} onClick={() => setConfirming(true)}>Revisar y emitir</Button>{!canIssue && <p className="text-xs text-amber-200">Tu rol no tiene permiso para emitir facturas de servicio.</p>}</CardContent></Card></aside>
    </div>
    <Dialog open={confirming} onOpenChange={setConfirming}><DialogContent className="max-w-xl"><DialogHeader><DialogTitle>Confirmar factura electrónica</DialogTitle><DialogDescription>Esta acción consume numeración y cupo DIAN. Los impuestos se recalcularán en servidor.</DialogDescription></DialogHeader><div className="space-y-3 rounded-2xl bg-muted p-4 text-sm"><Row label="Cliente" value={customer?.displayName ?? "—"}/><Row label="Servicios" value={`${lines.length}`}/><Row label="Total estimado" value={money.format(totals.total)} strong/></div><div className="flex justify-end gap-2"><Button variant="outline" onClick={() => setConfirming(false)}>Volver</Button><Button disabled={issuing} onClick={() => void issue()}>{issuing && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}Emitir factura</Button></div></DialogContent></Dialog>
    <Dialog open={Boolean(ticket)} onOpenChange={(open) => !open && setTicket(undefined)}><DialogContent className="max-w-sm"><DialogHeader><DialogTitle className="flex items-center gap-2"><CheckCircle2 className="h-6 w-6 text-emerald-600"/>Pago y factura registrados</DialogTitle><DialogDescription>El documento quedó en la cola fiscal existente.</DialogDescription></DialogHeader>{ticket && <><div className="rounded-2xl border bg-white p-5 font-mono text-sm shadow-inner"><div className="text-center"><strong className="text-lg">AURALY</strong><p className="text-xs text-muted-foreground">Factura de servicios</p></div><div className="my-4 border-t border-dashed"/><Row label="Documento" value={ticket.documentNumber}/><Row label="Número fiscal" value={ticket.fiscalNumber}/><Row label="Base" value={money.format(ticket.untaxedAmount)}/><Row label="IVA" value={money.format(ticket.taxAmount)}/><Row label="Total" value={money.format(ticket.payableAmount)} strong/><div className="my-4 border-t border-dashed"/><p className="break-all text-[10px] text-muted-foreground">CUFE {ticket.cufe}</p><Badge className="mt-4 w-full justify-center" variant="secondary">{ticket.fiscalStatus}</Badge></div><Button onClick={() => void printInvoice(ticket.documentId)}><Printer className="mr-2 h-4 w-4"/>Ver e imprimir</Button></>}</DialogContent></Dialog>
    <Dialog open={historyOpen} onOpenChange={(open) => { setHistoryOpen(open); if (!open) setDetail(undefined); }}><DialogContent className="max-h-[90vh] max-w-5xl overflow-y-auto"><DialogHeader><DialogTitle>{detail ? "Detalle de factura" : "Historial de facturas de servicios"}</DialogTitle><DialogDescription>{detail ? "Representación, trazabilidad fiscal y medios de pago." : "Consulta paginada exclusiva de servicios, sin mezclar documentos del POS."}</DialogDescription></DialogHeader>{detail ? <ServiceInvoiceDetailView value={detail} format={printFormat} onFormat={setPrintFormat} onBack={() => setDetail(undefined)} onPrint={() => void printInvoice(detail.documentId)}/> : <div className="space-y-4"><div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input className="pl-9" value={historyQuery} onChange={(event) => { setHistoryQuery(event.target.value); setHistoryPage(1); }} placeholder="Número, cliente o identificación"/></div>{historyLoading ? <div className="grid min-h-48 place-items-center"><Loader2 className="h-6 w-6 animate-spin"/></div> : <div className="space-y-2">{history.map((item) => <button key={item.documentId} type="button" onClick={() => void showDetail(item.documentId)} className="grid w-full gap-2 rounded-2xl border p-4 text-left transition hover:border-teal-400 hover:bg-teal-50 md:grid-cols-[1.1fr_1.4fr_.8fr_auto] md:items-center"><div><strong>{item.documentNumber}</strong><p className="text-xs text-muted-foreground">DIAN {item.fiscalNumber}</p></div><div><strong>{item.customerName}</strong><p className="text-xs text-muted-foreground">{item.customerIdentification}</p></div><div><strong>{money.format(item.payableAmount)}</strong><p className="text-xs text-muted-foreground">{new Date(item.issuedAt).toLocaleString("es-CO")}</p></div><span className="flex items-center gap-2"><Badge variant="secondary">{item.fiscalStatus}</Badge><Eye className="h-4 w-4"/></span></button>)}{!history.length && <p className="p-10 text-center text-muted-foreground">No hay facturas de servicios para estos filtros.</p>}</div>}<div className="flex items-center justify-between"><small>{historyTotal} documentos</small><div className="flex items-center gap-2"><Button size="icon" variant="outline" disabled={historyPage === 1} onClick={() => setHistoryPage((value) => value - 1)}><ChevronLeft className="h-4 w-4"/></Button><span className="text-sm">Página {historyPage}</span><Button size="icon" variant="outline" disabled={historyPage * 15 >= historyTotal} onClick={() => setHistoryPage((value) => value + 1)}><ChevronRight className="h-4 w-4"/></Button></div></div></div>}</DialogContent></Dialog>
  </div>;
}

function ServiceInvoiceDetailView({ value, format, onFormat, onBack, onPrint }: { value: ServiceInvoiceDetail; format: PosPrintTemplateFormat; onFormat: (value: PosPrintTemplateFormat) => void; onBack: () => void; onPrint: () => void }) {
  return <div className="space-y-5"><div className="flex flex-wrap items-center justify-between gap-3"><Button variant="ghost" onClick={onBack}><ArrowLeft className="mr-2 h-4 w-4"/>Volver</Button><div className="flex gap-2"><Select value={format} onValueChange={(next) => onFormat(next as PosPrintTemplateFormat)}><SelectTrigger className="w-40"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Receipt">Tirilla</SelectItem><SelectItem value="HalfLetter">Media carta</SelectItem><SelectItem value="HalfLegal">Media oficio</SelectItem><SelectItem value="Letter">Carta</SelectItem></SelectContent></Select><Button onClick={onPrint}><Printer className="mr-2 h-4 w-4"/>Imprimir</Button></div></div><div className="grid gap-4 rounded-2xl bg-slate-950 p-5 text-white md:grid-cols-3"><div><small>Documento</small><strong className="block">{value.documentNumber}</strong></div><div><small>Número DIAN</small><strong className="block">{value.fiscalNumber}</strong></div><div><small>Estado</small><strong className="block">{value.fiscalStatus}</strong></div><div><small>Cliente</small><strong className="block">{value.customerName}</strong></div><div><small>Identificación</small><strong className="block">{value.customerIdentification}</strong></div><div><small>Emisión</small><strong className="block">{new Date(value.issuedAt).toLocaleString("es-CO")}</strong></div></div><div className="overflow-x-auto rounded-2xl border"><table className="w-full text-sm"><thead className="bg-muted"><tr><th className="p-3 text-left">Servicio</th><th className="p-3 text-right">Cant.</th><th className="p-3 text-right">Precio</th><th className="p-3 text-right">IVA</th><th className="p-3 text-right">Total</th></tr></thead><tbody>{value.lines.map((line) => <tr key={line.lineNumber} className="border-t"><td className="p-3"><strong>{line.description}</strong><small className="block text-muted-foreground">{line.serviceCode}</small></td><td className="p-3 text-right">{line.quantity}</td><td className="p-3 text-right">{money.format(line.unitPrice)}</td><td className="p-3 text-right">{money.format(line.taxAmount)}</td><td className="p-3 text-right font-bold">{money.format(line.lineTotal)}</td></tr>)}</tbody></table></div><div className="ml-auto max-w-sm space-y-2 rounded-2xl border p-4"><Row label="Subtotal" value={money.format(value.untaxedAmount)}/><Row label="IVA" value={money.format(value.taxAmount)}/><Row label="Total" value={money.format(value.payableAmount)} strong/></div><p className="break-all rounded-xl bg-muted p-3 text-[10px]"><strong>CUFE</strong><br/>{value.cufe}</p></div>;
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return <div className={`flex items-center justify-between gap-4 ${strong ? "text-lg font-black" : ""}`}><span>{label}</span><span className="text-right">{value}</span></div>;
}
