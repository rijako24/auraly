"use client";

import Link from "next/link";
import Image from "next/image";
import { useState } from "react";
import { toast } from "sonner";
import {
  ArrowRight,
  BarChart3,
  Bot,
  CalendarCheck,
  Check,
  ChevronRight,
  Clock3,
  CreditCard,
  Calculator,
  FileCheck2,
  Landmark,
  MonitorSmartphone,
  ReceiptText,
  Gauge,
  HandCoins,
  LifeBuoy,
  Menu,
  MessageCircle,
  RefreshCcw,
  ShieldCheck,
  Sparkles,
  X,
} from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Progress } from "@/components/ui/progress";
import { Separator } from "@/components/ui/separator";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { ThemeToggle } from "@/components/layout/theme-toggle";
import { useTenantCommercialCatalog } from "@/components/tenants/tenant-commercial-plan-step";
import { cn } from "@/lib/utils";

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
  starter: { tagline: "Todo lo esencial", hint: "Una persona, una caja y facturación electrónica para empezar con orden." },
  essential: { tagline: "Empieza con control", hint: "Para organizar y facturar una operación que empieza a crecer." },
  business: { tagline: "Más capacidad", hint: "La combinación recomendada para equipos con operación diaria." },
  company: { tagline: "Opera a escala", hint: "Para varias áreas, más cajas y una operación exigente." },
  corporate: { tagline: "Capacidad configurable", hint: "Capacidad superior a Empresa, ajustada a una operación de gran escala." },
};

const cop = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

const PRODUCT_SCENES = [
  { icon: MonitorSmartphone, number: "01", kicker: "Punto de venta", title: "Vende rápido. Auraly mantiene el control.", text: "Caja online u offline, inventario, pedidos, devoluciones y cierres conectados con una sola operación.", metric: "Una venta, un inventario", accent: "from-[#69D9D0] to-[#1A5860]" },
  { icon: ReceiptText, number: "02", kicker: "Facturación electrónica", title: "De la caja a la DIAN, sin saltos manuales.", text: "Resoluciones por equipo, numeración segura, validación de vigencia y trazabilidad fiscal en cada documento.", metric: "Numeración protegida", accent: "from-cyan-300 to-blue-700" },
  { icon: Calculator, number: "03", kicker: "Contabilidad", title: "Cada movimiento explica sus números.", text: "Ventas, compras, inventario, cartera y gastos alimentan la contabilidad con reglas auditables.", metric: "Contabilidad conectada", accent: "from-amber-300 to-orange-600" },
  { icon: Landmark, number: "04", kicker: "Nómina", title: "Personas, novedades y pago en el mismo contexto.", text: "Administra empleados, periodos y documentos de nómina sin perder el aislamiento de tu empresa.", metric: "Equipo al día", accent: "from-violet-300 to-fuchsia-700" },
  { icon: Bot, number: "05", kicker: "Agentes de IA", title: "Tu operación también conversa y vende 24/7.", text: "Agentes configurables atienden, cotizan, agendan, cobran y escalan con datos reales del negocio.", metric: "IA que sí opera", accent: "from-emerald-300 to-teal-700" },
] as const;

const OUTCOMES = [
  { icon: Clock3, title: "Atiende 24/7", text: "Responde al instante fuera de horario, baja tiempos de espera y mantiene conversaciones activas cuando tu equipo no esta conectado." },
  { icon: CalendarCheck, title: "Agenda demos y citas", text: "Consulta disponibilidad, captura datos clave y lleva al cliente al siguiente paso sin romper el flujo de WhatsApp." },
  { icon: CreditCard, title: "Maximiza ventas", text: "Califica leads, recomienda servicios, recupera conversaciones y activa pagos o seguimientos para mejorar conversion." },
  { icon: BarChart3, title: "Aprende del negocio", text: "Usa catalogo, politicas, preguntas frecuentes, tono de marca e integraciones para responder con contexto real." },
];

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

