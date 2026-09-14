import {
  ArrowRight,
  BarChart3,
  Bot,
  Boxes,
  Calculator,
  Check,
  CheckCircle2,
  ChevronRight,
  CircleDollarSign,
  CloudOff,
  FileCheck2,
  Fingerprint,
  Landmark,
  Map,
  MapPin,
  PackageCheck,
  PackageOpen,
  ReceiptText,
  ScanLine,
  ShieldCheck,
  ShoppingCart,
  Sparkles,
  Truck,
  Users,
  Wifi,
} from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

const PILLARS = [
  {
    icon: ReceiptText,
    number: "01",
    title: "Facturación y finanzas",
    copy: "Factura electrónica, inventario, contabilidad y nómina trabajando sobre cada movimiento real.",
    tags: ["DIAN", "Inventario", "Contabilidad", "Nómina"],
    href: "#facturacion",
    tone: "bg-[#0d3135] text-white",
  },
  {
    icon: ShoppingCart,
    number: "02",
    title: "App de pedidos",
    copy: "Tu fuerza de ventas recorre clientes, consulta precios y existencias, y toma pedidos con o sin señal.",
    tags: ["Online + offline", "Rutas", "Precios", "Existencias"],
    href: "#pedidos",
    tone: "bg-[#dff7f2] text-[#082a2e]",
  },
  {
    icon: Truck,
    number: "03",
    title: "App de transportador",
    copy: "Entregas, novedades, devoluciones, gastos y cada forma de pago registrados desde la ruta.",
    tags: ["Mapa", "Recaudo", "Evidencias", "Cierre"],
    href: "#transporte",
    tone: "bg-[#d7e8ff] text-[#0b2038]",
  },
  {
    icon: Bot,
    number: "04",
    title: "Agentes de IA",
    copy: "Agentes que atienden clientes y asistentes internos que convierten tus datos autorizados en respuestas.",
    tags: ["WhatsApp", "Ventas", "Soporte", "Información"],
    href: "#agentes",
    tone: "bg-[#d9cffc] text-[#20113e]",
  },
] as const;

export function PlatformShowcase() {
  return (
    <>
      <section className="border-y border-[#10363a]/10 bg-white py-5" aria-label="Capacidades conectadas">
        <div className="landing-marquee mx-auto flex max-w-[90rem] flex-wrap items-center justify-center gap-x-7 gap-y-3 px-4 text-xs font-bold uppercase tracking-[.14em] text-[#47666a] sm:px-6 lg:px-8">
          {["Factura electrónica DIAN", "Inventario en línea", "Contabilidad", "Nómina", "Pedidos offline", "Despachos y recaudos", "Agentes conectados"].map((item, index) => (
            <span key={item} className="flex items-center gap-7"><span>{item}</span>{index < 6 && <span className="h-1 w-1 rounded-full bg-[#30b7aa]" />}</span>
          ))}
        </div>
      </section>

      <section id="plataforma" className="scroll-mt-20 overflow-hidden bg-[#f3f7f5] py-24 sm:py-32">
        <div className="mx-auto max-w-[90rem] px-4 sm:px-6 lg:px-8">
          <div className="grid gap-8 lg:grid-cols-[.85fr_1.15fr] lg:items-end">
            <div>
              <p className="text-sm font-bold uppercase tracking-[.2em] text-[#15766f]">Todo Auraly</p>
              <h2 className="mt-4 max-w-3xl text-balance text-4xl font-semibold leading-[.98] tracking-[-.055em] text-[#071b1e] sm:text-6xl">
                No son cuatro aplicaciones. Es una operación completa.
              </h2>
            </div>
            <p className="max-w-2xl text-lg leading-8 text-[#4d6669] lg:justify-self-end">
              Lo que ocurre en ventas se refleja en inventario, facturación, cartera y contabilidad. Los pedidos llegan a despacho. Los recaudos vuelven al cierre. Y la IA entiende ese contexto para ayudarte a decidir.
            </p>
          </div>

          <div className="mt-14 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {PILLARS.map((pillar, index) => {
              const Icon = pillar.icon;
              return (
                <a key={pillar.title} href={pillar.href} className={cn("landing-reveal landing-pillar group relative flex min-h-[25rem] flex-col overflow-hidden rounded-[2rem] p-6 shadow-sm transition duration-500 hover:-translate-y-2 hover:shadow-2xl", pillar.tone)} style={{ animationDelay: `${index * 90}ms` }}>
                  <div className="flex items-center justify-between">
                    <span className="grid h-12 w-12 place-items-center rounded-2xl border border-current/[.10] bg-white/[.12]"><Icon className="h-6 w-6" /></span>
                    <span className="font-mono text-xs opacity-50">{pillar.number}</span>
                  </div>
                  <div className="mt-auto">
                    <h3 className="text-2xl font-semibold tracking-[-.035em]">{pillar.title}</h3>
                    <p className="mt-3 text-sm leading-6 opacity-70">{pillar.copy}</p>
                    <div className="mt-5 flex flex-wrap gap-2">
                      {pillar.tags.map((tag) => <span key={tag} className="rounded-full border border-current/[.10] bg-white/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider">{tag}</span>)}
                    </div>
                    <span className="mt-7 flex items-center gap-2 text-sm font-semibold">Conocer más <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-1" /></span>
                  </div>
                  <span className="absolute -right-12 -top-12 h-40 w-40 rounded-full border border-current/[.10]" />
                </a>
              );
            })}
          </div>
        </div>
      </section>

      <FinanceSection />
      <OrdersSection />
      <TransportSection />
      <IntelligenceSection />
    </>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return <p className="text-xs font-bold uppercase tracking-[.22em] text-[#58cabf]">{children}</p>;
}

