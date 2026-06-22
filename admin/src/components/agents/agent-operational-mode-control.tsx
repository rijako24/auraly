"use client";

import { FlaskConical, Rocket } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  useIntegrationSettings,
  useUpdateOperationalMode,
} from "@/hooks/use-integrations";
import { cn } from "@/lib/utils";
import type { OperationalMode } from "@/services/api/integrations";

interface AgentOperationalModeControlProps {
  businessId?: string | null;
  compact?: boolean;
  className?: string;
}

export function AgentOperationalModeControl({
  businessId,
  compact = false,
  className,
}: AgentOperationalModeControlProps) {
  const { data, isLoading } = useIntegrationSettings(businessId);
  const updateMode = useUpdateOperationalMode(businessId);
  const mode = data?.wompi.mode ?? "test";
  const canUpdate = Boolean(businessId) && Boolean(data);

  const setMode = async (nextMode: OperationalMode) => {
    try {
      await updateMode.mutateAsync({ mode: nextMode });
      toast.success(
        nextMode === "production"
          ? "Agente publicado en modo produccion"
          : "Agente en modo pruebas"
      );
    } catch {
      toast.error("No se pudo cambiar el modo del agente");
    }
  };

  return (
    <div
      className={cn(
        "flex flex-wrap items-center justify-between gap-3 rounded-md border bg-background p-3",
        compact && "p-2",
        className
      )}
    >
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-medium">Modo del agente</span>
          <Badge variant={mode === "production" ? "default" : "secondary"}>
            {mode === "production" ? "Publicado" : "Pruebas"}
          </Badge>
        </div>
        {!compact && (
          <p className="text-xs text-muted-foreground">
            Hoy sincroniza Wompi; las siguientes integraciones usaran este mismo modo.
          </p>
        )}
      </div>
      <div className="flex items-center gap-2">
        <Button
          type="button"
          size="sm"
          variant={mode === "test" ? "secondary" : "outline"}
          disabled={!canUpdate || isLoading || updateMode.isPending || mode === "test"}
          onClick={() => setMode("test")}
        >
          <FlaskConical className="mr-1 h-4 w-4" />
          Pruebas
        </Button>
        <Button
          type="button"
          size="sm"
          variant={mode === "production" ? "secondary" : "default"}
          disabled={!canUpdate || isLoading || updateMode.isPending || mode === "production"}
          onClick={() => setMode("production")}
        >
          <Rocket className="mr-1 h-4 w-4" />
          Publicado
        </Button>
      </div>
    </div>
  );
}
