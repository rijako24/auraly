import dagre from "@dagrejs/dagre";
import type { Edge, Node } from "@xyflow/react";
import { computeBfsRankFromStart } from "@/lib/agents/flow-graph-ranks";

/** Alineado ~con el tamaño visual de `FlowGenericNode` compacto. */
const DEFAULT_NODE_WIDTH = 188;
const DEFAULT_NODE_HEIGHT = 74;
const AGENT_NODE_WIDTH = 320;
const AGENT_NODE_HEIGHT = 210;
const LANE_GAP = 220;

export type FlowLayoutDirection = "LR" | "TB";

function readFlowType(node: Node): number | undefined {
  const data = node.data as { flowType?: unknown } | undefined;
  return typeof data?.flowType === "number" ? data.flowType : undefined;
}

function readConfig(node: Node): Record<string, unknown> {
  const data = node.data as { config?: unknown } | undefined;
  return data?.config && typeof data.config === "object" && !Array.isArray(data.config)
    ? (data.config as Record<string, unknown>)
    : {};
}

function countSubNodes(node: Node): number {
  const data = node.data as { subNodes?: Record<string, unknown> } | undefined;
  if (!data?.subNodes) return 0;
  const sn = data.subNodes;
  let count = 0;
  if (sn.extract) count++;
  if (sn.classifier) count++;
  if (Array.isArray(sn.actions)) count += sn.actions.length;
  if (Array.isArray(sn.knowledge)) count += sn.knowledge.length;
  if (sn.event) count++;
  return count;
}

function estimateNodeSize(node: Node): { width: number; height: number } {
  const width = (node.width as number | undefined) ?? (node.style?.width as number | undefined);
  const height = (node.height as number | undefined) ?? (node.style?.height as number | undefined);
  if (typeof width === "number" && typeof height === "number") return { width, height };

  const flowType = readFlowType(node);

  if (flowType === 10) {
    const subCount = countSubNodes(node);
    const extraHeight = Math.min(160, subCount * 26);
    return { width: AGENT_NODE_WIDTH, height: AGENT_NODE_HEIGHT + extraHeight };
  }

  if (flowType === 5 || flowType === 9) return { width: 220, height: 100 };
  if (flowType === 6) return { width: 220, height: 88 };
  return { width: DEFAULT_NODE_WIDTH, height: DEFAULT_NODE_HEIGHT };
}

/**
 * Derives a lane offset from the node's `_ui.y` position relative to the main axis.
 * Nodes with explicit _ui positions get their intended vertical separation preserved.
 * Nodes without _ui use flowType-based heuristics (escalation below, agents above).
 */
function laneOffsetForNode(node: Node): number {
  const cfg = readConfig(node);
  const ui = cfg._ui as { y?: number } | undefined;

  if (ui && typeof ui.y === "number") {
    const mainY = 400;
    return (ui.y - mainY) * 0.3;
  }

  const flowType = readFlowType(node);
  if (flowType === 6) return LANE_GAP;
  return 0;
}

/**
 * Aristas para Dagre:
 * - Excluye back-edges (ciclos) para que no distorsionen el layout principal LR/TB.
 * - Ancla nodos sin entrada al Start para evitar componentes "a la izquierda" del inicio.
 */
function buildDagreEdgeList(
  nodes: Node[],
  edges: Edge[]
): { edges: Array<{ v: string; w: string }>; rank: Map<string, number> } {
  const ids = new Set(nodes.map((n) => n.id));
  const startId = nodes.find((n) => (n.data as { flowType?: number }).flowType === 0)?.id;
  const graphEdges = edges
    .filter((e) => ids.has(e.source) && ids.has(e.target))
    .map((e) => ({ source: e.source, target: e.target }));
  const rank = computeBfsRankFromStart(
    nodes.map((n) => n.id),
    graphEdges,
    startId
  );

  const list: Array<{ v: string; w: string }> = [];
  for (const e of edges) {
    if (!ids.has(e.source) || !ids.has(e.target)) continue;
    if (e.source === e.target) continue;

    const rs = rank.get(e.source);
    const rt = rank.get(e.target);
    // Excluir aristas de retorno del layout base (se dibujan igual, pero no "empujan" nodos).
    if (rs !== undefined && rt !== undefined && rt < rs) continue;

    list.push({ v: e.source, w: e.target });
  }

  if (!startId || !ids.has(startId)) return { edges: list, rank };

  const incoming = new Map<string, number>();
  for (const id of ids) incoming.set(id, 0);
  for (const e of list) {
    if (ids.has(e.w)) incoming.set(e.w, (incoming.get(e.w) ?? 0) + 1);
  }

  for (const n of nodes) {
    if (n.id === startId) continue;
    if ((incoming.get(n.id) ?? 0) === 0) {
      list.push({ v: startId, w: n.id });
    }
  }

  return { edges: list, rank };
}

/**
 * Recalcula posiciones con Dagre (izquierda→derecha o arriba→abajo).
 * No modifica `data` ni dimensiones reales del DOM; usa tamaños aproximados para nodos genéricos.
 */
export function layoutFlowWithDagre(
  nodes: Node[],
  edges: Edge[],
  direction: FlowLayoutDirection = "LR"
): Node[] {
  if (nodes.length === 0) return nodes;

  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({
    rankdir: direction,
    ranksep: direction === "LR" ? 110 : 80,
    nodesep: direction === "LR" ? 72 : 52,
    marginx: 16,
    marginy: 16,
  });

  for (const n of nodes) {
    const size = estimateNodeSize(n);
    const w = size.width;
    const h = size.height;
    g.setNode(n.id, { width: w, height: h });
  }

  const { edges: dagreEdges, rank } = buildDagreEdgeList(nodes, edges);
  for (const e of dagreEdges) {
    if (g.hasNode(e.v) && g.hasNode(e.w)) {
      g.setEdge(e.v, e.w);
    }
  }

  dagre.layout(g);

  return nodes.map((n) => {
    const pos = g.node(n.id);
    if (!pos) return n;
    const size = estimateNodeSize(n);
    const w = size.width;
    const h = size.height;
    const flowType = readFlowType(n);

    let laneOffsetY = 0;
    if (direction === "LR") {
      laneOffsetY += laneOffsetForNode(n);

      const r = rank.get(n.id) ?? 0;
      laneOffsetY += (r % 2 === 0 ? 1 : -1) * 5;
    }

    return {
      ...n,
      position: {
        x: pos.x - w / 2,
        y: pos.y - h / 2 + laneOffsetY,
      },
    };
  });
}