function FeatureList({ items, dark = false }: { items: string[]; dark?: boolean }) {
  return <ul className="mt-8 grid gap-3 sm:grid-cols-2">{items.map((item) => <li key={item} className={cn("flex items-start gap-3 text-sm leading-6", dark ? "text-white/70" : "text-[#496367]")}><span className={cn("mt-1 grid h-5 w-5 shrink-0 place-items-center rounded-full", dark ? "bg-[#69d9d0]/12 text-[#69d9d0]" : "bg-[#dff7f2] text-[#0e7775]")}><Check className="h-3 w-3" /></span>{item}</li>)}</ul>;
}

function FinanceSection() {
  return (
    <section id="facturacion" className="scroll-mt-20 overflow-hidden bg-[#07181b] py-24 text-white sm:py-32">
      <div className="mx-auto grid max-w-[90rem] items-center gap-14 px-4 sm:px-6 lg:grid-cols-[.72fr_1.28fr] lg:px-8">
        <div className="landing-reveal">
          <SectionLabel>Facturación + inventario + contabilidad + nómina</SectionLabel>
          <h2 className="mt-5 text-balance text-4xl font-semibold leading-[1] tracking-[-.055em] sm:text-6xl">Vendes una vez. Auraly conecta todo lo demás.</h2>
          <p className="mt-6 text-lg leading-8 text-white/62">Emite tu factura electrónica y conserva la trazabilidad que tu equipo necesita: existencias, cartera, movimientos contables y procesos de nómina en el mismo sistema.</p>
          <FeatureList dark items={["Facturación electrónica y seguimiento de documentos DIAN", "Inventario en línea por producto y bodega", "Contabilidad alimentada por la operación real", "Nómina, novedades, liquidaciones y emisión electrónica"]} />
          <Button asChild className="mt-9 rounded-full bg-[#69d9d0] px-6 text-[#062126] hover:bg-[#8be8e1]"><a href="#demo">Verlo con mis datos <ArrowRight className="ml-2 h-4 w-4" /></a></Button>
        </div>
        <FinanceWorkspace />
      </div>
    </section>
  );
}

