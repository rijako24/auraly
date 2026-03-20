"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ChevronLeft, ChevronRight } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Progress } from "@/components/ui/progress";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PageLoading } from "@/components/ui/page-loading";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAgentTypes, useCreateAgent } from "@/hooks/use-agents";
import { useToast } from "@/hooks/use-toast";

const steps = ["Básico", "Configuración", "Conocimiento", "Revisión", "Listo"] as const;

export default function NewAgentPage() {
  const router = useRouter();
  const { toast } = useToast();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data: types, isLoading: typesLoading } = useAgentTypes();
  const createMutation = useCreateAgent();

  const [step, setStep] = useState(0);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [agentTypeId, setAgentTypeId] = useState("");
  const [model, setModel] = useState("gpt-4o-mini");
  const [temperature, setTemperature] = useState("0.3");
  const [systemPrompt, setSystemPrompt] = useState("");
  const [knowledgeName, setKnowledgeName] = useState("");
  const [knowledgeContent, setKnowledgeContent] = useState("");

  const progress = ((step + 1) / steps.length) * 100;

  const buildSettingsJson = () =>
    JSON.stringify(
      {
        engine: { model, temperature: Number.parseFloat(temperature) || 0.3 },
      },
      null,
      2
    );

  const handleFinish = async () => {
    if (!businessId || !agentTypeId || !name.trim()) {
      toast({ title: "Faltan datos", description: "Nombre y tipo son obligatorios.", variant: "destructive" });
      return;
    }
    try {
      const agent = await createMutation.mutateAsync({
        businessId,
        agentTypeId,
        name: name.trim(),
        description: description.trim() || null,
        settingsJson: buildSettingsJson(),
        systemPrompt: systemPrompt.trim() || null,
      });
      toast({ title: "Agente creado" });
      if (knowledgeName.trim() && knowledgeContent.trim()) {
        const { agentsApi } = await import("@/services/api/agents");
        await agentsApi.addKnowledge(agent.agentId, {
          name: knowledgeName.trim(),
          type: "Document",
          content: knowledgeContent.trim(),
          autoInject: true,
        });
      }
      router.push(`/dashboard/agents/workspace/${agent.agentId}`);
    } catch {
      toast({ title: "Error al crear", variant: "destructive" });
    }
  };

  if (!businessId) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Selecciona un negocio</CardTitle>
          <CardDescription>El agente se asocia al negocio activo en la barra superior.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (typesLoading) return <PageLoading cards={1} />;

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/agents">
            <ChevronLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Nuevo agente</h1>
          <p className="text-sm text-muted-foreground">Asistente de creación</p>
        </div>
      </div>

      <Progress value={progress} className="h-2" />

      <Card>
        <CardHeader>
          <CardTitle>{steps[step]}</CardTitle>
          <CardDescription>
            Paso {step + 1} de {steps.length}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {step === 0 && (
            <>
              <div className="space-y-2">
                <Label htmlFor="name">Nombre</Label>
                <Input id="name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Ej. Asistente de ventas" />
              </div>
              <div className="space-y-2">
                <Label htmlFor="desc">Descripción</Label>
                <Textarea id="desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
              </div>
              <div className="space-y-2">
                <Label>Tipo de agente</Label>
                <Select value={agentTypeId} onValueChange={setAgentTypeId}>
                  <SelectTrigger>
                    <SelectValue placeholder="Selecciona tipo" />
                  </SelectTrigger>
                  <SelectContent>
                    {(types ?? []).map((t) => (
                      <SelectItem key={t.agentTypeId} value={t.agentTypeId}>
                        {t.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </>
          )}

          {step === 1 && (
            <>
              <div className="space-y-2">
                <Label htmlFor="model">Modelo</Label>
                <Input id="model" value={model} onChange={(e) => setModel(e.target.value)} />
              </div>
              <div className="space-y-2">
                <Label htmlFor="temp">Temperature</Label>
                <Input id="temp" type="number" step="0.1" min={0} max={2} value={temperature} onChange={(e) => setTemperature(e.target.value)} />
              </div>
              <div className="space-y-2">
                <Label htmlFor="sys">System prompt (sección de instrucciones)</Label>
                <Textarea id="sys" value={systemPrompt} onChange={(e) => setSystemPrompt(e.target.value)} rows={6} />
              </div>
              <p className="text-xs text-muted-foreground">
                Modelo y temperature se guardan en <code className="text-xs">SettingsJson</code>.
              </p>
            </>
          )}

          {step === 2 && (
            <>
              <div className="space-y-2">
                <Label htmlFor="kn">Documento de conocimiento (opcional)</Label>
                <Input id="kn" value={knowledgeName} onChange={(e) => setKnowledgeName(e.target.value)} placeholder="Nombre" />
              </div>
              <div className="space-y-2">
                <Label htmlFor="kc">Contenido (texto plano / markdown)</Label>
                <Textarea id="kc" value={knowledgeContent} onChange={(e) => setKnowledgeContent(e.target.value)} rows={8} />
              </div>
            </>
          )}

          {step === 3 && (
            <div className="text-sm space-y-2 rounded-md border p-4 bg-muted/40">
              <p>
                <span className="font-medium">Nombre:</span> {name || "—"}
              </p>
              <p>
                <span className="font-medium">Tipo:</span> {(types ?? []).find((t) => t.agentTypeId === agentTypeId)?.name ?? "—"}
              </p>
              <p>
                <span className="font-medium">Modelo:</span> {model}
              </p>
              <p>
                <span className="font-medium">System prompt:</span> {systemPrompt ? `${systemPrompt.slice(0, 80)}…` : "—"}
              </p>
            </div>
          )}

          {step === 4 && (
            <p className="text-sm text-muted-foreground">Pulsa &quot;Finalizar&quot; para crear el agente y abrir el workspace.</p>
          )}
        </CardContent>
        <CardFooter className="flex justify-between gap-2">
          <Button type="button" variant="outline" disabled={step === 0} onClick={() => setStep((s) => Math.max(0, s - 1))}>
            Atrás
          </Button>
          {step < steps.length - 1 ? (
            <Button type="button" onClick={() => setStep((s) => Math.min(steps.length - 1, s + 1))}>
              Siguiente
              <ChevronRight className="ml-1 h-4 w-4" />
            </Button>
          ) : (
            <Button type="button" onClick={handleFinish} disabled={createMutation.isPending}>
              Finalizar
            </Button>
          )}
        </CardFooter>
      </Card>
    </div>
  );
}
