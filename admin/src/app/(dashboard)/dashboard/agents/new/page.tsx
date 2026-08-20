"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, CalendarDays, CreditCard, MapPinned, ShoppingCart, Sparkles } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useCreateAgent } from "@/hooks/use-agents";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { ApiError } from "@/types/api";
import { AgentBotType } from "@/types/agent-bot-type";

const BOT_TYPE_VISUALS: Record<AgentBotType, { icon: typeof CalendarDays; accent: string }> = {
  [AgentBotType.Reservation]: { icon: CalendarDays, accent: "from-cyan-500/20 via-cyan-500/5 to-transparent" },
  [AgentBotType.Order]: { icon: ShoppingCart, accent: "from-emerald-500/20 via-emerald-500/5 to-transparent" },
  [AgentBotType.Delivery]: { icon: MapPinned, accent: "from-amber-500/20 via-amber-500/5 to-transparent" },
  [AgentBotType.PaymentValidator]: { icon: CreditCard, accent: "from-violet-500/20 via-violet-500/5 to-transparent" },
};


export default function NewAgentPage() {
  const router = useRouter();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const createAgent = useCreateAgent();
  const botTypeCatalog = useReferenceOptions("agent-bot-type");
  const botTypes = (botTypeCatalog.data ?? []).flatMap((option) => {
    const value = Number(option.code) as AgentBotType;
    const visual = BOT_TYPE_VISUALS[value];
    return visual ? [{ value, title: option.label, description: option.description ?? "", ...visual }] : [];
  });
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [botType, setBotType] = useState<AgentBotType | null>(null);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!name.trim() || !businessId || botType === null) return;

    try {
      const agent = await createAgent.mutateAsync({
        name: name.trim(),
        description: description.trim() || undefined,
        botType,
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

  if (botTypeCatalog.isLoading) {
    return <p className="text-sm text-muted-foreground">Cargando tipos de agente...</p>;
  }

  if (botTypeCatalog.isError) {
    return <p role="alert" className="text-sm text-destructive">No fue posible cargar los tipos de agente.</p>;
  }

  if (botType === null) {
    return (
      <div className="mx-auto max-w-6xl space-y-6">
        <div className="space-y-2">
          <Button variant="ghost" size="sm" className="-ml-2" asChild>
            <Link href="/dashboard/agents">
              <ArrowLeft className="mr-1 h-4 w-4" />
              Agentes
            </Link>
          </Button>
          <h1 className="text-2xl font-semibold tracking-tight">Elige el tipo de agente</h1>
          <p className="text-muted-foreground">
            Esta eleccion define las capacidades internas y los pasos del wizard de configuracion.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {botTypes.map((type) => {
            const Icon = type.icon;
            return (
              <button
                key={type.value}
                type="button"
                onClick={() => setBotType(type.value)}
                className="group overflow-hidden rounded-2xl border bg-card text-left shadow-sm transition-all hover:-translate-y-1 hover:border-primary/60 hover:shadow-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
              >
                <div className={`flex aspect-[4/3] items-center justify-center bg-gradient-to-b ${type.accent}`}>
                  <div className="rounded-3xl border border-primary/20 bg-background/80 p-7 shadow-2xl backdrop-blur">
                    <Icon className="h-14 w-14 text-primary transition-transform group-hover:scale-110" />
                  </div>
                </div>
                <div className="space-y-2 border-t p-5">
                  <h2 className="text-lg font-semibold text-primary">{type.title}</h2>
                  <p className="text-sm leading-6 text-muted-foreground">{type.description}</p>
                </div>
              </button>
            );
          })}
        </div>
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
            <div className="flex items-center justify-between rounded-xl border bg-muted/30 p-4">
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Tipo de agente</p>
                <p className="font-semibold">{botTypes.find((type) => type.value === botType)?.title}</p>
              </div>
              <Button type="button" variant="outline" size="sm" onClick={() => setBotType(null)}>Cambiar tipo</Button>
            </div>
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