function FinanceWorkspace() {
  return (
    <div className="landing-reveal relative">
      <div className="absolute -inset-20 bg-[radial-gradient(circle,rgba(80,207,196,.16),transparent_66%)]" />
      <div className="relative overflow-hidden rounded-[1.8rem] border border-white/[.12] bg-[#f6faf9] text-[#10272b] shadow-[0_45px_120px_rgba(0,0,0,.42)]">
        <div className="flex items-center justify-between border-b border-black/[.06] bg-white px-5 py-4">
          <div className="flex items-center gap-3"><span className="grid h-9 w-9 place-items-center rounded-xl bg-[#0d6366] text-white"><BarChart3 className="h-4 w-4" /></span><span><strong className="block text-sm">Centro financiero</strong><small className="text-[#708386]">Septiembre 2026</small></span></div>
          <div className="hidden gap-2 sm:flex">{["Resumen", "Inventario", "Contabilidad", "Nómina"].map((item, index) => <span key={item} className={cn("rounded-full px-3 py-1.5 text-[10px] font-semibold", index === 0 ? "bg-[#0d6366] text-white" : "bg-[#edf3f2] text-[#607579]")}>{item}</span>)}</div>
        </div>
        <div className="grid gap-3 p-4 sm:grid-cols-4 sm:p-6">
          {[
            [ReceiptText, "Facturado", "$ 284,6 M", "DIAN al día"],
            [Boxes, "Inventario", "$ 96,2 M", "4 bodegas"],
            [Landmark, "Cartera", "$ 31,7 M", "92% corriente"],
            [Users, "Nómina", "$ 48,4 M", "36 empleados"],
          ].map(([Icon, label, value, note]) => {
            const MetricIcon = Icon as typeof ReceiptText;
            return <div key={String(label)} className="rounded-2xl border border-black/[.06] bg-white p-4"><MetricIcon className="h-4 w-4 text-[#14766e]" /><span className="mt-4 block text-[10px] font-medium text-[#718588]">{String(label)}</span><strong className="mt-1 block text-lg tracking-tight">{String(value)}</strong><small className="mt-2 block text-[#14766e]">{String(note)}</small></div>;
          })}
        </div>
        <div className="grid gap-3 px-4 pb-4 sm:grid-cols-[1.25fr_.75fr] sm:px-6 sm:pb-6">
          <div className="overflow-hidden rounded-2xl border border-black/[.06] bg-white">
            <div className="flex items-center justify-between border-b px-4 py-3"><span className="text-xs font-semibold">Actividad conectada</span><span className="text-[10px] text-[#6d8285]">Ahora</span></div>
            {[
              ["FV-10482", "Factura electrónica aceptada", "$ 428.900", FileCheck2],
              ["INV-28491", "Inventario actualizado", "− 8 unidades", Boxes],
              ["AST-09142", "Movimiento contabilizado", "Cuadrado", Calculator],
              ["NOM-00926", "Nómina lista para pago", "36 personas", Users],
            ].map(([code, title, value, Icon], index) => {
              const RowIcon = Icon as typeof FileCheck2;
              return <div key={String(code)} className={cn("flex items-center gap-3 px-4 py-3", index < 3 && "border-b border-black/[.05]")}><span className="grid h-8 w-8 place-items-center rounded-xl bg-[#e5f7f3] text-[#14766e]"><RowIcon className="h-4 w-4" /></span><span className="min-w-0 flex-1"><strong className="block truncate text-xs">{String(title)}</strong><small className="text-[#7b8c8e]">{String(code)}</small></span><strong className="text-right text-[11px] text-[#34575b]">{String(value)}</strong></div>;
            })}
          </div>
          <div className="rounded-2xl bg-[#0c3035] p-5 text-white">
            <span className="text-xs text-white/45">Balance de comprobación</span><strong className="mt-2 block text-3xl">Cuadrado.</strong><div className="mt-6 space-y-3 text-xs">{[["Débitos", "$ 384.260.000"], ["Créditos", "$ 384.260.000"], ["Diferencia", "$ 0"]].map(([label, value], index) => <div key={label} className={cn("flex justify-between border-b border-white/10 pb-2", index === 2 && "border-0 text-[#75ded4]")}><span className="text-white/50">{label}</span><strong>{value}</strong></div>)}</div><div className="mt-6 flex items-center gap-2 text-[10px] text-[#a6f1ea]"><ShieldCheck className="h-4 w-4" /> Trazabilidad completa</div>
          </div>
        </div>
      </div>
    </div>
  );
}

