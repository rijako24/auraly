"use client";
import { useState, useMemo } from "react";
import { ArrowLeft, MessageSquare, Phone } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { ChatContainer } from "@/components/chat/chat-container";
import { ConversationList } from "@/components/chat/conversation-list";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import { ConversationStateLabels, ConversationStateColors } from "@/types/enums";
import { useMediaQuery } from "@/hooks/use-media-query";
import { getInitials, cn } from "@/lib/utils";
import { useConversations, useConversationWithMessages } from "@/hooks/use-conversations";

export default function ConversationsPage() {
  const isMobile = useMediaQuery("(max-width: 768px)");
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data: conversationsData, isLoading, isError, refetch } = useConversations();
  const { data: selectedConversation } = useConversationWithMessages(selectedId);
  const conversations = useMemo(() => { const items = conversationsData?.items ?? []; return [...items].sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()); }, [conversationsData]);
  const messages = selectedConversation?.messages ?? [];
  const showList = isMobile ? !selectedId : true;
  const showChat = isMobile ? !!selectedId : true;

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className={cn("flex h-[calc(100vh-3.5rem)] overflow-hidden rounded-lg border border-border bg-card", "max-lg:-mx-4 max-lg:h-[calc(100vh-3.5rem)] max-lg:rounded-none max-lg:border-x-0")}>
      <div className={cn("flex w-[350px] flex-shrink-0 flex-col overflow-hidden", isMobile && "absolute inset-0 z-10", isMobile && !showList && "hidden")}>
        <ConversationList conversations={conversations} selectedId={selectedId} onSelect={setSelectedId} searchQuery={searchQuery} onSearchChange={setSearchQuery} unreadCounts={{}} />
      </div>
      <div className={cn("flex flex-1 flex-col overflow-hidden bg-background", isMobile && "absolute inset-0 z-10", isMobile && !showChat && "hidden")}>
        {selectedConversation ? (
          <>
            <div className="flex flex-shrink-0 items-center gap-3 border-b border-border bg-muted/30 px-4 py-3">
              <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setSelectedId(null)}><ArrowLeft className="h-5 w-5" /></Button>
              <Avatar className="h-10 w-10"><AvatarFallback className="bg-primary/10 text-primary text-sm font-medium">{getInitials(selectedConversation.customerName ?? selectedConversation.userNumber)}</AvatarFallback></Avatar>
              <div className="min-w-0 flex-1"><p className="truncate font-semibold text-foreground">{selectedConversation.customerName ?? selectedConversation.userNumber}</p><p className="flex items-center gap-1.5 text-xs text-muted-foreground"><Phone className="h-3.5 w-3.5" />{selectedConversation.userNumber}</p></div>
              <Badge variant="secondary" className={cn("hidden sm:inline-flex", ConversationStateColors[selectedConversation.state])}>{ConversationStateLabels[selectedConversation.state]}</Badge>
            </div>
            <div className="flex flex-1 min-h-0"><ChatContainer messages={messages} onSendMessage={() => {}} placeholder="Escribe un mensaje..." /></div>
          </>
        ) : (
          <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
            <div className="rounded-full bg-muted p-4"><MessageSquare className="h-12 w-12 text-muted-foreground" /></div>
            <div><h2 className="text-lg font-semibold text-foreground">Selecciona una conversación</h2><p className="mt-1 text-sm text-muted-foreground">Elige una conversación de la lista para ver los mensajes</p></div>
          </div>
        )}
      </div>
    </div>
  );
}
