"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { cn } from "@/lib/utils";
import { formatDateTime } from "@/lib/utils";
import type { Message } from "@/types/entities";
import { Bot, User } from "lucide-react";

interface ChatBubbleProps {
  message: Message;
}

export function ChatBubble({ message }: ChatBubbleProps) {
  const isUser = message.sender === "User";

  return (
    <div
      className={cn(
        "flex w-full min-w-0 gap-2.5 sm:gap-3",
        isUser ? "flex-row-reverse" : "flex-row",
        "items-end"
      )}
    >
      <Avatar className="h-8 w-8 shrink-0">
        <AvatarFallback
          className={cn(
            "text-xs",
            isUser
              ? "bg-primary text-primary-foreground"
              : "border border-border bg-card text-muted-foreground"
          )}
        >
          {isUser ? <User className="h-4 w-4" aria-hidden /> : <Bot className="h-4 w-4" aria-hidden />}
        </AvatarFallback>
      </Avatar>
      <div
        className={cn(
          "flex min-w-0 flex-1 flex-col gap-1",
          isUser ? "items-end" : "items-start"
        )}
      >
        <div
          className={cn(
            // Ancho cómodo para lectura: cortos = w-fit; largos = envuelven a ~32rem (no todo el panel)
            "min-w-0 w-fit max-w-[min(100%,32rem)] rounded-2xl px-3.5 py-2.5 text-sm leading-relaxed shadow-sm",
            isUser
              ? "ml-auto rounded-tr-md bg-primary text-primary-foreground"
              : "rounded-tl-md border border-border/60 bg-muted/80 text-foreground"
          )}
        >
          <p className="whitespace-pre-wrap break-words [overflow-wrap:anywhere]">
            {message.messageText}
          </p>
        </div>
        <span
          className={cn(
            "max-w-[min(100%,32rem)] px-0.5 text-[11px] leading-snug text-muted-foreground/90 sm:text-xs",
            isUser ? "ml-auto text-right" : "text-left"
          )}
        >
          {formatDateTime(message.timestamp)}
        </span>
      </div>
    </div>
  );
}