function OrdersSection() {
  return (
    <section id="pedidos" className="scroll-mt-20 overflow-hidden bg-[#f3f7f5] py-24 sm:py-32">
      <div className="mx-auto grid max-w-[90rem] items-center gap-14 px-4 sm:px-6 lg:grid-cols-[1.2fr_.8fr] lg:px-8">
        <OrdersWorkspace />
        <div className="landing-reveal lg:order-2">
          <p className="text-xs font-bold uppercase tracking-[.22em] text-[#13746d]">App de pedidos</p>
          <h2 className="mt-5 text-balance text-4xl font-semibold leading-[1] tracking-[-.055em] text-[#071b1e] sm:text-6xl">Tu vendedor sigue vendiendo, incluso donde no llega la señal.</h2>
          <p className="mt-6 text-lg leading-8 text-[#50696c]">Prepara el teléfono antes de salir y lleva rutas, clientes, catálogo, precios e inventario. Cada pedido y visita pendiente se sincroniza cuando vuelve la conexión.</p>
          <FeatureList items={["Ruta del día y siguiente cliente", "Pedidos dentro o fuera de ruta", "Consulta de precios y existencias", "Trabajo online y offline en el mismo flujo"]} />
          <Button asChild variant="outline" className="mt-9 rounded-full border-[#123b3f]/20 bg-white px-6 text-[#0a3135] hover:bg-[#e4f7f3]"><a href="#demo">Quiero verla en acción <ArrowRight className="ml-2 h-4 w-4" /></a></Button>
        </div>
      </div>
    </section>
  );
}

function OrdersWorkspace() {
  return (
    <div className="landing-reveal relative mx-auto w-full max-w-3xl lg:order-1">
      <div className="absolute left-[8%] top-[12%] h-52 w-52 rounded-full bg-[#69d9d0]/35 blur-[75px]" />
      <div className="relative grid items-center gap-3 sm:grid-cols-[.7fr_1.3fr]">
        <div className="relative z-10 mx-auto w-[16rem] rounded-[2.7rem] border-[7px] border-[#0b2024] bg-white p-3 shadow-[0_35px_80px_rgba(7,27,30,.28)] sm:translate-x-5">
          <div className="mx-auto mb-3 h-1.5 w-16 rounded-full bg-[#d5e1e1]" />
          <div className="rounded-2xl bg-gradient-to-br from-[#071b1e] to-[#127b76] p-4 text-white"><span className="text-[10px] font-bold uppercase tracking-wider text-[#a6f1ea]">Mi ruta de hoy</span><strong className="mt-1 block text-lg">Ruta Centro</strong><div className="mt-3 flex gap-2"><MiniMetric label="Hechos" value="7" /><MiniMetric label="Faltan" value="5" /></div></div>
          <div className="mt-3 rounded-2xl border border-[#0f766e]/15 bg-[#e9faf6] p-3"><span className="text-[9px] font-bold text-[#13746d]">SIGUIENTE CLIENTE</span><strong className="mt-1 block text-sm">Mercado La 14</strong><span className="mt-1 flex items-center gap-1 text-[10px] text-[#617b7e]"><MapPin className="h-3 w-3" /> Cra. 19 # 8–42</span><div className="mt-3 rounded-xl bg-[#0e7775] py-2.5 text-center text-[10px] font-bold text-white">Tomar pedido</div></div>
          <div className="mt-3 flex items-center gap-2 rounded-xl bg-[#fff5d9] p-2.5 text-[9px] font-semibold text-[#87631c]"><CloudOff className="h-3.5 w-3.5" /> Modo local activo</div>
        </div>
        <div className="rounded-[2rem] border border-[#0b3438]/10 bg-white p-5 shadow-[0_30px_80px_rgba(7,27,30,.14)] sm:pl-10">
          <div className="flex items-center justify-between"><span><small className="text-[#789092]">Pedido para</small><strong className="block">Mercado La 14</strong></span><Badge className="bg-[#e5f7f3] text-[#116b67] hover:bg-[#e5f7f3]">Guardado local</Badge></div>
          <div className="mt-5 space-y-2">
            {[["CAF-500", "Café especial 500 g", "$42.800", "2"], ["LEC-12", "Leche entera x 12", "$68.400", "1"], ["GAL-06", "Galletas avena x 6", "$37.600", "2"]].map(([code, name, price, qty]) => <div key={code} className="flex items-center gap-3 rounded-xl border border-black/[.05] p-3"><span className="grid h-9 w-9 place-items-center rounded-xl bg-[#eff5f4] text-[#16746e]"><PackageOpen className="h-4 w-4" /></span><span className="min-w-0 flex-1"><strong className="block truncate text-xs">{name}</strong><small className="text-[#819294]">{code} · Disponible</small></span><span className="text-right"><strong className="block text-xs">{price}</strong><small className="text-[#13746d]">× {qty}</small></span></div>)}
          </div>
          <div className="mt-5 flex items-end justify-between border-t pt-4"><span><small className="text-[#7b8e90]">Total pedido</small><strong className="block text-2xl">$186.400</strong></span><span className="flex items-center gap-1 text-[10px] font-semibold text-[#13746d]"><Wifi className="h-3.5 w-3.5" /> Se sincroniza al volver</span></div>
        </div>
      </div>
    </div>
  );
}

