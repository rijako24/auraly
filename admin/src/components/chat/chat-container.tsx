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
    <div className="flex h-full flex-col">
      <ScrollArea className="flex-1 flex-grow overflow-y-auto px-4">
        <div className="flex flex-col gap-4 py-4">
          {messages.map((msg) => (
            <ChatBubble key={msg.messageId} message={msg} />
          ))}
          <div ref={scrollRef} aria-hidden />
        </div>
      </ScrollArea>
      <div className="flex-shrink-0 border-t border-border bg-background p-3">
        <form onSubmit={handleSubmit} className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="flex-shrink-0"
            disabled={disabled}
          >
            <Paperclip className="h-5 w-5" />
          </Button>
          <Input
            ref={inputRef}
            placeholder={placeholder}
            disabled={disabled}
            className={cn("flex-1")}
          />
          <Button
            type="submit"
            size="icon"
            className="flex-shrink-0"
            disabled={disabled}
          >
            <Send className="h-5 w-5" />
          </Button>
        </form>
      </div>
    </div>
  );
}
