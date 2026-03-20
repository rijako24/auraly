"use client";

import { useRef, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Paperclip, Send } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Message } from "@/types/entities";
import { ChatBubble } from "./chat-bubble";

interface ChatContainerProps {
  messages: Message[];
  onSendMessage: (text: string) => void;
  placeholder?: string;
  disabled?: boolean;
}

export function ChatContainer({
  messages,
  onSendMessage,
  placeholder = "Escribe un mensaje...",
  disabled = false,
}: ChatContainerProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const input = inputRef.current;
    const text = input?.value?.trim();
    if (text && !disabled && input) {
      onSendMessage(text);
      input.value = "";
    }
  };

  return (
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col bg-background">
      <ScrollArea className="h-full min-h-0 w-full min-w-0 flex-1">
        <div className="flex min-h-full w-full min-w-0 flex-col justify-end gap-3 px-4 py-3">
          {messages.map((msg) => (
            <ChatBubble key={msg.messageId} message={msg} />
          ))}
          <div ref={scrollRef} className="h-px w-full shrink-0" aria-hidden />
        </div>
      </ScrollArea>
      <div className="w-full min-w-0 shrink-0 border-t border-border bg-muted/20 px-3 py-3">
        <form onSubmit={handleSubmit} className="flex w-full min-w-0 items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="shrink-0 text-muted-foreground"
            disabled={disabled}
            aria-label="Adjuntar"
          >
            <Paperclip className="h-5 w-5" />
          </Button>
          <Input
            ref={inputRef}
            placeholder={placeholder}
            disabled={disabled}
            className={cn("h-11 flex-1 bg-background")}
          />
          <Button
            type="submit"
            size="icon"
            className="h-11 w-11 shrink-0"
            disabled={disabled}
            aria-label="Enviar"
          >
            <Send className="h-5 w-5" />
          </Button>
        </form>
      </div>
    </div>
  );
}
