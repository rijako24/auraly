"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, CalendarClock, Copy, Link2, Plug, Save } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { AvailabilityBlocksEditor } from "@/components/settings/availability-blocks-editor";
import { WorkingHoursEditor } from "@/components/settings/working-hours-editor";
import { useBusinessWorkingHours, useUpdateBusinessWorkingHours } from "@/hooks/use-working-hours";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { WorkingHour } from "@/types/entities";

export default function BusinessSettingsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const tenantKey = useAuthStore((state) => state.user?.tenantKey);
  const [loginUrl, setLoginUrl] = useState("");
  const { data, isLoading, isError, refetch } = useBusinessWorkingHours();
  const updateHours = useUpdateBusinessWorkingHours();
  const [workingHours, setWorkingHours] = useState<WorkingHour[]>([]);

  useEffect(() => {
    if (data) setWorkingHours(data);
  }, [data]);

  useEffect(() => {
    if (tenantKey)
      setLoginUrl(
        window.location.origin + "/login?tenant=" + encodeURIComponent(tenantKey),
      );
  }, [tenantKey]);

  if (!businessId) {
    return (
      <div className="space-y-6">
        <Header />
        <p className="text-sm text-muted-foreground">
          Selecciona un negocio en el selector superior.
        </p>
      </div>
    );
  }

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="space-y-6">
      <Header />

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Link2 className="h-4 w-4" />
            Enlace de acceso de la empresa
          </CardTitle>
          <CardDescription>
            La clave {tenantKey} es permanente. Comparte este enlace para que el login no vuelva a pedir la empresa.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex gap-2">
          <code className="min-w-0 flex-1 truncate rounded-md bg-muted px-3 py-2 text-xs">{loginUrl}</code>
          <Button
            type="button"
            variant="outline"
            size="icon"
            disabled={!loginUrl}
            onClick={() => navigator.clipboard.writeText(loginUrl)}
            aria-label="Copiar enlace de acceso"
          ><Copy className="h-4 w-4" /></Button>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[1fr_320px]">
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <CalendarClock className="h-4 w-4" />
                  Horario del negocio
                </CardTitle>
                <CardDescription>
                  Empleados sin horario propio usan estos bloques como fallback.
                </CardDescription>
              </div>
              <Button
                onClick={() => updateHours.mutate(workingHours)}
                disabled={updateHours.isPending}
              >
                <Save className="mr-2 h-4 w-4" />
                Guardar
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <WorkingHoursEditor value={workingHours} onChange={setWorkingHours} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Plug className="h-4 w-4" />
              Integraciones
            </CardTitle>
            <CardDescription>
              Calendar y Wompi se configuran en una vista dedicada.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild variant="outline">
              <Link href="/dashboard/settings/integrations">Abrir integraciones</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <CalendarClock className="h-4 w-4" />
            Bloqueos de disponibilidad
          </CardTitle>
          <CardDescription>
            Cierres o bloqueos puntuales para todo el negocio o un empleado. Se muestran después del horario habitual.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <AvailabilityBlocksEditor />
        </CardContent>
      </Card>
    </div>
  );
}

function Header() {
  return (
    <div className="flex items-center gap-4">
      <Button variant="ghost" size="icon" asChild>
        <Link href="/dashboard/settings">
          <ArrowLeft className="h-4 w-4" />
        </Link>
      </Button>
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">
          Configuracion del negocio
        </h1>
        <p className="text-muted-foreground">
          Horarios operativos y parametros del negocio seleccionado
        </p>
      </div>
    </div>
  );
}
