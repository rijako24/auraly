"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { cn } from "@/lib/utils";
import { formatDateTime } from "@/lib/utils";
import type { Message } from "@/types/entities";
import { Sparkles, User } from "lucide-react";

interface ChatBubbleProps {
  message: Message;
}

export function ChatBubble({ message }: ChatBubbleProps) {
  const isBot = message.sender.toLowerCase() === "bot";

  return (
    <div
      className={cn(
        "flex gap-3",
        isBot ? "flex-row-reverse" : "flex-row"
      )}
    >
      <Avatar className="h-8 w-8 flex-shrink-0">
        <AvatarFallback
          className={cn(
            "text-xs",
            isBot ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground"
          )}
        >
          {isBot ? <Sparkles className="h-4 w-4" /> : <User className="h-4 w-4" />}
        </AvatarFallback>
      </Avatar>
      <div
        className={cn(
          "flex max-w-[80%] flex-col gap-0.5",
          isBot ? "items-end" : "items-start"
        )}
      >
        <div
          className={cn(
            "rounded-2xl px-4 py-2.5 text-sm shadow-sm",
            isBot
              ? "rounded-tr-md bg-primary text-primary-foreground"
              : "rounded-tl-md bg-muted text-foreground"
          )}
        >
          <p className="whitespace-pre-wrap break-words">{message.messageText}</p>
        </div>
        <span className="text-xs text-muted-foreground">
          {formatDateTime(message.timestamp)}
        </span>
      </div>
    </div>
  );
}
