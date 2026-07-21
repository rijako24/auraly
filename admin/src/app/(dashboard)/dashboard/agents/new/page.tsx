"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Sparkles } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useCreateAgent } from "@/hooks/use-agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { ApiError } from "@/types/api";

export default function NewAgentPage() {
  const router = useRouter();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const createAgent = useCreateAgent();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!name.trim() || !businessId) return;

    try {
      const agent = await createAgent.mutateAsync({
        name: name.trim(),
        description: description.trim() || undefined,
      });
      toast.success("Borrador creado. Completa la configuraci\u00f3n del agente.");
      router.push(`/dashboard/agents/${agent.agentId}/setup`);
    } catch (error) {
      const apiError = error as Partial<ApiError>;
      toast.error(apiError.message ?? "No se pudo crear el agente");
    }
  };

  if (!businessId) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Crear agente</h1>
        <p className="text-muted-foreground">
          Selecciona un negocio antes de crear el agente.
        </p>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="space-y-2">
        <Button variant="ghost" size="sm" className="-ml-2" asChild>
          <Link href="/dashboard/agents">
            <ArrowLeft className="mr-1 h-4 w-4" />
            Agentes
          </Link>
        </Button>
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <Sparkles className="h-6 w-6 text-primary" />
          Crear agente
        </h1>
        <p className="text-muted-foreground">
          Primero crea el borrador. Despu&eacute;s configurar&aacute;s personalidad, pol&iacute;ticas,
          flujos, pagos, notificaciones y escalamiento desde el wizard.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Datos iniciales</CardTitle>
          <CardDescription>
            El agente permanecer&aacute; inactivo hasta que termines y lo publiques.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form className="space-y-5" onSubmit={handleSubmit}>
            <div className="space-y-2">
              <Label htmlFor="agent-name">Nombre del agente</Label>
              <Input
                id="agent-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                maxLength={200}
                placeholder="Ej. Sofia, asistente comercial"
                autoFocus
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="agent-description">Descripci&oacute;n</Label>
              <Textarea
                id="agent-description"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                maxLength={500}
                placeholder="Que debe resolver este agente para el negocio"
                className="min-h-28"
              />
            </div>
            <Button type="submit" disabled={!name.trim() || createAgent.isPending}>
              <Sparkles className="mr-2 h-4 w-4" />
              {createAgent.isPending ? "Creando borrador..." : "Crear y configurar"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
