"use client";

import * as React from "react";
import { useForm, type DefaultValues } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import type { z } from "zod";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

type ZodType = z.ZodTypeAny;

function getZodType(schema: ZodType): string {
  const def = (schema as { _def?: { typeName?: string } })._def;
  return def?.typeName ?? "unknown";
}

function getEnumValues(schema: ZodType): string[] {
  const def = (schema as { _def?: { values?: string[] } })._def;
  if (def?.values) return def.values as string[];
  const inner = (schema as { _def?: { innerType?: ZodType } })._def?.innerType;
  if (inner) return getEnumValues(inner);
  return [];
}

function isOptional(schema: ZodType): boolean {
  const def = (schema as { _def?: { typeName?: string } })._def;
  return def?.typeName === "ZodOptional" || def?.typeName === "ZodDefault";
}

function unwrapOptional(schema: ZodType): ZodType {
  const def = (schema as { _def?: { innerType?: ZodType; typeName?: string } })._def;
  if (def?.typeName === "ZodOptional" || def?.typeName === "ZodDefault") {
    return def.innerType ?? schema;
  }
  return schema;
}

function getSchemaShape(schema: ZodType): Record<string, ZodType> | null {
  const def = (schema as { _def?: { shape?: Record<string, ZodType> } })._def;
  if (def?.shape) return def.shape;
  return null;
}

export interface AutoFormFieldConfig {
  fieldName: string;
  label?: string;
  placeholder?: string;
  description?: string;
  render?: (props: {
    field: { value: unknown; onChange: (v: unknown) => void; onBlur: () => void };
    disabled?: boolean;
  }) => React.ReactNode;
}

export interface AutoFormProps<TSchema extends z.ZodObject<z.ZodRawShape>> {
  schema: TSchema;
  defaultValues?: Partial<z.infer<TSchema>>;
  onSubmit: (data: z.infer<TSchema>) => void | Promise<void>;
  onCancel?: () => void;
  fieldConfig?: Partial<Record<keyof z.infer<TSchema>, AutoFormFieldConfig>>;
  submitLabel?: string;
  cancelLabel?: string;
  disabled?: boolean;
  className?: string;
}

export function AutoForm<TSchema extends z.ZodObject<z.ZodRawShape>>({
  schema,
  defaultValues,
  onSubmit,
  onCancel,
  fieldConfig = {},
  submitLabel = "Guardar",
  cancelLabel = "Cancelar",
  disabled = false,
  className,
}: AutoFormProps<TSchema>) {
  type FormValues = z.infer<TSchema>;

  // Dynamic form: react-hook-form generics are too strict for runtime schema introspection
  const form = useForm<any>({
    resolver: zodResolver(schema),
    defaultValues: defaultValues ?? {},
  });

  const shape = getSchemaShape(schema);
  const fields = shape ? Object.entries(shape) : [];

  const renderField = (key: string, fieldSchema: ZodType) => {
    const config = fieldConfig[key as keyof FormValues];
    const unwrapped = unwrapOptional(fieldSchema);
    const typeName = getZodType(unwrapped);
    const optional = isOptional(fieldSchema);
    const label =
      config?.label ??
      key.replace(/([A-Z])/g, " $1").replace(/^./, (s) => s.toUpperCase());
    const placeholder = config?.placeholder ?? "";

    const fieldProps = form.register(key);

    if (config?.render) {
      return (
        <div key={key} className="space-y-2">
          <Label>
            {label}
            {optional && (
              <span className="ml-1 text-muted-foreground">(opcional)</span>
            )}
          </Label>
          {config.render({
            field: {
              value: form.watch(key),
              onChange: (v: unknown) => form.setValue(key, v),
              onBlur: () => form.trigger(key),
            },
            disabled,
          })}
        </div>
      );
    }

    if (typeName === "ZodBoolean") {
      return (
        <div key={key} className="flex items-center gap-2 space-y-0">
          <Switch
            id={key}
            checked={!!form.watch(key)}
            onCheckedChange={(checked) => form.setValue(key, checked)}
            disabled={disabled}
          />
          <Label htmlFor={key} className="cursor-pointer">
            {label}
          </Label>
        </div>
      );
    }

    if (typeName === "ZodEnum") {
      const options = getEnumValues(unwrapped);
      return (
        <div key={key} className="space-y-2">
          <Label htmlFor={key}>
            {label}
            {optional && (
              <span className="ml-1 text-muted-foreground">(opcional)</span>
            )}
          </Label>
          <Select
            value={String(form.watch(key) ?? "")}
            onValueChange={(v) => form.setValue(key, v)}
            disabled={disabled}
          >
            <SelectTrigger id={key}>
              <SelectValue placeholder={placeholder || "Seleccionar..."} />
            </SelectTrigger>
            <SelectContent>
              {options.map((opt) => (
                <SelectItem key={opt} value={opt}>
                  {opt}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      );
    }

    if (typeName === "ZodNumber") {
      return (
        <div key={key} className="space-y-2">
          <Label htmlFor={key}>
            {label}
            {optional && (
              <span className="ml-1 text-muted-foreground">(opcional)</span>
            )}
          </Label>
          <Input
            id={key}
            type="number"
            placeholder={placeholder}
            disabled={disabled}
            {...fieldProps}
          />
        </div>
      );
    }

    if (typeName === "ZodDate") {
      return (
        <div key={key} className="space-y-2">
          <Label htmlFor={key}>
            {label}
            {optional && (
              <span className="ml-1 text-muted-foreground">(opcional)</span>
            )}
          </Label>
          <Input
            id={key}
            type="date"
            placeholder={placeholder}
            disabled={disabled}
            {...fieldProps}
            value={
              form.watch(key)
                ? (form.watch(key) as Date).toISOString().slice(0, 10)
                : ""
            }
            onChange={(e) => {
              const val = e.target.value;
              form.setValue(key, val ? new Date(val) : undefined);
            }}
          />
        </div>
      );
    }

    return (
      <div key={key} className="space-y-2">
        <Label htmlFor={key}>
          {label}
          {optional && (
            <span className="ml-1 text-muted-foreground">(opcional)</span>
          )}
        </Label>
        <Input
          id={key}
          type="text"
          placeholder={placeholder}
          disabled={disabled}
          {...fieldProps}
        />
      </div>
    );
  };

  return (
    <form
      onSubmit={form.handleSubmit(async (data) => {
        await onSubmit(data);
      })}
      className={cn("space-y-6", className)}
    >
      <div className="space-y-4">
        {fields.map(([key, fieldSchema]) => renderField(key, fieldSchema))}
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={disabled || form.formState.isSubmitting}>
          {submitLabel}
        </Button>
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel} disabled={disabled}>
            {cancelLabel}
          </Button>
        )}
      </div>
    </form>
  );
}
