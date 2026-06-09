"use client";

import { ChevronDown } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { AgentFlowStage } from "@/types/agent-settings";

interface AgentFlowCanvasProps {
  stages: AgentFlowStage[];
  selectedStageId?: string | null;
  onSelectStage?: (stageId: string) => void;
  className?: string;
}

export function AgentFlowCanvas({
  stages,
  selectedStageId,
  onSelectStage,
  className,
}: AgentFlowCanvasProps) {
  if (stages.length === 0) {
    return (
      <div
        className={cn(
          "flex h-[280px] items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground",
          className
        )}
      >
        Añade etapas en el editor de lista para ver el flujo.
      </div>
    );
  }

  return (
    <div
      className={cn(
        "overflow-auto rounded-lg border bg-muted/20 p-4",
        className
      )}
    >
      <div className="mx-auto flex max-w-md flex-col items-center gap-0">
        {stages.map((stage, index) => {
          const tools = stage.allowedTools ?? stage.suggestedTools ?? [];
          const selected = stage.id === selectedStageId;

          return (
            <div key={stage.id} className="flex w-full flex-col items-center">
              <button
                type="button"
                onClick={() => onSelectStage?.(stage.id)}
                className={cn(
                  "w-full rounded-lg border bg-card px-4 py-3 text-left shadow-sm transition-colors hover:bg-muted/50",
                  selected && "border-primary ring-2 ring-primary/30"
                )}
              >
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-xs font-medium text-muted-foreground">
                    Etapa {index + 1}
                  </span>
                </div>
                <p className="font-semibold text-sm">{stage.id}</p>
                <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">
                  {stage.goal}
                </p>
                {tools.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1">
                    {tools.map((t) => (
                      <Badge
                        key={t}
                        variant="outline"
                        className="text-[10px] font-normal"
                      >
                        {t}
                      </Badge>
                    ))}
                  </div>
                )}
                {(stage.advanceWhenFacts?.length ?? 0) > 0 && (
                  <p className="mt-2 text-[10px] text-muted-foreground">
                    Avanza con: {stage.advanceWhenFacts!.join(", ")}
                  </p>
                )}
              </button>
              {index < stages.length - 1 && (
                <ChevronDown
                  className="my-1 h-5 w-5 shrink-0 text-muted-foreground"
                  aria-hidden
                />
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
