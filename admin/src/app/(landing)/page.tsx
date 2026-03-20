"use client";

import { useState } from "react";
import Link from "next/link";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";

import {
  Bot,
  CalendarDays,
  CreditCard,
  BarChart3,
  Users,
  MessageSquare,
  ChevronRight,
  Menu,
  Globe,
  X,
  Star,
  Zap,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { ThemeToggle } from "@/components/layout/theme-toggle";

const FEATURES = [
  {
    icon: Bot,
    title: "Agente IA Conversacional",
    description:
      "Tu cliente virtual que atiende 24/7, resuelve consultas y gestiona reservas en lenguaje natural.",
  },
  {
    icon: CalendarDays,
    title: "Reservas Automáticas",
    description: "Sistema de citas inteligente que evita conflictos y envía recordatorios automáticos.",
  },
  {
    icon: CreditCard,
    title: "Pagos Integrados",
    description: "Cobra depósitos, servicios y productos directamente por WhatsApp con pasarelas seguras.",
  },
  {
    icon: BarChart3,
    title: "Analytics en Tiempo Real",
    description: "Dashboard con métricas de conversiones, ingresos y comportamiento de clientes.",
  },
  {
    icon: Users,
    title: "Multi-negocio",
    description: "Gestiona varios establecimientos, sedes o franquicias desde un solo panel.",
  },
  {
    icon: MessageSquare,
    title: "WhatsApp Integration",
    description: "Conecta tu número de WhatsApp Business y atiende a todos tus clientes desde un solo lugar.",
  },
];

const STEPS = [
  {
    step: 1,
    title: "Configura tu negocio",
    description: "Define servicios, horarios, precios y políticas en minutos.",
  },
  {
    step: 2,
    title: "Conecta WhatsApp",
    description: "Vincula tu número de WhatsApp Business con un simple enlace.",
  },
  {
    step: 3,
    title: "Deja que la IA trabaje",
    description: "Tu agente atenderá clientes, tomará reservas y cobrará sin intervención manual.",
  },
];

const PLANS = [
  {
    name: "Starter",
    price: "Gratis",
    period: "para siempre",
    features: ["Hasta 50 conversaciones/mes", "1 negocio", "Reservas básicas"],
    cta: "Comenzar gratis",
    highlighted: false,
  },
  {
    name: "Pro",
    price: "$99.000",
    period: "COP/mes",
    features: [
      "Conversaciones ilimitadas",
      "Múltiples negocios",
      "Pagos integrados",
      "Analytics avanzado",
      "Soporte prioritario",
    ],
    cta: "Empezar ahora",
    highlighted: true,
  },
  {
    name: "Enterprise",
    price: "Personalizado",
    period: "según tu negocio",
    features: [
      "Todo en Pro",
      "Soporte dedicado",
      "Integraciones personalizadas",
      "SLA garantizado",
    ],
    cta: "Contactar ventas",
    highlighted: false,
  },
];

const TESTIMONIALS = [
  {
    quote:
      "Quantix AI redujo nuestras no-shows en un 60%. Los clientes reservan y pagan por WhatsApp sin llamar.",
    name: "María García",
    role: "Directora",
    company: "Spa Belleza Total",
    initials: "MG",
  },
  {
    quote:
      "La IA entiende el contexto y responde como una recepcionista real. Nuestros clientes lo aman.",
    name: "Carlos Rodríguez",
    role: "Fundador",
    company: "Barbería Vintage",
    initials: "CR",
  },
  {
    quote:
      "Pasamos de 3 horas de llamadas al día a cero. Todo se resuelve por chat automáticamente.",
    name: "Ana Martínez",
    role: "Gerente",
    company: "Clínica Dental Smile",
    initials: "AM",
  },
];

const FAQ_ITEMS = [
  {
    q: "¿Necesito un número de WhatsApp Business?",
    a: "Sí. Quantix AI se conecta a tu número de WhatsApp Business existente. Si no tienes uno, puedes crearlo desde la app de WhatsApp en minutos.",
  },
  {
    q: "¿Cómo funciona el agente de IA?",
    a: "El agente usa modelos de lenguaje avanzados para entender consultas en lenguaje natural, consultar tu catálogo de servicios y realizar reservas. Puedes entrenarlo con las respuestas de tu negocio.",
  },
  {
    q: "¿Qué métodos de pago aceptan?",
    a: "Integramos pasarelas como Mercado Pago, Nequi, Daviplata y pagos con tarjeta. Los cobros se realizan directamente en la conversación de WhatsApp.",
  },
  {
    q: "¿Puedo gestionar varios negocios?",
    a: "Sí. El plan Pro permite múltiples establecimientos. Cada uno puede tener su propio catálogo, horarios y número de WhatsApp.",
  },
  {
    q: "¿Hay contrato de permanencia?",
    a: "No. Puedes cancelar en cualquier momento. El plan Starter es gratuito y no requiere tarjeta de crédito.",
  },
];

export default function LandingPage() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [email, setEmail] = useState("");

  const handleCtaSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    // Mock: could show toast or redirect
    setEmail("");
  };

  return (
    <div className="min-h-screen bg-background">
      {/* Header */}
      <header className="sticky top-0 z-50 w-full border-b border-border/40 bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
        <nav className="container mx-auto flex h-16 items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2 font-semibold text-xl">
            <span className="bg-gradient-to-r from-primary to-primary/70 bg-clip-text text-transparent">
              Quantix AI
            </span>
          </Link>

          <div className="hidden md:flex items-center gap-8">
            <a
              href="#features"
              className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors"
            >
              Características
            </a>
            <a
              href="#pricing"
              className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors"
            >
              Precios
            </a>
            <a
              href="#faq"
              className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors"
            >
              FAQ
            </a>
          </div>

          <div className="flex items-center gap-3">
            <ThemeToggle />
            <Link href="/login" className="hidden sm:inline-flex">
              <Button variant="ghost">Iniciar Sesión</Button>
            </Link>
            <Link href="/register">
              <Button>Comenzar Gratis</Button>
            </Link>
            <Button
              variant="ghost"
              size="icon"
              className="md:hidden"
              onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
              aria-label="Menú"
            >
              {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
            </Button>
          </div>
        </nav>

        {/* Mobile menu */}
        {mobileMenuOpen && (
          <div className="md:hidden border-t border-border/40 bg-background px-4 py-4">
            <div className="flex flex-col gap-2">
              <a href="#features" onClick={() => setMobileMenuOpen(false)}>
                Características
              </a>
              <a href="#pricing" onClick={() => setMobileMenuOpen(false)}>
                Precios
              </a>
              <a href="#faq" onClick={() => setMobileMenuOpen(false)}>
                FAQ
              </a>
              <Separator />
              <Link href="/login" onClick={() => setMobileMenuOpen(false)}>
                Iniciar Sesión
              </Link>
              <Link href="/register" onClick={() => setMobileMenuOpen(false)}>
                <Button className="w-full">Comenzar Gratis</Button>
              </Link>
            </div>
          </div>
        )}
      </header>

      {/* Hero */}
      <section className="relative overflow-hidden">
        <div className="absolute inset-0 bg-gradient-to-br from-primary/5 via-transparent to-primary/10 dark:from-primary/10 dark:via-transparent dark:to-primary/5" />
        <div className="container relative mx-auto px-4 py-24 lg:py-32">
          <div className="mx-auto max-w-3xl text-center">
            <Badge
              variant="secondary"
              className="mb-6 rounded-full px-4 py-1.5 text-xs font-medium"
            >
              Nuevo: Agente conversacional con IA
            </Badge>
            <h1 className="text-4xl font-bold tracking-tight sm:text-5xl md:text-6xl lg:text-7xl">
              Automatiza tus reservaciones con{" "}
              <span className="bg-gradient-to-r from-primary to-primary/70 bg-clip-text text-transparent">
                inteligencia artificial
              </span>
            </h1>
            <p className="mt-6 text-lg text-muted-foreground sm:text-xl max-w-2xl mx-auto">
              Quantix AI conecta tu negocio con tus clientes a través de WhatsApp, gestiona reservas,
              cobros y más con un agente de IA.
            </p>
            <div className="mt-10 flex flex-col sm:flex-row gap-4 justify-center">
              <Link href="/register">
                <Button size="lg" className="w-full sm:w-auto min-w-[180px]">
                  Comenzar gratis
                  <ChevronRight className="ml-1 h-4 w-4" />
                </Button>
              </Link>
              <Button variant="outline" size="lg" className="w-full sm:w-auto">
                Ver demo
              </Button>
            </div>
          </div>
          {/* Hero image placeholder */}
          <div className="mt-16 mx-auto max-w-4xl">
            <div className="rounded-2xl border border-border/50 bg-gradient-to-br from-card to-card/50 p-2 shadow-2xl shadow-primary/5">
              <div className="aspect-video rounded-xl bg-gradient-to-br from-primary/20 via-muted/30 to-primary/10 dark:from-primary/30 dark:via-muted/20 dark:to-primary/20 flex items-center justify-center">
                <div className="text-center text-muted-foreground">
                  <BarChart3 className="mx-auto h-16 w-16 mb-2 opacity-50" />
                  <p className="text-sm font-medium">Dashboard Quantix AI</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Features */}
      <section id="features" className="py-24 bg-muted/30 dark:bg-muted/10">
        <div className="container mx-auto px-4">
          <div className="text-center mb-16">
            <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
              Todo lo que necesitas en un solo lugar
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              Herramientas diseñadas para negocios de servicios que quieren crecer sin contratar más
              personal.
            </p>
          </div>
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((feature) => {
              const Icon = feature.icon;
              return (
                <Card
                  key={feature.title}
                  className="group border-border/50 transition-all hover:border-primary/30 hover:shadow-lg hover:shadow-primary/5"
                >
                  <CardHeader>
                    <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary mb-2 group-hover:bg-primary/20 transition-colors">
                      <Icon className="h-6 w-6" />
                    </div>
                    <CardTitle className="text-lg">{feature.title}</CardTitle>
                    <CardDescription>{feature.description}</CardDescription>
                  </CardHeader>
                </Card>
              );
            })}
          </div>
        </div>
      </section>

      {/* How it works */}
      <section className="py-24">
        <div className="container mx-auto px-4">
          <div className="text-center mb-16">
            <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
              Cómo funciona
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              Tres pasos para automatizar tu negocio
            </p>
          </div>
          <div className="grid gap-8 md:grid-cols-3">
            {STEPS.map((item, idx) => (
              <div key={item.step} className="relative">
                {idx < STEPS.length - 1 && (
                  <div className="hidden md:block absolute top-8 left-[calc(50%+2rem)] w-[calc(100%-4rem)] h-0.5 bg-gradient-to-r from-primary/30 to-transparent" />
                )}
                <div className="flex flex-col items-center text-center">
                  <div className="flex h-16 w-16 items-center justify-center rounded-full bg-primary text-primary-foreground text-xl font-bold">
                    {item.step}
                  </div>
                  <h3 className="mt-4 text-xl font-semibold">{item.title}</h3>
                  <p className="mt-2 text-muted-foreground">{item.description}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Pricing */}
      <section id="pricing" className="py-24 bg-muted/30 dark:bg-muted/10">
        <div className="container mx-auto px-4">
          <div className="text-center mb-16">
            <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
              Planes flexibles para tu negocio
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              Comienza gratis y escala cuando lo necesites
            </p>
          </div>
          <div className="grid gap-8 lg:grid-cols-3 lg:gap-6">
            {PLANS.map((plan) => (
              <Card
                key={plan.name}
                className={cn(
                  "relative flex flex-col transition-all",
                  plan.highlighted &&
                    "border-primary shadow-xl shadow-primary/10 scale-[1.02] lg:scale-105"
                )}
              >
                {plan.highlighted && (
                  <div className="absolute -top-3 left-1/2 -translate-x-1/2">
                    <Badge>Más popular</Badge>
                  </div>
                )}
                <CardHeader>
                  <CardTitle>{plan.name}</CardTitle>
                  <div className="mt-2">
                    <span className="text-3xl font-bold">{plan.price}</span>
                    <span className="text-muted-foreground"> {plan.period}</span>
                  </div>
                  <CardDescription className="sr-only">Características del plan</CardDescription>
                </CardHeader>
                <CardContent className="flex-1">
                  <ul className="space-y-3">
                    {plan.features.map((f) => (
                      <li key={f} className="flex items-center gap-2 text-sm">
                        <Zap className="h-4 w-4 shrink-0 text-primary" />
                        {f}
                      </li>
                    ))}
                  </ul>
                  <Button
                    className="mt-6 w-full"
                    variant={plan.highlighted ? "default" : "outline"}
                  >
                    {plan.cta}
                  </Button>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* Testimonials */}
      <section className="py-24">
        <div className="container mx-auto px-4">
          <div className="text-center mb-16">
            <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
              Lo que dicen nuestros clientes
            </h2>
            <p className="mt-4 text-lg text-muted-foreground max-w-2xl mx-auto">
              Negocios que ya automatizaron con Quantix AI
            </p>
          </div>
          <div className="grid gap-8 md:grid-cols-3">
            {TESTIMONIALS.map((t) => (
              <Card key={t.name} className="border-border/50">
                <CardHeader>
                  <div className="flex gap-1 mb-2">
                    {[...Array(5)].map((_, i) => (
                      <Star key={i} className="h-4 w-4 fill-primary text-primary" />
                    ))}
                  </div>
                  <CardDescription className="text-base">{t.quote}</CardDescription>
                  <div className="flex items-center gap-3 pt-4">
                    <Avatar>
                      <AvatarFallback>{t.initials}</AvatarFallback>
                    </Avatar>
                    <div>
                      <p className="font-medium">{t.name}</p>
                      <p className="text-sm text-muted-foreground">
                        {t.role}, {t.company}
                      </p>
                    </div>
                  </div>
                </CardHeader>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section id="faq" className="py-24 bg-muted/30 dark:bg-muted/10">
        <div className="container mx-auto px-4">
          <div className="mx-auto max-w-2xl">
            <div className="text-center mb-16">
              <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
                Preguntas frecuentes
              </h2>
            </div>
            <Accordion type="single" collapsible className="w-full">
              {FAQ_ITEMS.map((item) => (
                <AccordionItem key={item.q} value={item.q}>
                  <AccordionTrigger>{item.q}</AccordionTrigger>
                  <AccordionContent>{item.a}</AccordionContent>
                </AccordionItem>
              ))}
            </Accordion>
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="py-24">
        <div className="container mx-auto px-4">
          <Card className="mx-auto max-w-2xl overflow-hidden border-0 bg-gradient-to-br from-primary to-primary/80 text-primary-foreground">
            <CardHeader className="text-center pb-2">
              <CardTitle className="text-2xl sm:text-3xl">
                ¿Listo para automatizar tu negocio?
              </CardTitle>
              <CardDescription className="text-primary-foreground/90">
                Deja tu email y te contactamos
              </CardDescription>
            </CardHeader>
            <CardContent className="pb-8">
              <form onSubmit={handleCtaSubmit} className="flex gap-2 max-w-md mx-auto">
                <Input
                  type="email"
                  placeholder="tu@email.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="bg-background/90 text-foreground placeholder:text-muted-foreground border-0"
                  required
                />
                <Button type="submit" variant="secondary" size="lg">
                  Comenzar
                </Button>
              </form>
            </CardContent>
          </Card>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-border/40 py-16">
        <div className="container mx-auto px-4">
          <div className="grid gap-12 md:grid-cols-4">
            <div>
              <Link href="/" className="font-semibold text-xl">
                Quantix AI
              </Link>
              <p className="mt-4 text-sm text-muted-foreground">
                Automatiza reservas, cobros y atención al cliente con IA.
              </p>
            </div>
            <div>
              <h4 className="font-medium">Producto</h4>
              <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
                <li>
                  <a href="#features" className="hover:text-foreground transition-colors">
                    Características
                  </a>
                </li>
                <li>
                  <a href="#pricing" className="hover:text-foreground transition-colors">
                    Precios
                  </a>
                </li>
                <li>
                  <a href="#faq" className="hover:text-foreground transition-colors">
                    FAQ
                  </a>
                </li>
              </ul>
            </div>
            <div>
              <h4 className="font-medium">Empresa</h4>
              <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
                <li>
                  <a href="#" className="hover:text-foreground transition-colors">
                    Nosotros
                  </a>
                </li>
                <li>
                  <a href="#" className="hover:text-foreground transition-colors">
                    Blog
                  </a>
                </li>
                <li>
                  <a href="#" className="hover:text-foreground transition-colors">
                    Contacto
                  </a>
                </li>
              </ul>
            </div>
            <div>
              <h4 className="font-medium">Legal</h4>
              <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
                <li>
                  <a href="#" className="hover:text-foreground transition-colors">
                    Términos
                  </a>
                </li>
                <li>
                  <a href="#" className="hover:text-foreground transition-colors">
                    Privacidad
                  </a>
                </li>
              </ul>
            </div>
          </div>
          <div className="mt-12 pt-8 border-t border-border/40 flex flex-col sm:flex-row justify-between items-center gap-4">
            <p className="text-sm text-muted-foreground">
              © {new Date().getFullYear()} Quantix AI. Todos los derechos reservados.
            </p>
            <div className="flex gap-4">
              <a href="#" className="text-muted-foreground hover:text-foreground transition-colors">
                <Globe className="h-5 w-5" />
              </a>
            </div>
          </div>
        </div>
      </footer>
    </div>
  );
}
