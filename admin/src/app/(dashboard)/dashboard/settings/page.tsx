"use client";

import Link from "next/link";
import { Building2, Bell, User, Settings, Bot } from "lucide-react";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

const SETTINGS_CARDS = [
  {
    title: "Agente IA (motor)",
    description: "Asistente: catálogo PDF, persona, flujo, facts y publicación de SettingsJson",
    href: "/dashboard/agents",
    icon: Bot,
  },
  {
    title: "Configuración del Negocio",
    description: "Horarios, política de reserva, pagos e integraciones (operativo)",
    href: "/dashboard/settings/business",
    icon: Building2,
  },
  {
    title: "Configuración del Sistema",
    description: "Tono, estilo y parámetros globales del sistema",
    href: "/dashboard/settings/system",
    icon: Settings,
  },
  {
    title: "Perfil",
    description: "Tu información personal y contraseña",
    href: "/dashboard/settings/profile",
    icon: User,
  },
  {
    title: "Notificaciones",
    description: "Preferencias de notificaciones por email y alertas",
    href: "/dashboard/settings/notifications",
    icon: Bell,
  },
];

export default function SettingsPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Configuración</h1>
        <p className="text-muted-foreground">
          Gestiona la configuración del negocio, sistema y preferencias
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-2">
        {SETTINGS_CARDS.map((item) => {
          const Icon = item.icon;
          return (
            <Card key={item.href} className="transition-colors hover:bg-muted/50">
              <CardHeader>
                <div className="flex items-center gap-3">
                  <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
                    <Icon className="h-5 w-5 text-primary" />
                  </div>
                  <div>
                    <CardTitle className="text-lg">{item.title}</CardTitle>
                    <CardDescription>{item.description}</CardDescription>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                <Button asChild variant="outline" size="sm">
                  <Link href={item.href}>Configurar</Link>
                </Button>
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
