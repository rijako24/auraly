"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { getInitials, truncate } from "@/lib/utils";
import { ConversationStateLabels, ConversationStateColors } from "@/types/enums";
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
        "flex w-full items-start gap-3 rounded-lg p-3 text-left transition-colors",
        "hover:bg-muted/80 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2",
        isActive && "bg-muted"
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
      <div className="min-w-0 flex-1 overflow-hidden">
        <div className="flex items-center justify-between gap-2">
          <span className="truncate font-medium text-foreground">
            {displayName}
          </span>
          <span className="flex-shrink-0 text-xs text-muted-foreground">
            {formatRelativeTime(conversation.timestamp)}
          </span>
        </div>
        <div className="mt-0.5 flex items-center gap-2">
          <span
            className={cn(
              "truncate text-sm",
              unreadCount > 0 ? "font-medium text-foreground" : "text-muted-foreground"
            )}
          >
            {truncate(conversation.lastMessage ?? "Sin mensajes", 40)}
          </span>
        </div>
        <div className="mt-1.5">
          <Badge
            variant="secondary"
            className={cn(
              "text-xs font-normal",
              ConversationStateColors[conversation.state]
            )}
          >
            {ConversationStateLabels[conversation.state]}
          </Badge>
        </div>
      </div>
    </button>
  );
}
