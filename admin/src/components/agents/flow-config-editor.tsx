"use client";

import { useMemo, useState } from "react";
import { ChevronDown, Plus, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";

/** Claves que edita el orquestador (FlowOrchestrationService); no van al panel de "extras". */
export const ORCHESTRATOR_CONFIG_KEYS = ["executeWhen", "setFlags", "setVariables"] as const;

/** JSON Schema del catálogo + extensiones x-* (JSON Schema oficial para vendor extensions). */
export type FlowJsonSchema = {
  type?: string;
  properties?: Record<string, FlowJsonSchema>;
  items?: FlowJsonSchema;
  required?: string[];
  title?: string;
  description?: string;
  enum?: unknown[];
  const?: unknown;
  additionalProperties?: boolean | FlowJsonSchema;
  anyOf?: FlowJsonSchema[];
  oneOf?: FlowJsonSchema[];
  /** Si true, cada valor de este campo en config genera un puerto de salida en el canvas. */
  "x-dynamicOutputPort"?: boolean;
};

/** Esquema fijo para opciones que lee el motor en todos los nodos. */
export const ORCHESTRATOR_SCHEMA: FlowJsonSchema = {
  type: "object",
  properties: {
    executeWhen: {
      type: "object",
      title: "Condición executeWhen",
      description: "Si no se cumple, el nodo se omite (puerto skipped).",
      properties: {
        type: {
          type: "string",
          title: "Tipo",
          enum: [
            "FlagIsTrue",
            "FlagIsFalse",
            "VariableIsNull",
            "VariableIsNotNull",
            "VariableEquals",
            "And",
            "Or",
          ],
        },
        parameters: {
          type: "object",
          title: "Parámetros",
          additionalProperties: { type: "string" },
        },
        conditions: {
          type: "array",
          title: "Sub-condiciones (And / Or)",
          items: { type: "object" },
        },
      },
    },
    setFlags: {
      type: "object",
      title: "setFlags (tras ejecutar el nodo)",
      additionalProperties: { type: "boolean" },
    },
    setVariables: {
      type: "object",
      title: "setVariables (tras ejecutar el nodo)",
      additionalProperties: { type: "string" },
    },
  },
};

function parseSchema(json: string): FlowJsonSchema | null {
  try {
    const v = JSON.parse(json) as FlowJsonSchema;
    return v && typeof v === "object" ? v : null;
  } catch {
    return null;
  }
}

function getAt(obj: Record<string, unknown>, key: string): unknown {
  return obj[key];
}

function setAt(obj: Record<string, unknown>, key: string, val: unknown): Record<string, unknown> {
  const next = { ...obj };
  if (val === undefined) delete next[key];
  else next[key] = val;
  return next;
}

function isPlainObject(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function pickOrchestratorSubset(value: Record<string, unknown>): Record<string, unknown> {
  const o: Record<string, unknown> = {};
  for (const k of ORCHESTRATOR_CONFIG_KEYS) {
    if (k in value) o[k] = value[k];
  }
  return o;
}

function mergeOrchestratorIntoValue(
  full: Record<string, unknown>,
  orch: Record<string, unknown>
): Record<string, unknown> {
  const next = { ...full };
  for (const k of ORCHESTRATOR_CONFIG_KEYS) {
    if (!Object.prototype.hasOwnProperty.call(orch, k)) continue;
    if (orch[k] === undefined) delete next[k];
    else next[k] = orch[k];
  }
  return next;
}

type AdditionalPropsHint = "string" | "boolean" | "json";

function getAdditionalPropertiesHint(schema: FlowJsonSchema): AdditionalPropsHint {
  const ap = schema.additionalProperties;
  if (ap === true || ap === undefined) return "json";
  if (typeof ap === "object" && ap !== null && ap.type === "boolean") return "boolean";
  if (typeof ap === "object" && ap !== null && ap.type === "string") return "string";
  return "json";
}

/** Propiedades extra en un objeto (p. ej. payment en Action, parámetros en executeWhen). */
function AdditionalObjectPropertiesEditor({
  schema,
  value,
  onChange,
  depth,
}: {
  schema: FlowJsonSchema;
  value: Record<string, unknown>;
  onChange: (v: Record<string, unknown>) => void;
  depth: number;
}) {
  const [newKey, setNewKey] = useState("");

  if (schema.additionalProperties === false) return null;

  const safeValue: Record<string, unknown> = isPlainObject(value) ? value : {};

  const declared = new Set(Object.keys(schema.properties ?? {}));
  const skip = new Set<string>(declared);
  skip.add("_ui");
  if (depth === 0) {
    for (const k of ORCHESTRATOR_CONFIG_KEYS) skip.add(k);
  }

  const extraKeys = Object.keys(safeValue).filter((k) => !skip.has(k));
  const hint = getAdditionalPropertiesHint(schema);

  const updateKey = (oldKey: string, newKeyVal: string, val: unknown) => {
    const next = { ...safeValue };
    delete next[oldKey];
    if (newKeyVal.trim()) next[newKeyVal.trim()] = val;
    onChange(next);
  };

  const removeKey = (k: string) => {
    const next = { ...safeValue };
    delete next[k];
    onChange(next);
  };

  const addPair = () => {
    const k = newKey.trim();
    if (!k || k in safeValue) return;
    const next = { ...safeValue };
    if (hint === "boolean") next[k] = false;
    else if (hint === "string") next[k] = "";
    else next[k] = {};
    onChange(next);
    setNewKey("");
  };

  const ap = schema.additionalProperties;
  const showSection =
    extraKeys.length > 0 || ap === true || (typeof ap === "object" && ap !== null);

  if (!showSection) return null;

  return (
    <div className={cn("space-y-2 rounded-md border border-dashed border-border/80 p-2 bg-muted/10", depth > 0 && "mt-2")}>
      <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {depth === 0 ? "Propiedades adicionales (config libre)" : "Campos extra"}
      </p>
      {extraKeys.map((k) => (
        <div key={k} className="flex flex-col gap-1 sm:flex-row sm:items-start sm:gap-2">
          <Input
            className="h-8 text-xs font-mono sm:max-w-[140px]"
            value={k}
            onChange={(e) => updateKey(k, e.target.value, safeValue[k])}
          />
          <div className="flex-1 flex gap-1">
            {hint === "boolean" ? (
              <div className="flex items-center gap-2 h-8">
                <Switch
                  checked={safeValue[k] === true}
                  onCheckedChange={(c) => onChange(setAt(safeValue, k, c))}
                />
              </div>
            ) : hint === "string" ? (
              <Input
                className="h-8 text-xs flex-1"
                value={safeValue[k] === undefined || safeValue[k] === null ? "" : String(safeValue[k])}
                onChange={(e) => onChange(setAt(safeValue, k, e.target.value))}
              />
            ) : (
              <Textarea
                className="font-mono text-xs min-h-[48px] flex-1"
                value={
                  safeValue[k] === undefined
                    ? ""
                    : typeof safeValue[k] === "string"
                      ? (safeValue[k] as string)
                      : JSON.stringify(safeValue[k], null, 2)
                }
                onChange={(e) => {
                  const raw = e.target.value;
                  try {
                    onChange(setAt(safeValue, k, JSON.parse(raw || "null")));
                  } catch {
                    onChange(setAt(safeValue, k, raw));
                  }
                }}
              />
            )}
            <Button type="button" variant="ghost" size="icon" className="h-8 w-8 shrink-0" onClick={() => removeKey(k)}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>
      ))}
      <div className="flex flex-wrap items-center gap-2">
        <Input
          className="h-8 text-xs font-mono max-w-[160px]"
          placeholder="nueva clave"
          value={newKey}
          onChange={(e) => setNewKey(e.target.value)}
        />
        <Button type="button" variant="outline" size="sm" className="h-8 text-xs" onClick={addPair}>
          <Plus className="h-3 w-3 mr-1" />
          Añadir
        </Button>
      </div>
    </div>
  );
}

type FieldProps = {
  schema: FlowJsonSchema;
  value: unknown;
  label: string;
  required?: boolean;
  onChange: (v: unknown) => void;
  depth: number;
};

type AnyOfStringOrObjectProps = FieldProps & {
  stringSchema: FlowJsonSchema;
  objectSchema: FlowJsonSchema;
};

function AnyOfStringOrObjectField({
  schema,
  stringSchema,
  objectSchema,
  value,
  label,
  required,
  onChange,
  depth,
}: AnyOfStringOrObjectProps) {
  const title = schema.title ?? label;
  const desc = schema.description;
  const mode: "string" | "object" = isPlainObject(value) ? "object" : "string";

  return (
    <div className={cn("space-y-2", depth > 0 && "pl-2 border-l border-border ml-1")}>
      <div className="space-y-1">
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
      </div>
      <Select
        value={mode}
        onValueChange={(v) => {
          if (v === "string") onChange("");
          else onChange({});
        }}
      >
        <SelectTrigger className="h-8 text-xs w-full max-w-[320px]">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="string" className="text-xs">
            Clave de intención (texto)
          </SelectItem>
          <SelectItem value="object" className="text-xs">
            Condición estructurada (objeto)
          </SelectItem>
        </SelectContent>
      </Select>
      {mode === "string" ? (
        <SchemaField
          schema={{ ...stringSchema, title: stringSchema.title ?? title }}
          value={typeof value === "string" ? value : ""}
          label={label}
          onChange={onChange}
          depth={depth + 1}
          required={required}
        />
      ) : (
        <SchemaField
          schema={{ ...objectSchema, title: objectSchema.title ?? title }}
          value={isPlainObject(value) ? value : {}}
          label={label}
          onChange={onChange}
          depth={depth + 1}
          required={required}
        />
      )}
    </div>
  );
}

function SchemaField({ schema, value, label, required, onChange, depth }: FieldProps) {
  const st = schema.type;
  const title = schema.title ?? label;
  const desc = schema.description;
  const hasPropList = Boolean(schema.properties && Object.keys(schema.properties).length > 0);
  const isObject = hasPropList && (!st || st === "object");
  const freeformObject =
    (st === "object" || st === undefined) &&
    !hasPropList &&
    schema.additionalProperties !== false;

  const unionBranches = schema.anyOf ?? schema.oneOf;
  if (unionBranches && unionBranches.length > 0) {
    const stringBranch = unionBranches.find((b) => b.type === "string");
    const objectBranch = unionBranches.find((b) => b.type === "object");
    const isStringObjectPair =
      stringBranch &&
      objectBranch &&
      unionBranches.length === 2 &&
      unionBranches.every((b) => b === stringBranch || b === objectBranch);
    if (isStringObjectPair) {
      return (
        <AnyOfStringOrObjectField
          schema={schema}
          stringSchema={stringBranch}
          objectSchema={objectBranch}
          value={value}
          label={label}
          required={required}
          onChange={onChange}
          depth={depth}
        />
      );
    }
    const raw =
      value === undefined ? "" : typeof value === "string" ? value : JSON.stringify(value, null, 2);
    return (
      <div className={cn("space-y-1", depth > 0 && "pl-2 border-l border-border ml-1")}>
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}{" "}
          <span className="font-normal text-muted-foreground">(JSON — varios tipos)</span>
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        <Textarea
          className="font-mono text-xs min-h-[88px]"
          value={raw}
          onChange={(e) => {
            const t = e.target.value;
            const trimmed = t.trim();
            if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
              try {
                onChange(JSON.parse(t || "null"));
                return;
              } catch {
                onChange(t);
                return;
              }
            }
            onChange(t);
          }}
        />
      </div>
    );
  }

  if (schema.enum && schema.enum.length > 0) {
    const sval = value === undefined || value === null ? "" : String(value);
    const selVal = sval === "" ? "__none__" : sval;
    return (
      <div className={cn("space-y-1", depth > 0 && "pl-2 border-l border-border ml-1")}>
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        <Select value={selVal} onValueChange={(v) => onChange(v === "__none__" ? undefined : v)}>
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Selecciona…" />
          </SelectTrigger>
          <SelectContent>
            {!required && (
              <SelectItem value="__none__" className="text-xs text-muted-foreground">
                (vacío)
              </SelectItem>
            )}
            {schema.enum.map((ev) => (
              <SelectItem key={String(ev)} value={String(ev)} className="text-xs">
                {String(ev)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    );
  }

  if (st === "boolean") {
    const checked = value === true;
    return (
      <div className="flex items-center justify-between gap-2 rounded-md border border-border/60 px-2 py-1.5">
        <div>
          <Label className="text-xs font-medium">{title}</Label>
          {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        </div>
        <Switch checked={checked} onCheckedChange={(c) => onChange(c)} />
      </div>
    );
  }

  if (st === "number" || st === "integer") {
    const n = typeof value === "number" ? value : value === undefined || value === null ? "" : Number(value);
    return (
      <div className="space-y-1">
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        <Input
          type="number"
          className="h-8 text-xs"
          value={Number.isFinite(n as number) ? String(n) : ""}
          onChange={(e) => {
            const raw = e.target.value;
            if (raw === "") onChange(undefined);
            else onChange(Number(raw));
          }}
        />
      </div>
    );
  }

  if (st === "string") {
    const long =
      (title + (schema.description ?? "")).length > 80 || label === "instructions" || label === "prompt";
    const s = value === undefined || value === null ? "" : String(value);
    return (
      <div className="space-y-1">
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}
          {schema["x-dynamicOutputPort"] ? (
            <span className="ml-1 text-[10px] font-normal text-muted-foreground">(puerto)</span>
          ) : null}
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        {long ? (
          <Textarea className="font-mono text-xs min-h-[160px]" value={s} onChange={(e) => onChange(e.target.value)} />
        ) : (
          <Input className="h-8 text-xs" value={s} onChange={(e) => onChange(e.target.value)} />
        )}
      </div>
    );
  }

  if (st === "array") {
    const items = schema.items ?? { type: "string" };
    const arr = Array.isArray(value) ? (value as unknown[]) : [];

    const addItem = () => {
      if (items.type === "object" && items.properties) {
        const row: Record<string, unknown> = {};
        for (const [k, ps] of Object.entries(items.properties)) {
          if (ps.type === "string") row[k] = "";
          else if (ps.type === "boolean") row[k] = false;
          else if (ps.type === "number" || ps.type === "integer") row[k] = 0;
          else if (ps.type === "object" && ps.properties) row[k] = {};
        }
        onChange([...arr, row]);
      } else {
        onChange([...arr, ""]);
      }
    };

    const removeAt = (i: number) => {
      onChange(arr.filter((_, j) => j !== i));
    };

    return (
      <div className={cn("space-y-2", depth > 0 && "pl-2 border-l border-border ml-1")}>
        <div className="flex items-center justify-between gap-2">
          <div>
            <Label className="text-xs">
              {title}
              {required ? <span className="text-destructive"> *</span> : null}
            </Label>
            {schema["x-dynamicOutputPort"] ? (
              <p className="text-[10px] text-muted-foreground">Cada valor define un puerto de salida.</p>
            ) : null}
            {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
          </div>
          <Button type="button" variant="outline" size="sm" className="h-7 text-xs" onClick={addItem}>
            <Plus className="h-3 w-3 mr-1" />
            Añadir
          </Button>
        </div>
        <div className="space-y-2">
          {arr.map((item, i) => (
            <div key={i} className="rounded-md border border-border/70 bg-muted/20 p-2 space-y-2 relative group">
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="absolute top-1 right-1 h-7 w-7 opacity-70 hover:opacity-100"
                onClick={() => removeAt(i)}
                aria-label="Eliminar fila"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
              {items.type === "object" && items.properties ? (
                <ObjectFields
                  schema={items}
                  value={(item as Record<string, unknown>) ?? {}}
                  onChange={(next) => {
                    const na = [...arr];
                    na[i] = next;
                    onChange(na);
                  }}
                  depth={depth + 1}
                />
              ) : (
                <Input
                  className="h-8 text-xs pr-10"
                  value={item === undefined || item === null ? "" : String(item)}
                  onChange={(e) => {
                    const na = [...arr];
                    na[i] = e.target.value;
                    onChange(na);
                  }}
                />
              )}
            </div>
          ))}
          {arr.length === 0 && <p className="text-[10px] text-muted-foreground italic">Sin elementos</p>}
        </div>
      </div>
    );
  }

  if (freeformObject) {
    return (
      <div className={cn("space-y-1", depth > 0 && "pl-2 border-l border-border ml-1")}>
        <Label className="text-xs">
          {title}
          {required ? <span className="text-destructive"> *</span> : null}
        </Label>
        {desc ? <p className="text-[10px] text-muted-foreground">{desc}</p> : null}
        <AdditionalObjectPropertiesEditor
          schema={{
            type: "object",
            properties: {},
            additionalProperties: schema.additionalProperties === undefined ? true : schema.additionalProperties,
          }}
          value={(value as Record<string, unknown>) ?? {}}
          onChange={onChange}
          depth={depth}
        />
      </div>
    );
  }

  if (isObject) {
    return (
      <div className={cn("space-y-2", depth > 0 && "rounded-md border border-border/60 p-2 bg-muted/10")}>
        {depth === 0 ? null : (
          <p className="text-xs font-medium text-muted-foreground">
            {title}
            {required ? <span className="text-destructive"> *</span> : null}
          </p>
        )}
        <ObjectFields
          schema={schema}
          value={(value as Record<string, unknown>) ?? {}}
          onChange={(v) => onChange(v)}
          depth={depth}
        />
      </div>
    );
  }

  const raw = value === undefined ? "" : JSON.stringify(value, null, 2);
  return (
    <div className="space-y-1">
      <Label className="text-xs">{title} (JSON)</Label>
      <Textarea
        className="font-mono text-xs min-h-[56px]"
        value={raw}
        onChange={(e) => {
          try {
            onChange(JSON.parse(e.target.value || "null"));
          } catch {
            onChange(e.target.value);
          }
        }}
      />
    </div>
  );
}

function ObjectFields({
  schema,
  value,
  onChange,
  depth,
}: {
  schema: FlowJsonSchema;
  value: Record<string, unknown>;
  onChange: (v: Record<string, unknown>) => void;
  depth: number;
}) {
  const props = schema.properties ?? {};
  const required = new Set(schema.required ?? []);

  return (
    <div className="space-y-3">
      {Object.entries(props).map(([key, sub]) => (
        <SchemaField
          key={key}
          schema={sub}
          label={key}
          required={required.has(key)}
          value={getAt(value, key)}
          depth={depth + 1}
          onChange={(v) => onChange(setAt(value, key, v))}
        />
      ))}
      <AdditionalObjectPropertiesEditor schema={schema} value={value} onChange={onChange} depth={depth} />
    </div>
  );
}

function OrchestratorSection({
  value,
  onChange,
}: {
  value: Record<string, unknown>;
  onChange: (next: Record<string, unknown>) => void;
}) {
  const [open, setOpen] = useState(false);
  const subset = useMemo(() => pickOrchestratorSubset(value), [value]);

  return (
    <Collapsible open={open} onOpenChange={setOpen}>
      <CollapsibleTrigger className="flex w-full items-center justify-between rounded-md border border-border/70 bg-muted/30 px-2 py-1.5 text-left text-xs font-medium hover:bg-muted/50">
        <span>Opciones del orquestador</span>
        <ChevronDown className={cn("h-4 w-4 shrink-0 transition-transform", open && "rotate-180")} />
      </CollapsibleTrigger>
      <CollapsibleContent className="pt-2 space-y-2">
        <p className="text-[10px] text-muted-foreground">
          executeWhen, setFlags y setVariables los aplica el motor al ejecutar cualquier nodo.
        </p>
        <ObjectFields
          schema={ORCHESTRATOR_SCHEMA}
          value={subset}
          onChange={(orch) => onChange(mergeOrchestratorIntoValue(value, orch))}
          depth={0}
        />
      </CollapsibleContent>
    </Collapsible>
  );
}

export function FlowConfigEditor({
  schemaJson,
  value,
  onChange,
  className,
}: {
  schemaJson: string;
  value: Record<string, unknown>;
  onChange: (next: Record<string, unknown>) => void;
  className?: string;
}) {
  const schema = useMemo(() => parseSchema(schemaJson), [schemaJson]);

  if (!schema || !schema.properties || (schema.type && schema.type !== "object")) {
    return (
      <div className={cn("space-y-3", className)}>
        <div className="space-y-1">
          <Label className="text-xs">Config (sin esquema o esquema no soportado)</Label>
          <Textarea
            className="font-mono text-xs min-h-[120px]"
            value={JSON.stringify(value, null, 2)}
            onChange={(e) => {
              try {
                onChange(JSON.parse(e.target.value || "{}") as Record<string, unknown>);
              } catch {
                /* ignore */
              }
            }}
          />
        </div>
        <OrchestratorSection value={value} onChange={onChange} />
      </div>
    );
  }

  const propCount = Object.keys(schema.properties).length;

  return (
    <div className={cn("space-y-4", className)}>
      {propCount === 0 ? (
        <p className="text-xs text-muted-foreground">Este nodo no expone campos específicos en el esquema.</p>
      ) : (
        <ObjectFields schema={schema} value={value} onChange={onChange} depth={0} />
      )}
      <OrchestratorSection value={value} onChange={onChange} />
    </div>
  );
}
