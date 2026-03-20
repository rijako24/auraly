"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Clock, MessageSquare } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { ScrollArea } from "@/components/ui/scroll-area";
import type {
  Conversation,
  Message,
  ConversationContext,
} from "@/types/entities";
import { formatDateTime } from "@/lib/utils";
import { ChatContainer } from "@/components/chat/chat-container";

// Mock data (página demo; la lista principal usa la API)
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
];

const MOCK_CONVERSATION: Conversation = {
  conversationId: "conv-1",
  businessId: "bus-1",
  userNumber: "+57 300 123 4567",
  lastMessage: "Hola, quisiera agendar una cita para mi bebé",
  timestamp: new Date().toISOString(),
  customerName: "María García",
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
  const id = params.id as string;

  const conversation = MOCK_CONVERSATION;
  const messages = MOCK_MESSAGES;
  const context = MOCK_CONTEXT;

  const handleSendMessage = (text: string) => {
    console.log("Send message:", text, id);
  };

  const displayName = conversation.customerName ?? conversation.userNumber;

  return (
    <div className="flex h-[calc(100vh-5.5rem)] -m-4 flex-col overflow-hidden lg:-m-6 lg:h-[calc(100vh-5.5rem)]">
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
      </div>

      <div className="flex flex-1 min-h-0">
        <div className="flex flex-1 flex-col min-w-0 border-r border-border">
          <ChatContainer
            messages={messages}
            onSendMessage={handleSendMessage}
            placeholder="Escribe un mensaje..."
          />
        </div>

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
                  {conversation.lastMessage && (
                    <div className="flex flex-col gap-1">
                      <span className="text-muted-foreground">Último mensaje (usuario)</span>
                      <span className="font-medium text-xs leading-snug">
                        {conversation.lastMessage}
                      </span>
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
