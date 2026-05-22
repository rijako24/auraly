"use client";

import { useState, useMemo } from "react";
import Link from "next/link";
import { ArrowLeft, Pencil, Save, X } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { JsonEditor } from "@/components/forms/json-editor";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import {
  BusinessConfigurationKey,
  BusinessConfigurationKeyLabels,
} from "@/types/enums";
import { truncate } from "@/lib/utils";
import { configurationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";

const CONFIG_KEYS: {
  key: BusinessConfigurationKey;
  description: string;
  useJsonEditor: boolean;
}[] = [
  {
    key: BusinessConfigurationKey.Integrations,
    description: "Credenciales y configuración de servicios externos (Google Calendar, Wompi, etc.)",
    useJsonEditor: true,
  },
  {
    key: BusinessConfigurationKey.PaymentConfirmationMessages,
    description: "Mensajes enviados al cliente cuando se confirma el pago. Soporta texto y adjuntos.",
    useJsonEditor: true,
  },
  {
    key: BusinessConfigurationKey.SchedulingPolicy,
    description:
      "Horarios de operación por día (schedule), intervalo entre slots, buffer entre citas y reglas de empleado.",
    useJsonEditor: true,
  },
  {
    key: BusinessConfigurationKey.BookingPolicy,
    description:
      "Política de reserva: si requiere anticipo, porcentaje del total y moneda para links de pago.",
    useJsonEditor: true,
  },
];

export default function BusinessSettingsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const queryClient = useQueryClient();
  const [editingKey, setEditingKey] = useState<BusinessConfigurationKey | null>(null);
  const [editValue, setEditValue] = useState<string | Record<string, unknown>>("");

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["business-configurations", businessId],
    queryFn: () => configurationsApi.getBusinessConfigurations(businessId!),
    enabled: !!businessId,
  });

  const updateMutation = useMutation({
    mutationFn: (payload: { configurations: Record<string, string> }) =>
      configurationsApi.updateBusinessConfigurations(businessId!, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["business-configurations", businessId] });
      setEditingKey(null);
      setEditValue("");
    },
  });

  const configs = useMemo(() => {
    const configsMap = data?.configurations ?? {};
    return CONFIG_KEYS.map(({ key, description, useJsonEditor }) => ({
      key,
      keyLabel: BusinessConfigurationKeyLabels[key],
      description,
      useJsonEditor,
      value: configsMap[BusinessConfigurationKey[key]] ?? "",
    }));
  }, [data]);

  const startEdit = (key: BusinessConfigurationKey, value: string, useJsonEditor: boolean) => {
    setEditingKey(key);
    if (useJsonEditor) {
      try {
        setEditValue(JSON.parse(value || "{}") as Record<string, unknown>);
      } catch {
        setEditValue({ raw: value });
      }
    } else {
      setEditValue(value);
    }
  };

  const cancelEdit = () => {
    setEditingKey(null);
    setEditValue("");
  };

  const saveEdit = () => {
    if (editingKey == null) return;
    const newValue =
      typeof editValue === "object"
        ? JSON.stringify(editValue)
        : String(editValue);
    const current = data?.configurations ?? {};
    updateMutation.mutate({
      configurations: { ...current, [BusinessConfigurationKey[editingKey]]: newValue },
    });
  };

  const getValuePreview = (value: string, isJson: boolean): string => {
    if (isJson) {
      try {
        const parsed = JSON.parse(value);
        return truncate(JSON.stringify(parsed), 60);
      } catch {
        return truncate(value, 60);
      }
    }
    return truncate(value, 80);
  };

  if (!businessId) {
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
              Configuración del Negocio
            </h1>
            <p className="text-muted-foreground">
              Selecciona un negocio en el selector superior para editar su configuración
            </p>
          </div>
        </div>
      </div>
    );
  }

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={() => refetch()} />;

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
            Configuración del Negocio
          </h1>
          <p className="text-muted-foreground">
            Integraciones externas y plantillas de mensajes de pago
          </p>
        </div>
      </div>

      <div className="space-y-4">
        {configs.map((cfg) => {
          const isEditing = editingKey === cfg.key;

          return (
            <Card key={cfg.key}>
              <CardHeader>
                <div className="flex items-start justify-between">
                  <div>
                    <CardTitle className="text-base">{cfg.keyLabel}</CardTitle>
                    <CardDescription className="mt-1">
                      {cfg.description}
                    </CardDescription>
                  </div>
                  {!isEditing ? (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => startEdit(cfg.key, cfg.value, cfg.useJsonEditor)}
                    >
                      <Pencil className="mr-2 h-4 w-4" />
                      Editar
                    </Button>
                  ) : (
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        onClick={saveEdit}
                        disabled={updateMutation.isPending}
                      >
                        <Save className="mr-2 h-4 w-4" />
                        Guardar
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={cancelEdit}
                        disabled={updateMutation.isPending}
                      >
                        <X className="mr-2 h-4 w-4" />
                        Cancelar
                      </Button>
                    </div>
                  )}
                </div>
              </CardHeader>
              <CardContent>
                {isEditing ? (
                  cfg.useJsonEditor ? (
                    <JsonEditor
                      value={
                        typeof editValue === "object" ? editValue : { value: editValue }
                      }
                      onChange={(v) => setEditValue(v)}
                    />
                  ) : (
                    <Textarea
                      value={typeof editValue === "string" ? editValue : ""}
                      onChange={(e) => setEditValue(e.target.value)}
                      rows={6}
                      className="font-mono text-sm"
                    />
                  )
                ) : (
                  <p className="rounded-md bg-muted/50 p-3 font-mono text-sm">
                    {cfg.value
                      ? getValuePreview(cfg.value, cfg.useJsonEditor)
                      : "Sin configurar"}
                  </p>
                )}
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
