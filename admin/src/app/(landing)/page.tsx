"use client";

import Link from "next/link";
import Image from "next/image";
import { useState } from "react";
import { toast } from "sonner";
import {
  Bot,
  Check,
  ChevronRight,
  HandCoins,
  LifeBuoy,
  Menu,
  RefreshCcw,
  Sparkles,
  X,
} from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { useTenantCommercialCatalog } from "@/components/tenants/tenant-commercial-plan-step";
import { cn } from "@/lib/utils";
import { ProductHero } from "@/components/landing/product-hero";
import { PlatformShowcase } from "@/components/landing/platform-showcase";

const PLANS = [
  {
    name: "Esencial",
    price: "350.000",
    hint: "Para equipos que quieren vender por WhatsApp sin perder chats.",
    credits: "10k",
    capacity: "20-30 conversaciones diarias",
    highlight: false,
    features: [
      "1 agente de IA", "Hasta 3 usuarios", "5 GB de almacenamiento de base de datos",
      "1 linea de WhatsApp", "Reservas, pagos y catalogo", "Dashboard de consumo", "Alertas de consumo del plan",
    ],
  },
  {
    name: "Crecimiento",
    price: "1.750.000",
    hint: "Para negocios con mas volumen, sedes o varias lineas comerciales.",
    credits: "50k",
    capacity: "120-160 conversaciones diarias",
    highlight: true,
    features: [
      "3 agentes de IA", "Hasta 5 usuarios", "10 GB de almacenamiento de base de datos",
      "Hasta 3 lineas de WhatsApp", "Analytics avanzado", "Integraciones operativas", "Soporte prioritario",
    ],
  },
  {
    name: "Pro",
    price: "5.250.000",
    hint: "Para equipos que necesitan escala, control y acompanamiento.",
    credits: "150k",
    capacity: "350-500 conversaciones diarias",
    highlight: false,
    features: [
      "Agentes de IA ilimitados", "Usuarios ilimitados", "20 GB de almacenamiento de base de datos",
      "Multi-sede", "Reportes avanzados", "Flujos de venta complejos", "Acompanamiento prioritario",
    ],
  },
  {
    name: "Enterprise",
    price: "A medida",
    hint: "Para operaciones con SLA, integraciones o volumen personalizado.",
    credits: "Flexible",
    capacity: "Sin limite fijo",
    highlight: false,
    features: [
      "Creditos por consumo", "Agentes personalizados", "50 GB de almacenamiento de base de datos",
      "Soporte dedicado", "Acuerdos de servicio", "Arquitectura a medida",
    ],
  },
];

const OPERATIONS_PLAN_COPY: Record<string, { tagline: string; hint: string }> = {
  starter: { tagline: "Empieza con control", hint: "Facturación electrónica e inventario para una operación pequeña que todavía no necesita caja, contabilidad ni nómina." },
  essential: { tagline: "Empieza con control", hint: "Para organizar y facturar una operación que empieza a crecer." },
  business: { tagline: "Más capacidad", hint: "La combinación recomendada para equipos con operación diaria." },
  company: { tagline: "Opera a escala", hint: "Para varias áreas, más cajas y una operación exigente." },
  corporate: { tagline: "Capacidad configurable", hint: "Capacidad superior a Empresa, ajustada a una operación de gran escala." },
};

const cop = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

const AGENTS = [
  {
    name: "Agente de Agenda",
    tagline: "Atiende campanas, responde 24/7 y convierte interes en citas confirmadas.",
    image: "/agents/agent-office-assistant.png",
    icon: Bot,
    features: [
      "Contesta campanas y chats entrantes 24/7",
      "Da informacion del negocio, servicios y precios",
      "Revisa disponibilidad y agenda citas",
      "Actualiza datos y entrega contexto al equipo",
    ],
  },
  {
    name: "Agente de Cobros",
    tagline: "Cierra ventas dentro del chat con resumen claro, link de pago y seguimiento.",
    image: "/agents/agent-senior-analyst.png",
    icon: HandCoins,
    features: [
      "Responde dudas de pago y condiciones 24/7",
      "Genera links o instrucciones de pago",
      "Confirma abonos y estados de la reserva",
      "Recuerda pagos pendientes sin friccion",
    ],
  },
  {
    name: "Agente de Soporte",
    tagline: "Resuelve preguntas frecuentes, orienta al cliente y escala cuando hace falta.",
    image: "/agents/agent-receptionist.png",
    icon: LifeBuoy,
    features: [
      "Atiende solicitudes y preguntas frecuentes 24/7",
      "Explica politicas, horarios y ubicaciones",
      "Detecta problemas y propone siguientes pasos",
      "Escala casos sensibles con historial completo",
    ],
  },
  {
    name: "Agente Recuperador",
    tagline: "Reactiva pagos, reservas y conversaciones que quedaron a mitad de camino.",
    image: "/agents/agent-executive.png",
    icon: RefreshCcw,
    features: [
      "Recupera pagos vencidos o abandonados",
      "Reengancha clientes con mensajes oportunos",
      "Responde informacion del negocio 24/7",
      "Prioriza oportunidades con mayor probabilidad",
    ],
  },
];