function MiniMetric({ label, value }: { label: string; value: string }) {
  return <span className="flex-1 rounded-xl bg-white/10 px-2 py-2 text-center"><strong className="block text-base">{value}</strong><small className="text-[9px] text-white/55">{label}</small></span>;
}

function TransportSection() {
  return (
    <section id="transporte" className="scroll-mt-20 overflow-hidden bg-[#e6edfb] py-24 sm:py-32">
      <div className="mx-auto grid max-w-[90rem] items-center gap-14 px-4 sm:px-6 lg:grid-cols-[.78fr_1.22fr] lg:px-8">
        <div className="landing-reveal">
          <p className="text-xs font-bold uppercase tracking-[.22em] text-[#235e9a]">App del transportador</p>
          <h2 className="mt-5 text-balance text-4xl font-semibold leading-[1] tracking-[-.055em] text-[#091d33] sm:text-6xl">Cada entrega explica qué pasó y cómo pagó el cliente.</h2>
          <p className="mt-6 text-lg leading-8 text-[#526981]">El transportador ve su recorrido, confirma entregas, registra efectivo o consignación, devoluciones, gastos y novedades. Al terminar, el cierre muestra exactamente lo que debe entregar.</p>
          <ul className="mt-8 space-y-3">{[[Map, "Lista y mapa del recorrido"], [CircleDollarSign, "Efectivo, consignaciones y ventas a crédito"], [PackageCheck, "Entregas completas, parciales o no entregadas"], [FileCheck2, "Cierre con recaudos, gastos, devoluciones y diferencias"]].map(([Icon, text]) => { const ItemIcon = Icon as typeof Map; return <li key={String(text)} className="flex items-center gap-3 text-sm text-[#405d78]"><span className="grid h-9 w-9 place-items-center rounded-xl bg-white text-[#235e9a] shadow-sm"><ItemIcon className="h-4 w-4" /></span>{String(text)}</li>; })}</ul>
          <Button asChild className="mt-9 rounded-full bg-[#0c2948] px-6 text-white hover:bg-[#143c66]"><a href="#demo">Ver la app del transportador <ArrowRight className="ml-2 h-4 w-4" /></a></Button>
        </div>
        <TransportWorkspace />
      </div>
    </section>
  );
}

