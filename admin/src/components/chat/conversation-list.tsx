"use client";

import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Search } from "lucide-react";
import type { Conversation } from "@/types/entities";
import { ConversationItem } from "./conversation-item";

interface ConversationListProps {
  conversations: Conversation[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  searchQuery: string;
  onSearchChange: (q: string) => void;
  unreadCounts?: Record<string, number>;
}

export function ConversationList({
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
    <div className="flex h-full flex-col border-r border-border bg-muted/30">
      <div className="flex-shrink-0 p-3">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Buscar conversaciones..."
            value={searchQuery}
            onChange={(e) => onSearchChange(e.target.value)}
            className="pl-9"
          />
        </div>
      </div>
      <ScrollArea className="flex-1">
        <div className="space-y-0.5 p-2">
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
      </ScrollArea>
    </div>
  );
}
