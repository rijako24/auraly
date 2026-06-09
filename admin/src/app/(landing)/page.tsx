"use client";

import Link from "next/link";
import { useState } from "react";
import {
  ArrowRight,
  BarChart3,
  Bot,
  CalendarCheck,
  Check,
  ChevronRight,
  Clock3,
  CreditCard,
  Gauge,
  Menu,
  MessageCircle,
  ShieldCheck,
  Sparkles,
  Users,
  X,
  Zap,
} from "lucide-react";

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
import { cn } from "@/lib/utils";

const PLANS = [
  {
    name: "Esencial",
    price: "389.999",
    hint: "Para equipos que quieren vender por WhatsApp sin perder chats.",
    credits: "15.000",
    capacity: "30-40 conversaciones diarias",
    highlight: false,
    features: [
      "1 agente de IA",
      "1 linea de WhatsApp",
      "Reservas, pagos y catalogo",
      "Dashboard de consumo",
      "Pausa automatica al llegar al limite",
    ],
  },
  {
    name: "Crecimiento",
    price: "899.999",
    hint: "Para negocios con mas volumen, sedes o varias lineas comerciales.",
    credits: "45.000",
    capacity: "100-130 conversaciones diarias",
    highlight: true,
    features: [
      "3 agentes de IA",
      "Hasta 3 usuarios",
      "Analytics avanzado",
      "Integraciones operativas",
      "Soporte prioritario",
    ],
  },
  {
    name: "Pro",
    price: "1.799.999",
    hint: "Para equipos que necesitan escala, control y acompanamiento.",
    credits: "120.000",
    capacity: "250-350 conversaciones diarias",
    highlight: false,
    features: [
      "8 agentes de IA",
      "Multi-sede",
      "Reportes avanzados",
      "Flujos de venta complejos",
      "Acompanamiento prioritario",
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
      "Creditos por consumo",
      "Agentes personalizados",
      "Soporte dedicado",
      "Acuerdos de servicio",
      "Arquitectura a medida",
    ],
  },
];

const OUTCOMES = [
  { icon: Clock3, title: "Responde en segundos", text: "El agente atiende preguntas, precios y disponibilidad sin esperar a que alguien abra WhatsApp." },
  { icon: CalendarCheck, title: "Agenda sin friccion", text: "Consulta horarios, captura datos y confirma reservas con reglas de negocio." },
  { icon: CreditCard, title: "Cobra en el chat", text: "Genera links de pago y sigue la conversacion hasta cerrar la venta." },
  { icon: BarChart3, title: "Mide cada peso", text: "Ves creditos, costo operativo y margen antes de que el uso se salga de control." },
];

