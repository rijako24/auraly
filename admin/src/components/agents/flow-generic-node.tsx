"use client";

import { memo, useMemo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
  Box,
  Brain,
  BrainCircuit,
  CirclePlay,
  CircleStop,
  Clock,
  GitBranch,
  ListChecks,
  MessageSquare,
  UserRound,
  Zap,
  type LucideIcon,
} from "lucide-react";

import { resolveDynamicOutputs } from "@/lib/agents/dynamic-ports";
import { isFlowNodeConfigMissingRequired } from "@/lib/agents/flow-node-validation";
import { cn } from "@/lib/utils";
import type { FlowPort } from "@/types/entities";

export type FlowLayoutOrientation = "horizontal" | "vertical";

export type FlowGenericNodeData = {
  label: string;
  flowType: number;
  catalogKey?: string;
  /** JSON Schema del catálogo (incluye x-dynamicOutputPort en propiedades). */
  configSchemaJson?: string;
  icon?: string;
  accentColor?: string;
  inputs?: FlowPort[];
  outputs?: FlowPort[];
  config: Record<string, unknown>;
  /** Cluster-node sub-nodes. Present for Agent and Router nodes. */
  subNodes?: import("@/types/entities").FlowSubNodeSet;
  /** The routing intent this agent handles. Used for visual labels. */
  handlesIntent?: string;
};

const CATALOG_ICONS: Record<string, LucideIcon> = {
  "circle-play": CirclePlay,
  "circle-stop": CircleStop,
  "list-checks": ListChecks,
  zap: Zap,
  brain: Brain,
  "brain-circuit": BrainCircuit,
  "git-branch": GitBranch,
  "message-square": MessageSquare,
  clock: Clock,
  "user-round": UserRound,
};

function handleOffset(i: number, n: number): string {
  if (n <= 1) return "50%";
  return `${((i + 1) / (n + 1)) * 100}%`;
}

function readOrientation(config: Record<string, unknown> | undefined): FlowLayoutOrientation {
  const ui = config?._ui as { orientation?: string } | undefined;
  return ui?.orientation === "vertical" ? "vertical" : "horizontal";
}

function FlowGenericNodeInner({ data, selected }: NodeProps) {
  const d = data as FlowGenericNodeData;
  const inputs = d.inputs?.length ? d.inputs : [{ id: "default", label: "Entrada" }];
  const vertical = readOrientation(d.config) === "vertical";

  const outputs = useMemo(() => {
    const staticOut = d.outputs ?? [];
    const schema = d.configSchemaJson ?? "{}";
    return resolveDynamicOutputs(schema, d.config ?? {}, staticOut);
  }, [d.outputs, d.config, d.configSchemaJson]);

  const Icon = (d.icon && CATALOG_ICONS[d.icon]) || Box;
  const accent = d.accentColor;

  const inPos = vertical ? Position.Top : Position.Left;
  const outPos = vertical ? Position.Bottom : Position.Right;

  const missingRequired = useMemo(
    () => isFlowNodeConfigMissingRequired(d.configSchemaJson, d.config),
    [d.configSchemaJson, d.config]
  );

  return (
    <div
      className={cn(
        "rounded-lg border border-border bg-card px-2 py-1.5 shadow-sm min-w-[128px] max-w-[200px] relative",
        vertical && "py-2.5",
        selected && "ring-2 ring-primary ring-offset-2 ring-offset-background"
      )}
      style={
        accent
          ? {
              borderColor: `${accent}99`,
              boxShadow: selected ? undefined : `0 0 0 1px ${accent}33`,
            }
          : undefined
      }
    >
      {missingRequired ? (
        <span
          className="absolute -right-1 -top-1 z-10 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-0.5 text-[9px] font-bold text-destructive-foreground shadow-sm"
          title="Faltan campos obligatorios en la configuración del nodo"
        >
          !
        </span>
      ) : null}
      {inputs.map((p, i) => (
        <Handle
          key={`in-${p.id}`}
          type="target"
          position={inPos}
          id={p.id}
          className="!size-2.5 !border-2 !border-background"
          style={
            vertical
              ? {
                  left: handleOffset(i, inputs.length),
                  top: undefined,
                  background: accent ?? "hsl(var(--muted-foreground))",
                }
              : {
                  top: handleOffset(i, inputs.length),
                  background: accent ?? "hsl(var(--muted-foreground))",
                }
          }
          title={p.label}
        />
      ))}

      <div className={cn("flex items-start gap-1.5 pr-0.5", vertical && "justify-center px-0.5")}>
        <div
          className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-md bg-muted"
          style={accent ? { background: `${accent}22`, color: accent } : undefined}
        >
          <Icon className="h-3.5 w-3.5" aria-hidden />
        </div>
        <div className="min-w-0 flex-1">
          <div className="text-xs font-medium leading-tight truncate">{d.label}</div>
          <div className="text-[9px] text-muted-foreground font-mono truncate">t{d.flowType}</div>
        </div>
      </div>

      {outputs.map((p, i) => (
        <Handle
          key={`out-${p.id}`}
          type="source"
          position={outPos}
          id={p.id}
          className="!size-2.5 !border-2 !border-background"
          style={
            vertical
              ? {
                  left: handleOffset(i, outputs.length),
                  bottom: undefined,
                  background: accent ?? "hsl(var(--muted-foreground))",
                }
              : {
                  top: handleOffset(i, outputs.length),
                  background: accent ?? "hsl(var(--muted-foreground))",
                }
          }
          title={p.label}
        />
      ))}
    </div>
  );
}

export const FlowGenericNode = memo(FlowGenericNodeInner);
