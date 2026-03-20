"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import {
  addEdge,
  Background,
  BackgroundVariant,
  Connection,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import {
  ArrowDownUp,
  ArrowLeft,
  ArrowLeftRight,
  MessageSquare,
  Play,
  Redo2,
  Save,
  Sparkles,
  Trash2,
  Undo2,
} from "lucide-react";

import { FlowAgentNode } from "@/components/agents/flow-agent-node";
import { FlowConfigEditor } from "@/components/agents/flow-config-editor";
import { FlowBackEdge } from "@/components/agents/flow-back-edge";
import {
  FlowGenericNode,
  type FlowGenericNodeData,
  type FlowLayoutOrientation,
} from "@/components/agents/flow-generic-node";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Separator } from "@/components/ui/separator";
import { Textarea } from "@/components/ui/textarea";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { layoutFlowWithDagre, type FlowLayoutDirection } from "@/lib/agents/flow-auto-layout";
import {
  flowDocumentToReactFlow,
  parseFlowDocument,
  reactFlowToFlowDocument,
  stringifyFlowDocument,
} from "@/lib/agents/flow-document";
import { FLOW_CATALOG_DRAG_MIME, styleFlowConnection } from "@/lib/agents/flow-edge-styles";
import { reapplyEdgeRoutingTypes } from "@/lib/agents/flow-graph-ranks";
import { cn } from "@/lib/utils";
import {
  useAgentChat,
  useAgentWorkflow,
  useNodeCatalog,
  useSaveAgentWorkflow,
} from "@/hooks/use-agents";
import type { FlowDocument, FlowNodeCatalogEntry } from "@/types/entities";
import { useToast } from "@/hooks/use-toast";

const nodeTypes = { flowGeneric: FlowGenericNode, flowAgent: FlowAgentNode };
const edgeTypes = { flowBackEdge: FlowBackEdge };

/** Encuadre inicial / tras auto-layout: más alejado para flujos grandes. */
const FLOW_FIT_OVERVIEW = {
  padding: 0.06,
  maxZoom: 0.72,
  duration: 220,
} as const;

const MAX_FLOW_HISTORY = 50;

type AgentWorkspaceProps = {
  agentId: string;
  agentName: string;
};

function cloneFlowState(nodes: Node[], edges: Edge[]) {
  return { nodes: structuredClone(nodes), edges: structuredClone(edges) };
}

