"use client";

import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Clock, MessageSquare } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  ConversationStateEnum,
  ConversationStateLabels,
  ConversationStateColors,
} from "@/types/enums";
import type {
  Conversation,
  Message,
  ConversationContext,
} from "@/types/entities";
import { formatDateTime } from "@/lib/utils";
import { ChatContainer } from "@/components/chat/chat-container";
import { cn } from "@/lib/utils";

// Mock data for this conversation
const MOCK_CONTEXT: ConversationContext[] = [
  {
    conversationContextId: "ctx-1",
    conversationId: "conv-1",
    field: "customerName",
    value: "María García",
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
  {
    conversationContextId: "ctx-2",
    conversationId: "conv-1",
    field: "babyName",
    value: "Sofía",
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
  {
    conversationContextId: "ctx-3",
    conversationId: "conv-1",
    field: "babyAge",
    value: "4",
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
  {
    conversationContextId: "ctx-4",
    conversationId: "conv-1",
    field: "preferredDate",
    value: "2025-03-20",
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
  {
    conversationContextId: "ctx-5",
    conversationId: "conv-1",
    field: "service",
    value: "Masaje relajante + hidroterapia",
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
];

const MOCK_MESSAGES: Message[] = [
  {
    messageId: "m1",
    conversationId: "conv-1",
    sender: "Bot",
    messageText:
      "¡Hola! Soy el asistente de Mimos Baby Spa. ¿En qué puedo ayudarte hoy?",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
  },
  {
    messageId: "m2",
    conversationId: "conv-1",
    sender: "User",
    messageText: "Hola, quisiera agendar una cita para mi bebé",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 60000).toISOString(),
  },
  {
    messageId: "m3",
    conversationId: "conv-1",
    sender: "Bot",
    messageText:
      "¡Perfecto! Nos encantaría atender a tu bebé. ¿Cómo se llama tu bebé y cuántos meses tiene?",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 120000).toISOString(),
  },
  {
    messageId: "m4",
    conversationId: "conv-1",
    sender: "User",
    messageText: "Se llama Sofía y tiene 4 meses",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 180000).toISOString(),
  },
  {
    messageId: "m5",
    conversationId: "conv-1",
    sender: "Bot",
    messageText:
      "¡Qué linda! Para Sofía de 4 meses tenemos el paquete Baby Spa que incluye masaje relajante e hidroterapia. ¿Te gustaría reservar para algún día en particular?",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 240000).toISOString(),
  },
  {
    messageId: "m6",
    conversationId: "conv-1",
    sender: "User",
    messageText: "Sí, el próximo viernes 20 de marzo si hay disponibilidad",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 300000).toISOString(),
  },
  {
    messageId: "m7",
    conversationId: "conv-1",
    sender: "Bot",
    messageText:
      "Déjame verificar la disponibilidad para el viernes 20 de marzo. Tenemos horarios a las 10:00, 11:30 y 15:00. ¿Cuál prefieres?",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 360000).toISOString(),
  },
  {
    messageId: "m8",
    conversationId: "conv-1",
    sender: "User",
    messageText: "Las 11:30 me queda bien",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 420000).toISOString(),
  },
  {
    messageId: "m9",
    conversationId: "conv-1",
    sender: "Bot",
    messageText:
      "Excelente. He reservado el viernes 20 de marzo a las 11:30 para Sofía. El servicio tiene un valor de $85.000. ¿Deseas proceder con el pago?",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 480000).toISOString(),
  },
  {
    messageId: "m10",
    conversationId: "conv-1",
    sender: "User",
    messageText: "Sí, por favor",
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000 + 540000).toISOString(),
  },
];

const MOCK_CONVERSATION: Conversation = {
  conversationId: "conv-1",
  businessId: "bus-1",
  userNumber: "+57 300 123 4567",
  lastMessage: "Sí, por favor",
  lastIntent: "ConfirmPayment",
  timestamp: new Date().toISOString(),
  customerName: "María García",
  babyAge: 4,
  recommendedPlan: "Baby Spa",
  state: 5, // WaitingForPayment
  business: {
    businessId: "bus-1",
    tenantId: "t1",
    name: "Mimos Baby Spa",
    description: "Spa infantil",
    address: "Calle 123 #45-67",
    phone: "+57 1 234 5678",
    email: "info@mimosbaby.com",
    website: "https://mimosbaby.com",
    logoUrl: null,
    isActive: true,
    createdAt: new Date().toISOString(),
    updatedAt: null,
  },
};