const DEFAULT_WHATSAPP_CONTACT_NUMBER = "573117324418";
const WHATSAPP_CONTACT_NUMBER =
  process.env.NEXT_PUBLIC_WHATSAPP_CONTACT_NUMBER || DEFAULT_WHATSAPP_CONTACT_NUMBER;
const WHATSAPP_CONTACT_MESSAGE =
  process.env.NEXT_PUBLIC_WHATSAPP_CONTACT_MESSAGE ||
  "Hola, quiero conocer la plataforma y agendar una demo.";

type DemoForm = {
  name: string;
  email: string;
  company: string;
  phone: string;
};

type DemoFormErrors = Partial<Record<keyof DemoForm, string>>;
const FAQ = [
  {
    q: "¿La facturación electrónica está conectada con inventario y contabilidad?",
    a: "Sí. La operación parte del mismo documento: Auraly mantiene la trazabilidad fiscal, actualiza el inventario cuando corresponde y lleva los movimientos hacia los procesos financieros y contables configurados.",
  },
  {
    q: "¿La app de pedidos funciona sin Internet?",
    a: "Sí. El vendedor prepara su teléfono con rutas, clientes, pedidos, precios y existencias. Puede trabajar en modo local y los pendientes se sincronizan cuando vuelve la conexión.",
  },
  {
    q: "¿Qué registra el transportador durante la ruta?",
    a: "Puede confirmar entregas completas o parciales, novedades, devoluciones, efectivo, consignaciones, créditos, gastos y evidencias. Al finalizar, el cierre consolida lo recaudado y cualquier diferencia.",
  },
  {
    q: "¿Qué información pueden consultar los agentes de IA?",
    a: "Los agentes trabajan con la información y las acciones autorizadas para cada caso. Pueden atender conversaciones y consultar datos reales del software sin convertirse en la fuente de verdad de inventario, dinero o permisos.",
  },
  {
    q: "¿Puedo ampliar el plan cuando crezca mi operación?",
    a: "Sí. Puedes ampliar usuarios, cajas, documentos DIAN, empleados de nómina y capacidad de agentes según las necesidades de tu empresa.",
  },
];

