import type { Edge, Node } from "@xyflow/react";
import type { FlowDocument, FlowDocumentEdge, FlowDocumentNode, FlowNodeCatalogEntry, FlowSubNodeSet } from "@/types/entities";
import type { FlowGenericNodeData } from "@/components/agents/flow-generic-node";
import { styleFlowEdge } from "@/lib/agents/flow-edge-styles";
import { computeBackEdgeIdsFromFlowDocument } from "@/lib/agents/flow-graph-ranks";

function defaultPosition(index: number) {
  const col = index % 4;
  const row = Math.floor(index / 4);
  return { x: 32 + col * 200, y: 32 + row * 118 };
}

function readUiPosition(config: Record<string, unknown>, index: number) {
  const ui = config._ui as { x?: number; y?: number } | undefined;
  if (ui && typeof ui.x === "number" && typeof ui.y === "number") return { x: ui.x, y: ui.y };
  return defaultPosition(index);
}

/** Resuelve la fila del catálogo para un nodo del documento persistido. */
export function resolveCatalogEntryForDocumentNode(
  flowType: number,
  config: Record<string, unknown>,
  catalog: FlowNodeCatalogEntry[] | null | undefined
): FlowNodeCatalogEntry | undefined {
  if (!catalog?.length) return undefined;
  const catalogKey = typeof config.catalogKey === "string" ? config.catalogKey : undefined;
  if (catalogKey) {
    const byKey = catalog.find((c) => c.id === catalogKey);
    if (byKey) return byKey;
  }
  return catalog.find((c) => c.type === flowType);
}

export function parseFlowDocument(json: string): FlowDocument {
  const raw = JSON.parse(json) as Partial<FlowDocument>;
  return {
    variables: Array.isArray(raw.variables) ? raw.variables : [],
    intentionSchema: Array.isArray(raw.intentionSchema) ? raw.intentionSchema : [],
    routingIntents: Array.isArray(raw.routingIntents) ? raw.routingIntents : [],
    sessionConfig:
      raw.sessionConfig && typeof raw.sessionConfig === "object" ? (raw.sessionConfig as Record<string, unknown>) : {},
    engineSettings:
      raw.engineSettings && typeof raw.engineSettings === "object"
        ? (raw.engineSettings as Record<string, unknown>)
        : {},
    nodes: Array.isArray(raw.nodes) ? (raw.nodes as FlowDocumentNode[]) : [],
    edges: Array.isArray(raw.edges) ? (raw.edges as FlowDocumentEdge[]) : [],
    extractionInstructions: typeof raw.extractionInstructions === "string" ? raw.extractionInstructions : undefined,
  };
}

/**
 * Convierte el documento de flujo a nodos/aristas de React Flow.
 * Si se pasa `catalog`, cada nodo recibe inputs/outputs/schema del catálogo **antes** de montar,
 * para que los handles existan cuando se renderizan las aristas (evita edges “huérfanos”).
 */
export function flowDocumentToReactFlow(
  doc: FlowDocument,
  catalog?: FlowNodeCatalogEntry[] | null
): { nodes: Node[]; edges: Edge[] } {
  const nodes: Node[] = doc.nodes.map((n, i) => {
    const cfg =
      n.config && typeof n.config === "object" && !Array.isArray(n.config)
        ? { ...(n.config as Record<string, unknown>) }
        : {};

    const entry = resolveCatalogEntryForDocumentNode(n.type, cfg, catalog);

    const data: FlowGenericNodeData = {
      label: n.label ?? n.id,
      flowType: n.type,
      config: cfg,
    };

    if (entry) {
      data.catalogKey = entry.id;
      data.icon = entry.icon;
      data.accentColor = entry.color ?? undefined;
      data.inputs = entry.inputs;
      data.outputs = entry.outputs;
      data.configSchemaJson = entry.configSchemaJson;
    }

    if (n.subNodes) data.subNodes = n.subNodes;
    if (n.handlesIntent) data.handlesIntent = n.handlesIntent;

    const rfType = n.type === 10 || cfg.catalogKey === "agent" ? "flowAgent" : "flowGeneric";

    return {
      id: n.id,
      type: rfType,
      position: readUiPosition(cfg, i),
      data,
    };
  });

  const backIds = computeBackEdgeIdsFromFlowDocument(doc);
  const edges: Edge[] = doc.edges.map((e) => {
    const isBack = backIds.has(e.id);
    return styleFlowEdge(
      {
        id: e.id,
        source: e.sourceNodeId,
        target: e.targetNodeId,
        sourceHandle: e.portId ?? undefined,
        type: isBack ? "flowBackEdge" : "smoothstep",
      },
      { isBackEdge: isBack }
    );
  });

  return { nodes, edges };
}

export function reactFlowToFlowDocument(
  base: FlowDocument,
  rfNodes: Node[],
  rfEdges: Edge[]
): FlowDocument {
  const posById = new Map(rfNodes.map((n) => [n.id, n.position]));

  const nodes: FlowDocumentNode[] = rfNodes.map((n) => {
    const data = n.data as {
      label?: string;
      flowType?: number;
      config?: Record<string, unknown>;
      subNodes?: FlowSubNodeSet;
      handlesIntent?: string;
    };
    const pos = posById.get(n.id);
    const cfg = { ...(data.config ?? {}) };
    if (pos) {
      const prevUi = cfg._ui;
      const baseUi =
        prevUi && typeof prevUi === "object" && !Array.isArray(prevUi)
          ? { ...(prevUi as Record<string, unknown>) }
          : {};
      cfg._ui = { ...baseUi, x: pos.x, y: pos.y };
    }
    const node: FlowDocumentNode = {
      id: n.id,
      type: typeof data.flowType === "number" ? data.flowType : 0,
      label: data.label ?? n.id,
      config: cfg,
    };
    if (data.subNodes) node.subNodes = data.subNodes;
    if (data.handlesIntent) node.handlesIntent = data.handlesIntent;
    return node;
  });

  const edges: FlowDocumentEdge[] = rfEdges.map((e) => ({
    id: e.id,
    sourceNodeId: e.source,
    targetNodeId: e.target,
    portId: e.sourceHandle ?? null,
  }));

  return {
    ...base,
    nodes,
    edges,
  };
}

export function stringifyFlowDocument(doc: FlowDocument): string {
  return JSON.stringify(doc, null, 2);
}