const FAQ = [
  {
    q: "Como funcionan los creditos?",
    a: "Los creditos representan uso operativo del agente. Una respuesta simple suele consumir 1 credito; acciones avanzadas como agenda, cotizacion, pagos, audio o documentos pueden consumir mas.",
  },
  {
    q: "Que pasa cuando se llega al limite?",
    a: "El agente se pausa automaticamente. El sistema recibe el mensaje, no llama a la IA y no envia respuesta hasta que se renueve el plan o se compre una bolsa adicional.",
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
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [email, setEmail] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    setEmail("");
  };

  return (
    <main className="min-h-screen bg-[#f7f8f2] text-[#151515]">
      <header className="sticky top-0 z-50 border-b border-black/10 bg-[#f7f8f2]/90 backdrop-blur">
        <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2 text-xl font-semibold">
            <span className="flex h-8 w-8 items-center justify-center rounded-md bg-[#151515] text-[#d7ff3f]">T</span>
            Talkio
          </Link>
          <div className="hidden items-center gap-8 md:flex">
            <a href="#producto" className="text-sm font-medium text-black/65 hover:text-black">Producto</a>
            <a href="#planes" className="text-sm font-medium text-black/65 hover:text-black">Planes</a>
            <a href="#faq" className="text-sm font-medium text-black/65 hover:text-black">FAQ</a>
          </div>
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <Button variant="ghost" asChild className="hidden sm:inline-flex"><Link href="/login">Entrar</Link></Button>
            <Button asChild className="bg-[#151515] text-white hover:bg-black"><Link href="/register">Solicitar demo</Link></Button>
            <Button variant="ghost" size="icon" className="md:hidden" onClick={() => setMobileMenuOpen((v) => !v)} aria-label="Menu">
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </nav>
        {mobileMenuOpen && (
          <div className="border-t border-black/10 px-4 py-4 md:hidden">
            <div className="mx-auto flex max-w-7xl flex-col gap-3 text-sm">
              <a href="#producto" onClick={() => setMobileMenuOpen(false)}>Producto</a>
              <a href="#planes" onClick={() => setMobileMenuOpen(false)}>Planes</a>
              <a href="#faq" onClick={() => setMobileMenuOpen(false)}>FAQ</a>
            </div>
          </div>
        )}
      </header>

      <section className="mx-auto grid min-h-[calc(100vh-4rem)] max-w-7xl items-center gap-10 px-4 py-10 lg:grid-cols-[1.05fr_0.95fr]">
        <div className="max-w-3xl">
          <Badge className="mb-5 bg-[#d7ff3f] text-black hover:bg-[#d7ff3f]">Agentes de IA para vender por WhatsApp</Badge>
          <h1 className="text-5xl font-semibold leading-[1.02] tracking-normal sm:text-6xl lg:text-7xl">
            Cierra ventas mas rapido con un agente que nunca deja chats en visto.
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-black/70">
            Talkio responde, cotiza, agenda y cobra desde WhatsApp. Tu equipo conserva el control; la IA hace el trabajo repetitivo y te muestra consumo, creditos y margen en tiempo real.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Button size="lg" asChild className="bg-[#151515] text-white hover:bg-black">
              <Link href="/register">Quiero vender con IA <ArrowRight className="ml-2 h-4 w-4" /></Link>
            </Button>
            <Button size="lg" variant="outline" asChild className="border-black/20 bg-white">
              <a href="#planes">Ver planes</a>
            </Button>
          </div>
          <div className="mt-8 grid max-w-2xl gap-3 sm:grid-cols-3">
            {["WhatsApp Cloud API", "Azure OpenAI", "Pagos y reservas"].map((item) => (
              <div key={item} className="flex items-center gap-2 text-sm text-black/70"><Check className="h-4 w-4 text-[#15803d]" />{item}</div>
            ))}
          </div>
        </div>

        <div className="relative">
          <div className="absolute -left-6 top-8 hidden h-24 w-24 rounded-full bg-[#d7ff3f] md:block" />
          <div className="relative overflow-hidden rounded-[2rem] border border-black/10 bg-[#151515] p-5 text-white shadow-2xl">
            <div className="flex items-center justify-between border-b border-white/10 pb-4">
              <div>
                <p className="text-sm text-white/55">Pipeline de hoy</p>
                <p className="text-2xl font-semibold">47 chats atendidos</p>
              </div>
              <Badge className="bg-[#d7ff3f] text-black hover:bg-[#d7ff3f]">+38% cierres</Badge>
            </div>
            <div className="mt-5 space-y-3">
              {[
                ["Cliente", "Hola, tienen agenda para manana?"],
                ["Talkio", "Si. Tengo 10:30 a.m. y 3:00 p.m. Tambien puedo enviarte el link de pago para separar."],
                ["Cliente", "La de 3 esta bien"],
                ["Talkio", "Perfecto. Te envio el resumen y el pago seguro para confirmar tu cupo."],
              ].map(([sender, text], index) => (
                <div key={`${sender}-${index}`} className={cn("max-w-[86%] rounded-2xl px-4 py-3 text-sm", sender === "Talkio" ? "ml-auto bg-[#d7ff3f] text-black" : "bg-white/10 text-white")}>
                  <p className="mb-1 text-xs opacity-65">{sender}</p>
                  <p>{text}</p>
                </div>
              ))}
            </div>
            <div className="mt-5 grid gap-3 rounded-2xl bg-white p-4 text-black sm:grid-cols-3">
              <div><p className="text-xs text-black/55">Credito usado</p><p className="font-semibold">8.420 / 15.000</p></div>
              <div><p className="text-xs text-black/55">Uso</p><p className="font-semibold">56%</p></div>
              <div><p className="text-xs text-black/55">Estado</p><p className="font-semibold text-[#15803d]">Activo</p></div>
              <div className="sm:col-span-3"><Progress value={56} /></div>
            </div>
          </div>
        </div>
      </section>

      <section id="producto" className="border-y border-black/10 bg-white py-20">
        <div className="mx-auto max-w-7xl px-4">
          <div className="max-w-2xl">
            <Badge variant="outline" className="mb-4">Sistema comercial completo</Badge>
            <h2 className="text-3xl font-semibold sm:text-5xl">No es un chatbot. Es una recepcionista comercial con tablero de control.</h2>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            {OUTCOMES.map((item) => {
              const Icon = item.icon;
              return (
                <Card key={item.title} className="rounded-lg border-black/10">
                  <CardHeader>
                    <Icon className="h-6 w-6 text-[#0f766e]" />
                    <CardTitle className="text-lg">{item.title}</CardTitle>
                  </CardHeader>
                  <CardContent className="text-sm leading-6 text-black/65">{item.text}</CardContent>
                </Card>
              );
            })}
          </div>
        </div>
      </section>

      <section className="mx-auto grid max-w-7xl gap-10 px-4 py-20 lg:grid-cols-[0.9fr_1.1fr]">
        <div>
          <Badge className="mb-4 bg-[#151515] text-white">Control de margen</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Tus planes tienen creditos. Tu operacion tiene limite de costo.</h2>
          <p className="mt-5 text-lg leading-8 text-black/70">
            El cliente ve una medicion simple. Tu administras costo real por IA, WhatsApp, tools e integraciones. Si el negocio llega al limite, el agente se pausa antes de gastar mas.
          </p>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          {[
            [Bot, "IA", "Tokens de entrada y salida medidos por turno."],
            [MessageCircle, "Canales", "WhatsApp entrante, secuencias y plantillas separadas."],
            [Gauge, "Uso", "Creditos y porcentaje visibles para el cliente."],
            [ShieldCheck, "Margen", "Costo variable maximo por plan y periodo."],
          ].map(([Icon, title, text]) => {
            const LucideIcon = Icon as typeof Bot;
            return (
              <div key={String(title)} className="rounded-lg border border-black/10 bg-white p-5">
                <LucideIcon className="mb-4 h-6 w-6 text-[#0f766e]" />
                <h3 className="font-semibold">{String(title)}</h3>
                <p className="mt-2 text-sm leading-6 text-black/65">{String(text)}</p>
              </div>
            );
          })}
        </div>
      </section>

      <section id="planes" className="bg-[#151515] py-20 text-white">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <Badge className="mb-4 bg-[#d7ff3f] text-black hover:bg-[#d7ff3f]">Planes Talkio</Badge>
              <h2 className="text-3xl font-semibold sm:text-5xl">Precios claros para volumen real de WhatsApp.</h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-white/60">Campanas y plantillas marketing de WhatsApp se cobran aparte. Asi protegemos margen y evitamos sorpresas.</p>
          </div>
          <div className="mt-10 grid gap-4 lg:grid-cols-4">
            {PLANS.map((plan) => (
              <Card key={plan.name} className={cn("rounded-lg border-white/10 bg-white/[0.04] text-white", plan.highlight && "border-[#d7ff3f] bg-[#d7ff3f]/10")}>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>{plan.name}</CardTitle>
                    {plan.highlight && <Badge className="bg-[#d7ff3f] text-black hover:bg-[#d7ff3f]">Recomendado</Badge>}
                  </div>
                  <div className="pt-4">
                    <p className="text-4xl font-semibold">{plan.price === "A medida" ? plan.price : `$${plan.price}`}</p>
                    {plan.price !== "A medida" && <p className="text-sm text-white/55">COP / mes</p>}
                  </div>
                </CardHeader>
                <CardContent className="space-y-5">
                  <p className="min-h-12 text-sm leading-6 text-white/70">{plan.hint}</p>
                  <Separator className="bg-white/10" />
                  <div className="grid gap-2 text-sm">
                    <p><span className="text-[#d7ff3f]">{plan.credits}</span> creditos mensuales</p>
                    <p className="text-white/65">{plan.capacity}</p>
                  </div>
                  <ul className="space-y-3 text-sm">
                    {plan.features.map((feature) => (
                      <li key={feature} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-[#d7ff3f]" />{feature}</li>
                    ))}
                  </ul>
                  <Button className={cn("w-full", plan.highlight ? "bg-[#d7ff3f] text-black hover:bg-[#c8ef34]" : "bg-white text-black hover:bg-white/90")} asChild>
                    <Link href="/register">Empezar <ChevronRight className="ml-1 h-4 w-4" /></Link>
                  </Button>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      <section id="faq" className="mx-auto grid max-w-7xl gap-10 px-4 py-20 lg:grid-cols-[0.8fr_1.2fr]">
        <div>
          <Badge variant="outline" className="mb-4">Preguntas frecuentes</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Creditos simples para el cliente, costos controlados para ti.</h2>
        </div>
        <Accordion type="single" collapsible className="w-full">
          {FAQ.map((item) => (
            <AccordionItem key={item.q} value={item.q}>
              <AccordionTrigger>{item.q}</AccordionTrigger>
              <AccordionContent className="text-black/65">{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      </section>

      <section className="bg-white py-20">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 lg:grid-cols-[1fr_0.8fr] lg:items-center">
          <div>
            <Sparkles className="mb-5 h-8 w-8 text-[#0f766e]" />
            <h2 className="text-3xl font-semibold sm:text-5xl">Convierte WhatsApp en tu mejor canal de ventas.</h2>
            <p className="mt-4 max-w-2xl text-lg text-black/65">Agenda una demo y revisamos tu flujo actual: preguntas frecuentes, reservas, pagos, plantillas y limites de consumo.</p>
          </div>
          <form onSubmit={submit} className="flex flex-col gap-3 rounded-lg border border-black/10 bg-[#f7f8f2] p-4 sm:flex-row">
            <Input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="tu@email.com" required className="bg-white" />
            <Button type="submit" className="bg-[#151515] text-white hover:bg-black">Solicitar demo</Button>
          </form>
        </div>
      </section>

      <footer className="border-t border-black/10 py-10">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 text-sm text-black/60 sm:flex-row sm:items-center sm:justify-between">
          <p>Talkio AI. Agentes para vender mas rapido por WhatsApp.</p>
          <div className="flex gap-4">
            <a href="#producto">Producto</a>
            <a href="#planes">Planes</a>
            <a href="#faq">FAQ</a>
          </div>
        </div>
      </footer>
    </main>
  );
}
