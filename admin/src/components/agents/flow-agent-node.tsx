"use client";

import { memo, useMemo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
  BrainCircuit,
  Eye,
  MessageCircle,
  Network,
  BookOpen,
  Clock,
  Tag,
  Zap,
} from "lucide-react";

import { resolveDynamicOutputs } from "@/lib/agents/dynamic-ports";
import { cn } from "@/lib/utils";
import type { FlowSubNodeSet, FlowSubNodeConfig } from "@/types/entities";
import type { FlowGenericNodeData } from "@/components/agents/flow-generic-node";

const SLOT_ICONS: Record<number, typeof Zap> = {
  0: Eye,
  1: Zap,
  2: BookOpen,
  3: Clock,
  4: Tag,
};

const SLOT_LABELS: Record<number, string> = {
  0: "Extracción",
  1: "Acciones",
  2: "Conocimiento",
  3: "Evento",
  4: "Clasificador",
};

function handleOffset(i: number, n: number): string {
  if (n <= 1) return "50%";
  return `${((i + 1) / (n + 1)) * 100}%`;
}

function flattenSubNodes(sn: FlowSubNodeSet): FlowSubNodeConfig[] {
  const result: FlowSubNodeConfig[] = [];
  if (sn.extract) result.push(sn.extract);
  if (sn.classifier) result.push(sn.classifier);
  if (sn.actions) result.push(...sn.actions);
  if (sn.knowledge) result.push(...sn.knowledge);
  if (sn.event) result.push(sn.event);
  return result;
}

function FlowAgentNodeInner({ data, selected }: NodeProps) {
  const d = data as FlowGenericNodeData;
  const inputs = d.inputs?.length ? d.inputs : [{ id: "default", label: "Entrada" }];
  const accent = d.accentColor ?? "#a855f7";

  const outputs = useMemo(() => {
    const staticOut = d.outputs ?? [];
    const schema = d.configSchemaJson ?? "{}";
    return resolveDynamicOutputs(schema, d.config ?? {}, staticOut);
  }, [d.outputs, d.config, d.configSchemaJson]);

  const subNodes = d.subNodes;
  const subNodeList = subNodes ? flattenSubNodes(subNodes) : [];

  const extractFields = subNodes?.extract?.config?.fields;
  const extractFieldList = Array.isArray(extractFields) ? (extractFields as string[]) : [];

  const nonExtractSubNodes = subNodeList.filter((sn) => sn.slot !== 0);
  const hasContent = extractFieldList.length > 0 || nonExtractSubNodes.length > 0;

  return (
    <div
      className={cn(
        "rounded-xl border-2 bg-card shadow-md min-w-[240px] max-w-[340px] relative",
        selected && "ring-2 ring-primary ring-offset-2 ring-offset-background"
      )}
      style={{
        borderColor: `${accent}88`,
        boxShadow: selected ? undefined : `0 0 12px ${accent}22, 0 0 0 1px ${accent}33`,
      }}
    >
      {inputs.map((p, i) => (
        <Handle
          key={`in-${p.id}`}
          type="target"
          position={Position.Left}
          id={p.id}
          className="!size-3 !border-2 !border-background"
          style={{
            top: handleOffset(i, inputs.length),
            background: accent,
          }}
          title={p.label}
        />
      ))}

      <div
        className="flex items-center gap-2 px-3 py-2 rounded-t-[10px]"
        style={{ background: `${accent}18` }}
      >
        <div
          className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg"
          style={{ background: `${accent}30`, color: accent }}
        >
          <BrainCircuit className="h-4 w-4" aria-hidden />
        </div>
        <div className="min-w-0 flex-1">
          <div className="text-sm font-semibold leading-tight truncate">{d.label}</div>
          <div className="text-[10px] text-muted-foreground font-mono">
            Agent
            {d.handlesIntent && (
              <span className="ml-1 opacity-60">· {d.handlesIntent}</span>
            )}
          </div>
        </div>
      </div>

      <div className="px-3 py-2 space-y-2">
        {extractFieldList.length > 0 && (
          <div>
            <div className="flex items-center gap-1.5 mb-1">
              <Eye className="h-3 w-3 text-muted-foreground" />
              <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                Extracción
              </span>
            </div>
            <div className="flex flex-wrap gap-1">
              {extractFieldList.map((f) => (
                <span
                  key={f}
                  className="inline-flex items-center rounded-md px-1.5 py-0.5 text-[10px] font-medium border"
                  style={{
                    borderColor: `${accent}40`,
                    background: `${accent}0a`,
                    color: accent,
                  }}
                >
                  {f}
                </span>
              ))}
            </div>
          </div>
        )}

        {nonExtractSubNodes.length > 0 && (
          <div>
            <div className="flex items-center gap-1.5 mb-1">
              <Network className="h-3 w-3 text-muted-foreground" />
              <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                Sub-nodos
              </span>
            </div>
            <div className="space-y-1">
              {nonExtractSubNodes.map((sn, i) => {
                const SlotIcon = SLOT_ICONS[sn.slot] ?? Network;
                return (
                  <div
                    key={sn.id || i}
                    className="flex items-center gap-2 rounded-md border px-2 py-1"
                    style={{
                      borderColor: `${accent}25`,
                      background: `${accent}08`,
                    }}
                  >
                    <SlotIcon className="h-3 w-3 shrink-0 text-muted-foreground" />
                    <span className="text-[10px] truncate flex-1">{sn.label}</span>
                    <span className="text-[8px] text-muted-foreground font-mono">
                      {SLOT_LABELS[sn.slot] ?? `s${sn.slot}`}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {!hasContent && (
          <div className="flex items-center gap-1.5 py-1">
            <MessageCircle className="h-3 w-3 text-muted-foreground" />
            <span className="text-[10px] text-muted-foreground">
              Agente conversacional
            </span>
          </div>
        )}
      </div>

      <div
        className="flex items-center justify-end gap-2 px-3 py-1.5 rounded-b-[10px] border-t"
        style={{ borderColor: `${accent}20`, background: `${accent}08` }}
      >
        {outputs.map((p) => (
          <span
            key={p.id}
            className="text-[9px] font-mono text-muted-foreground"
          >
            {p.label}
          </span>
        ))}
      </div>

      {outputs.map((p, i) => (
        <Handle
          key={`out-${p.id}`}
          type="source"
          position={Position.Right}
          id={p.id}
          className="!size-3 !border-2 !border-background"
          style={{
            top: handleOffset(i, outputs.length),
            background: accent,
          }}
          title={p.label}
        />
      ))}
    </div>
  );
}

export const FlowAgentNode = memo(FlowAgentNodeInner);