function AgentWorkspaceInner({ agentId, agentName }: AgentWorkspaceProps) {
  const { toast } = useToast();
  const { fitView, screenToFlowPosition } = useReactFlow();
  const { data: workflow, isLoading, isError, refetch } = useAgentWorkflow(agentId);
  const { data: catalog } = useNodeCatalog();
  const saveMutation = useSaveAgentWorkflow(agentId);
  const chatMutation = useAgentChat(agentId);

  const [baseDoc, setBaseDoc] = useState<FlowDocument | null>(null);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [propLabel, setPropLabel] = useState("");
  const [propConfig, setPropConfig] = useState<Record<string, unknown>>({});
  const [propOrientation, setPropOrientation] = useState<FlowLayoutOrientation>("horizontal");
  const [showRawConfig, setShowRawConfig] = useState(false);
  const [chatOpen, setChatOpen] = useState(true);
  const [chatLines, setChatLines] = useState<{ role: "user" | "assistant"; text: string }[]>([]);
  const [chatInput, setChatInput] = useState("");

  const historyPast = useRef<{ nodes: Node[]; edges: Edge[] }[]>([]);
  const historyFuture = useRef<{ nodes: Node[]; edges: Edge[] }[]>([]);
  const nodesRef = useRef<Node[]>([]);
  const edgesRef = useRef<Edge[]>([]);
  const initialFitWorkflowKey = useRef<string | null>(null);
  /** Evita doble creación de nodo si el navegador dispara click tras arrastrar desde la biblioteca. */
  const catalogDragEndAt = useRef(0);

  useEffect(() => {
    nodesRef.current = nodes;
  }, [nodes]);
  useEffect(() => {
    edgesRef.current = edges;
  }, [edges]);

  const commitBeforeChange = useCallback(() => {
    historyPast.current.push(cloneFlowState(nodesRef.current, edgesRef.current));
    if (historyPast.current.length > MAX_FLOW_HISTORY) {
      historyPast.current.shift();
    }
    historyFuture.current = [];
  }, []);

  const undo = useCallback(() => {
    if (historyPast.current.length === 0) {
      toast({ title: "Nada que deshacer", variant: "destructive" });
      return;
    }
    const current = cloneFlowState(nodesRef.current, edgesRef.current);
    const prev = historyPast.current.pop()!;
    historyFuture.current.unshift(current);
    setNodes(prev.nodes);
    setEdges(prev.edges);
    setSelectedId(null);
  }, [setNodes, setEdges, toast]);

  const redo = useCallback(() => {
    if (historyFuture.current.length === 0) {
      toast({ title: "Nada que rehacer", variant: "destructive" });
      return;
    }
    const current = cloneFlowState(nodesRef.current, edgesRef.current);
    const next = historyFuture.current.shift()!;
    historyPast.current.push(current);
    setNodes(next.nodes);
    setEdges(next.edges);
    setSelectedId(null);
  }, [setNodes, setEdges, toast]);

  useEffect(() => {
    historyPast.current = [];
    historyFuture.current = [];
    initialFitWorkflowKey.current = null;
  }, [agentId, workflow?.flowDefinitionId]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target?.closest("input, textarea, select, [contenteditable='true']")) return;

      const mod = e.ctrlKey || e.metaKey;
      if (!mod) return;

      if (e.key === "z" || e.key === "Z") {
        e.preventDefault();
        if (e.shiftKey) redo();
        else undo();
      } else if (e.key === "y" || e.key === "Y") {
        e.preventDefault();
        redo();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [undo, redo]);

  useEffect(() => {
    const key = workflow?.flowDefinitionId ?? null;
    if (!key || nodes.length === 0) return;
    if (initialFitWorkflowKey.current === key) return;
    initialFitWorkflowKey.current = key;
    requestAnimationFrame(() => {
      void fitView({ padding: 0.12, duration: 200 });
    });
  }, [workflow?.flowDefinitionId, nodes.length, fitView]);

  useEffect(() => {
    if (!workflow?.definitionJson) return;
    try {
      const doc = parseFlowDocument(workflow.definitionJson);
      setBaseDoc(doc);
      const { nodes: n, edges: e } = flowDocumentToReactFlow(doc, catalog ?? null);
      const laidOut = n.length > 0 ? layoutFlowWithDagre(n, e, "LR") : n;
      setNodes(laidOut);
      setEdges(e);
    } catch {
      setBaseDoc(null);
      setNodes([]);
      setEdges([]);
    }
  }, [workflow?.flowDefinitionId, workflow?.definitionJson, catalog, setNodes, setEdges]);

  const groupedCatalog = useMemo(() => {
    const m = new Map<string, FlowNodeCatalogEntry[]>();
    for (const c of catalog ?? []) {
      if (c.type === 8 || c.id === "extract") continue;
      const cat = (c.category ?? "").trim() || "General";
      if (!m.has(cat)) m.set(cat, []);
      m.get(cat)!.push(c);
    }
    return [...m.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [catalog]);

  const selectedNode = useMemo(
    () => (selectedId ? nodes.find((n) => n.id === selectedId) : undefined),
    [nodes, selectedId]
  );

  useEffect(() => {
    if (!selectedNode) {
      setPropLabel("");
      setPropConfig({});
      setPropOrientation("horizontal");
      setShowRawConfig(false);
      return;
    }
    const d = selectedNode.data as FlowGenericNodeData;
    setPropLabel(d.label ?? "");
    const { _ui, ...rest } = d.config ?? {};
    const ui = _ui as { orientation?: string } | undefined;
    setPropOrientation(ui?.orientation === "vertical" ? "vertical" : "horizontal");
    setPropConfig({ ...rest });
    setShowRawConfig(false);
  }, [selectedNode]);

  const selectedCatalogEntry = useMemo(() => {
    if (!selectedNode || !catalog?.length) return undefined;
    const d = selectedNode.data as FlowGenericNodeData;
    return catalog.find((c) => c.id === d.catalogKey) ?? catalog.find((c) => c.type === d.flowType);
  }, [selectedNode, catalog]);

  const onNodesChangeWrapped = useCallback(
    (changes: NodeChange<Node>[]) => {
      if (changes.some((c) => c.type === "remove")) {
        commitBeforeChange();
      }
      onNodesChange(changes);
    },
    [onNodesChange, commitBeforeChange]
  );

  const onEdgesChangeWrapped = useCallback(
    (changes: EdgeChange<Edge>[]) => {
      if (changes.some((c) => c.type === "remove")) {
        commitBeforeChange();
      }
      onEdgesChange(changes);
    },
    [onEdgesChange, commitBeforeChange]
  );

  const onNodeDragStart = useCallback(() => {
    commitBeforeChange();
  }, [commitBeforeChange]);

  const onConnect = useCallback(
    (params: Connection) => {
      commitBeforeChange();
      const id = `e-${params.source}-${params.target}-${Date.now()}`;
      setEdges((eds) =>
        addEdge(styleFlowConnection(params, id, { nodes: nodesRef.current, edges: eds }), eds)
      );
    },
    [commitBeforeChange, setEdges]
  );

  const onReconnect = useCallback(
    (oldEdge: Edge, newConnection: Connection) => {
      commitBeforeChange();
      setEdges((els) => {
        const next = els.map((e) =>
          e.id === oldEdge.id
            ? {
                ...e,
                source: newConnection.source!,
                target: newConnection.target!,
                sourceHandle: newConnection.sourceHandle ?? undefined,
                targetHandle: newConnection.targetHandle ?? undefined,
              }
            : e
        );
        return reapplyEdgeRoutingTypes(nodesRef.current, next);
      });
    },
    [commitBeforeChange, setEdges]
  );

  const runAutoLayout = useCallback(
    (direction: FlowLayoutDirection) => {
      if (nodesRef.current.length === 0) return;
      commitBeforeChange();
      const laidOut = layoutFlowWithDagre(nodesRef.current, edgesRef.current, direction);
      setNodes(laidOut);
      requestAnimationFrame(() => {
        void fitView(FLOW_FIT_OVERVIEW);
      });
    },
    [commitBeforeChange, setNodes, fitView]
  );

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  }, []);

  const addCatalogNodeAtPosition = useCallback(
    (entry: FlowNodeCatalogEntry, position: { x: number; y: number }) => {
      const id = `${entry.id}-${Date.now()}`;
      const newNode: Node = {
        id,
        type: "flowGeneric",
        position: { x: position.x - 110, y: position.y - 44 },
        data: {
          label: entry.name,
          flowType: entry.type,
          catalogKey: entry.id,
          configSchemaJson: entry.configSchemaJson,
          icon: entry.icon,
          accentColor: entry.color ?? undefined,
          inputs: entry.inputs,
          outputs: entry.outputs,
          config: {},
        } satisfies FlowGenericNodeData,
      };
      setNodes((ns) => [...ns, newNode]);
    },
    [setNodes]
  );

  const onDropOnCanvas = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      const raw = e.dataTransfer.getData(FLOW_CATALOG_DRAG_MIME);
      if (!raw) return;
      let entry: FlowNodeCatalogEntry;
      try {
        entry = JSON.parse(raw) as FlowNodeCatalogEntry;
      } catch {
        return;
      }
      commitBeforeChange();
      const position = screenToFlowPosition({ x: e.clientX, y: e.clientY });
      addCatalogNodeAtPosition(entry, position);
    },
    [screenToFlowPosition, commitBeforeChange, addCatalogNodeAtPosition]
  );

  const applyOrientationToCanvas = useCallback(
    (orientation: FlowLayoutOrientation) => {
      if (!selectedId) return;
      commitBeforeChange();
      setPropOrientation(orientation);
      setNodes((ns) =>
        ns.map((n) => {
          if (n.id !== selectedId) return n;
          const data = n.data as FlowGenericNodeData;
          const prevCfg = data.config ?? {};
          const prevUiRaw = prevCfg._ui;
          const nextUi =
            prevUiRaw && typeof prevUiRaw === "object" && !Array.isArray(prevUiRaw)
              ? { ...(prevUiRaw as Record<string, unknown>) }
              : {};
          if (orientation === "vertical") {
            nextUi.orientation = "vertical";
          } else {
            delete nextUi.orientation;
          }
          const config: Record<string, unknown> = { ...prevCfg };
          if (Object.keys(nextUi).length > 0) {
            config._ui = nextUi;
          } else {
            delete config._ui;
          }
          return { ...n, data: { ...data, config } };
        })
      );
    },
    [selectedId, setNodes, commitBeforeChange]
  );

  const deleteSelectedNode = useCallback(() => {
    if (!selectedNode) return;
    const d = selectedNode.data as FlowGenericNodeData;
    if (d.flowType === 0) {
      toast({
        title: "No se puede eliminar",
        description: "El nodo Inicio es obligatorio para el flujo.",
        variant: "destructive",
      });
      return;
    }
    commitBeforeChange();
    const id = selectedNode.id;
    setNodes((ns) => ns.filter((n) => n.id !== id));
    setEdges((es) => es.filter((e) => e.source !== id && e.target !== id));
    setSelectedId(null);
    toast({ title: "Nodo eliminado", description: "Guarda el flujo para persistir los cambios." });
  }, [selectedNode, setNodes, setEdges, toast, commitBeforeChange]);

  const applyNodeProps = () => {
    if (!selectedId) return;
    commitBeforeChange();
    const config: Record<string, unknown> = { ...propConfig };
    const prev = nodes.find((n) => n.id === selectedId);
    const prevData = prev?.data as FlowGenericNodeData | undefined;
    const prevCfg = prevData?.config ?? {};
    const prevUiRaw = prevCfg._ui;
    const nextUi =
      prevUiRaw && typeof prevUiRaw === "object" && !Array.isArray(prevUiRaw)
        ? { ...(prevUiRaw as Record<string, unknown>) }
        : {};
    if (propOrientation === "vertical") {
      nextUi.orientation = "vertical";
    } else {
      delete nextUi.orientation;
    }
    if (Object.keys(nextUi).length > 0) {
      config._ui = nextUi;
    }

    setNodes((ns) =>
      ns.map((n) =>
        n.id === selectedId
          ? {
              ...n,
              data: {
                ...n.data,
                label: propLabel,
                config,
              },
            }
          : n
      )
    );
    toast({ title: "Nodo actualizado", description: "Los cambios están en el canvas (guarda el flujo para persistir)." });
  };

  const handleSave = async () => {
    if (!baseDoc || !workflow) return;
    const merged = reactFlowToFlowDocument(baseDoc, nodes, edges);
    try {
      await saveMutation.mutateAsync({
        name: workflow.name,
        description: workflow.description,
        definitionJson: stringifyFlowDocument(merged),
      });
      setBaseDoc(merged);
      toast({ title: "Flujo guardado" });
      await refetch();
    } catch {
      toast({ title: "Error al guardar", variant: "destructive" });
    }
  };

  const addCatalogNode = useCallback(
    (entry: FlowNodeCatalogEntry) => {
      commitBeforeChange();
      const id = `${entry.id}-${Date.now()}`;
      const newNode: Node = {
        id,
        type: "flowGeneric",
        position: { x: 120 + Math.random() * 80, y: 120 + Math.random() * 80 },
        data: {
          label: entry.name,
          flowType: entry.type,
          catalogKey: entry.id,
          configSchemaJson: entry.configSchemaJson,
          icon: entry.icon,
          accentColor: entry.color ?? undefined,
          inputs: entry.inputs,
          outputs: entry.outputs,
          config: {},
        } satisfies FlowGenericNodeData,
      };
      setNodes((ns) => [...ns, newNode]);
    },
    [commitBeforeChange, setNodes]
  );

  const sendChat = async (reset: boolean) => {
    const text = chatInput.trim();
    if (!text) return;
    setChatInput("");
    setChatLines((c) => [...c, { role: "user", text }]);
    try {
      const res = await chatMutation.mutateAsync({ message: text, resetSession: reset });
      const reply = res.botResponse || res.errorMessage || "(sin respuesta)";
      setChatLines((c) => [...c, { role: "assistant", text: reply }]);
    } catch {
      setChatLines((c) => [...c, { role: "assistant", text: "Error al contactar el API." }]);
    }
  };

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !workflow) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="flex h-[calc(100vh-3.5rem)] flex-col gap-2 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 shrink-0">
        <div className="flex items-center gap-2 min-w-0">
          <Button variant="ghost" size="icon" asChild>
            <Link href="/dashboard/agents" aria-label="Volver">
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
          <div className="min-w-0">
            <h1 className="text-lg font-semibold truncate">{agentName}</h1>
            <p className="text-xs text-muted-foreground truncate">Workspace · {workflow.name}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={undo} title="Ctrl+Z">
            <Undo2 className="h-4 w-4" />
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={redo} title="Ctrl+Y o Ctrl+Shift+Z">
            <Redo2 className="h-4 w-4" />
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => runAutoLayout("LR")}
            disabled={nodes.length === 0}
            title="Organizar con Dagre (izquierda → derecha)"
          >
            <ArrowLeftRight className="h-4 w-4 mr-1" />
            Auto layout
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => runAutoLayout("TB")}
            disabled={nodes.length === 0}
            title="Organizar con Dagre (arriba → abajo)"
          >
            <ArrowDownUp className="h-4 w-4 mr-1" />
            Vertical
          </Button>
          <Button variant="outline" size="sm" onClick={() => setChatOpen((v) => !v)}>
            <MessageSquare className="h-4 w-4 mr-1" />
            Chat
          </Button>
          <Button variant="outline" size="sm" onClick={() => sendChat(true)} disabled={chatMutation.isPending}>
            <Play className="h-4 w-4 mr-1" />
            Test (reiniciar sesión)
          </Button>
          <Button size="sm" onClick={handleSave} disabled={saveMutation.isPending || !baseDoc}>
            <Save className="h-4 w-4 mr-1" />
            Guardar flujo
          </Button>
        </div>
      </div>

      <div className="flex flex-1 min-h-0 gap-2">
        <aside className="w-52 shrink-0 rounded-lg border bg-card flex flex-col overflow-hidden">
          <div className="p-2 border-b text-xs font-medium text-muted-foreground leading-snug">
            Biblioteca
            <span className="block font-normal text-[10px] text-muted-foreground/90 mt-0.5">
              Clic para añadir · Arrastra al lienzo
            </span>
          </div>
          <ScrollArea className="flex-1 p-2">
            <div className="space-y-3">
              {groupedCatalog.map(([category, items]) => (
                <div key={category}>
                  <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground mb-1 px-1">
                    {category}
                  </div>
                  <div className="space-y-0.5">
                    {items.map((c) => (
                      <button
                        key={c.id}
                        type="button"
                        draggable
                        onDragStart={(e) => {
                          e.dataTransfer.setData(FLOW_CATALOG_DRAG_MIME, JSON.stringify(c));
                          e.dataTransfer.effectAllowed = "copy";
                        }}
                        onDragEnd={() => {
                          catalogDragEndAt.current = Date.now();
                        }}
                        onClick={() => {
                          if (Date.now() - catalogDragEndAt.current < 400) return;
                          addCatalogNode(c);
                        }}
                        className="w-full text-left rounded-md px-2 py-1.5 text-sm hover:bg-accent transition-colors flex items-center gap-2 cursor-grab active:cursor-grabbing"
                      >
                        {c.color ? (
                          <span className="h-2 w-2 shrink-0 rounded-full" style={{ backgroundColor: c.color }} />
                        ) : (
                          <span className="h-2 w-2 shrink-0 rounded-full bg-muted-foreground/40" />
                        )}
                        <span className="truncate">{c.name}</span>
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </ScrollArea>
        </aside>

        <div className="flex-1 min-w-0 rounded-lg border bg-background overflow-hidden relative">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChangeWrapped}
            onEdgesChange={onEdgesChangeWrapped}
            onConnect={onConnect}
            onNodeDragStart={onNodeDragStart}
            onDragOver={onDragOver}
            onDrop={onDropOnCanvas}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            defaultEdgeOptions={{ type: "smoothstep" }}
            edgesReconnectable
            onReconnect={onReconnect}
            onNodeClick={(_, n) => setSelectedId(n.id)}
            onPaneClick={() => setSelectedId(null)}
            proOptions={{ hideAttribution: true }}
            className="bg-muted/30"
            minZoom={0.04}
            maxZoom={2}
            deleteKeyCode={["Backspace", "Delete"]}
            nodesDraggable
            nodesConnectable
            elementsSelectable
          >
            <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
            <Controls />
            <MiniMap className="!bg-card !border-border" />
          </ReactFlow>
        </div>

        <aside
          className={cn(
            "shrink-0 flex flex-col gap-2 overflow-hidden transition-all duration-200",
            chatOpen ? "w-[min(100%,380px)]" : "w-72"
          )}
        >
          <div className="rounded-lg border bg-card flex flex-col flex-1 min-h-0 overflow-hidden">
            <div className="p-2 border-b flex items-center gap-2 text-sm font-medium">
              <Sparkles className="h-4 w-4" />
              Propiedades
            </div>
            <ScrollArea className="flex-1 p-3">
              {selectedNode ? (
                <div className="space-y-3">
                  <div className="space-y-1">
                    <Label>Etiqueta</Label>
                    <Input value={propLabel} onChange={(e) => setPropLabel(e.target.value)} />
                  </div>
                  <div className="space-y-1">
                    <Label>Tipo de nodo</Label>
                    <p className="text-xs text-muted-foreground">
                      {selectedCatalogEntry?.name ?? `Tipo ${(selectedNode.data as FlowGenericNodeData).flowType}`}
                      <span className="ml-1 font-mono opacity-70">
                        ({(selectedNode.data as FlowGenericNodeData).flowType})
                      </span>
                    </p>
                  </div>
                  <div className="space-y-2">
                    <Label>Disposición en canvas</Label>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        size="sm"
                        variant={propOrientation === "horizontal" ? "default" : "outline"}
                        className="flex-1"
                        onClick={() => applyOrientationToCanvas("horizontal")}
                      >
                        Horizontal
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant={propOrientation === "vertical" ? "default" : "outline"}
                        className="flex-1"
                        onClick={() => applyOrientationToCanvas("vertical")}
                      >
                        Vertical
                      </Button>
                    </div>
                    <p className="text-[10px] text-muted-foreground">
                      Solo afecta la vista del editor; se aplica al instante. Guarda el flujo para persistir.
                    </p>
                  </div>
                  {!showRawConfig ? (
                    <FlowConfigEditor
                      schemaJson={selectedCatalogEntry?.configSchemaJson ?? "{}"}
                      value={propConfig}
                      onChange={setPropConfig}
                    />
                  ) : (
                    <div className="space-y-1">
                      <Label className="text-xs">Config (JSON)</Label>
                      <Textarea
                        value={JSON.stringify(propConfig, null, 2)}
                        onChange={(e) => {
                          try {
                            setPropConfig(JSON.parse(e.target.value || "{}") as Record<string, unknown>);
                          } catch {
                            /* espera JSON válido */
                          }
                        }}
                        rows={12}
                        className="font-mono text-xs"
                      />
                    </div>
                  )}
                  <div className="flex flex-wrap gap-2">
                    <Button size="sm" variant="outline" type="button" onClick={() => setShowRawConfig((v) => !v)}>
                      {showRawConfig ? "Editor guiado" : "JSON avanzado"}
                    </Button>
                    <Button size="sm" onClick={applyNodeProps}>
                      Aplicar al nodo
                    </Button>
                    <Button size="sm" variant="destructive" type="button" onClick={deleteSelectedNode}>
                      <Trash2 className="h-4 w-4 mr-1" />
                      Borrar nodo
                    </Button>
                  </div>
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">Selecciona un nodo en el canvas.</p>
              )}
            </ScrollArea>
          </div>

          {chatOpen && (
            <div className="rounded-lg border bg-card flex flex-col h-[min(40vh,320px)] shrink-0 overflow-hidden">
              <div className="p-2 border-b text-sm font-medium flex items-center gap-2">
                <MessageSquare className="h-4 w-4" />
                Playground
              </div>
              <ScrollArea className="flex-1 p-2">
                <div className="space-y-2">
                  {chatLines.length === 0 && (
                    <p className="text-xs text-muted-foreground">Escribe un mensaje para probar el agente.</p>
                  )}
                  {chatLines.map((line, i) => (
                    <div
                      key={i}
                      className={cn(
                        "text-xs rounded-md px-2 py-1.5",
                        line.role === "user" ? "bg-primary/10 ml-4" : "bg-muted mr-4"
                      )}
                    >
                      <span className="font-medium text-[10px] uppercase text-muted-foreground">{line.role}</span>
                      <p className="whitespace-pre-wrap">{line.text}</p>
                    </div>
                  ))}
                </div>
              </ScrollArea>
              <Separator />
              <div className="p-2 flex gap-2">
                <Input
                  placeholder="Mensaje…"
                  value={chatInput}
                  onChange={(e) => setChatInput(e.target.value)}
                  onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    void sendChat(false);
                  }
                }}
                />
                <Button size="sm" onClick={() => sendChat(false)} disabled={chatMutation.isPending}>
                  Enviar
                </Button>
              </div>
            </div>
          )}
        </aside>
      </div>
    </div>
  );
}

export function AgentWorkspace(props: AgentWorkspaceProps) {
  return (
    <ReactFlowProvider>
      <AgentWorkspaceInner {...props} />
    </ReactFlowProvider>
  );
}
