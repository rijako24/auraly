import type { FlowDocument, FlowDocumentNode } from "@/types/entities";
import type { Edge, Node } from "@xyflow/react";

import type { FlowGenericNodeData } from "@/components/agents/flow-generic-node";

/** Distancias mínimas (saltos) desde el nodo Start (type 0). */
export function computeBfsRankFromStart(
  nodeIds: readonly string[],
  graphEdges: readonly { source: string; target: string }[],
  startId: string | undefined
): Map<string, number> {
  const rank = new Map<string, number>();
  const idSet = new Set(nodeIds);
  if (!startId || !idSet.has(startId)) return rank;

  const adj = new Map<string, string[]>();
  for (const id of nodeIds) adj.set(id, []);
  for (const e of graphEdges) {
    if (idSet.has(e.source) && idSet.has(e.target)) {
      adj.get(e.source)!.push(e.target);
    }
  }

  rank.set(startId, 0);
  const q: string[] = [startId];
  while (q.length > 0) {
    const u = q.shift()!;
    const ru = rank.get(u)!;
    for (const v of adj.get(u) ?? []) {
      const nv = ru + 1;
      if (!rank.has(v) || nv < rank.get(v)!) {
        rank.set(v, nv);
        q.push(v);
      }
    }
  }
  return rank;
}

function findStartNodeId(docNodes: readonly FlowDocumentNode[]): string | undefined {
  return docNodes.find((n) => n.type === 0)?.id;
}

/** IDs de aristas que van “hacia atrás” respecto al BFS desde Start (ciclos / reinicios). */
export function computeBackEdgeIdsFromFlowDocument(doc: FlowDocument): Set<string> {
  const startId = findStartNodeId(doc.nodes);
  const nodeIds = doc.nodes.map((n) => n.id);
  const graphEdges = doc.edges.map((e) => ({ source: e.sourceNodeId, target: e.targetNodeId }));
  const rank = computeBfsRankFromStart(nodeIds, graphEdges, startId);
  const back = new Set<string>();

  for (const e of doc.edges) {
    const rs = rank.get(e.sourceNodeId);
    const rt = rank.get(e.targetNodeId);
    if (rs === undefined) continue;
    if (rt === undefined) continue;
    if (rt < rs) back.add(e.id);
  }
  return back;
}

/** Misma lógica sobre el estado actual del editor (p. ej. nueva conexión). */
export function computeBackEdgeIdsFromReactFlow(nodes: Node[], edges: Edge[]): Set<string> {
  const startId = nodes.find((n) => (n.data as FlowGenericNodeData).flowType === 0)?.id;
  const nodeIds = nodes.map((n) => n.id);
  const graphEdges = edges.map((e) => ({ source: e.source, target: e.target }));
  const rank = computeBfsRankFromStart(nodeIds, graphEdges, startId);
  const back = new Set<string>();

  for (const e of edges) {
    const rs = rank.get(e.source);
    const rt = rank.get(e.target);
    if (rs === undefined) continue;
    if (rt === undefined) continue;
    if (rt < rs) back.add(e.id);
  }
  return back;
}

/** ¿Una arista adicional source→target sería “back” según el grafo actual? */
export function wouldConnectionBeBackEdge(nodes: Node[], edges: Edge[], source: string, target: string): boolean {
  const tmpId = "__pending-connection__";
  const synthetic = [...edges, { id: tmpId, source, target } as Edge];
  return computeBackEdgeIdsFromReactFlow(nodes, synthetic).has(tmpId);
}

/** Reasigna `type` flowBackEdge | smoothstep según ranks (tras mover aristas en el editor). */
export function reapplyEdgeRoutingTypes(nodes: Node[], edges: Edge[]): Edge[] {
  const back = computeBackEdgeIdsFromReactFlow(nodes, edges);
  return edges.map((e) => {
    const isBack = back.has(e.id);
    const nextType = isBack ? "flowBackEdge" : "smoothstep";
    if (e.type === nextType) return e;
    return { ...e, type: nextType };
  });
}
