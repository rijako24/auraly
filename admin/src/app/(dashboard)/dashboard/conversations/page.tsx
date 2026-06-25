"use client";

import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, MessageSquare, Phone } from "lucide-react";
import { toast } from "sonner";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { ChatContainer } from "@/components/chat/chat-container";
import { ConversationList } from "@/components/chat/conversation-list";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import {
  getConversationStageColor,
  getConversationStageLabel,
  getConversationStageStyle,
} from "@/types/enums";
import { useMediaQuery } from "@/hooks/use-media-query";
import { cn, getInitials } from "@/lib/utils";
import {
  useConversations,
  useConversationWithMessages,
  useSendWebConversationMessage,
  useUpdateConversationOwner,
} from "@/hooks/use-conversations";

function getErrorMessage(error: unknown) {
  if (error && typeof error === "object" && "message" in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === "string" && message.trim()) return message;
  }

  return "No se pudo enviar el mensaje.";
}

export default function ConversationsPage() {
  const isMobile = useMediaQuery("(max-width: 768px)");
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data: conversationsData, isLoading, isError, refetch } = useConversations();
  const { data: selectedConversation } = useConversationWithMessages(selectedId);
  const sendWebMessage = useSendWebConversationMessage();
  const updateOwner = useUpdateConversationOwner();

  const conversations = useMemo(() => {
    const items = conversationsData?.items ?? [];
    return [...items].sort(
      (a, b) =>
        new Date(b.lastActivityAt ?? b.timestamp).getTime() -
        new Date(a.lastActivityAt ?? a.timestamp).getTime()
    );
  }, [conversationsData]);

  const messages = selectedConversation?.messages ?? [];
  const showList = isMobile ? !selectedId : true;
  const showChat = isMobile ? !!selectedId : true;
  const stageLabel = getConversationStageLabel(selectedConversation?.currentStageName);
  const stageColor = getConversationStageColor();
  const stageStyle = getConversationStageStyle(selectedConversation?.currentStageName);
  const botEnabled = selectedConversation?.botEnabled ?? selectedConversation?.owner !== "Human";

  useEffect(() => {
    if (!isMobile && !selectedId && conversations.length > 0) {
      setSelectedId(conversations[0].conversationId);
    }
  }, [conversations, isMobile, selectedId]);

  const handleSelectConversation = (conversationId: string) => {
    setSelectedId(conversationId);
  };

  const handleBackToList = () => {
    setSelectedId(null);
  };

  const handleSendMessage = async (message: string) => {
    if (!selectedId) return;

    try {
      await sendWebMessage.mutateAsync({ conversationId: selectedId, message });
      toast.success("Mensaje enviado");
    } catch (error) {
      toast.error(getErrorMessage(error));
      throw error;
    }
  };

  const handleBotEnabledChange = async (checked: boolean) => {
    if (!selectedConversation) return;

    const owner = checked ? "Bot" : "Human";
    try {
      await updateOwner.mutateAsync({
        conversationId: selectedConversation.conversationId,
        owner,
      });
      toast.success(checked ? "Bot activado" : "Bot desactivado");
    } catch (error) {
      toast.error("No se pudo actualizar el bot.");
    }
  };

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div
      className={cn(
        "relative flex h-[calc(100dvh-7rem)] overflow-hidden rounded-lg border border-border bg-card",
        "max-lg:-mx-4 max-lg:h-[calc(100dvh-5.5rem)] max-lg:rounded-none max-lg:border-x-0"
      )}
    >
      <div
        className={cn(
          "flex w-[360px] flex-shrink-0 flex-col overflow-hidden",
          isMobile && "absolute inset-0 z-10 w-full",
          isMobile && !showList && "hidden"
        )}
      >
        <ConversationList
          conversations={conversations}
          selectedId={selectedId}
          onSelect={handleSelectConversation}
          searchQuery={searchQuery}
          onSearchChange={setSearchQuery}
          unreadCounts={{}}
        />
      </div>

      <div
        className={cn(
          "flex min-w-0 flex-1 flex-col overflow-hidden bg-background",
          isMobile && "absolute inset-0 z-10",
          isMobile && !showChat && "hidden"
        )}
      >
        {selectedConversation ? (
          <>
            <div className="flex flex-shrink-0 items-center gap-3 border-b border-border bg-muted/30 px-3 py-3 sm:px-4">
              <Button
                variant="ghost"
                size="icon"
                className="md:hidden"
                onClick={handleBackToList}
                aria-label="Volver a conversaciones"
              >
                <ArrowLeft className="h-5 w-5" />
              </Button>
              <Avatar className="h-10 w-10 flex-shrink-0">
                <AvatarFallback className="bg-primary/10 text-sm font-medium text-primary">
                  {getInitials(selectedConversation.customerName ?? selectedConversation.userNumber)}
                </AvatarFallback>
              </Avatar>
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold text-foreground">
                  {selectedConversation.customerName ?? selectedConversation.userNumber}
                </p>
                <p className="flex min-w-0 items-center gap-1.5 text-xs text-muted-foreground">
                  <Phone className="h-3.5 w-3.5 flex-shrink-0" />
                  <span className="truncate">{selectedConversation.userNumber}</span>
                </p>
              </div>
              <Badge
                variant="secondary"
                style={stageStyle}
                className={cn("hidden flex-shrink-0 font-medium shadow-sm sm:inline-flex", stageColor)}
              >
                {stageLabel}
              </Badge>
              <div className="flex flex-shrink-0 items-center gap-2">
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
            <div className="flex min-h-0 min-w-0 flex-1">
              <ChatContainer
                messages={messages}
                onSendMessage={handleSendMessage}
                placeholder="Escribe un mensaje..."
                disabled={sendWebMessage.isPending}
              />
            </div>
          </>
        ) : (
          <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
            <div className="rounded-full bg-muted p-4">
              <MessageSquare className="h-12 w-12 text-muted-foreground" />
            </div>
            <div>
              <h2 className="text-lg font-semibold text-foreground">
                Selecciona una conversacion
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                Elige una conversacion de la lista para ver los mensajes.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
