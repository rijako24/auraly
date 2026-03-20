"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { cn } from "@/lib/utils";
import { getInitials } from "@/lib/utils";
import type { Conversation } from "@/types/entities";
import { formatRelativeTime } from "@/lib/utils";

interface ConversationItemProps {
  conversation: Conversation;
  isActive: boolean;
  onClick: () => void;
  unreadCount?: number;
}

export function ConversationItem({
  conversation,
  isActive,
  onClick,
  unreadCount = 0,
}: ConversationItemProps) {
  const displayName = conversation.customerName ?? conversation.userNumber;

  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex w-full items-start gap-3 rounded-lg px-3 py-2.5 text-left transition-colors",
        "hover:bg-muted/70 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-0",
        isActive &&
          "bg-primary/10 ring-1 ring-inset ring-primary/35 shadow-none"
      )}
    >
      <div className="relative flex-shrink-0">
        <Avatar className="h-11 w-11">
          <AvatarFallback className="bg-primary/10 text-primary text-sm font-medium">
            {getInitials(displayName)}
          </AvatarFallback>
        </Avatar>
        {unreadCount > 0 && (
          <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-xs font-medium text-primary-foreground">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        )}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <span className="min-w-0 flex-1 break-words font-medium leading-snug text-foreground line-clamp-2">
            {displayName}
          </span>
          <span className="shrink-0 pt-0.5 text-[11px] tabular-nums text-muted-foreground/90">
            {formatRelativeTime(conversation.timestamp)}
          </span>
        </div>
        <p
          title={conversation.lastMessage?.trim() || undefined}
          className={cn(
            "mt-1 line-clamp-4 text-left text-sm leading-snug break-words [overflow-wrap:anywhere]",
            unreadCount > 0 ? "font-medium text-foreground" : "text-muted-foreground"
          )}
        >
          {conversation.lastMessage?.trim() || "Sin mensajes"}
        </p>
      </div>
    </button>
  );
}
