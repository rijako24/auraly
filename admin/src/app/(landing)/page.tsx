"use client";

import Link from "next/link";
import { useState } from "react";
import {
  ArrowRight,
  BarChart3,
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
  X,
} from "lucide-react";

import { AuralyLogo } from "@/components/brand/auraly-logo";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Progress } from "@/components/ui/progress";
import { Separator } from "@/components/ui/separator";
import { Textarea } from "@/components/ui/textarea";
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
    price: "350.000",
    hint: "Para equipos que quieren vender por WhatsApp sin perder chats.",
    credits: "10k",
    capacity: "20-30 conversaciones diarias",
    highlight: false,
    features: [
      "1 agente de IA",
      "Hasta 3 usuarios",
      "5 GB de almacenamiento de base de datos",
      "1 linea de WhatsApp",
      "Reservas, pagos y catalogo",
      "Dashboard de consumo",
      "Alertas de consumo del plan",
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
      "3 agentes de IA",
      "Hasta 5 usuarios",
      "10 GB de almacenamiento de base de datos",
      "Hasta 3 lineas de WhatsApp",
      "Analytics avanzado",
      "Integraciones operativas",
      "Soporte prioritario",
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
      "Agentes de IA ilimitados",
      "Usuarios ilimitados",
      "20 GB de almacenamiento de base de datos",
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
      "50 GB de almacenamiento de base de datos",
      "Soporte dedicado",
      "Acuerdos de servicio",
      "Arquitectura a medida",
    ],
  },
];

const OUTCOMES = [
  { icon: Clock3, title: "Responde y califica", text: "Atiende preguntas, detecta intencion de compra y captura datos utiles sin dejar el chat esperando." },
  { icon: CalendarCheck, title: "Agenda con reglas", text: "Consulta disponibilidad, toma datos y confirma reservas respetando horarios, sedes y politicas." },
  { icon: CreditCard, title: "Cobra sin salir del chat", text: "Genera resumen, link de pago y seguimiento para que la venta no se enfrie." },
  { icon: BarChart3, title: "Mide consumo y margen", text: "Controla creditos, costos de IA y limites operativos antes de que el canal se vuelva caro." },
];

