"use client";

import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { MessageSquarePlus, Search } from "lucide-react";
import type { Agent, Conversation } from "@/types/entities";
import { ConversationItem } from "./conversation-item";

interface ConversationListProps {
  agents: Agent[];
  selectedAgentId: string;
  onAgentChange: (agentId: string) => void;
  conversations: Conversation[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  searchQuery: string;
  onSearchChange: (q: string) => void;
  unreadCounts?: Record<string, number>;
}

export function ConversationList({
  agents,
  selectedAgentId,
  onAgentChange,
  conversations,
  selectedId,
  onSelect,
  searchQuery,
  onSearchChange,
  unreadCounts = {},
}: ConversationListProps) {
  const filtered = conversations.filter((c) => {
    const name = (c.customerName ?? c.userNumber).toLowerCase();
    const msg = (c.lastMessage ?? "").toLowerCase();
    const q = searchQuery.toLowerCase().trim();
    if (!q) return true;
    return name.includes(q) || msg.includes(q) || c.userNumber.includes(q);
  });

  return (
    <div className="flex h-full min-w-0 flex-col border-r border-border bg-muted/30">
      <div className="flex-shrink-0 border-b border-border/70 bg-[#f0f2f5] px-3 pb-3 pt-3 dark:bg-muted/60"><div className="mb-2 flex items-center justify-between gap-2 px-1"><h1 className="text-lg font-semibold text-foreground">Conversaciones</h1><MessageSquarePlus className="h-4 w-4 shrink-0 text-muted-foreground" /></div><Select value={selectedAgentId} onValueChange={onAgentChange}><SelectTrigger className="mb-3 h-9 w-full bg-background/90 text-sm"><SelectValue placeholder="Selecciona un agente" /></SelectTrigger><SelectContent>{agents.map((agent) => <SelectItem key={agent.agentId} value={agent.agentId}>{`${agent.name}${agent.kind === "customer" ? " · Principal" : ""}${agent.isActive ? "" : " · Inactivo"}`}</SelectItem>)}</SelectContent></Select><div className="relative">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Buscar conversaciones..."
            value={searchQuery}
            onChange={(e) => onSearchChange(e.target.value)}
            className="h-10 rounded-lg border-0 bg-background/90 pl-9 shadow-none focus-visible:ring-1"
          />
        </div>
      </div>
      <div className="flex-1 overflow-y-scroll">
        <div className="divide-y divide-border/60">
          {filtered.map((conv) => (
            <ConversationItem
              key={conv.conversationId}
              conversation={conv}
              isActive={conv.conversationId === selectedId}
              onClick={() => onSelect(conv.conversationId)}
              unreadCount={unreadCounts[conv.conversationId] ?? 0}
            />
          ))}
          {filtered.length === 0 && (
            <div className="py-8 text-center text-sm text-muted-foreground">
              No se encontraron conversaciones
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
