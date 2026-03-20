import type { Connection, Edge, Node } from "@xyflow/react";

import { wouldConnectionBeBackEdge } from "@/lib/agents/flow-graph-ranks";

/** MIME type para drag & drop desde la biblioteca de nodos. */
export const FLOW_CATALOG_DRAG_MIME = "application/quantix-flow-catalog";

export function getEdgeStyleForPort(portId?: string | null): {
  stroke: string;
  strokeDasharray?: string;
  strokeWidth?: number;
} {
  switch (portId) {
    case "success":
      return { stroke: "#22c55e", strokeWidth: 2 };
    case "failure":
      return { stroke: "#ef4444", strokeDasharray: "6 4", strokeWidth: 2 };
    case "not_required":
      return { stroke: "#64748b", strokeDasharray: "4 4" };
    case "received":
      return { stroke: "#3b82f6", strokeWidth: 2 };
    case "skipped":
      return { stroke: "#a855f7", strokeDasharray: "3 3" };
    default:
      return { stroke: "#94a3b8", strokeWidth: 1.5 };
  }
}

export type StyleFlowEdgeOptions = {
  /** Arista de retorno (ciclo): se dibuja con ruta por debajo y tipo `flowBackEdge`. */
  isBackEdge?: boolean;
};

/**
 * Aplica estilo visual según el puerto de salida (similar a n8n: éxito sólido, error punteado).
 * Las aristas normales usan `smoothstep`; las de retorno usan `flowBackEdge`.
 */
export function styleFlowEdge(
  edge: Pick<Edge, "id" | "source" | "target"> &
    Partial<Omit<Edge, "id" | "source" | "target">>,
  options?: StyleFlowEdgeOptions
): Edge {
  const portId = edge.sourceHandle ?? undefined;
  const line = getEdgeStyleForPort(portId);
  const showLabel = portId && portId !== "default";
  const isBack = options?.isBackEdge === true || edge.type === "flowBackEdge";

  return {
    ...edge,
    type: isBack ? "flowBackEdge" : (edge.type ?? "smoothstep"),
    style: {
      ...edge.style,
      ...line,
    },
    animated: !isBack && portId === "success",
    label: edge.label ?? (showLabel ? String(portId) : undefined),
  } as Edge;
}

/** Crea una arista nueva a partir de una conexión del canvas (detecta retorno respecto al grafo actual). */
export function styleFlowConnection(
  connection: Connection,
  id: string,
  graph?: { nodes: Node[]; edges: Edge[] }
): Edge {
  const src = connection.source;
  const tgt = connection.target;
  const isBack =
    graph &&
    src &&
    tgt &&
    wouldConnectionBeBackEdge(graph.nodes, graph.edges, src, tgt);

  return styleFlowEdge(
    {
      id,
      source: src!,
      target: tgt!,
      sourceHandle: connection.sourceHandle ?? undefined,
      targetHandle: connection.targetHandle ?? undefined,
    },
    { isBackEdge: !!isBack }
  );
}
