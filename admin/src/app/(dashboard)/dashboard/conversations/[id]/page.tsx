"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Clock, MessageSquare } from "lucide-react";
import { toast } from "sonner";

import { ChatContainer } from "@/components/chat/chat-container";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Separator } from "@/components/ui/separator";
import {
  useConversationWithMessages,
  useSendWebConversationMessage,
  useUpdateConversationOwner,
} from "@/hooks/use-conversations";
import { cn, formatDateTime } from "@/lib/utils";
import {
  getConversationStageColor,
  getConversationStageLabel,
  getConversationStageStyle,
} from "@/types/enums";

function getErrorMessage(error: unknown) {
  if (error && typeof error === "object" && "message" in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === "string" && message.trim()) return message;
  }

  return "No se pudo enviar el mensaje.";
}

export default function ConversationDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: conversation, isLoading, isError, refetch } =
    useConversationWithMessages(id);
  const sendWebMessage = useSendWebConversationMessage();
  const updateOwner = useUpdateConversationOwner();

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !conversation) return <PageError onRetry={refetch} />;

  const messages = conversation.messages ?? [];
  const displayName = conversation.customerName ?? conversation.userNumber;
  const stageLabel = getConversationStageLabel(conversation.currentStageName);
  const stageColor = getConversationStageColor();
  const stageStyle = getConversationStageStyle(conversation.currentStageName);
  const botEnabled = conversation.botEnabled ?? conversation.owner !== "Human";

  const handleSendMessage = async (message: string) => {
    try {
      await sendWebMessage.mutateAsync({ conversationId: id, message });
      toast.success("Mensaje enviado");
    } catch (error) {
      toast.error(getErrorMessage(error));
      throw error;
    }
  };

  const handleBotEnabledChange = async (checked: boolean) => {
    const owner = checked ? "Bot" : "Human";
    try {
      await updateOwner.mutateAsync({ conversationId: id, owner });
      toast.success(checked ? "Bot activado" : "Bot desactivado");
    } catch (error) {
      toast.error("No se pudo actualizar el bot.");
    }
  };

  return (
    <div className="-m-4 flex h-[calc(100dvh-5.5rem)] flex-col overflow-hidden lg:-m-6 lg:h-[calc(100dvh-5.5rem)]">
      <div className="flex shrink-0 items-center gap-4 border-b border-border bg-background px-4 py-3">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/conversations">
            <ArrowLeft className="h-5 w-5" />
          </Link>
        </Button>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-lg font-semibold">{displayName}</h1>
          <p className="text-sm text-muted-foreground">
            {conversation.userNumber}
          </p>
        </div>
        <Badge
          variant="secondary"
          style={stageStyle}
          className={cn("shrink-0 font-medium shadow-sm", stageColor)}
        >
          {stageLabel}
        </Badge>
        <div className="flex shrink-0 items-center gap-2">
          <span className="hidden text-xs font-medium text-muted-foreground sm:inline">
            {botEnabled ? "Bot activo" : "Humano"}
          </span>
          <Switch
            checked={botEnabled}
            disabled={updateOwner.isPending}
            onCheckedChange={handleBotEnabledChange}
            aria-label={botEnabled ? "Desactivar bot" : "Activar bot"}
          />
        </div>
      </div>

      <div className="flex min-h-0 flex-1">
        <div className="flex min-w-0 flex-1 flex-col border-r border-border">
          <ChatContainer
            messages={messages}
            onSendMessage={handleSendMessage}
            placeholder="Escribe un mensaje..."
            disabled={sendWebMessage.isPending}
          />
        </div>

        <aside className="hidden w-80 flex-shrink-0 border-l border-border bg-muted/30 lg:block">
          <ScrollArea className="h-full">
            <div className="space-y-6 p-4">
              <div>
                <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold text-foreground">
                  <MessageSquare className="h-4 w-4" />
                  Informacion de la conversacion
                </h3>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Etapa</span>
                    <Badge
                      variant="secondary"
                      style={stageStyle}
                      className={cn("text-xs font-medium shadow-sm", stageColor)}
                    >
                      {stageLabel}
                    </Badge>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Bot</span>
                    <span className="font-medium">
                      {botEnabled ? "Activo" : "Humano"}
                    </span>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Telefono</span>
                    <span className="font-medium">{conversation.userNumber}</span>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-muted-foreground">Ultima actividad</span>
                    <span className="flex items-center gap-1">
                      <Clock className="h-3.5 w-3.5" />
                      {formatDateTime(
                        conversation.lastActivityAt ?? conversation.timestamp
                      )}
                    </span>
                  </div>
                  {conversation.recommendedPlan && (
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">Plan recomendado</span>
                      <span className="font-medium">
                        {conversation.recommendedPlan}
                      </span>
                    </div>
                  )}
                </div>
              </div>

              <Separator />

              <div>
                <h3 className="mb-3 text-sm font-semibold text-foreground">
                  Datos capturados
                </h3>
                <div className="space-y-2">
                  <div className="rounded-lg border border-border bg-background p-3">
                    <p className="text-xs font-medium text-muted-foreground">
                      Cliente
                    </p>
                    <p className="mt-0.5 text-sm font-medium">
                      {conversation.customerName ?? "Sin nombre"}
                    </p>
                  </div>
                  {conversation.customerEmail && (
                    <div className="rounded-lg border border-border bg-background p-3">
                      <p className="text-xs font-medium text-muted-foreground">
                        Email
                      </p>
                      <p className="mt-0.5 text-sm font-medium">
                        {conversation.customerEmail}
                      </p>
                    </div>
                  )}
                  {conversation.babyAge != null && (
                    <div className="rounded-lg border border-border bg-background p-3">
                      <p className="text-xs font-medium text-muted-foreground">
                        Edad del bebe
                      </p>
                      <p className="mt-0.5 text-sm font-medium">
                        {conversation.babyAge} meses
                      </p>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </ScrollArea>
        </aside>
      </div>
    </div>
  );
}
