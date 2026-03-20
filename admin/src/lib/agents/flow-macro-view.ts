import type { Edge, Node } from "@xyflow/react";
import type { FlowGenericNodeData } from "@/components/agents/flow-generic-node";

type MacroGroupDef = {
  key: string;
  label: string;
  color: string;
  icon: FlowGenericNodeData["icon"];
};

type UiGroupMeta = {
  groupId?: string;
  groupLabel?: string;
  groupColor?: string;
  groupIcon?: string;
  groupCollapse?: boolean;
};

const TYPE_DEFAULTS: Record<number, Omit<MacroGroupDef, "key">> = {
  0: { label: "Inicio", color: "#22c55e", icon: "circle-play" },
  6: { label: "Escalación", color: "#dc2626", icon: "user-round" },
  7: { label: "Fin", color: "#ef4444", icon: "circle-stop" },
  9: { label: "Router", color: "#f97316", icon: "git-branch" },
  10: { label: "Agente", color: "#a855f7", icon: "brain-circuit" },
};

const FALLBACK: Omit<MacroGroupDef, "key"> = {
  label: "Otros",
  color: "#64748b",
  icon: "list-checks",
};

function readNodeData(node: Node): FlowGenericNodeData {
  return (node.data ?? {}) as FlowGenericNodeData;
}

function readUiMeta(node: Node): UiGroupMeta {
  const d = readNodeData(node);
  const cfg = d.config;
  if (!cfg || typeof cfg !== "object") return {};

  const uiRaw = cfg._ui;
  if (!uiRaw || typeof uiRaw !== "object" || Array.isArray(uiRaw)) return {};

  const ui = uiRaw as Record<string, unknown>;
  return {
    groupId: typeof ui.groupId === "string" && ui.groupId.trim() ? ui.groupId.trim() : undefined,
    groupLabel:
      typeof ui.groupLabel === "string" && ui.groupLabel.trim() ? ui.groupLabel.trim() : undefined,
    groupColor:
      typeof ui.groupColor === "string" && ui.groupColor.trim() ? ui.groupColor.trim() : undefined,
    groupIcon: typeof ui.groupIcon === "string" && ui.groupIcon.trim() ? ui.groupIcon.trim() : undefined,
    groupCollapse: typeof ui.groupCollapse === "boolean" ? ui.groupCollapse : undefined,
  };
}

/**
 * Derives the group key for a node. Priority:
 *  1. Explicit `_ui.groupId` from the flow document
 *  2. Agent nodes (type 10) → each agent is its own group (key = node.id)
 *  3. Node type number as fallback (e.g. "type:0", "type:9")
 */
function deriveGroupKey(node: Node): string {
  const meta = readUiMeta(node);
  if (meta.groupId) return meta.groupId;

  const d = readNodeData(node);
  const flowType = d.flowType;

  if (flowType === 10) return node.id;

  return `type:${flowType}`;
}

function resolveGroupDef(groupKey: string, members: Node[], sampleMeta?: UiGroupMeta): MacroGroupDef {
  const firstData = members.length > 0 ? readNodeData(members[0]) : undefined;
  const flowType = firstData?.flowType;
  const typeDefaults = flowType !== undefined ? TYPE_DEFAULTS[flowType] : undefined;

  const label =
    sampleMeta?.groupLabel ??
    (flowType === 10 && firstData ? firstData.label : undefined) ??
    typeDefaults?.label ??
    FALLBACK.label;

  return {
    key: groupKey,
    label: members.length > 1 ? `${label} (${members.length})` : label,
    color: sampleMeta?.groupColor ?? typeDefaults?.color ?? FALLBACK.color,
    icon: (sampleMeta?.groupIcon as FlowGenericNodeData["icon"]) ?? typeDefaults?.icon ?? FALLBACK.icon,
  };
}

function centroid(nodes: Node[]): { x: number; y: number } {
  if (nodes.length === 0) return { x: 0, y: 0 };
  const sum = nodes.reduce(
    (acc, n) => ({ x: acc.x + n.position.x, y: acc.y + n.position.y }),
    { x: 0, y: 0 }
  );
  return { x: sum.x / nodes.length, y: sum.y / nodes.length };
}

