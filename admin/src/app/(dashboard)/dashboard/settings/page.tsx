"use client";

import Link from "next/link";
import { Building2, Plug, User } from "lucide-react";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

const SETTINGS_CARDS = [
  {
    title: "Configuración del negocio",
    description: "Horarios, disponibilidad y reglas operativas del negocio seleccionado",
    href: "/dashboard/settings/business",
    icon: Building2,
  },
  {
    title: "Integraciones",
    description: "Google Calendar, pagos y conexiones externas del negocio seleccionado",
    href: "/dashboard/settings/integrations",
    icon: Plug,
  },
  {
    title: "Perfil",
    description: "Tu información personal y contraseña",
    href: "/dashboard/settings/profile",
    icon: User,
  },
];

export default function SettingsPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Configuración</h1>
        <p className="text-muted-foreground">Administra el negocio seleccionado, sus integraciones y tu perfil.</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        {SETTINGS_CARDS.map((item) => {
          const Icon = item.icon;
          return <Card key={item.href} className="transition-colors hover:bg-muted/50"><CardHeader><div className="flex items-center gap-3"><div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10"><Icon className="h-5 w-5 text-primary" /></div><div><CardTitle className="text-lg">{item.title}</CardTitle><CardDescription>{item.description}</CardDescription></div></div></CardHeader><CardContent><Button asChild variant="outline" size="sm"><Link href={item.href}>Configurar</Link></Button></CardContent></Card>;
        })}
      </div>
    </div>
  );
}