"use client";

import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { MessageCircle, RotateCcw, Send, UserRound } from "lucide-react";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

import { agentsApi, type AgentTestChatMessage } from "@/services/api/agents";
import type { ApiError } from "@/types/api";
import type { Agent } from "@/types/entities";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Textarea } from "@/components/ui/textarea";

interface AgentTestChatProps {
  agent: Agent;
  hasUnsavedChanges?: boolean;
  className?: string;
  compact?: boolean;
}

type AgentTestEvent = {
  type: string;
  source: string;
  payload?: unknown;
  timestampUtc: string;
};

export function AgentTestChat({
  agent,
  hasUnsavedChanges = false,
  className,
  compact = false,
}: AgentTestChatProps) {
  const [customerName, setCustomerName] = useState("Cliente de prueba");
  const [customerPhone, setCustomerPhone] = useState("+573001112233");
  const [input, setInput] = useState("");
  const [facts, setFacts] = useState<Record<string, string>>({});
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  const [events, setEvents] = useState<AgentTestEvent[]>([]);
  const [messages, setMessages] = useState<AgentTestChatMessage[]>([
    {
      role: "assistant",
      content: `Hola, soy ${agent.name}. Escribe un mensaje para probar el flujo.`,
    },
  ]);

  const visibleHistory = useMemo(
    () => messages.filter((m) => !m.content.startsWith("Hola, soy ")),
    [messages]
  );

  const mutation = useMutation({
    mutationFn: (message: string) =>
      agentsApi.testTurn(agent.agentId, {
        message,
        customerName,
        customerPhone,
        facts,
        history: visibleHistory,
      }),
    onSuccess: (data) => {
      if (!data.success) {
        toast.error(data.errorMessage ?? "No se pudo procesar el turno");
        return;
      }

      const nextMessages: AgentTestChatMessage[] = [];
      if (data.response?.trim()) {
        nextMessages.push({ role: "assistant", content: data.response.trim() });
      }

      data.outboundMessages
        ?.filter((m) => m.body?.trim() || m.mediaUrl?.trim())
        .forEach((m) => {
          nextMessages.push({
            role: "assistant",
            content: [m.body, m.mediaUrl].filter(Boolean).join("\n"),
          });
        });

      setMessages((current) => [
        ...current,
        ...(nextMessages.length > 0
          ? nextMessages
          : [
              {
                role: "assistant" as const,
                content: "El agente no devolvio texto en este turno.",
              },
            ]),
      ]);
      setEvents(data.events ?? []);
      setFacts(data.facts ?? {});
      inputRef.current?.focus();
    },
    onError: (error) => {
      const apiError = error as Partial<ApiError>;
      toast.error(apiError.message ?? "No se pudo probar el agente");
      setMessages((current) => current.slice(0, -1));
    },
  });

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [messages, mutation.isPending]);

  const sendMessage = () => {
    const text = input.trim();
    if (!text || mutation.isPending) return;

    setInput("");
    setEvents([]);
    setMessages((current) => [...current, { role: "user", content: text }]);
    mutation.mutate(text);
    window.requestAnimationFrame(() => inputRef.current?.focus());
  };

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    sendMessage();
  };

  const handleInputKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key !== "Enter" || event.shiftKey) return;

    event.preventDefault();
    sendMessage();
  };

  const handleReset = () => {
    setMessages([
      {
        role: "assistant",
        content: `Hola, soy ${agent.name}. Escribe un mensaje para probar el flujo.`,
      },
    ]);
    setEvents([]);
    setFacts({});
  };

  return (
    <Card className={cn("overflow-hidden", className)}>
      <CardHeader className={cn("space-y-3", compact && "p-4")}>
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <MessageCircle className="h-4 w-4 text-primary" />
            Probar agente
          </CardTitle>
          <Badge variant="secondary">No guarda mensajes</Badge>
        </div>
        {hasUnsavedChanges && (
          <p className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-900/60 dark:bg-amber-950/30 dark:text-amber-200">
            Hay cambios pendientes. Guarda para probar esa configuracion.
          </p>
        )}
        <div className="grid gap-2 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor={`test-name-${agent.agentId}`}>Nombre</Label>
            <Input
              id={`test-name-${agent.agentId}`}
              value={customerName}
              onChange={(event) => setCustomerName(event.target.value)}
              placeholder="Cliente de prueba"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor={`test-phone-${agent.agentId}`}>Telefono</Label>
            <Input
              id={`test-phone-${agent.agentId}`}
              value={customerPhone}
              onChange={(event) => setCustomerPhone(event.target.value)}
              placeholder="+573001112233"
            />
          </div>
        </div>
      </CardHeader>
      <CardContent className={cn("space-y-3", compact && "p-4 pt-0")}>
        <ScrollArea className="h-[340px] rounded-md border bg-muted/20 p-3">
          <div className="space-y-3 pr-3">
            {messages.map((message, index) => (
              <div
                key={`${message.role}-${index}`}
                className={cn(
                  "flex",
                  message.role === "user" ? "justify-end" : "justify-start"
                )}
              >
                <div
                  className={cn(
                    "max-w-[85%] whitespace-pre-wrap rounded-md px-3 py-2 text-sm leading-relaxed",
                    message.role === "user"
                      ? "bg-primary text-primary-foreground"
                      : "border bg-background"
                  )}
                >
                  {message.content}
                </div>
              </div>
            ))}
            {mutation.isPending && (
              <div className="flex justify-start">
                <div className="rounded-md border bg-background px-3 py-2 text-sm text-muted-foreground">
                  Pensando...
                </div>
              </div>
            )}
            <div ref={bottomRef} />
          </div>
        </ScrollArea>

        <form className="space-y-2" onSubmit={handleSubmit}>
          <Textarea
            ref={inputRef}
            value={input}
            onChange={(event) => setInput(event.target.value)}
            onKeyDown={handleInputKeyDown}
            placeholder="Escribe como cliente para validar el flujo"
            className={cn(
              "min-h-[72px] resize-none",
              mutation.isPending && "cursor-wait opacity-70"
            )}
            readOnly={mutation.isPending}
          />
          <div className="flex items-center justify-between gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={handleReset}>
              <RotateCcw className="mr-1 h-4 w-4" />
              Reiniciar
            </Button>
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <UserRound className="h-3.5 w-3.5" />
              {visibleHistory.length} mensajes de contexto
            </div>
            <Button type="submit" disabled={!input.trim() || mutation.isPending}>
              <Send className="mr-1 h-4 w-4" />
              Enviar
            </Button>
          </div>
        </form>
        {events.length > 0 && (
          <div className="rounded-md border bg-muted/20 p-3">
            <div className="mb-2 text-xs font-medium text-muted-foreground">
              Eventos simulados
            </div>
            <div className="space-y-1">
              {events.map((event, index) => (
                <div
                  key={`${event.type}-${index}`}
                  className="flex items-center justify-between gap-2 text-xs"
                >
                  <span className="truncate">
                    {formatTestEvent(event)}
                  </span>
                  <Badge variant="outline">{isFactEvent(event) ? "extractor" : "operation"}</Badge>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function formatTestEvent(event: AgentTestEvent) {
  const fact = getFactPayload(event);
  if (fact?.key) {
    return `Guardado: ${fact.key} = ${fact.value ?? ""}`;
  }

  return `${event.source} - ${event.type}`;
}

function isFactEvent(event: AgentTestEvent) {
  return event.type === "fact_set";
}

function getFactPayload(event: AgentTestEvent) {
  if (!isFactEvent(event)) return null;

  const payload = toRecord(event.payload);
  if (!payload) return null;

  if (event.type === "fact_set") {
    return {
      key: readString(payload, "key"),
      value: readString(payload, "value"),
    };
  }

  const result = parseMaybeJson(readValue(payload, "result"));
  const resultData = toRecord(readValue(toRecord(result), "data"));
  const args = toRecord(readValue(payload, "arguments"));

  return {
    key: readString(resultData, "key") ?? readString(args, "key"),
    value: readString(resultData, "value") ?? readString(args, "value"),
  };
}

function readString(record: Record<string, unknown> | null, key: string) {
  const value = readValue(record, key);
  if (value === undefined || value === null) return undefined;
  return typeof value === "string" ? value : String(value);
}

function readValue(record: Record<string, unknown> | null, key: string) {
  if (!record) return undefined;

  const exact = record[key];
  if (exact !== undefined) return exact;

  const foundKey = Object.keys(record).find(
    (candidate) => candidate.toLowerCase() === key.toLowerCase()
  );
  return foundKey ? record[foundKey] : undefined;
}

function parseMaybeJson(value: unknown) {
  if (typeof value !== "string") return value;

  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}

function toRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}