const CONTEXT_LABELS: Record<string, string> = {
  customerName: "Cliente",
  babyName: "Nombre del bebé",
  babyAge: "Edad del bebé (meses)",
  preferredDate: "Fecha preferida",
  service: "Servicio",
  preferredTime: "Horario preferido",
};

export default function ConversationDetailPage() {
  const params = useParams();
  const router = useRouter();
  const id = params.id as string;

  const conversation = MOCK_CONVERSATION;
  const messages = MOCK_MESSAGES;
  const context = MOCK_CONTEXT;

  const handleSendMessage = (text: string) => {
    console.log("Send message:", text);
    // Mock: would send via API
  };

  const displayName = conversation.customerName ?? conversation.userNumber;

  return (
    <div className="flex h-[calc(100vh-5.5rem)] -m-4 flex-col overflow-hidden lg:-m-6 lg:h-[calc(100vh-5.5rem)]">
      {/* Header */}
      <div className="flex shrink-0 items-center gap-4 border-b border-border bg-background px-4 py-3">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/conversations">
            <ArrowLeft className="h-5 w-5" />
          </Link>
        </Button>
        <div className="flex-1 min-w-0">
          <h1 className="truncate font-semibold text-lg">{displayName}</h1>
          <p className="text-sm text-muted-foreground">
            {conversation.userNumber}
          </p>
        </div>
        <Badge
          variant="secondary"
          className={cn(
            "shrink-0",
            ConversationStateColors[conversation.state as ConversationStateEnum]
          )}
        >
          {ConversationStateLabels[conversation.state as ConversationStateEnum]}
        </Badge>
      </div>

      <div className="flex flex-1 min-h-0">
        {/* Chat area */}
        <div className="flex flex-1 flex-col min-w-0 border-r border-border">
          <ChatContainer
            messages={messages}
            onSendMessage={handleSendMessage}
            placeholder="Escribe un mensaje..."
          />
        </div>

        {/* Side panel - conversation context */}
        <aside className="hidden w-80 flex-shrink-0 border-l border-border bg-muted/30 lg:block">
          <ScrollArea className="h-full">
            <div className="p-4 space-y-6">
              <div>
                <h3 className="font-semibold text-sm text-foreground mb-2 flex items-center gap-2">
                  <MessageSquare className="h-4 w-4" />
                  Información de la conversación
                </h3>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Estado</span>
                    <Badge
                      variant="secondary"
                      className={cn(
                        "text-xs",
                        ConversationStateColors[conversation.state as ConversationStateEnum]
                      )}
                    >
                      {ConversationStateLabels[conversation.state as ConversationStateEnum]}
                    </Badge>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Negocio</span>
                    <span className="font-medium truncate max-w-[140px]">
                      {conversation.business?.name ?? "—"}
                    </span>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Última actividad</span>
                    <span className="flex items-center gap-1">
                      <Clock className="h-3.5 w-3.5" />
                      {formatDateTime(conversation.timestamp)}
                    </span>
                  </div>
                  {conversation.recommendedPlan && (
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">Plan recomendado</span>
                      <span className="font-medium">{conversation.recommendedPlan}</span>
                    </div>
                  )}
                </div>
              </div>

              <Separator />

              <div>
                <h3 className="font-semibold text-sm text-foreground mb-3">
                  Contexto extraído
                </h3>
                <div className="space-y-2">
                  {context.map((ctx) => (
                    <div
                      key={ctx.conversationContextId}
                      className="rounded-lg border border-border bg-background p-3"
                    >
                      <p className="text-xs text-muted-foreground font-medium">
                        {CONTEXT_LABELS[ctx.field] ?? ctx.field}
                      </p>
                      <p className="text-sm font-medium mt-0.5">{ctx.value}</p>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </ScrollArea>
        </aside>
      </div>
    </div>
  );
}