const DIFFERENTIATORS = [
  ["Vertical primero", "No empieza como constructor generico. AURALY viene pensado para WhatsApp, ventas, reservas y pagos."],
  ["Humano cuando importa", "El agente sabe pausar, escalar y entregar contexto para que tu equipo intervenga sin perder la conversacion."],
  ["Costo visible", "Cada plan tiene creditos y limites claros para proteger margen, no solo promesas de automatizacion."],
];

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
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [form, setForm] = useState({
    name: "",
    email: "",
    company: "",
    phone: "",
    message: "",
  });
  const [status, setStatus] = useState<"idle" | "loading" | "success" | "error">("idle");
  const [statusMessage, setStatusMessage] = useState("");

  const updateForm = (field: keyof typeof form, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
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

      setForm({ name: "", email: "", company: "", phone: "", message: "" });
      setStatus("success");
      setStatusMessage("Listo. Recibimos tu solicitud y te contactaremos pronto.");
    } catch (error) {
      setStatus("error");
      setStatusMessage(error instanceof Error ? error.message : "No se pudo enviar la solicitud.");
    }
  };

  return (
    <main className="min-h-screen bg-auraly-text text-auraly-background">
      <header className="sticky top-0 z-50 border-b border-auraly-primary/50 bg-auraly-background/90 backdrop-blur">
        <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2 text-xl font-semibold">
            <AuralyLogo className="[&>span]:text-auraly-text" />
          </Link>
          <div className="hidden items-center gap-8 md:flex">
            <a href="#producto" className="text-sm font-medium text-auraly-secondary hover:text-auraly-text">Producto</a>
            <a href="#planes" className="text-sm font-medium text-auraly-secondary hover:text-auraly-text">Planes</a>
            <a href="#faq" className="text-sm font-medium text-auraly-secondary hover:text-auraly-text">FAQ</a>
          </div>
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <Button variant="ghost" asChild className="hidden text-auraly-text hover:bg-auraly-surface hover:text-auraly-text sm:inline-flex"><Link href="/login">Entrar</Link></Button>
            <Button asChild className="bg-auraly-accent text-auraly-background hover:bg-[#7CE3DB]"><a href="#demo">Solicitar demo</a></Button>
            <Button variant="ghost" size="icon" className="md:hidden" onClick={() => setMobileMenuOpen((v) => !v)} aria-label="Menu">
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </nav>
        {mobileMenuOpen && (
          <div className="border-t border-auraly-primary/50 px-4 py-4 md:hidden">
            <div className="mx-auto flex max-w-7xl flex-col gap-3 text-sm">
              <a href="#producto" onClick={() => setMobileMenuOpen(false)}>Producto</a>
              <a href="#planes" onClick={() => setMobileMenuOpen(false)}>Planes</a>
              <a href="#faq" onClick={() => setMobileMenuOpen(false)}>FAQ</a>
            </div>
          </div>
        )}
      </header>

      <section className="bg-[radial-gradient(circle_at_78%_18%,rgba(105,217,208,0.16),transparent_28%),linear-gradient(135deg,#07161A_0%,#0F2C33_68%,#07161A_100%)] text-auraly-text">
        <div className="mx-auto grid min-h-[calc(100vh-4rem)] max-w-7xl items-center gap-10 px-4 py-10 lg:grid-cols-[1.05fr_0.95fr]">
        <div className="max-w-3xl">
          <Badge className="mb-5 border-auraly-primary bg-auraly-surface text-auraly-accent hover:bg-auraly-surface">Para negocios que venden por WhatsApp</Badge>
          <h1 className="text-5xl font-semibold leading-[1.02] tracking-normal sm:text-6xl lg:text-7xl">
            Convierte chats en ventas, reservas y pagos medibles.
          </h1>
          <p className="mt-6 max-w-2xl text-lg leading-8 text-auraly-secondary">
            AURALY atiende, califica, cotiza y agenda desde WhatsApp con contexto de tu negocio. Tu equipo conserva el control; la IA ejecuta lo repetitivo y deja trazabilidad de consumo, margen y resultados.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Button size="lg" asChild className="bg-auraly-accent text-auraly-background hover:bg-[#7CE3DB]">
              <a href="#demo">Solicitar demo <ArrowRight className="ml-2 h-4 w-4" /></a>
            </Button>
            <Button size="lg" variant="outline" asChild className="border-auraly-primary bg-auraly-surface text-auraly-text hover:bg-auraly-primary">
              <a href="#planes">Ver planes</a>
            </Button>
          </div>
          <div className="mt-8 grid max-w-2xl gap-3 sm:grid-cols-3">
            {["WhatsApp Cloud API", "Azure OpenAI", "Pagos y reservas"].map((item) => (
              <div key={item} className="flex items-center gap-2 text-sm text-auraly-secondary"><Check className="h-4 w-4 text-auraly-accent" />{item}</div>
            ))}
          </div>
        </div>

        <div className="relative">
          <div className="relative overflow-hidden rounded-lg border border-auraly-primary bg-auraly-surface p-5 text-auraly-text shadow-2xl shadow-auraly-accent/10">
            <div className="flex items-center justify-between border-b border-auraly-primary pb-4">
              <div>
                <p className="text-sm text-auraly-secondary">Pipeline de hoy</p>
                <p className="text-2xl font-semibold">47 conversaciones activas</p>
              </div>
              <Badge className="bg-auraly-accent text-auraly-background hover:bg-auraly-accent">+38% cierres</Badge>
            </div>
            <div className="mt-5 space-y-3">
              {[
                ["Cliente", "Hola, tienen agenda para manana?"],
                ["AURALY", "Si. Tengo 10:30 a.m. y 3:00 p.m. Tambien puedo enviarte el link de pago para separar."],
                ["Cliente", "La de 3 esta bien"],
                ["AURALY", "Perfecto. Te envio el resumen y el pago seguro para confirmar tu cupo."],
              ].map(([sender, text], index) => (
                <div key={`${sender}-${index}`} className={cn("max-w-[86%] rounded-lg px-4 py-3 text-sm", sender === "AURALY" ? "ml-auto bg-auraly-accent text-auraly-background" : "bg-auraly-background text-auraly-text")}>
                  <p className="mb-1 text-xs opacity-65">{sender}</p>
                  <p>{text}</p>
                </div>
              ))}
            </div>
            <div className="mt-5 grid gap-3 rounded-lg bg-auraly-background p-4 text-auraly-text sm:grid-cols-3">
              <div><p className="text-xs text-auraly-secondary">Credito usado</p><p className="font-semibold">8.420 / 10k</p></div>
              <div><p className="text-xs text-auraly-secondary">Uso</p><p className="font-semibold">84%</p></div>
              <div><p className="text-xs text-auraly-secondary">Estado</p><p className="font-semibold text-auraly-accent">Activo</p></div>
              <div className="sm:col-span-3"><Progress value={84} /></div>
            </div>
          </div>
        </div>
        </div>
      </section>

      <section id="producto" className="border-y border-auraly-primary/20 bg-auraly-text py-20 text-auraly-background">
        <div className="mx-auto max-w-7xl px-4">
          <div className="max-w-2xl">
            <Badge variant="outline" className="mb-4 border-auraly-primary text-auraly-primary">Sistema comercial completo</Badge>
            <h2 className="text-3xl font-semibold sm:text-5xl">No es un chatbot generico. Es una capa operativa para vender mejor por mensajeria.</h2>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            {OUTCOMES.map((item) => {
              const Icon = item.icon;
              return (
                <Card key={item.title} className="rounded-lg border-auraly-primary/20 bg-white text-auraly-background">
                  <CardHeader>
                    <Icon className="h-6 w-6 text-auraly-accent" />
                    <CardTitle className="text-lg">{item.title}</CardTitle>
                  </CardHeader>
                  <CardContent className="text-sm leading-6 text-muted-foreground">{item.text}</CardContent>
                </Card>
              );
            })}
          </div>
        </div>
      </section>

      <section className="bg-white py-20 text-auraly-background">
        <div className="mx-auto grid max-w-7xl gap-10 px-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div>
            <Badge className="mb-4 bg-auraly-accent text-auraly-background">Control de margen</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Automatizacion con numeros claros, no una caja negra.</h2>
          <p className="mt-5 text-lg leading-8 text-auraly-primary">
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
              <div key={String(title)} className="rounded-lg border border-auraly-primary/20 bg-auraly-text p-5">
                <LucideIcon className="mb-4 h-6 w-6 text-auraly-accent" />
                <h3 className="font-semibold">{String(title)}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{String(text)}</p>
              </div>
            );
          })}
        </div>
        </div>
      </section>

      <section className="bg-auraly-background py-20 text-auraly-text">
        <div className="mx-auto max-w-7xl px-4">
          <div className="max-w-2xl">
            <Badge className="mb-4 bg-auraly-accent text-auraly-background hover:bg-auraly-accent">Diferente a un builder generico</Badge>
            <h2 className="text-3xl font-semibold sm:text-5xl">AURALY se enfoca en operar conversaciones que terminan en accion.</h2>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-3">
            {DIFFERENTIATORS.map(([title, text]) => (
              <div key={title} className="rounded-lg border border-auraly-primary bg-auraly-surface p-6">
                <h3 className="text-lg font-semibold">{title}</h3>
                <p className="mt-3 text-sm leading-6 text-auraly-secondary">{text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section id="planes" className="bg-auraly-text py-20 text-auraly-background">
        <div className="mx-auto max-w-7xl px-4">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <Badge className="mb-4 bg-auraly-accent text-auraly-background hover:bg-auraly-accent">Planes AURALY</Badge>
              <h2 className="text-3xl font-semibold sm:text-5xl">Precios claros para volumen real de WhatsApp.</h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-auraly-primary">Campanas y plantillas marketing de WhatsApp se cobran aparte. Asi protegemos margen y evitamos sorpresas.</p>
          </div>
          <div className="mt-10 grid gap-4 lg:grid-cols-4">
            {PLANS.map((plan) => (
              <Card key={plan.name} className={cn("rounded-lg border-auraly-primary/20 bg-white text-auraly-background", plan.highlight && "border-auraly-accent bg-[#E9FFFC]")}>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>{plan.name}</CardTitle>
                    {plan.highlight && <Badge className="bg-auraly-accent text-auraly-background hover:bg-auraly-accent">Recomendado</Badge>}
                  </div>
                  <div className="pt-4">
                    <p className="text-4xl font-semibold">{plan.price === "A medida" ? plan.price : `$${plan.price}`}</p>
                    {plan.price !== "A medida" && <p className="text-sm text-auraly-primary">COP / mes</p>}
                  </div>
                </CardHeader>
                <CardContent className="space-y-5">
                  <p className="min-h-12 text-sm leading-6 text-muted-foreground">{plan.hint}</p>
                  <Separator className="bg-auraly-primary/20" />
                  <div className="grid gap-2 text-sm">
                    <p><span className="text-auraly-accent">{plan.credits}</span> creditos mensuales</p>
                    <p className="text-muted-foreground">{plan.capacity}</p>
                  </div>
                  <ul className="space-y-3 text-sm">
                    {plan.features.map((feature) => (
                      <li key={feature} className="flex gap-2"><Check className="mt-0.5 h-4 w-4 shrink-0 text-auraly-accent" />{feature}</li>
                    ))}
                  </ul>
                  <Button className={cn("w-full", plan.highlight ? "bg-auraly-accent text-auraly-background hover:bg-[#7CE3DB]" : "bg-auraly-background text-auraly-text hover:bg-auraly-primary")} asChild>
                    <a href="#demo">Solicitar demo <ChevronRight className="ml-1 h-4 w-4" /></a>
                  </Button>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      <section id="faq" className="bg-white py-20 text-auraly-background">
        <div className="mx-auto grid max-w-7xl gap-10 px-4 lg:grid-cols-[0.8fr_1.2fr]">
        <div>
          <Badge variant="outline" className="mb-4 border-auraly-primary text-auraly-primary">Preguntas frecuentes</Badge>
          <h2 className="text-3xl font-semibold sm:text-5xl">Creditos simples para el cliente, costos controlados para ti.</h2>
        </div>
        <Accordion type="single" collapsible className="w-full">
          {FAQ.map((item) => (
            <AccordionItem key={item.q} value={item.q}>
              <AccordionTrigger>{item.q}</AccordionTrigger>
              <AccordionContent className="text-muted-foreground">{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
        </div>
      </section>

      <section id="demo" className="bg-auraly-surface py-20 text-auraly-text">
        <div className="mx-auto grid max-w-7xl gap-8 px-4 lg:grid-cols-[1fr_0.8fr] lg:items-center">
          <div>
            <Sparkles className="mb-5 h-8 w-8 text-auraly-accent" />
            <h2 className="text-3xl font-semibold sm:text-5xl">Revisemos tu flujo actual de WhatsApp.</h2>
            <p className="mt-4 max-w-2xl text-lg text-auraly-secondary">En la demo mapeamos preguntas frecuentes, datos que necesitas capturar, reglas de agenda, pagos, handoff humano y limites de consumo.</p>
          </div>
          <form onSubmit={submit} className="grid gap-3 rounded-lg border border-auraly-primary bg-auraly-background p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <Input value={form.name} onChange={(event) => updateForm("name", event.target.value)} placeholder="Nombre" className="bg-auraly-surface" />
              <Input type="email" value={form.email} onChange={(event) => updateForm("email", event.target.value)} placeholder="Correo" required className="bg-auraly-surface" />
              <Input value={form.company} onChange={(event) => updateForm("company", event.target.value)} placeholder="Empresa" className="bg-auraly-surface" />
              <Input value={form.phone} onChange={(event) => updateForm("phone", event.target.value)} placeholder="WhatsApp" className="bg-auraly-surface" />
            </div>
            <Textarea value={form.message} onChange={(event) => updateForm("message", event.target.value)} placeholder="Cuentame que quieres automatizar" className="min-h-24 bg-auraly-surface" />
            {statusMessage && (
              <p className={cn("text-sm", status === "success" ? "text-auraly-success" : "text-auraly-error")}>
                {statusMessage}
              </p>
            )}
            <Button type="submit" disabled={status === "loading"} className="bg-auraly-accent text-auraly-background hover:bg-[#7CE3DB]">
              {status === "loading" ? "Enviando..." : "Solicitar demo"}
            </Button>
          </form>
        </div>
      </section>

      <footer className="border-t border-auraly-primary py-10">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 text-sm text-auraly-secondary sm:flex-row sm:items-center sm:justify-between">
          <p>AURALY. Intelligence Amplified. Imagination Realized.</p>
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