export default function LandingPage() {
  const operationsCatalog = useTenantCommercialCatalog();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [form, setForm] = useState<DemoForm>({
    name: "",
    email: "",
    company: "",
    phone: "",
  });
  const [status, setStatus] = useState<"idle" | "loading" | "success" | "error">("idle");
  const [statusMessage, setStatusMessage] = useState("");
  const [errors, setErrors] = useState<DemoFormErrors>({});
  const whatsappContactNumber = WHATSAPP_CONTACT_NUMBER.replace(/\D/g, "");
  const whatsappContactHref = whatsappContactNumber
    ? `https://wa.me/${whatsappContactNumber}?text=${encodeURIComponent(WHATSAPP_CONTACT_MESSAGE)}`
    : null;
  const visibleOperationsPlans = operationsCatalog.data?.plans.slice(0, 3) ?? [];

  const updateForm = (field: keyof DemoForm, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrors((current) => {
      if (!current[field]) return current;
      const next = { ...current };
      delete next[field];
      return next;
    });
  };

  const validateForm = () => {
    const nextErrors: DemoFormErrors = {};
    if (!form.name.trim()) nextErrors.name = "Cuéntanos tu nombre.";
    if (!form.company.trim()) nextErrors.company = "Cuéntanos de qué empresa nos escribes.";
    if (!form.phone.trim()) nextErrors.phone = "Déjanos tu número de WhatsApp.";
    if (!form.email.trim()) {
      nextErrors.email = "Déjanos tu correo para enviarte la invitación de la demo.";
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) {
      nextErrors.email = "Escribe un correo válido.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!validateForm()) {
      setStatus("error");
      setStatusMessage("Revisa los campos marcados para poder solicitar la demo.");
      return;
    }

    setStatus("loading");
    setStatusMessage("");

    try {
      const response = await fetch("/api/demo-requests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form),
      });

      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.title || payload?.message || "No se pudo enviar la solicitud.");
      }

      setForm({ name: "", email: "", company: "", phone: "" });
      setErrors({});
      setStatus("success");
      setStatusMessage("");
      toast.success("Solicitud enviada", {
        description: "Aly, nuestro agente de ventas, te contactará por WhatsApp para iniciar la demo.",
      });
    } catch (error) {
      setStatus("error");
      const message = error instanceof Error ? error.message : "No se pudo enviar la solicitud.";
      setStatusMessage(message);
      toast.error("No se pudo enviar la solicitud", { description: message });
    }
  };

  return (
    <main className="min-h-screen bg-[#f3f7f5] text-[#151515]">
      <header className="sticky top-0 z-50 border-b border-white/10 bg-[#051518]/90 text-white shadow-[0_8px_30px_rgba(3,20,23,.18)] backdrop-blur-xl">
        <nav className="mx-auto flex h-[4.5rem] max-w-[90rem] items-center justify-between px-4 sm:px-6 lg:px-8">
          <Link href="/" className="flex items-center gap-2 text-xl font-semibold">
            <AuralyLogo className="[&>span]:text-white" priority />
          </Link>
          <div className="hidden items-center gap-6 lg:flex xl:gap-8">
            <a href="#plataforma" className="text-sm font-medium text-white/60 transition hover:text-white">Plataforma</a>
            <a href="#facturacion" className="text-sm font-medium text-white/60 transition hover:text-white">Facturación electrónica</a>
            <a href="#pedidos" className="text-sm font-medium text-white/60 transition hover:text-white">Pedidos</a>
            <a href="#transporte" className="text-sm font-medium text-white/60 transition hover:text-white">Transportador</a>
            <a href="#agentes" className="text-sm font-medium text-white/60 transition hover:text-white">Agentes</a>
            <a href="#planes" className="text-sm font-medium text-white/60 transition hover:text-white">Planes</a>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" asChild className="rounded-full text-white hover:bg-white/10 hover:text-white"><Link href="/login">Entrar</Link></Button>
            <Button asChild className="hidden rounded-full bg-[#69d9d0] px-5 text-[#041417] shadow-[0_10px_28px_rgba(105,217,208,.16)] hover:bg-[#8be8e1] sm:inline-flex"><a href="#demo">Solicitar demo</a></Button>
            <Button variant="ghost" size="icon" className="rounded-full text-white hover:bg-white/10 hover:text-white lg:hidden" onClick={() => setMobileMenuOpen((v) => !v)} aria-label={mobileMenuOpen ? "Cerrar menú" : "Abrir menú"}>
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </nav>
        {mobileMenuOpen && (
          <div className="border-t border-white/10 bg-[#051518] px-4 py-5 lg:hidden">
            <div className="mx-auto flex max-w-[90rem] flex-col gap-4 text-sm text-white/75">
              <a href="#plataforma" onClick={() => setMobileMenuOpen(false)}>Plataforma</a>
              <a href="#facturacion" onClick={() => setMobileMenuOpen(false)}>Facturación electrónica</a>
              <a href="#pedidos" onClick={() => setMobileMenuOpen(false)}>Pedidos</a>
              <a href="#transporte" onClick={() => setMobileMenuOpen(false)}>Transportador</a>
              <a href="#agentes" onClick={() => setMobileMenuOpen(false)}>Agentes</a>
              <a href="#planes" onClick={() => setMobileMenuOpen(false)}>Planes</a>
              <Button asChild className="mt-2 rounded-full bg-[#69d9d0] text-[#041417] hover:bg-[#8be8e1] sm:hidden">
                <a href="#demo" onClick={() => setMobileMenuOpen(false)}>Solicitar demo</a>
              </Button>
            </div>
          </div>
        )}
      </header>

      <ProductHero />

      <PlatformShowcase />

      <section id="agentes" className="bg-[#06090B] py-20 text-white">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div className="max-w-3xl">
              <Badge className="mb-4 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Agentes listos para operar</Badge>
              <h2 className="text-3xl font-semibold sm:text-5xl">Estos son los agentes que puedes tener trabajando para tu negocio.</h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-white/65">
              Todos responden 24/7, usan la informacion de tu negocio y dejan trazabilidad para que el equipo tome control cuando lo necesite.
            </p>
          </div>

          <div className="mt-10 grid auto-rows-fr gap-4 md:grid-cols-2 xl:grid-cols-4">
            {AGENTS.map((agent) => {
              const Icon = agent.icon;
              return (
                <article key={agent.name} className="flex h-full flex-col overflow-hidden rounded-lg border border-white/10 bg-[#0B0E10] shadow-2xl shadow-cyan-950/20">
                  <div className="relative h-80 overflow-hidden border-b border-white/10 bg-black">
                    <Image
                      src={agent.image}
                      alt={agent.name}
                      fill
                      sizes="(min-width: 1280px) 25vw, (min-width: 768px) 50vw, 100vw"
                      className="object-cover object-top"
                    />
                    <div className="absolute inset-x-0 bottom-0 h-28 bg-gradient-to-t from-[#0B0E10] to-transparent" />
                  </div>
                  <div className="flex flex-1 flex-col p-5">
                    <div className="flex items-start gap-3">
                      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-[#69D9D0]/35 bg-[#69D9D0]/10 text-[#69D9D0]">
                        <Icon className="h-5 w-5" />
                      </span>
                      <div>
                        <h3 className="text-lg font-semibold text-[#69D9D0]">{agent.name}</h3>
                        <p className="mt-1 text-sm leading-6 text-white/65">{agent.tagline}</p>
                      </div>
                    </div>
                    <ul className="mt-5 space-y-3 text-sm text-white/85">
                      {agent.features.map((feature) => (
                        <li key={feature} className="flex gap-2">
                          <Check className="mt-0.5 h-4 w-4 shrink-0 text-[#69D9D0]" />
                          <span>{feature}</span>
                        </li>
                      ))}
                    </ul>
                    <Button className="mt-6 w-full border border-[#69D9D0]/30 bg-white text-[#07161A] hover:bg-[#E6FFFD]" asChild>
                      <a href="#demo">Quiero este agente</a>
                    </Button>
                  </div>
                </article>
              );
            })}
          </div>
          <div id="planes-agentes" className="mt-8 rounded-[2rem] border border-[#69D9D0]/20 bg-[#0f2c33]/70 p-5 sm:p-7">
            <Accordion type="single" collapsible>
              <AccordionItem value="precios-agentes" className="border-0">
                <AccordionTrigger className="rounded-2xl px-1 py-2 text-left text-white hover:no-underline">
                  <span className="flex items-center gap-4">
                    <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-[#69D9D0] text-[#07161A]"><Bot className="h-5 w-5" /></span>
                    <span><strong className="block text-base">Ver planes y precios de agentes</strong><small className="mt-1 block font-normal text-white/55">Oferta independiente de los planes de facturación y POS.</small></span>
                  </span>
                </AccordionTrigger>
                <AccordionContent className="pt-6">
                  <div className="mb-5 rounded-2xl border border-[#69D9D0]/20 bg-[#69D9D0]/10 p-4 text-sm leading-6 text-[#d7fffb]">
                    Estos valores corresponden únicamente a agentes de IA, créditos de conversación, almacenamiento y líneas de WhatsApp. No son los precios de facturación, POS, inventario, contabilidad o nómina.
                  </div>
                  <div className="grid auto-rows-fr gap-4 min-[560px]:grid-cols-2 xl:grid-cols-4">
                    {PLANS.map((plan) => <Card key={plan.name} className={cn("flex h-full flex-col rounded-2xl border-white/10 bg-white/[.06] text-white", plan.highlight && "border-[#69D9D0] bg-[#123a42]")}>
                      <CardHeader><p className="text-[10px] font-bold uppercase tracking-[.18em] text-[#69D9D0]">Plan de agentes de IA</p><div className="flex items-center justify-between gap-2"><CardTitle>IA {plan.name}</CardTitle>{plan.highlight && <Badge className="bg-[#69D9D0] text-[#07161A]">Recomendado</Badge>}</div><p className="pt-3 text-3xl font-semibold">{plan.price === "A medida" ? plan.price : `$${plan.price}`}</p>{plan.price !== "A medida" && <p className="text-xs text-white/45">COP / mes · solo agentes</p>}</CardHeader>
                      <CardContent className="flex flex-1 flex-col gap-5"><p className="min-h-12 text-sm leading-6 text-white/60">{plan.hint}</p><Separator className="bg-white/10"/><div className="text-sm"><strong className="text-[#69D9D0]">{plan.credits}</strong> créditos mensuales<p className="mt-1 text-white/55">{plan.capacity}</p></div><ul className="space-y-3 text-sm">{plan.features.map(feature => <li key={feature} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[#69D9D0]"/>{feature}</li>)}</ul><Button asChild className="mt-auto w-full bg-[#69D9D0] text-[#07161A] hover:bg-[#7CE3DB]"><a href="#demo">Cotizar agentes <ChevronRight className="ml-1 h-4 w-4"/></a></Button></CardContent>
                    </Card>)}
                  </div>
                </AccordionContent>
              </AccordionItem>
            </Accordion>
          </div>
        </div>
      </section>

      <section id="planes" className="bg-[#f7f8f2] py-20">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <Badge className="mb-4 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Planes AURALY</Badge>
              <h2 className="text-3xl font-semibold sm:text-5xl">Planes que crecen contigo.</h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-black/65">Tres planes claros para empezar con facturación electrónica y sumar operación a medida que tu empresa crece.</p>
          </div>
          {operationsCatalog.isLoading && <div className="mt-10 rounded-2xl border border-black/10 bg-white p-8 text-center text-black/60">Cargando planes vigentes…</div>}
          {operationsCatalog.isError && <div role="alert" className="mt-10 grid gap-5 overflow-hidden rounded-[2rem] bg-[#0b292d] p-6 text-white sm:grid-cols-[1fr_auto] sm:items-center sm:p-8"><div><p className="text-xs font-bold uppercase tracking-[.18em] text-[#69D9D0]">Capacidad a tu medida</p><h3 className="mt-2 text-2xl font-semibold">Encuentra el plan correcto con nuestro equipo.</h3><p className="mt-2 max-w-2xl text-sm leading-6 text-white/60">Te ayudamos a calcular usuarios, cajas, documentos DIAN y empleados de nómina según tu operación real.</p></div><Button asChild className="rounded-full bg-[#69D9D0] px-6 text-[#07161A] hover:bg-[#8be8e1]"><a href="#demo">Consultar planes</a></Button></div>}
          <div className="mt-10 grid auto-rows-fr items-stretch gap-5 md:grid-cols-3">
            {visibleOperationsPlans.map((plan) => {
              const copy = OPERATIONS_PLAN_COPY[plan.code] ?? { tagline: "Plan Auraly", hint: "Capacidad configurable para tu operación." };
              const capacity = plan.isCustom ? ["Capacidad superior a Empresa"] : [
                `${plan.includedFullUsers} ${plan.includedFullUsers === 1 ? "usuario completo" : "usuarios completos"}`,
                ...(plan.includedPosDevices > 0 ? [`${plan.includedPosDevices} ${plan.includedPosDevices === 1 ? "caja" : "cajas"}`] : []),
                `${plan.includedDianDocuments.toLocaleString("es-CO")} documentos DIAN / mes`,
                ...(plan.includedPayrollEmployees > 0 ? [`${plan.includedPayrollEmployees} empleados de nómina`] : []),
              ];
              const visibleFeatures = [...new Set([
                ...capacity,
                ...plan.features.filter(feature => !feature.toLocaleLowerCase("es-CO").includes("documentos dian")),
              ])];
              return <Card key={plan.planId} data-plan-code={plan.code} className={cn("flex h-full flex-col rounded-lg border-black/10 bg-white text-[#151515]", plan.isRecommended && "border-[#69D9D0] bg-[#E6FFFD]")}>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>{plan.name}</CardTitle>
                    {plan.isRecommended && <Badge className="bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Recomendado</Badge>}
                  </div>
                  <p className="mt-3 text-xs font-bold uppercase tracking-[.16em] text-[#1A5860]">{copy.tagline}</p>
                  <div className="min-h-[84px] pt-4">
                    <p className="text-4xl font-semibold">{plan.isCustom ? "A medida" : cop.format(plan.monthlyPriceCop)}</p>
                    {!plan.isCustom && <p className="text-sm text-black/55">COP / mes antes de IVA</p>}
                  </div>
                </CardHeader>
                <CardContent className="flex flex-1 flex-col gap-5">
                  <p className="min-h-12 text-sm leading-6 text-black/65">{copy.hint}</p>
                  {plan.code === "starter" && <p className="rounded-xl bg-[#f2f5f3] px-3 py-2 text-xs font-medium text-[#496064]">No incluye caja, contabilidad ni nómina.</p>}
                  <Separator className="bg-black/10" />
                  <ul className="space-y-3 text-sm">
                    {visibleFeatures.map((feature) => (
                      <li key={feature} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[#1A5860]" />{feature}</li>
                    ))}
                  </ul>
                  <Button className={cn("mt-auto w-full", plan.isRecommended ? "bg-[#69D9D0] text-[#07161A] hover:bg-[#7CE3DB]" : "bg-[#151515] text-white hover:bg-black")} asChild>
                    <Link href="/register">Crear empresa <ChevronRight className="ml-1 h-4 w-4" /></Link>
                  </Button>
                </CardContent>
              </Card>;
            })}
          </div>
        </div>
      </section>

      <section id="faq" className="bg-[#0a2529] py-24 text-white sm:py-32">
        <div className="mx-auto grid max-w-[90rem] gap-12 px-4 sm:px-6 lg:grid-cols-[0.8fr_1.2fr] lg:px-8">
        <div>
          <Badge className="mb-5 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Preguntas frecuentes</Badge>
          <h2 className="text-balance text-4xl font-semibold leading-[1] tracking-[-.05em] sm:text-6xl">Lo que necesitas saber antes de verlo en vivo.</h2>
        </div>
        <Accordion type="single" collapsible className="w-full">
          {FAQ.map((item) => (
            <AccordionItem key={item.q} value={item.q} className="border-white/20">
              <AccordionTrigger className="py-6 text-left text-base text-white hover:text-[#69D9D0] sm:text-lg">{item.q}</AccordionTrigger>
              <AccordionContent className="max-w-2xl pb-6 text-base leading-7 text-white/60">{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
        </div>
      </section>

      <section id="demo" className="scroll-mt-20 overflow-hidden border-t border-black/10 bg-[#69d9d0] py-24 sm:py-32">
        <div className="mx-auto grid max-w-[90rem] gap-10 px-4 sm:px-6 lg:grid-cols-[1fr_0.78fr] lg:items-center lg:px-8">
          <div>
            <span className="mb-6 grid h-12 w-12 place-items-center rounded-2xl bg-[#062126] text-[#69d9d0]"><Sparkles className="h-6 w-6" /></span>
            <p className="text-xs font-bold uppercase tracking-[.22em] text-[#0e6564]">Una demo. Tu operación.</p>
            <h2 className="mt-4 max-w-3xl text-balance text-4xl font-semibold leading-[.98] tracking-[-.055em] text-[#041719] sm:text-6xl">Mira cómo se vería todo tu negocio conectado.</h2>
            <p className="mt-6 max-w-2xl text-lg leading-8 text-[#164d50]">Déjanos tus datos. Te escribimos por WhatsApp para entender tu operación y mostrarte Auraly con los procesos que realmente te importan.</p>
          </div>
          <form onSubmit={submit} noValidate className="grid gap-4 rounded-[2rem] border border-white/45 bg-white/90 p-5 shadow-[0_30px_80px_rgba(4,33,38,.18)] backdrop-blur sm:p-7">
            <div><p className="text-lg font-semibold text-[#0b292d]">Agenda tu demo</p><p className="mt-1 text-sm text-[#61777a]">Cuatro datos y empezamos la conversación.</p></div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1">
                <label htmlFor="demo-name" className="text-xs font-semibold text-[#35575a]">Nombre</label>
                <Input id="demo-name" value={form.name} onChange={(event) => updateForm("name", event.target.value)} placeholder="Tu nombre" required aria-required="true" aria-invalid={Boolean(errors.name)} className="h-11 bg-white text-[#151515] placeholder:text-black/35" />
                {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
              </div>
              <div className="space-y-1">
                <label htmlFor="demo-email" className="text-xs font-semibold text-[#35575a]">Correo</label>
                <Input id="demo-email" type="email" value={form.email} onChange={(event) => updateForm("email", event.target.value)} placeholder="nombre@empresa.com" required aria-required="true" aria-invalid={Boolean(errors.email)} className="h-11 bg-white text-[#151515] placeholder:text-black/35" />
                {errors.email && <p className="text-xs text-destructive">{errors.email}</p>}
              </div>
              <div className="space-y-1">
                <label htmlFor="demo-company" className="text-xs font-semibold text-[#35575a]">Empresa</label>
                <Input id="demo-company" value={form.company} onChange={(event) => updateForm("company", event.target.value)} placeholder="Nombre de tu empresa" required aria-required="true" aria-invalid={Boolean(errors.company)} className="h-11 bg-white text-[#151515] placeholder:text-black/35" />
                {errors.company && <p className="text-xs text-destructive">{errors.company}</p>}
              </div>
              <div className="space-y-1">
                <label htmlFor="demo-phone" className="text-xs font-semibold text-[#35575a]">WhatsApp</label>
                <Input id="demo-phone" value={form.phone} onChange={(event) => updateForm("phone", event.target.value)} placeholder="Número de contacto" required aria-required="true" aria-invalid={Boolean(errors.phone)} className="h-11 bg-white text-[#151515] placeholder:text-black/35" />
                {errors.phone && <p className="text-xs text-destructive">{errors.phone}</p>}
              </div>
            </div>
            {statusMessage && (
              <p className={cn("text-sm", status === "success" ? "text-[#1A5860]" : "text-destructive")}>
                {statusMessage}
              </p>
            )}
            <Button type="submit" disabled={status === "loading"} className="h-12 rounded-full bg-[#061c1f] text-white hover:bg-[#0d3438]">
              {status === "loading" ? "Enviando..." : "Solicitar demo"}
            </Button>
          </form>
        </div>
      </section>

      <footer className="border-t border-white/10 bg-[#051518] py-10 text-white">
        <div className="mx-auto flex max-w-[90rem] flex-col gap-5 px-4 text-sm text-white/55 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
          <AuralyLogo className="[&>span]:text-white" />
          <p>Tu operación conectada de punta a punta.</p>
          <div className="flex flex-wrap gap-4">
            <a href="#plataforma">Plataforma</a>
            <a href="#facturacion">Facturación</a>
            <a href="#pedidos">Pedidos</a>
            <a href="#transporte">Transportador</a>
            <a href="#agentes">Agentes</a>
            <a href="#planes">Planes</a>
          </div>
        </div>
      </footer>

      {whatsappContactHref && (
        <a
          href={whatsappContactHref}
          target="_blank"
          rel="noreferrer"
          aria-label="Abrir WhatsApp con Aly"
          className="group fixed bottom-5 right-5 z-50 flex h-14 items-center justify-center gap-2 rounded-full bg-[#25D366] px-4 text-white shadow-2xl shadow-black/25 transition hover:-translate-y-1 hover:bg-[#1EBE57] hover:shadow-[0_18px_45px_rgba(37,211,102,.35)] focus:outline-none focus:ring-2 focus:ring-[#25D366] focus:ring-offset-2"
        >
          <WhatsAppIcon className="h-6 w-6" />
          <span className="hidden pr-1 text-sm font-bold sm:inline">Hablemos por WhatsApp</span>
        </a>
      )}
    </main>
  );
}

function WhatsAppIcon({ className }: { className?: string }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" className={className}>
      <path d="M20.5 11.7a8.5 8.5 0 0 1-12.7 7.4L3 20.5l1.5-4.6A8.5 8.5 0 1 1 20.5 11.7Z" fill="currentColor" />
      <path d="M8.25 7.7c.2-.45.4-.46.68-.47h.58c.18 0 .38.05.48.34.12.34.48 1.18.52 1.27.05.09.08.2.02.32-.06.13-.1.2-.2.3-.09.12-.2.25-.28.33-.1.1-.2.21-.08.42.11.21.5.82 1.08 1.33.74.66 1.36.86 1.57.96.21.1.33.08.45-.05.13-.15.55-.64.7-.86.14-.21.28-.18.47-.1.2.07 1.23.58 1.44.69.21.1.35.15.4.24.05.08.05.5-.12.98-.17.48-1  .92-1.38.98-.36.06-.83.09-1.34-.08-.31-.1-.72-.23-1.24-.45-.22-.09-.96-.36-1.65-.96-.58-.5-1.83-1.71-2.13-2.95-.3-1.24.02-1.85.13-2.08Z" fill="#25D366" />
    </svg>
  );
}
