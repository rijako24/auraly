"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft, Pencil, Save, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { Input } from "@/components/ui/input";
import {
  SystemConfigurationKey,
  SystemConfigurationKeyLabels,
} from "@/types/enums";
import { formatDate } from "@/lib/utils";

interface MockSystemConfig {
  systemConfigurationId: string;
  key: number;
  value: string;
  description: string | null;
  createdAt: string;
  updatedAt: string | null;
  isActive: boolean;
  keyLabel: string;
}

const MOCK_SYSTEM_CONFIGS: MockSystemConfig[] = [
  {
    systemConfigurationId: "1",
    key: 1,
    value: JSON.stringify({
      tone: "friendly",
      style: "conversational",
      formality: "medium",
      language: "es-CO",
    }),
    description: "Tono y estilo por defecto del asistente de IA",
    createdAt: "2025-01-01T00:00:00Z",
    updatedAt: "2025-03-01T10:00:00Z",
    isActive: true,
    keyLabel: SystemConfigurationKeyLabels[SystemConfigurationKey.ToneAndStyle],
  },
  {
    systemConfigurationId: "2",
    key: 2,
    value: "3",
    description: "Número de errores consecutivos antes de escalar a humano (1-10)",
    createdAt: "2025-01-01T00:00:00Z",
    updatedAt: null,
    isActive: true,
    keyLabel:
      SystemConfigurationKeyLabels[
        SystemConfigurationKey.HumanEscalationErrorThreshold
      ],
  },
];

export default function SystemSettingsPage() {
  const [configs, setConfigs] = useState(MOCK_SYSTEM_CONFIGS);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState("");

  const startEdit = (cfg: MockSystemConfig) => {
    setEditingId(cfg.systemConfigurationId);
    setEditValue(cfg.value);
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditValue("");
  };

  const saveEdit = () => {
    if (!editingId) return;
    setConfigs((prev) =>
      prev.map((c) =>
        c.systemConfigurationId === editingId
          ? { ...c, value: editValue, updatedAt: new Date().toISOString() }
          : c
      )
    );
    setEditingId(null);
    setEditValue("");
  };

  const isJsonValue = (val: string): boolean => {
    try {
      JSON.parse(val);
      return val.startsWith("{") || val.startsWith("[");
    } catch {
      return false;
    }
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
            Configuración del Sistema
          </h1>
          <p className="text-muted-foreground">
            Parámetros globales del sistema y comportamiento del asistente
          </p>
        </div>
      </div>

      <div className="space-y-4">
        {configs.map((cfg) => {
          const isEditing = editingId === cfg.systemConfigurationId;

          return (
            <Card key={cfg.systemConfigurationId}>
              <CardHeader>
                <div className="flex items-start justify-between">
                  <div>
                    <CardTitle className="text-base">{cfg.keyLabel}</CardTitle>
                    {cfg.description && (
                      <p className="mt-1 text-sm text-muted-foreground">
                        {cfg.description}
                      </p>
                    )}
                  </div>
                  {!isEditing ? (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => startEdit(cfg)}
                    >
                      <Pencil className="mr-2 h-4 w-4" />
                      Editar
                    </Button>
                  ) : (
                    <div className="flex gap-2">
                      <Button size="sm" onClick={saveEdit}>
                        <Save className="mr-2 h-4 w-4" />
                        Guardar
                      </Button>
                      <Button variant="outline" size="sm" onClick={cancelEdit}>
                        <X className="mr-2 h-4 w-4" />
                        Cancelar
                      </Button>
                    </div>
                  )}
                </div>
              </CardHeader>
              <CardContent>
                {isEditing ? (
                  isJsonValue(cfg.value) ? (
                    <Textarea
                      value={editValue}
                      onChange={(e) => setEditValue(e.target.value)}
                      rows={8}
                      className="font-mono text-sm"
                    />
                  ) : (
                    <Input
                      value={editValue}
                      onChange={(e) => setEditValue(e.target.value)}
                      className="max-w-xs"
                    />
                  )
                ) : (
                  <div className="space-y-2">
                    <pre className="overflow-auto rounded-md bg-muted/50 p-3 font-mono text-sm">
                      {isJsonValue(cfg.value)
                        ? JSON.stringify(
                            JSON.parse(cfg.value) as Record<string, unknown>,
                            null,
                            2
                          )
                        : cfg.value}
                    </pre>
                    <p className="text-xs text-muted-foreground">
                      Actualizado: {formatDate(cfg.updatedAt ?? cfg.createdAt)}
                    </p>
                  </div>
                )}
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
