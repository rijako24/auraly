import type { FlowPort } from "@/types/entities";

/**
 * Fragmento de JSON Schema con extensión estándar x-dynamicOutputPort.
 * Los puertos dinámicos se derivan del config según el esquema del catálogo (una sola fuente de verdad).
 */
export type FlowSchemaNode = {
  type?: string;
  properties?: Record<string, FlowSchemaNode>;
  items?: FlowSchemaNode;
  "x-dynamicOutputPort"?: boolean;
};

function parseRoot(json: string): FlowSchemaNode | null {
  try {
    const v = JSON.parse(json) as FlowSchemaNode;
    return v && typeof v === "object" ? v : null;
  } catch {
    return null;
  }
}

function portsFromMarkedValue(schema: FlowSchemaNode, value: unknown): FlowPort[] {
  if (value == null) return [];
  if (typeof value === "string") {
    const s = value.trim();
    return s ? [{ id: s, label: s }] : [];
  }
  if (Array.isArray(value)) {
    const asStrings = value.filter((x): x is string => typeof x === "string" && x.trim().length > 0);
    if (asStrings.length === value.length || schema.items?.type === "string") {
      return asStrings.map((id) => ({ id, label: id }));
    }
  }
  return [];
}

/**
 * Recorre el schema y acumula puertos donde aparezca x-dynamicOutputPort: true.
 */
export function collectDynamicPortsFromSchema(schema: FlowSchemaNode | null | undefined, data: unknown): FlowPort[] {
  if (!schema) return [];

  if (schema["x-dynamicOutputPort"]) {
    return portsFromMarkedValue(schema, data);
  }

  if (schema.properties && data && typeof data === "object" && !Array.isArray(data)) {
    const obj = data as Record<string, unknown>;
    const ports: FlowPort[] = [];
    for (const [key, sub] of Object.entries(schema.properties)) {
      ports.push(...collectDynamicPortsFromSchema(sub, obj[key]));
    }
    return ports;
  }

  if (schema.type === "array" && schema.items && Array.isArray(data)) {
    const ports: FlowPort[] = [];
    for (const item of data) {
      ports.push(...collectDynamicPortsFromSchema(schema.items, item));
    }
    return ports;
  }

  return [];
}

function mergePortLists(staticPorts: FlowPort[], dynamicPorts: FlowPort[]): FlowPort[] {
  const byId = new Map<string, FlowPort>();
  for (const p of staticPorts) {
    byId.set(p.id, p);
  }
  for (const p of dynamicPorts) {
    if (!byId.has(p.id)) {
      byId.set(p.id, p);
    }
  }
  return [...byId.values()];
}

/**
 * Puertos de salida finales: estáticos del catálogo + dinámicos según config y schema.
 */
export function resolveDynamicOutputs(
  configSchemaJson: string,
  config: Record<string, unknown>,
  staticOutputs: FlowPort[]
): FlowPort[] {
  const root = parseRoot(configSchemaJson);
  const dynamic = root ? collectDynamicPortsFromSchema(root, config) : [];
  const merged = mergePortLists(staticOutputs, dynamic);
  if (merged.length > 0) return merged;
  return [{ id: "default", label: "Salida" }];
}
