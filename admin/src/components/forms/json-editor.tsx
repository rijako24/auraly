"use client";

import * as React from "react";
import { ChevronDown, ChevronRight, Plus, Trash2 } from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Checkbox } from "@/components/ui/checkbox";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";

type JsonPrimitive = string | number | boolean | null;
type JsonValue = JsonPrimitive | { [key: string]: JsonValue } | JsonValue[];

const TYPE_OPTIONS = ["string", "number", "boolean", "object", "array"] as const;
type JsonType = (typeof TYPE_OPTIONS)[number];

function detectType(val: unknown): JsonType {
  if (val === null || val === undefined) return "string";
  if (typeof val === "string") return "string";
  if (typeof val === "number") return "number";
  if (typeof val === "boolean") return "boolean";
  if (Array.isArray(val)) return "array";
  if (typeof val === "object") return "object";
  return "string";
}

function createDefaultValue(type: JsonType): JsonValue {
  switch (type) {
    case "string":
      return "";
    case "number":
      return 0;
    case "boolean":
      return false;
    case "array":
      return [];
    case "object":
      return {};
    default:
      return "";
  }
}

export interface JsonEditorProps {
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
  className?: string;
}

function KeyValueEditor({
  keyName,
  value,
  onChange,
  onRemove,
  depth = 0,
}: {
  keyName: string;
  value: unknown;
  onChange: (v: unknown) => void;
  onRemove?: () => void;
  depth?: number;
}) {
  const [isOpen, setIsOpen] = React.useState(true);
  const [key, setKey] = React.useState(keyName);

  const currentType = detectType(value);

  const handleTypeChange = (newType: JsonType) => {
    onChange(createDefaultValue(newType));
  };

  const handleAddProperty = () => {
    const obj = (typeof value === "object" && value !== null && !Array.isArray(value)
      ? value
      : {}) as Record<string, unknown>;
    const newKey = `newKey_${Date.now()}`;
    onChange({ ...obj, [newKey]: "" });
  };

  const handleAddArrayItem = () => {
    const arr = Array.isArray(value) ? [...value] : [];
    arr.push("");
    onChange(arr);
  };

  const handleObjectPropertyChange = (propKey: string, propVal: unknown) => {
    const obj = (typeof value === "object" && value !== null && !Array.isArray(value)
      ? { ...value }
      : {}) as Record<string, unknown>;
    if (propVal === undefined) {
      delete obj[propKey];
    } else {
      obj[propKey] = propVal;
    }
    onChange(Object.keys(obj).length ? obj : {});
  };

  const handleObjectKeyRename = (oldKey: string, newKey: string) => {
    if (oldKey === newKey) return;
    const obj = (typeof value === "object" && value !== null && !Array.isArray(value)
      ? value
      : {}) as Record<string, unknown>;
    const entries = Object.entries(obj);
    const updated: Record<string, unknown> = {};
    for (const [k, v] of entries) {
      updated[k === oldKey ? newKey : k] = v;
    }
    onChange(updated);
  };

  const handleArrayItemChange = (idx: number, itemVal: unknown) => {
    const arr = Array.isArray(value) ? [...value] : [];
    if (itemVal === undefined) {
      arr.splice(idx, 1);
    } else {
      arr[idx] = itemVal;
    }
    onChange(arr);
  };

  if (currentType === "object") {
    const obj = (value as Record<string, unknown>) ?? {};
    const entries = Object.entries(obj);

    return (
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <div className="rounded border border-border">
          <CollapsibleTrigger asChild>
            <button
              type="button"
              className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-sm hover:bg-muted/50"
            >
              {isOpen ? (
                <ChevronDown className="h-4 w-4 shrink-0" />
              ) : (
                <ChevronRight className="h-4 w-4 shrink-0" />
              )}
              <Input
                value={key}
                onChange={(e) => setKey(e.target.value)}
                onBlur={() => onChange(value)}
                className="h-7 w-32 border-0 bg-transparent font-medium focus-visible:ring-0"
                onClick={(e) => e.stopPropagation()}
              />
              <span className="text-muted-foreground">{"{}"}</span>
              {onRemove && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="ml-auto h-6 w-6"
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemove();
                  }}
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              )}
            </button>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div className="space-y-1 border-t border-border p-2 pl-4">
              {entries.map(([propKey, propVal]) => (
                <div key={propKey} className="flex items-start gap-2">
                  <KeyValueEditor
                    keyName={propKey}
                    value={propVal}
                    onChange={(v) => handleObjectPropertyChange(propKey, v)}
                    onRemove={() => handleObjectPropertyChange(propKey, undefined)}
                    depth={depth + 1}
                  />
                </div>
              ))}
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-7 gap-1"
                onClick={handleAddProperty}
              >
                <Plus className="h-3 w-3" />
                Añadir propiedad
              </Button>
            </div>
          </CollapsibleContent>
        </div>
      </Collapsible>
    );
  }

  if (currentType === "array") {
    const arr = Array.isArray(value) ? value : [];

    return (
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <div className="rounded border border-border">
          <CollapsibleTrigger asChild>
            <button
              type="button"
              className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-sm hover:bg-muted/50"
            >
              {isOpen ? (
                <ChevronDown className="h-4 w-4 shrink-0" />
              ) : (
                <ChevronRight className="h-4 w-4 shrink-0" />
              )}
              <span className="font-medium">{key}</span>
              <span className="text-muted-foreground">[{arr.length}]</span>
              {onRemove && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="ml-auto h-6 w-6"
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemove();
                  }}
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              )}
            </button>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div className="space-y-1 border-t border-border p-2 pl-4">
              {arr.map((item, idx) => (
                <div key={idx} className="flex items-start gap-2">
                  <KeyValueEditor
                    keyName={`[${idx}]`}
                    value={item}
                    onChange={(v) => handleArrayItemChange(idx, v)}
                    onRemove={() => handleArrayItemChange(idx, undefined)}
                    depth={depth + 1}
                  />
                </div>
              ))}
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-7 gap-1"
                onClick={handleAddArrayItem}
              >
                <Plus className="h-3 w-3" />
                Añadir elemento
              </Button>
            </div>
          </CollapsibleContent>
        </div>
      </Collapsible>
    );
  }

  return (
    <div className="flex items-center gap-2">
      <div className="flex min-w-0 items-center gap-2">
        <Input
          value={key}
          onChange={(e) => setKey(e.target.value)}
          className="h-8 w-32 shrink-0 font-medium"
        />
        <Select value={currentType} onValueChange={handleTypeChange}>
          <SelectTrigger className="h-8 w-24">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {TYPE_OPTIONS.map((t) => (
              <SelectItem key={t} value={t}>
                {t}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {currentType === "string" && (
          <Input
            value={String(value ?? "")}
            onChange={(e) => onChange(e.target.value)}
            className="min-w-[120px]"
            placeholder="Valor"
          />
        )}
        {currentType === "number" && (
          <Input
            type="number"
            value={String(value ?? 0)}
            onChange={(e) => onChange(Number(e.target.value) || 0)}
            className="min-w-[120px]"
          />
        )}
        {currentType === "boolean" && (
          <div className="flex items-center gap-2">
            <Checkbox
              checked={!!value}
              onCheckedChange={(c) => onChange(!!c)}
            />
          </div>
        )}
      </div>
      {onRemove && (
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="h-8 w-8 shrink-0"
          onClick={onRemove}
        >
          <Trash2 className="h-3 w-3" />
        </Button>
      )}
    </div>
  );
}

export function JsonEditor({ value, onChange, className }: JsonEditorProps) {
  const entries = Object.entries(value ?? {});

  const handleAdd = () => {
    onChange({ ...value, [`newKey_${Date.now()}`]: "" });
  };

  const handleChange = (key: string, val: unknown) => {
    const next = { ...value };
    if (val === undefined) {
      delete next[key];
    } else {
      next[key] = val;
    }
    onChange(next);
  };

  return (
    <div className={cn("space-y-2", className)}>
      {entries.map(([key, val]) => (
        <KeyValueEditor
          key={key}
          keyName={key}
          value={val}
          onChange={(v) => handleChange(key, v)}
          onRemove={() => handleChange(key, undefined)}
        />
      ))}
      <Button type="button" variant="outline" size="sm" className="gap-1" onClick={handleAdd}>
        <Plus className="h-4 w-4" />
        Añadir propiedad
      </Button>
    </div>
  );
}
