"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, Pencil, Save, X } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Textarea } from "@/components/ui/textarea";
import { systemConfigurationsApi } from "@/services/api";

export default function SystemSettingsPage() {
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ["system-configurations"],
    queryFn: () => systemConfigurationsApi.listSystemConfigurations({ page: 1, pageSize: 100 }),
  });
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState("");

  const startEdit = (id: string | number, value: string) => {
    setEditingId(String(id));
    setEditValue(value);
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditValue("");
  };

  const saveEdit = async () => {
    if (!editingId) return;
    try {
      await systemConfigurationsApi.updateSystemConfiguration(editingId, { value: editValue });
      await refetch();
      toast.success("Configuracion actualizada");
      cancelEdit();
    } catch {
      toast.error("No se pudo actualizar la configuracion");
    }
  };

  const isJsonValue = (value: string): boolean => {
    try {
      JSON.parse(value);
      return value.startsWith("{") || value.startsWith("[");
    } catch {
      return false;
    }
  };

  if (isLoading) return <PageLoading cards={2} />;
  if (isError) return <PageError onRetry={refetch} />;

  const configs = data?.items ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/settings"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Configuracion del Sistema</h1>
          <p className="text-muted-foreground">Parametros globales retornados por la API</p>
        </div>
      </div>

      <div className="space-y-4">
        {configs.map((config) => {
          const configId = String(config.systemConfigurationId);
          const isEditing = editingId === configId;
          return (
            <Card key={configId}>
              <CardHeader>
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <CardTitle className="text-base">{configId}</CardTitle>
                    {config.description && <p className="mt-1 text-sm text-muted-foreground">{config.description}</p>}
                  </div>
                  {!isEditing ? (
                    <Button variant="outline" size="sm" onClick={() => startEdit(config.systemConfigurationId, config.value)}>
                      <Pencil className="mr-2 h-4 w-4" />Editar
                    </Button>
                  ) : (
                    <div className="flex gap-2">
                      <Button size="sm" onClick={saveEdit}><Save className="mr-2 h-4 w-4" />Guardar</Button>
                      <Button variant="outline" size="sm" onClick={cancelEdit}><X className="mr-2 h-4 w-4" />Cancelar</Button>
                    </div>
                  )}
                </div>
              </CardHeader>
              <CardContent>
                {isEditing ? (
                  isJsonValue(config.value) ? (
                    <Textarea value={editValue} onChange={(e) => setEditValue(e.target.value)} rows={8} className="font-mono text-sm" />
                  ) : (
                    <Input value={editValue} onChange={(e) => setEditValue(e.target.value)} className="max-w-xs" />
                  )
                ) : (
                  <pre className="overflow-auto rounded-md bg-muted/50 p-3 font-mono text-sm">
                    {isJsonValue(config.value) ? JSON.stringify(JSON.parse(config.value), null, 2) : config.value}
                  </pre>
                )}
              </CardContent>
            </Card>
          );
        })}
        {configs.length === 0 && <Card><CardContent className="py-8 text-sm text-muted-foreground">Sin configuraciones de sistema.</CardContent></Card>}
      </div>
    </div>
  );
}

