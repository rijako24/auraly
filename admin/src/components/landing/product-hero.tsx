"use client";

import { useEffect, useState } from "react";
import {
  ArrowRight,
  Bot,
  Check,
  ChevronRight,
  CircleDollarSign,
  CloudOff,
  MapPin,
  PackageCheck,
  ReceiptText,
  Route,
  ShoppingBag,
  Sparkles,
  Truck,
  Wifi,
} from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

const HERO_SLIDES = [
  { label: "Facturación y control", eyebrow: "Todo conectado", render: BillingPreview },
  { label: "Pedidos sin señal", eyebrow: "Venta en calle", render: OrdersPreview },
  { label: "Entregas y recaudos", eyebrow: "Ruta bajo control", render: DeliveryPreview },
  { label: "IA con datos reales", eyebrow: "Decide preguntando", render: AiPreview },
] as const;

export function ProductHero() {
  const [active, setActive] = useState(0);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const timer = window.setInterval(() => {
      setActive((current) => (current + 1) % HERO_SLIDES.length);
    }, 5200);
    return () => window.clearInterval(timer);
  }, []);

  const Preview = HERO_SLIDES[active].render;

  return (
    <section className="landing-hero relative isolate overflow-hidden bg-[#051518] text-white">
      <div className="landing-grid absolute inset-0 opacity-35" />
      <div className="landing-aurora landing-aurora-a absolute -left-52 -top-56 h-[34rem] w-[34rem] rounded-full bg-[#31d6c8]/25 blur-[100px]" />
      <div className="landing-aurora landing-aurora-b absolute -bottom-72 right-[-9rem] h-[42rem] w-[42rem] rounded-full bg-[#126b74]/35 blur-[120px]" />

      <div className="relative mx-auto grid min-h-[calc(100svh-4rem)] max-w-[90rem] items-center gap-12 px-4 py-14 sm:px-6 lg:grid-cols-[.86fr_1.14fr] lg:px-8 lg:py-20">
        <div className="max-w-3xl">
          <Badge className="mb-6 border border-[#7ce3db]/30 bg-[#7ce3db]/10 px-3 py-1.5 text-[#a6f1ea] hover:bg-[#7ce3db]/10">
            <Sparkles className="mr-2 h-3.5 w-3.5" />
            El sistema operativo de tu empresa
          </Badge>
          <h1 className="text-balance text-[clamp(3rem,7vw,6.6rem)] font-semibold leading-[.92] tracking-[-.065em]">
            Tu negocio,
            <span className="mt-1 block bg-gradient-to-r from-[#a6f1ea] via-[#69d9d0] to-[#56aeb8] bg-clip-text text-transparent">
              en una sola verdad.
            </span>
          </h1>
          <p className="mt-7 max-w-2xl text-balance text-lg leading-8 text-white/[.67] sm:text-xl">
            Factura electrónicamente, controla inventario, contabilidad y nómina, toma pedidos incluso sin señal, entrega, recauda y consulta toda tu operación con agentes de IA.
          </p>
          <div className="mt-9 flex flex-col gap-3 sm:flex-row">
            <Button size="lg" asChild className="h-[3.25rem] rounded-full bg-[#69d9d0] px-7 text-[#041417] shadow-[0_16px_45px_rgba(105,217,208,.24)] hover:bg-[#8be8e1]">
              <a href="#demo">Solicitar demo <ArrowRight className="ml-2 h-4 w-4" /></a>
            </Button>
            <Button size="lg" variant="outline" asChild className="h-[3.25rem] rounded-full border-white/[.18] bg-white/[.06] px-7 text-white backdrop-blur hover:bg-white/10 hover:text-white">
              <a href="#plataforma">Explorar la plataforma <ChevronRight className="ml-1 h-4 w-4" /></a>
            </Button>
          </div>
          <div className="mt-8 flex flex-wrap gap-x-6 gap-y-3 text-sm text-white/58">
            {["Facturación electrónica DIAN", "Operación online y offline", "Información en tiempo real"].map((item) => (
              <span key={item} className="flex items-center gap-2">
                <span className="grid h-5 w-5 place-items-center rounded-full bg-[#69d9d0]/12 text-[#69d9d0]"><Check className="h-3 w-3" /></span>
                {item}
              </span>
            ))}
          </div>
        </div>

        <div className="relative mx-auto w-full max-w-3xl lg:pl-4">
          <div className="landing-product-shell relative rounded-[2rem] border border-white/15 bg-white/[.075] p-2.5 shadow-[0_45px_120px_rgba(0,0,0,.48)] backdrop-blur-2xl sm:p-4">
            <div className="mb-3 flex items-center justify-between px-2 py-1">
              <div className="flex items-center gap-2">
                <span className="h-2.5 w-2.5 rounded-full bg-[#ff7f78]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[#f4c65d]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[#69d9d0]" />
              </div>
              <div className="flex items-center gap-2 rounded-full border border-white/10 bg-black/15 px-3 py-1 text-[11px] font-medium text-white/55">
                <span className="h-1.5 w-1.5 rounded-full bg-[#69d9d0] shadow-[0_0_12px_#69d9d0]" /> En vivo
              </div>
            </div>
            <div key={active} className="landing-slide-in min-h-[31rem] overflow-hidden rounded-[1.4rem] bg-[#f6faf9] text-[#0d2529] sm:min-h-[34rem]">
              <Preview />
            </div>
          </div>

          <div className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-4" role="tablist" aria-label="Vistas del producto">
            {HERO_SLIDES.map((slide, index) => (
              <button
                key={slide.label}
                type="button"
                role="tab"
                aria-selected={active === index}
                onClick={() => setActive(index)}
                className={cn(
                  "group rounded-2xl border px-3 py-3 text-left transition duration-300",
                  active === index
                    ? "border-[#69d9d0]/55 bg-[#69d9d0]/12 text-white"
                    : "border-white/10 bg-white/[.035] text-white/45 hover:border-white/20 hover:text-white/75",
                )}
              >
                <span className="block text-[10px] font-bold uppercase tracking-[.16em] opacity-60">{slide.eyebrow}</span>
                <span className="mt-1 block text-xs font-semibold sm:text-sm">{slide.label}</span>
                <span className={cn("mt-2 block h-0.5 origin-left rounded-full bg-[#69d9d0]", active === index ? "landing-slide-progress" : "scale-x-0")} />
              </button>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

function PreviewHeader({ icon: Icon, title, detail }: { icon: typeof ReceiptText; title: string; detail: string }) {
  return (
    <div className="flex items-center gap-3 border-b border-[#0e3a3f]/10 bg-white px-4 py-4 sm:px-6">
      <span className="grid h-10 w-10 place-items-center rounded-xl bg-[#0e5f64] text-white"><Icon className="h-5 w-5" /></span>
      <div className="min-w-0 flex-1"><strong className="block truncate text-sm sm:text-base">{title}</strong><span className="block truncate text-xs text-[#567276]">{detail}</span></div>
      <span className="hidden items-center gap-1.5 rounded-full bg-[#e1f8f4] px-3 py-1 text-xs font-semibold text-[#126a65] sm:flex"><Wifi className="h-3.5 w-3.5" /> Sincronizado</span>
    </div>
  );
}

function BillingPreview() {
  return (
    <div>
      <PreviewHeader icon={ReceiptText} title="Panel de operación" detail="Hoy · todas las sedes" />
      <div className="grid gap-3 p-4 sm:grid-cols-3 sm:p-6">
        {[
          ["Ventas de hoy", "$ 18.420.500", "+18,4%"],
          ["Inventario", "$ 214,8 M", "4 bodegas"],
          ["Por cobrar", "$ 31.760.000", "12 vencen hoy"],
        ].map(([label, value, note], index) => (
          <div key={label} className={cn("rounded-2xl border p-4", index === 0 ? "border-[#0e7775]/20 bg-[#e6faf6]" : "border-black/[.06] bg-white")}>
            <span className="text-xs text-[#6a8083]">{label}</span><strong className="mt-2 block text-lg tracking-tight sm:text-xl">{value}</strong><span className="mt-2 block text-[11px] font-semibold text-[#14766e]">{note}</span>
          </div>
        ))}
      </div>
      <div className="grid gap-3 px-4 pb-4 sm:grid-cols-[1.15fr_.85fr] sm:px-6 sm:pb-6">
        <div className="rounded-2xl border border-black/[.06] bg-white p-4">
          <div className="flex items-center justify-between"><div><span className="text-xs text-[#6a8083]">Ventas esta semana</span><strong className="block text-lg">$ 86,2 M</strong></div><span className="rounded-full bg-[#e6faf6] px-2 py-1 text-[10px] font-bold text-[#14766e]">EN TIEMPO REAL</span></div>
          <div className="mt-5 flex h-28 items-end gap-2">
            {[34, 54, 43, 72, 62, 90, 76].map((height, index) => <span key={index} className="landing-chart-bar flex-1 rounded-t-md bg-gradient-to-t from-[#0e5f64] to-[#69d9d0]" style={{ height: `${height}%`, animationDelay: `${index * 70}ms` }} />)}
          </div>
          <div className="mt-2 flex justify-between text-[9px] font-medium text-[#829598]"><span>LUN</span><span>MAR</span><span>MIÉ</span><span>JUE</span><span>VIE</span><span>SÁB</span><span>DOM</span></div>
        </div>
        <div className="rounded-2xl bg-[#092a2f] p-4 text-white">
          <span className="text-xs text-white/50">Documentos DIAN</span>
          <strong className="mt-1 block text-2xl">1.284</strong>
          <div className="mt-4 space-y-3">
            {["FV-10482", "FV-10481", "NC-00184"].map((document, index) => <div key={document} className="flex items-center justify-between border-b border-white/10 pb-2 text-xs"><span>{document}</span><span className="flex items-center gap-1 text-[#77dfd2]"><Check className="h-3 w-3" /> {index === 2 ? "Procesada" : "Aceptada"}</span></div>)}
          </div>
          <div className="mt-4 rounded-xl bg-white/[.07] p-3 text-xs text-white/65"><strong className="block text-white">Contabilidad conectada</strong>Los movimientos llegan listos para revisar.</div>
        </div>
      </div>
    </div>
  );
}

function OrdersPreview() {
  return (
    <div>
      <PreviewHeader icon={Route} title="Mi ruta de hoy" detail="Ruta Norte · 7 de 12 visitas" />
      <div className="grid gap-4 p-4 sm:grid-cols-[.84fr_1.16fr] sm:p-6">
        <div className="relative mx-auto w-full max-w-[15rem] rounded-[2.2rem] border-[6px] border-[#11282c] bg-white p-3 shadow-2xl">
          <div className="mx-auto mb-3 h-1.5 w-14 rounded-full bg-[#dce7e7]" />
          <div className="rounded-2xl bg-gradient-to-br from-[#08292e] to-[#0e7775] p-3 text-white">
            <span className="text-[10px] font-bold text-[#a6f1ea]">SIGUIENTE CLIENTE</span><strong className="mt-1 block text-sm">Mercado La 14</strong><span className="mt-1 flex gap-1 text-[10px] text-white/65"><MapPin className="h-3 w-3" /> Cra. 19 # 8–42</span>
          </div>
          <div className="mt-3 space-y-2">
            {["Café especial 500 g", "Leche entera x 12", "Galletas avena x 6"].map((product, index) => <div key={product} className="rounded-xl border border-black/[.06] p-2.5"><span className="block text-[10px] font-semibold">{product}</span><div className="mt-1 flex items-center justify-between text-[9px] text-[#6f8385]"><span>Disponible {24 - index * 5}</span><strong className="text-[#0e6564]">{index + 1} und.</strong></div></div>)}
          </div>
          <div className="mt-3 rounded-xl bg-[#0e7775] py-2.5 text-center text-[11px] font-bold text-white">Guardar pedido · $186.400</div>
        </div>
        <div className="flex flex-col gap-3">
          <div className="rounded-2xl border border-[#e1bb63]/35 bg-[#fff8e8] p-4"><span className="flex items-center gap-2 text-xs font-bold text-[#8b641d]"><CloudOff className="h-4 w-4" /> Modo local activo</span><p className="mt-2 text-xs leading-5 text-[#715d35]">Rutas, clientes, precios y existencias ya están guardados en el teléfono.</p></div>
          <div className="flex-1 rounded-2xl border border-black/[.06] bg-white p-4">
            <div className="flex items-center justify-between"><span className="text-xs font-semibold">Recorrido</span><span className="text-[10px] text-[#688184]">7/12 listos</span></div>
            <div className="mt-4 space-y-3">
              {[["Distribuciones Luna", "Pedido tomado", true], ["Mercado La 14", "Siguiente visita", false], ["Tienda El Parque", "Pendiente", false]].map(([name, status, done], index) => <div key={String(name)} className="flex items-center gap-3"><span className={cn("grid h-7 w-7 place-items-center rounded-lg text-[10px] font-bold", done ? "bg-[#44b99e] text-white" : "bg-[#edf3f2] text-[#4f696c]")}>{done ? <Check className="h-3.5 w-3.5" /> : index + 7}</span><span className="min-w-0 flex-1"><strong className="block truncate text-xs">{name}</strong><span className="block text-[10px] text-[#74888b]">{status}</span></span></div>)}
            </div>
          </div>
          <div className="flex items-center gap-3 rounded-2xl bg-[#092a2f] p-4 text-white"><span className="grid h-9 w-9 place-items-center rounded-xl bg-[#69d9d0]/15 text-[#69d9d0]"><ShoppingBag className="h-4 w-4" /></span><span><strong className="block text-xs">Nada se pierde sin señal</strong><span className="text-[10px] text-white/55">Sincronización automática al volver</span></span></div>
        </div>
      </div>
    </div>
  );
}

function DeliveryPreview() {
  return (
    <div>
      <PreviewHeader icon={Truck} title="Despacho DSP-00842" detail="Camilo R. · WHK 284 · 6/9 entregas" />
      <div className="grid gap-4 p-4 sm:grid-cols-[1.08fr_.92fr] sm:p-6">
        <div className="rounded-2xl border border-black/[.06] bg-[#eaf4f1] p-4">
          <div className="relative h-64 overflow-hidden rounded-xl bg-[linear-gradient(25deg,transparent_48%,rgba(20,118,110,.12)_49%,rgba(20,118,110,.12)_51%,transparent_52%),linear-gradient(115deg,transparent_48%,rgba(20,118,110,.09)_49%,rgba(20,118,110,.09)_51%,transparent_52%)] bg-[length:70px_70px]">
            <div className="landing-route-line absolute left-[23%] top-[14%] h-[72%] w-[48%] rounded-[45%] border-r-[3px] border-t-[3px] border-dashed border-[#0e7775]" />
            {[["18%","18%","1"],["57%","34%","2"],["43%","70%","3"],["73%","77%","4"]].map(([top,left,label], index) => <span key={label} className={cn("absolute grid h-8 w-8 place-items-center rounded-xl border-2 border-white text-[10px] font-black text-white shadow-lg", index < 2 ? "bg-[#41b89e]" : "bg-[#102c31]")} style={{top,left}}>{index < 2 ? <Check className="h-3.5 w-3.5" /> : label}</span>)}
            <div className="absolute bottom-3 left-3 right-3 rounded-xl border border-white/70 bg-white/85 p-3 backdrop-blur"><span className="text-[10px] text-[#6f8587]">Siguiente entrega</span><strong className="block text-xs">Supermercado El Prado</strong><span className="mt-1 block text-[10px] text-[#0e7775]">Factura FV-10482 · $428.900</span></div>
          </div>
        </div>
        <div className="space-y-3">
          <div className="rounded-2xl bg-[#092a2f] p-4 text-white"><span className="text-[10px] uppercase tracking-wider text-white/45">Resultado de visita</span><div className="mt-3 grid grid-cols-2 gap-2"><span className="rounded-xl bg-[#69d9d0] p-3 text-center text-xs font-bold text-[#062126]"><PackageCheck className="mx-auto mb-1 h-4 w-4" /> Entregado</span><span className="rounded-xl border border-white/10 p-3 text-center text-xs text-white/55">No entregado</span></div></div>
          <div className="rounded-2xl border border-black/[.06] bg-white p-4"><span className="text-xs font-semibold">¿Cómo pagó el cliente?</span><div className="mt-3 flex gap-2"><span className="flex flex-1 items-center justify-center gap-1 rounded-xl border border-[#0e7775] bg-[#e4f7f3] px-2 py-3 text-[10px] font-bold text-[#0e6966]"><CircleDollarSign className="h-4 w-4" /> Efectivo</span><span className="flex flex-1 items-center justify-center gap-1 rounded-xl border px-2 py-3 text-[10px] text-[#718689]"><ReceiptText className="h-4 w-4" /> Consignación</span></div></div>
          <div className="rounded-2xl border border-black/[.06] bg-white p-4"><div className="flex justify-between text-xs"><span>Recaudado</span><strong>$ 2.840.500</strong></div><div className="mt-2 flex justify-between text-xs"><span>Gastos aprobados</span><strong>$ 68.000</strong></div><div className="mt-3 flex justify-between border-t pt-3 text-sm"><span>Cierre esperado</span><strong className="text-[#0e7775]">$ 2.772.500</strong></div></div>
        </div>
      </div>
    </div>
  );
}

function AiPreview() {
  return (
    <div>
      <PreviewHeader icon={Bot} title="Aly · asistente de negocio" detail="Conectada a tu información autorizada" />
      <div className="grid gap-4 p-4 sm:grid-cols-[1.15fr_.85fr] sm:p-6">
        <div className="rounded-2xl border border-black/[.06] bg-white p-4 sm:p-5">
          <div className="max-w-[88%] rounded-2xl rounded-tl-md bg-[#eff4f3] p-3 text-xs leading-5">¿Cuánto vendimos hoy y qué productos debería reponer?</div>
          <div className="ml-auto mt-3 max-w-[94%] rounded-2xl rounded-tr-md bg-[#0b5c61] p-4 text-xs leading-5 text-white">
            Hoy llevas <strong>$18.420.500</strong> en ventas. Tres productos están por debajo del mínimo en Bodega Principal.
            <div className="mt-3 space-y-2 border-t border-white/10 pt-3">
              {["Café especial 500 g · faltan 18", "Leche entera x 12 · faltan 11", "Galletas avena x 6 · faltan 8"].map((item) => <div key={item} className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-[#69d9d0]" />{item}</div>)}
            </div>
          </div>
          <div className="mt-4 flex items-center rounded-xl border border-black/[.07] px-3 py-3 text-xs text-[#809194]">Pregúntale a Auraly sobre tu negocio…<ArrowRight className="ml-auto h-4 w-4 text-[#0e7775]" /></div>
        </div>
        <div className="space-y-3">
          <div className="rounded-2xl bg-gradient-to-br from-[#092a2f] to-[#0e5f64] p-5 text-white"><Sparkles className="h-5 w-5 text-[#69d9d0]" /><strong className="mt-8 block text-2xl">Tu información responde.</strong><p className="mt-2 text-xs leading-5 text-white/60">Ventas, inventario, cartera y operación disponibles según los permisos de cada persona.</p></div>
          {["Resumen de ventas listo", "Inventario consultado", "3 alertas detectadas"].map((item) => <div key={item} className="flex items-center gap-3 rounded-xl border border-black/[.06] bg-white p-3 text-xs"><span className="grid h-6 w-6 place-items-center rounded-lg bg-[#e4f7f3] text-[#0e7775]"><Check className="h-3.5 w-3.5" /></span>{item}</div>)}
        </div>
      </div>
    </div>
  );
}