function TransportWorkspace() {
  return (
    <div className="landing-reveal relative">
      <div className="absolute -right-12 -top-12 h-64 w-64 rounded-full bg-[#79aee7]/35 blur-[70px]" />
      <div className="relative grid items-center gap-4 sm:grid-cols-[1.15fr_.85fr]">
        <div className="overflow-hidden rounded-[2rem] border border-[#173d65]/10 bg-white shadow-[0_35px_90px_rgba(17,55,94,.18)]">
          <div className="bg-gradient-to-br from-[#081e34] via-[#0d3457] to-[#1d67a0] p-5 text-white"><div className="flex items-center justify-between"><span><small className="font-semibold text-[#b7dcff]">Centro de entregas</small><strong className="mt-1 block text-2xl">Mis despachos</strong></span><Truck className="h-7 w-7" /></div><div className="mt-5 grid grid-cols-3 gap-2"><MiniMetric label="Asignados" value="9"/><MiniMetric label="En ruta" value="6"/><MiniMetric label="Por liquidar" value="1"/></div></div>
          <div className="p-4">
            <div className="flex items-center justify-between"><span><strong className="block text-sm">DSP-00842</strong><small className="text-[#71869a]">Camilo R. · WHK 284</small></span><Badge className="bg-[#e5f7f3] text-[#116b67] hover:bg-[#e5f7f3]">En ruta</Badge></div>
            <div className="mt-4 h-2 overflow-hidden rounded-full bg-[#e8eef3]"><span className="block h-full w-2/3 rounded-full bg-[#2cb59d]" /></div>
            <div className="mt-5 space-y-2">{[["Supermercado El Prado", "FV-10482 · $428.900", true], ["Tienda La Esquina", "FV-10483 · $216.500", false], ["Distribuciones Sol", "FV-10484 · $704.200", false]].map(([name, detail, done], index) => <div key={String(name)} className="flex items-center gap-3 rounded-xl border border-black/[.05] p-3"><span className={cn("grid h-8 w-8 place-items-center rounded-xl text-xs font-bold", done ? "bg-[#38b69c] text-white" : "bg-[#102d49] text-white")}>{done ? <Check className="h-4 w-4" /> : index + 2}</span><span className="min-w-0 flex-1"><strong className="block truncate text-xs">{name}</strong><small className="text-[#7e90a1]">{detail}</small></span><ChevronRight className="h-4 w-4 text-[#8192a1]" /></div>)}</div>
          </div>
        </div>
        <div className="relative z-10 -ml-0 rounded-[2.3rem] border-[6px] border-[#0a2035] bg-white p-3 shadow-[0_35px_75px_rgba(17,55,94,.28)] sm:-ml-10 sm:mt-24">
          <div className="mx-auto mb-3 h-1.5 w-14 rounded-full bg-[#d5e0e8]" />
          <div className="rounded-2xl bg-[#0c2948] p-4 text-white"><small className="text-[#b8d8fa]">Confirmar entrega</small><strong className="mt-1 block">Supermercado El Prado</strong><span className="text-[10px] text-white/50">FV-10482</span><div className="mt-3 grid grid-cols-3 gap-1 text-center text-[9px]"><span>Factura<strong className="block">$428.900</strong></span><span>Devuelve<strong className="block">$0</strong></span><span>Recibir<strong className="block text-[#8be8e1]">$428.900</strong></span></div></div>
          <span className="mt-3 block text-[10px] font-bold">¿Cómo pagó?</span><div className="mt-2 grid grid-cols-2 gap-2"><span className="rounded-xl border border-[#2768a3] bg-[#e7f1fb] p-2 text-center text-[9px] font-bold text-[#1b598f]"><CircleDollarSign className="mx-auto mb-1 h-4 w-4"/>Efectivo</span><span className="rounded-xl border p-2 text-center text-[9px] text-[#74899b]"><ReceiptText className="mx-auto mb-1 h-4 w-4"/>Consignación</span></div><div className="mt-3 rounded-xl bg-[#2a66a1] py-2.5 text-center text-[10px] font-bold text-white">Guardar entrega</div>
        </div>
      </div>
      <div className="landing-float-slow absolute -bottom-8 left-[24%] z-20 hidden items-center gap-3 rounded-2xl border border-white/75 bg-white/90 p-3 shadow-xl backdrop-blur sm:flex"><span className="grid h-9 w-9 place-items-center rounded-xl bg-[#e3f6f2] text-[#16806f]"><CheckCircle2 className="h-4 w-4" /></span><span><strong className="block text-xs text-[#15334c]">Cierre calculado</strong><small className="text-[#637d92]">Recaudo esperado · $2.772.500</small></span></div>
    </div>
  );
}