const DIFFERENTIATORS = [
  ["Problema claro", "AURALY resuelve chats perdidos, respuestas lentas, leads sin seguimiento, agendas manuales y equipos saturados por tareas repetitivas."],
  ["Configurable de punta a punta", "Aly adapta tono, servicios, objeciones, horarios, reglas de agenda, datos a capturar, plantillas y escalamiento humano."],
  ["Operacion medible", "Cada conversacion deja historial, estado, consumo y contexto para optimizar ventas, soporte y recuperacion."],
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
    q: "Como funcionan los creditos?",
    a: "Los creditos representan uso operativo del agente. Una respuesta simple suele consumir 1 credito; acciones avanzadas como agenda, cotizacion, pagos, audio o documentos pueden consumir mas.",
  },
  {
    q: "Que pasa cuando se llega al limite?",
    a: "El tablero muestra alertas de consumo para que puedas renovar, ampliar creditos o ajustar la operacion antes de afectar tus conversaciones.",
  },
  {
    q: "WhatsApp marketing esta incluido?",
    a: "No. La atencion entrante esta contemplada en el uso del plan; campanas, reactivaciones y plantillas de marketing se cobran aparte para proteger tu margen y evitar sorpresas.",
  },
  {
    q: "Puedo cambiar de plan?",
    a: "Si. El plan activo conserva sus limites del periodo actual y los cambios aplican de forma controlada en el siguiente ciclo o al activar una ampliacion.",
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
    <main className="min-h-screen bg-[#f7f8f2] text-[#151515]">
      <header className="sticky top-0 z-50 border-b border-black/10 bg-[#f7f8f2]/90 backdrop-blur">
        <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2 text-xl font-semibold">
            <AuralyLogo className="[&>span]:text-[#151515]" />
          </Link>
          <div className="hidden items-center gap-8 md:flex">
            <a href="#servicios" className="text-sm font-medium text-black/65 hover:text-black">Servicios</a>
            <a href="#facturacion" className="text-sm font-medium text-black/65 hover:text-black">Facturación electrónica</a>
            <a href="#agentes" className="text-sm font-medium text-black/65 hover:text-black">Agentes</a>
            <a href="#planes" className="text-sm font-medium text-black/65 hover:text-black">Planes</a>
            <a href="#faq" className="text-sm font-medium text-black/65 hover:text-black">FAQ</a>
          </div>
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <Button variant="ghost" asChild><Link href="/login">Entrar</Link></Button>
            <Button asChild className="hidden bg-[#151515] text-white hover:bg-black sm:inline-flex"><a href="#demo">Solicitar demo</a></Button>
            <Button variant="ghost" size="icon" className="md:hidden" onClick={() => setMobileMenuOpen((v) => !v)} aria-label={mobileMenuOpen ? "Cerrar menú" : "Abrir menú"}>
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </nav>
        {mobileMenuOpen && (
          <div className="border-t border-black/10 px-4 py-4 md:hidden">
            <div className="mx-auto flex max-w-7xl flex-col gap-3 text-sm">
              <a href="#servicios" onClick={() => setMobileMenuOpen(false)}>Servicios</a>
              <a href="#facturacion" onClick={() => setMobileMenuOpen(false)}>Facturación electrónica</a>
              <a href="#agentes" onClick={() => setMobileMenuOpen(false)}>Agentes</a>
              <a href="#planes" onClick={() => setMobileMenuOpen(false)}>Planes</a>
              <a href="#faq" onClick={() => setMobileMenuOpen(false)}>FAQ</a>
              <Button asChild className="mt-2 bg-[#151515] text-white hover:bg-black sm:hidden">
                <a href="#demo" onClick={() => setMobileMenuOpen(false)}>Solicitar demo</a>
              </Button>
            </div>
          </div>
        )}
      </header>

      <section className="bg-[#f7f8f2]">
        <div className="mx-auto grid min-h-[calc(100vh-4rem)] max-w-7xl items-center gap-10 px-4 py-10 lg:grid-cols-[1.05fr_0.95fr]">
        <div className="max-w-3xl">
          <Badge className="mb-5 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Tu empresa, conectada de punta a punta</Badge>
          <h1 className="text-5xl font-semibold leading-[1.02] tracking-normal sm:text-6xl lg:text-7xl">
            Factura, opera y crece con una inteligencia que entiende tu negocio.
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-black/70">
            POS, facturación electrónica, contabilidad, nómina y agentes de IA trabajan sobre la misma verdad. Menos tareas separadas; más control para decidir y vender.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Button size="lg" asChild className="bg-[#151515] text-white hover:bg-black">
              <a href="#demo">Solicitar demo <ArrowRight className="ml-2 h-4 w-4" /></a>
            </Button>
            <Button size="lg" variant="outline" asChild className="border-black/20 bg-white text-[#151515] hover:bg-white/90">
              <a href="#planes">Ver planes</a>
            </Button>
          </div>
          <div className="mt-8 grid max-w-2xl gap-3 sm:grid-cols-3">
            {["Facturación DIAN", "Operación y finanzas", "Agentes de IA 24/7"].map((item) => (
              <div key={item} className="flex items-center gap-2 text-sm text-black/70"><Check className="h-4 w-4 text-[#1A5860]" />{item}</div>
            ))}
          </div>
        </div>

        <div className="relative">
          <div className="absolute -left-6 top-8 hidden h-24 w-24 rounded-full bg-[#69D9D0] md:block" />
          <div className="relative overflow-hidden rounded-lg border border-black/10 bg-[#151515] p-5 text-white shadow-2xl">
            <div className="flex items-center justify-between border-b border-white/10 pb-4">
              <div>
                <p className="text-sm text-white/55">Pipeline de hoy</p>
                <p className="text-2xl font-semibold">47 conversaciones activas</p>
              </div>
              <Badge className="bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">+38% cierres</Badge>
            </div>
            <div className="mt-5 space-y-3">
              {[
                ["Cliente", "Hola, tienen agenda para manana?"],
                ["AURALY", "Si. Tengo 10:30 a.m. y 3:00 p.m. Tambien puedo enviarte el link de pago para separar."],
                ["Cliente", "La de 3 esta bien"],
                ["AURALY", "Perfecto. Te envio el resumen y el pago seguro para confirmar tu cupo."],
              ].map(([sender, text], index) => (
                <div key={`${sender}-${index}`} className={cn("max-w-[86%] rounded-lg px-4 py-3 text-sm", sender === "AURALY" ? "ml-auto bg-[#69D9D0] text-[#07161A]" : "bg-white/10 text-white")}>
                  <p className="mb-1 text-xs opacity-65">{sender}</p>
                  <p>{text}</p>
                </div>
              ))}
            </div>
            <div className="mt-5 grid gap-3 rounded-lg bg-white p-4 text-black sm:grid-cols-3">
              <div><p className="text-xs text-black/55">Credito usado</p><p className="font-semibold">8.420 / 10k</p></div>
              <div><p className="text-xs text-black/55">Uso</p><p className="font-semibold">84%</p></div>
              <div><p className="text-xs text-black/55">Estado</p><p className="font-semibold text-[#1A5860]">Activo</p></div>
              <div className="sm:col-span-3"><Progress value={84} /></div>
            </div>
          </div>
        </div>
        </div>
      </section>

      <section id="servicios" className="relative overflow-clip bg-[#07161A] py-24 text-white">
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_12%_18%,rgba(105,217,208,.2),transparent_32%),radial-gradient(circle_at_88%_72%,rgba(42,122,130,.25),transparent_34%)]" />
        <div className="relative mx-auto max-w-7xl px-4">
          <div className="max-w-3xl">
            <p className="text-xs font-bold uppercase tracking-[.26em] text-[#69D9D0]">Una plataforma. Cinco escenas.</p>
            <h2 className="mt-4 text-4xl font-semibold tracking-[-.04em] sm:text-6xl">Desplázate por una operación que por fin habla el mismo idioma.</h2>
          </div>
          <div className="mt-16 space-y-7">
            {PRODUCT_SCENES.map((scene, index) => {
              const Icon = scene.icon;
              return <article id={index === 1 ? "facturacion" : undefined} key={scene.number} className="landing-scene group sticky overflow-hidden rounded-[2rem] border border-white/10 bg-[#0F2C33]/90 p-6 shadow-[0_35px_100px_rgba(0,0,0,.32)] backdrop-blur-xl sm:p-10" style={{ top: `${84 + index * 14}px` }}>
                <div className="grid min-h-[58vh] items-center gap-10 lg:grid-cols-[.85fr_1.15fr]">
                  <div>
                    <div className="flex items-center gap-3 text-sm text-[#A6F1EA]"><span className="font-mono">{scene.number}</span><span className="h-px w-12 bg-[#69D9D0]/50"/><span className="font-semibold uppercase tracking-[.18em]">{scene.kicker}</span></div>
                    <h3 className="mt-7 text-4xl font-semibold leading-tight tracking-[-.035em] sm:text-5xl">{scene.title}</h3>
                    <p className="mt-5 max-w-xl text-lg leading-8 text-white/65">{scene.text}</p>
                    <div className="mt-9 inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-2 text-sm"><FileCheck2 className="h-4 w-4 text-[#69D9D0]"/>{scene.metric}</div>
                  </div>
                  <div className="relative mx-auto aspect-square w-full max-w-lg">
                    <div className={cn("landing-orbit absolute inset-[8%] rounded-full bg-gradient-to-br opacity-25 blur-2xl", scene.accent)} />
                    <div className="absolute inset-[14%] rounded-full border border-white/10" />
                    <div className="landing-float absolute inset-[25%] grid place-items-center rounded-[2.5rem] border border-white/15 bg-white/[.08] shadow-2xl backdrop-blur-xl">
                      <Icon className="h-24 w-24 text-[#A6F1EA] sm:h-32 sm:w-32" strokeWidth={1.15}/>
                    </div>
                    {["top-4 left-1/2", "bottom-8 right-3", "bottom-12 left-2"].map((position, dot) => <span key={position} className={cn("absolute h-3 w-3 rounded-full bg-[#69D9D0] shadow-[0_0_25px_#69D9D0]", position)} style={{ animationDelay: `${dot * 350}ms` }}/>) }
                  </div>
                </div>
              </article>;
            })}
          </div>
        </div>
      </section>

      <section id="producto" className="border-y border-black/10 bg-white py-20">
        <div className="mx-auto max-w-7xl px-4">
          <div className="max-w-2xl">
            <Badge variant="outline" className="mb-4 border-[#1A5860]/30 bg-white text-[#1A5860] hover:bg-white">Empleados digitales configurables</Badge>
            <h2 className="text-3xl font-semibold sm:text-5xl">No es un chatbot generico. Es un empleado digital entrenado para operar ventas, agenda, soporte y seguimiento.</h2>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            {OUTCOMES.map((item) => {
              const Icon = item.icon;
              return (
                <Card key={item.title} className="rounded-lg border-black/10 bg-white text-[#151515]">
                  <CardHeader>
                    <Icon className="h-6 w-6 text-[#1A5860]" />
                    <CardTitle className="text-lg">{item.title}</CardTitle>
                  </CardHeader>
                  <CardContent className="text-sm leading-6 text-black/65">{item.text}</CardContent>
                </Card>
              );
            })}
          </div>
        </div>
      </section>

      <section className="bg-[#f7f8f2] py-20">
        <div className="mx-auto grid max-w-7xl gap-10 px-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div>
            <Badge className="mb-4 bg-[#151515] text-white">Control de margen</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Automatizacion con numeros claros, no una caja negra.</h2>
          <p className="mt-5 text-lg leading-8 text-black/70">
            El cliente ve una medicion simple. Tu administras costo real por IA, WhatsApp, herramientas e integraciones. Si el negocio llega al limite, el agente se pausa antes de gastar mas.
          </p>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          {[
            [Sparkles, "IA", "Tokens de entrada y salida medidos por turno."],
            [MessageCircle, "Canales", "WhatsApp entrante, secuencias y plantillas separadas."],
            [Gauge, "Uso", "Creditos y porcentaje visibles para el cliente."],
            [ShieldCheck, "Margen", "Costo variable maximo por plan y periodo."],
          ].map(([Icon, title, text]) => {
            const LucideIcon = Icon as typeof Sparkles;
            return (
              <div key={String(title)} className="rounded-lg border border-black/10 bg-white p-5">
                <LucideIcon className="mb-4 h-6 w-6 text-[#1A5860]" />
                <h3 className="font-semibold">{String(title)}</h3>
                <p className="mt-2 text-sm leading-6 text-black/65">{String(text)}</p>
              </div>
            );
          })}
        </div>
        </div>
      </section>

      <section className="bg-[#151515] py-20 text-white">
        <div className="mx-auto max-w-7xl px-4">
          <div className="max-w-2xl">
            <Badge className="mb-4 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Diferente a un builder generico</Badge>
            <h2 className="text-3xl font-semibold sm:text-5xl">AURALY se enfoca en operar conversaciones que terminan en accion.</h2>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-3">
            {DIFFERENTIATORS.map(([title, text]) => (
              <div key={title} className="rounded-lg border border-white/10 bg-white/[0.04] p-6">
                <h3 className="text-lg font-semibold">{title}</h3>
                <p className="mt-3 text-sm leading-6 text-white/65">{text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

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
        </div>
      </section>

      <section id="planes" className="bg-[#f7f8f2] py-20">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <Badge className="mb-4 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Planes AURALY</Badge>
              <h2 className="text-3xl font-semibold sm:text-5xl">Planes que crecen contigo.</h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-black/65">POS, facturación electrónica, contabilidad y nómina con capacidad visible y ampliaciones sin sorpresas.</p>
          </div>
          {operationsCatalog.isLoading && <div className="mt-10 rounded-2xl border border-black/10 bg-white p-8 text-center text-black/60">Cargando planes vigentes…</div>}
          {operationsCatalog.isError && <div role="alert" className="mt-10 rounded-2xl border border-red-200 bg-red-50 p-6 text-red-800">No fue posible consultar los planes en este momento.</div>}
          <div className="mt-10 grid auto-rows-fr items-stretch gap-4 min-[560px]:grid-cols-2 xl:grid-cols-5">
            {operationsCatalog.data?.plans.map((plan) => {
              const copy = OPERATIONS_PLAN_COPY[plan.code] ?? { tagline: "Plan Auraly", hint: "Capacidad configurable para tu operación." };
              const capacity = plan.isCustom ? ["Capacidad superior a Empresa"] : [
                `${plan.includedFullUsers} ${plan.includedFullUsers === 1 ? "usuario completo" : "usuarios completos"}`,
                `${plan.includedPosDevices} ${plan.includedPosDevices === 1 ? "caja" : "cajas"}`,
                `${plan.includedDianDocuments.toLocaleString("es-CO")} documentos DIAN / mes`,
                ...(plan.includedPayrollEmployees > 0 ? [`${plan.includedPayrollEmployees} empleados de nómina`] : []),
              ];
              const visibleFeatures = [...new Set([
                ...capacity,
                ...plan.features.filter(feature => !feature.toLocaleLowerCase("es-CO").includes("documentos dian")),
              ])];
              return <Card key={plan.planId} className={cn("flex h-full flex-col rounded-lg border-black/10 bg-white text-[#151515]", plan.isRecommended && "border-[#69D9D0] bg-[#E6FFFD]")}>
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
          <div className="mt-6 grid gap-5 rounded-[2rem] border border-[#1A5860]/15 bg-gradient-to-br from-white to-[#E6FFFD] p-6 lg:grid-cols-[.72fr_1.28fr] lg:p-8">
            <div><p className="text-xs font-bold uppercase tracking-[.2em] text-[#1A5860]">Amplía cuando lo necesites</p><h3 className="mt-2 text-2xl font-semibold">Tu plan no limita tu crecimiento.</h3><p className="mt-3 text-sm leading-6 text-black/60">Agrega capacidad al crear la empresa o desde tu suscripción. El total se recalcula antes del pago.</p></div>
            <div className="grid gap-2 sm:grid-cols-2">
              {operationsCatalog.data?.addOns.map(addOn => <div key={addOn.addOnId} className="flex items-center justify-between gap-4 rounded-xl border border-black/5 bg-white/80 px-4 py-3 text-sm"><span>{addOn.name}<small className="block text-black/50">{addOn.unitLabel}</small></span><strong className="whitespace-nowrap text-[#0F2C33]">{cop.format(addOn.monthlyUnitPriceCop)} / mes</strong></div>)}
              <div className="flex items-center justify-between gap-4 rounded-xl border border-black/5 bg-white/80 px-4 py-3 text-sm"><span>Sedes dentro del mismo NIT</span><strong className="whitespace-nowrap text-[#0F2C33]">Sin costo</strong></div>
            </div>
          </div>
        </div>
      </section>

      <section id="planes-ia" className="border-t border-white/10 bg-[#06090B] py-20 text-white">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div><Badge className="mb-4 bg-[#69D9D0] text-[#07161A]">Planes de agentes de IA</Badge><h2 className="text-3xl font-semibold sm:text-5xl">Créditos y capacidad para conversaciones reales.</h2></div>
            <p className="max-w-md text-sm leading-6 text-white/60">Conserva la oferta de agentes, almacenamiento y líneas de WhatsApp; escala según el volumen de atención.</p>
          </div>
          <div className="mt-10 grid auto-rows-fr gap-4 min-[560px]:grid-cols-2 xl:grid-cols-4">
            {PLANS.map((plan) => <Card key={plan.name} className={cn("flex h-full flex-col rounded-2xl border-white/10 bg-white/[.06] text-white", plan.highlight && "border-[#69D9D0] bg-[#0F2C33]")}>
              <CardHeader><div className="flex items-center justify-between gap-2"><CardTitle>{plan.name}</CardTitle>{plan.highlight && <Badge className="bg-[#69D9D0] text-[#07161A]">Recomendado</Badge>}</div><p className="pt-4 text-4xl font-semibold">{plan.price === "A medida" ? plan.price : `$${plan.price}`}</p>{plan.price !== "A medida" && <p className="text-sm text-white/50">COP / mes</p>}</CardHeader>
              <CardContent className="flex flex-1 flex-col gap-5"><p className="min-h-12 text-sm leading-6 text-white/60">{plan.hint}</p><Separator className="bg-white/10"/><div className="text-sm"><strong className="text-[#69D9D0]">{plan.credits}</strong> créditos mensuales<p className="mt-1 text-white/55">{plan.capacity}</p></div><ul className="space-y-3 text-sm">{plan.features.map(feature => <li key={feature} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[#69D9D0]"/>{feature}</li>)}</ul><Button asChild className="mt-auto w-full bg-[#69D9D0] text-[#07161A] hover:bg-[#7CE3DB]"><a href="#demo">Solicitar demo <ChevronRight className="ml-1 h-4 w-4"/></a></Button></CardContent>
            </Card>)}
          </div>
        </div>
      </section>

      <section id="faq" className="bg-[#151515] py-20 text-white">
        <div className="mx-auto grid max-w-7xl gap-10 px-4 lg:grid-cols-[0.8fr_1.2fr]">
        <div>
          <Badge className="mb-4 bg-[#69D9D0] text-[#07161A] hover:bg-[#69D9D0]">Preguntas frecuentes</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Creditos simples para el cliente, costos controlados para ti.</h2>
        </div>
        <Accordion type="single" collapsible className="w-full">
          {FAQ.map((item) => (
            <AccordionItem key={item.q} value={item.q} className="border-white/20">
              <AccordionTrigger className="text-white hover:text-[#69D9D0]">{item.q}</AccordionTrigger>
              <AccordionContent className="text-white/65">{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
        </div>
      </section>

      <section id="demo" className="border-t border-black/10 bg-white py-20">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 lg:grid-cols-[1fr_0.8fr] lg:items-center">
          <div>
            <Sparkles className="mb-5 h-8 w-8 text-[#1A5860]" />
            <h2 className="text-3xl font-semibold sm:text-5xl">Revisemos tu flujo actual de WhatsApp.</h2>
            <p className="mt-4 max-w-2xl text-lg text-black/65">Dejanos tus datos y te escribimos por WhatsApp para revisar tu flujo, detectar oportunidades y agendar una demo en vivo de AURALY.</p>
          </div>
          <form onSubmit={submit} noValidate className="grid gap-3 rounded-lg border border-black/10 bg-[#f7f8f2] p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <Input value={form.name} onChange={(event) => updateForm("name", event.target.value)} placeholder="Nombre" required aria-required="true" aria-invalid={Boolean(errors.name)} className="bg-white text-[#151515] placeholder:text-black/45" />
                {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
              </div>
              <div className="space-y-1">
                <Input type="email" value={form.email} onChange={(event) => updateForm("email", event.target.value)} placeholder="Correo requerido" required aria-required="true" aria-invalid={Boolean(errors.email)} className="bg-white text-[#151515] placeholder:text-black/45" />
                {errors.email && <p className="text-xs text-destructive">{errors.email}</p>}
              </div>
              <div className="space-y-1">
                <Input value={form.company} onChange={(event) => updateForm("company", event.target.value)} placeholder="Empresa" required aria-required="true" aria-invalid={Boolean(errors.company)} className="bg-white text-[#151515] placeholder:text-black/45" />
                {errors.company && <p className="text-xs text-destructive">{errors.company}</p>}
              </div>
              <div className="space-y-1">
                <Input value={form.phone} onChange={(event) => updateForm("phone", event.target.value)} placeholder="WhatsApp" required aria-required="true" aria-invalid={Boolean(errors.phone)} className="bg-white text-[#151515] placeholder:text-black/45" />
                {errors.phone && <p className="text-xs text-destructive">{errors.phone}</p>}
              </div>
            </div>
            {statusMessage && (
              <p className={cn("text-sm", status === "success" ? "text-[#1A5860]" : "text-destructive")}>
                {statusMessage}
              </p>
            )}
            <Button type="submit" disabled={status === "loading"} className="bg-[#151515] text-white hover:bg-black">
              {status === "loading" ? "Enviando..." : "Solicitar demo"}
            </Button>
          </form>
        </div>
      </section>

      <footer className="border-t border-black/10 py-10">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 text-sm text-black/60 sm:flex-row sm:items-center sm:justify-between">
          <p>AURALY. Intelligence Amplified. Imagination Realized.</p>
          <div className="flex gap-4">
            <a href="#servicios">Servicios</a>
            <a href="#facturacion">Facturación</a>
            <a href="#agentes">Agentes</a>
            <a href="#planes">Planes</a>
            <a href="#faq">FAQ</a>
          </div>
        </div>
      </footer>

      {whatsappContactHref && (
        <a
          href={whatsappContactHref}
          target="_blank"
          rel="noreferrer"
          aria-label="Abrir WhatsApp con Aly"
          className="fixed bottom-5 right-5 z-50 flex h-14 w-14 items-center justify-center rounded-full bg-[#25D366] text-white shadow-2xl shadow-black/25 transition hover:bg-[#1EBE57] focus:outline-none focus:ring-2 focus:ring-[#25D366] focus:ring-offset-2"
        >
          <MessageCircle className="h-6 w-6" />
        </a>
      )}
    </main>
  );
}
