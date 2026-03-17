"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

const MOCK_PREFERENCES = [
  {
    id: "new_reservation",
    label: "Nueva reservación",
    description: "Recibe notificación cuando se crea una nueva reservación",
    enabled: true,
  },
  {
    id: "payment_received",
    label: "Pago recibido",
    description: "Alerta cuando se confirma un pago",
    enabled: true,
  },
  {
    id: "new_lead",
    label: "Nuevo lead",
    description: "Cuando un potencial cliente inicia conversación",
    enabled: true,
  },
  {
    id: "escalation_alerts",
    label: "Alertas de escalamiento",
    description: "Cuando el asistente necesita pasar a un humano",
    enabled: true,
  },
  {
    id: "daily_summary",
    label: "Resumen diario por email",
    description: "Email con estadísticas del día (reservaciones, leads, pagos)",
    enabled: false,
  },
];

export default function NotificationsSettingsPage() {
  const [prefs, setPrefs] = useState(
    MOCK_PREFERENCES.reduce(
      (acc, p) => ({ ...acc, [p.id]: p.enabled }),
      {} as Record<string, boolean>
    )
  );

  const handleToggle = (id: string) => {
    setPrefs((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    // Mock save
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/settings">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Notificaciones
          </h1>
          <p className="text-muted-foreground">
            Configura cómo y cuándo recibir notificaciones
          </p>
        </div>
      </div>

      <form onSubmit={handleSave}>
        <Card>
          <CardHeader>
            <CardTitle>Preferencias de notificaciones</CardTitle>
            <p className="text-sm text-muted-foreground">
              Activa o desactiva los tipos de notificación que deseas recibir
            </p>
          </CardHeader>
          <CardContent className="space-y-6">
            {MOCK_PREFERENCES.map((item) => (
              <div
                key={item.id}
                className="flex items-center justify-between space-x-4 rounded-lg border p-4"
              >
                <div className="flex-1 space-y-0.5">
                  <Label htmlFor={item.id} className="text-base font-medium">
                    {item.label}
                  </Label>
                  <p className="text-sm text-muted-foreground">
                    {item.description}
                  </p>
                </div>
                <Switch
                  id={item.id}
                  checked={prefs[item.id] ?? false}
                  onCheckedChange={() => handleToggle(item.id)}
                />
              </div>
            ))}

            <Button type="submit">Guardar preferencias</Button>
          </CardContent>
        </Card>
      </form>
    </div>
  );
}
