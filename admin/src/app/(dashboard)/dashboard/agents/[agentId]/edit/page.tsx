"use client";

import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import Link from "next/link";
import { ChevronLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useAgentDetail, useUpdateAgent } from "@/hooks/use-agents";
import { useToast } from "@/hooks/use-toast";

export default function EditAgentPage() {
  const params = useParams();
  const router = useRouter();
  const { toast } = useToast();
  const agentId = typeof params.agentId === "string" ? params.agentId : "";
  const { data: agent, isLoading, isError, refetch } = useAgentDetail(agentId || null);
  const updateMutation = useUpdateAgent();

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [settingsJson, setSettingsJson] = useState("");
  const [isActive, setIsActive] = useState(true);

  useEffect(() => {
    if (!agent) return;
    setName(agent.name);
    setDescription(agent.description ?? "");
    setSettingsJson(agent.settingsJson ?? "");
    setIsActive(agent.isActive);
  }, [agent]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!agentId) return;
    try {
      await updateMutation.mutateAsync({
        id: agentId,
        body: {
          name: name.trim(),
          description: description.trim() || null,
          settingsJson: settingsJson.trim() || null,
          isActive,
        },
      });
      toast({ title: "Agente actualizado" });
      router.push("/dashboard/agents");
    } catch {
      toast({ title: "Error al guardar", variant: "destructive" });
    }
  };

  if (!agentId) return null;
  if (isLoading) return <PageLoading cards={1} />;
  if (isError || !agent) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="mx-auto max-w-xl space-y-6">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/agents">
            <ChevronLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="text-2xl font-bold tracking-tight">Editar agente</h1>
      </div>

      <form onSubmit={handleSubmit}>
        <Card>
          <CardHeader>
            <CardTitle>{agent.name}</CardTitle>
            <CardDescription>Identificador: {agent.agentId}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="name">Nombre</Label>
              <Input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
            </div>
            <div className="space-y-2">
              <Label htmlFor="description">Descripción</Label>
              <Textarea id="description" value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="settings">Settings JSON</Label>
              <Textarea id="settings" value={settingsJson} onChange={(e) => setSettingsJson(e.target.value)} rows={8} className="font-mono text-xs" />
            </div>
            <div className="flex items-center gap-2">
              <Switch id="active" checked={isActive} onCheckedChange={setIsActive} />
              <Label htmlFor="active">Activo</Label>
            </div>
          </CardContent>
          <CardContent className="flex gap-2 pt-0">
            <Button type="submit" disabled={updateMutation.isPending}>
              Guardar
            </Button>
            <Button type="button" variant="outline" asChild>
              <Link href={`/dashboard/agents/workspace/${agentId}`}>Ir al workspace</Link>
            </Button>
          </CardContent>
        </Card>
      </form>
    </div>
  );
}