function IntelligenceSection() {
  return (
    <section className="overflow-hidden bg-[#efeafe] py-24 sm:py-32">
      <div className="mx-auto max-w-[90rem] px-4 sm:px-6 lg:px-8">
        <div className="landing-reveal overflow-hidden rounded-[2.5rem] bg-[#221442] text-white shadow-[0_35px_100px_rgba(45,25,90,.24)]">
          <div className="grid lg:grid-cols-[.78fr_1.22fr]">
            <div className="p-7 sm:p-12 lg:p-16"><p className="text-xs font-bold uppercase tracking-[.22em] text-[#c5b6ff]">IA conectada al software</p><h2 className="mt-5 text-balance text-4xl font-semibold leading-[1] tracking-[-.055em] sm:text-6xl">Pregunta. Auraly cruza la operación y te responde.</h2><p className="mt-6 text-lg leading-8 text-white/60">Consulta ventas, inventario, cartera o resultados sin saltar entre reportes. La respuesta respeta la información y los permisos disponibles para cada persona.</p><div className="mt-8 flex flex-wrap gap-2">{["¿Qué debo reponer?", "¿Cuánto vendimos?", "¿Quién me debe?", "¿Cómo va cada ruta?"].map((question) => <span key={question} className="rounded-full border border-white/[.12] bg-white/[.06] px-3 py-2 text-xs text-white/70">{question}</span>)}</div><Button asChild className="mt-9 rounded-full bg-[#b8a2ff] px-6 text-[#211141] hover:bg-[#c9b9ff]"><a href="#agentes">Conocer los agentes <ArrowRight className="ml-2 h-4 w-4" /></a></Button></div>
            <div className="relative min-h-[31rem] border-t border-white/10 bg-[radial-gradient(circle_at_70%_20%,rgba(190,164,255,.25),transparent_36%)] p-5 sm:p-10 lg:border-l lg:border-t-0">
              <div className="mx-auto max-w-2xl rounded-[1.7rem] border border-white/[.12] bg-[#120b25]/75 p-4 shadow-2xl backdrop-blur-xl sm:p-6">
                <div className="flex items-center gap-3 border-b border-white/10 pb-4"><span className="grid h-10 w-10 place-items-center rounded-xl bg-[#b8a2ff] text-[#211141]"><Sparkles className="h-5 w-5" /></span><span><strong className="block text-sm">Aly · inteligencia de negocio</strong><small className="text-white/40">Conectada a Auraly</small></span><span className="ml-auto flex items-center gap-1.5 text-[10px] text-[#83e0d4]"><span className="h-1.5 w-1.5 rounded-full bg-[#69d9d0]" /> Disponible</span></div>
                <div className="mt-5 max-w-[86%] rounded-2xl rounded-tl-md bg-white/[.07] p-4 text-sm">¿Cómo cerró la operación de hoy?</div>
                <div className="ml-auto mt-3 max-w-[92%] rounded-2xl rounded-tr-md bg-[#b8a2ff] p-4 text-sm leading-6 text-[#211141]">Vendiste <strong>$18.420.500</strong>. Se entregaron 26 de 31 facturas y quedan <strong>$2.772.500</strong> por recibir en el cierre de la ruta Norte.<div className="mt-4 grid grid-cols-2 gap-2 border-t border-[#211141]/10 pt-4"><Insight icon={ReceiptText} label="Ventas" value="+18,4%"/><Insight icon={Truck} label="Entregas" value="84%"/><Insight icon={Boxes} label="Stock bajo" value="3 ítems"/><Insight icon={CircleDollarSign} label="Cartera" value="$31,7 M"/></div></div>
                <div className="mt-5 flex items-center rounded-xl border border-white/10 bg-white/[.04] px-4 py-3 text-sm text-white/35">Pregúntale a Auraly…<ArrowRight className="ml-auto h-4 w-4 text-[#b8a2ff]" /></div>
              </div>
              <span className="absolute right-8 top-8 hidden h-16 w-16 items-center justify-center rounded-2xl border border-white/10 bg-white/[.05] text-[#b8a2ff] sm:flex"><Fingerprint className="h-7 w-7" /></span>
              <span className="absolute bottom-8 left-8 hidden h-12 w-12 items-center justify-center rounded-2xl border border-white/10 bg-white/[.05] text-[#69d9d0] sm:flex"><ScanLine className="h-5 w-5" /></span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function Insight({ icon: Icon, label, value }: { icon: typeof ReceiptText; label: string; value: string }) {
  return <span className="flex items-center gap-2 rounded-xl bg-[#211141]/[.07] p-2"><Icon className="h-3.5 w-3.5" /><span className="text-[10px]"><small className="block opacity-55">{label}</small><strong>{value}</strong></span></span>;
}