function buildGroupMap(nodes: Node[]): {
  nodeToGroup: Map<string, string>;
  groups: Map<string, { nodes: Node[]; metas: UiGroupMeta[] }>;
} {
  const nodeToGroup = new Map<string, string>();
  const groups = new Map<string, { nodes: Node[]; metas: UiGroupMeta[] }>();

  for (const n of nodes) {
    const key = deriveGroupKey(n);
    const meta = readUiMeta(n);
    nodeToGroup.set(n.id, key);
    if (!groups.has(key)) groups.set(key, { nodes: [], metas: [] });
    groups.get(key)!.nodes.push(n);
    groups.get(key)!.metas.push(meta);
  }

  return { nodeToGroup, groups };
}

function buildCollapsedGraph(
  nodes: Node[],
  edges: Edge[],
  collapsePredicate: (key: string, members: Node[], metas: UiGroupMeta[]) => boolean
): { nodes: Node[]; edges: Edge[] } {
  if (nodes.length === 0) return { nodes: [], edges: [] };

  const { groups } = buildGroupMap(nodes);
  const nodeById = new Map(nodes.map((n) => [n.id, n]));
  const nodeToVisible = new Map<string, string>();

  for (const [key, payload] of groups.entries()) {
    const collapsed = collapsePredicate(key, payload.nodes, payload.metas);
    for (const n of payload.nodes) {
      nodeToVisible.set(n.id, collapsed ? `grp:${key}` : n.id);
    }
  }

  const visibleNodes: Node[] = [];
  for (const n of nodes) {
    if (nodeToVisible.get(n.id) === n.id) visibleNodes.push(n);
  }

  for (const [key, payload] of groups.entries()) {
    const collapsed = collapsePredicate(key, payload.nodes, payload.metas);
    if (!collapsed) continue;

    const sampleMeta = payload.metas.find((m) => !!(m.groupLabel || m.groupColor || m.groupIcon));
    const def = resolveGroupDef(key, payload.nodes, sampleMeta);

    visibleNodes.push({
      id: `grp:${key}`,
      type: "flowGeneric",
      position: centroid(payload.nodes),
      data: {
        label: def.label,
        flowType: 900,
        icon: def.icon,
        accentColor: def.color,
        inputs: [{ id: "default", label: "Entrada" }],
        outputs: [{ id: "default", label: "Salida" }],
        config: {},
      } satisfies FlowGenericNodeData,
    });
  }

  const edgeMap = new Map<string, { source: string; target: string; labels: Set<string> }>();
  for (const e of edges) {
    const sv = nodeToVisible.get(e.source);
    const tv = nodeToVisible.get(e.target);
    if (!sv || !tv || sv === tv) continue;
    if (!nodeById.has(e.source) || !nodeById.has(e.target)) continue;

    const k = `${sv}->${tv}`;
    if (!edgeMap.has(k)) edgeMap.set(k, { source: sv, target: tv, labels: new Set<string>() });
    if (e.sourceHandle) edgeMap.get(k)!.labels.add(e.sourceHandle);
  }

  const visibleEdges: Edge[] = [...edgeMap.entries()].map(([k, v]) => ({
    id: `grp:${k}`,
    source: v.source,
    target: v.target,
    type: "smoothstep",
    label: v.labels.size > 0 ? [...v.labels].join(", ") : undefined,
  }));

  return { nodes: visibleNodes, edges: visibleEdges };
}

/**
 * Clustered view: collapse groups that have explicit `groupCollapse: true`
 * or contain 3+ nodes (except single-node groups like individual agents).
 */
export function buildClusteredDetailGraph(nodes: Node[], edges: Edge[]): { nodes: Node[]; edges: Edge[] } {
  return buildCollapsedGraph(nodes, edges, (_key, members, metas) => {
    if (metas.some((m) => m.groupCollapse === true)) return true;
    if (members.length < 3) return false;
    const hasAgent = members.some((n) => readNodeData(n).flowType === 10);
    return !hasAgent;
  });
}

/**
 * Macro view: collapse every group into a single macro node.
 */
export function buildMacroGraph(nodes: Node[], edges: Edge[]): { nodes: Node[]; edges: Edge[] } {
  return buildCollapsedGraph(nodes, edges, () => true);
}
