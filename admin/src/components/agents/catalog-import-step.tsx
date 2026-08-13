"use client";

import { useState } from "react";
import { FileUp, Loader2 } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { catalogApi } from "@/services/api/catalog";
import type { CatalogImportServiceLine } from "@/types/catalog-import";

interface CatalogImportStepProps {
  businessId: string;
  onImported?: (result: { created: number; skipped: number }) => void;
}

export function CatalogImportStep({ businessId, onImported }: CatalogImportStepProps) {
  const [lines, setLines] = useState<CatalogImportServiceLine[]>([]);
  const [preview, setPreview] = useState<string | null>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const [extracting, setExtracting] = useState(false);
  const [importing, setImporting] = useState(false);

  const handleFile = async (file: File | null) => {
    if (!file) return;
    setExtracting(true);
    setFileName(file.name);
    try {
      const draft = await catalogApi.extractFromFile(businessId, file);
      setLines(
        draft.services.map((s) => ({
          ...s,
          selected: s.selected !== false,
        }))
      );
      setPreview(draft.extractedTextPreview ?? null);
      if (draft.services.length === 0) {
        toast.warning("No se detectaron servicios en el documento");
      } else {
        toast.success(`Se detectaron ${draft.services.length} servicios`);
      }
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error al procesar el archivo");
    } finally {
      setExtracting(false);
    }
  };

  const updateLine = (index: number, partial: Partial<CatalogImportServiceLine>) => {
    setLines((prev) =>
      prev.map((line, i) => (i === index ? { ...line, ...partial } : line))
    );
  };

  const handleImport = async () => {
    const selected = lines.filter((l) => l.selected);
    if (selected.length === 0) {
      toast.error("Selecciona al menos un servicio");
      return;
    }
    setImporting(true);
    try {
      const result = await catalogApi.confirmImport(businessId, selected);
      toast.success(
        `Importados ${result.servicesCreated} servicios (${result.servicesSkipped} omitidos)`
      );
      onImported?.({
        created: result.servicesCreated,
        skipped: result.servicesSkipped,
      });
      if (result.warnings.length > 0) {
        result.warnings.forEach((w) => toast.warning(w));
      }
    } catch {
      toast.error("No se pudo importar el catálogo");
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Importar catálogo</CardTitle>
          <CardDescription>
            Sube un PDF, CSV, TXT o JSON con tus planes y servicios. El sistema extrae
            nombre, precio, duración y categoría para poblar las tablas del negocio.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <Label
              htmlFor="catalog-file"
              className="flex cursor-pointer items-center gap-2 rounded-md border border-dashed px-4 py-6 text-sm text-muted-foreground hover:bg-muted/50"
            >
              {extracting ? (
                <Loader2 className="h-5 w-5 animate-spin" />
              ) : (
                <FileUp className="h-5 w-5" />
              )}
              {fileName ? fileName : "Seleccionar PDF o documento"}
            </Label>
            <Input
              id="catalog-file"
              type="file"
              accept=".pdf,.csv,.txt,.json,.md"
              className="sr-only"
              disabled={extracting}
              onChange={(e) => handleFile(e.target.files?.[0] ?? null)}
            />
          </div>
          {preview && (
            <div className="rounded-md bg-muted/50 p-3 text-xs text-muted-foreground">
              <p className="mb-1 font-medium text-foreground">Vista previa del texto</p>
              <pre className="whitespace-pre-wrap">{preview}</pre>
            </div>
          )}
        </CardContent>
      </Card>

      {lines.length > 0 && (
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="text-base">Revisar servicios detectados</CardTitle>
            <Button onClick={handleImport} disabled={importing}>
              {importing ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : null}
              Importar a la base de datos
            </Button>
          </CardHeader>
          <CardContent className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10" />
                  <TableHead>Nombre</TableHead>
                  <TableHead>Categoría</TableHead>
                  <TableHead>Duración</TableHead>
                  <TableHead>Precio</TableHead>
                  <TableHead>Tipo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lines.map((line, index) => (
                  <TableRow key={index}>
                    <TableCell>
                      <Checkbox
                        checked={line.selected}
                        onCheckedChange={(c) =>
                          updateLine(index, { selected: c === true })
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        value={line.serviceName}
                        onChange={(e) =>
                          updateLine(index, { serviceName: e.target.value })
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        value={line.categoryName}
                        onChange={(e) =>
                          updateLine(index, { categoryName: e.target.value })
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        className="w-20"
                        value={line.durationMinutes}
                        onChange={(e) =>
                          updateLine(index, {
                            durationMinutes: parseInt(e.target.value, 10) || 60,
                          })
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <FormattedNumberInput
                        kind="currency"
                        className="w-24"
                        value={line.price}
                        onValueChange={(value) => updateLine(index, { price: value ?? 0 })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        value={line.serviceType}
                        onChange={(e) =>
                          updateLine(index, { serviceType: e.target.value })
                        }
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
