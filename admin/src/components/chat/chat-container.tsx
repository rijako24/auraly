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
  onSendMessage: (text: string) => void | Promise<void>;
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const input = inputRef.current;
    const text = input?.value?.trim();
    if (text && !disabled && input) {
      try {
        await onSendMessage(text);
        if (inputRef.current) {
          inputRef.current.value = "";
          inputRef.current.focus();
        }
      } catch {
        input.focus();
      }
    }
  };

  return (
    <div className="flex h-full w-full flex-1 flex-col">
      <ScrollArea className="whatsapp-chat-surface min-h-0 flex-1 overflow-y-auto px-3 sm:px-8">
        <div className="mx-auto flex min-h-full max-w-5xl flex-col gap-2 py-4 sm:gap-3 sm:py-6">
          {messages.map((msg) => (
            <ChatBubble key={msg.messageId} message={msg} />
          ))}
          <div ref={scrollRef} aria-hidden />
        </div>
      </ScrollArea>
      <div className="flex-shrink-0 border-t border-border/70 bg-[#f0f2f5] p-2 pb-[calc(0.5rem+env(safe-area-inset-bottom))] dark:bg-muted/60 sm:px-4 sm:py-3">
        <form onSubmit={handleSubmit} className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="hidden flex-shrink-0 sm:inline-flex"
            disabled={disabled}
          >
            <Paperclip className="h-5 w-5" />
          </Button>
          <Input
            ref={inputRef}
            placeholder={placeholder}
            disabled={disabled}
            className={cn("h-11 flex-1 rounded-xl border-0 bg-background px-4 shadow-none focus-visible:ring-1")}
          />
          <Button
            type="submit"
            size="icon"
            className="h-10 w-10 flex-shrink-0 rounded-full bg-primary text-primary-foreground hover:bg-primary/90"
            disabled={disabled}
          >
            <Send className="h-5 w-5" />
          </Button>
        </form>
      </div>
    </div>
  );
}

