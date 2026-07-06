"use client";

import { useMemo, useState } from "react";
import type { ChangeEvent } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarClock, Download, Megaphone, Plus, Send, Upload } from "lucide-react";
import { toast } from "sonner";
import { strToU8, zipSync } from "fflate";
import { readSheet } from "read-excel-file/browser";

import { StatCard } from "@/components/cards/stat-card";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useCampaigns, useCreateCampaign } from "@/hooks/use-campaigns";
import { useWhatsAppTemplates } from "@/hooks/use-whatsapp-templates";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type {
  Campaign,
  CampaignStatus,
  CreateCampaignRequest,
  ImportedCampaignRecipientRequest,
  WhatsAppTemplate,
} from "@/types/entities";
import { cn, formatDateTime } from "@/lib/utils";

const statusLabels: Record<CampaignStatus, string> = {
  Draft: "Borrador",
  Scheduled: "Programada",
  Queued: "En cola",
  Processing: "Procesando",
  Completed: "Completada",
  CompletedWithErrors: "Con errores",
  Cancelled: "Cancelada",
  Failed: "Fallida",
  QueueFailed: "Cola fallida",
};

const statusStyles: Record<CampaignStatus, string> = {
  Draft: "bg-muted text-muted-foreground",
  Scheduled: "bg-sky-100 text-sky-800 dark:bg-sky-900 dark:text-sky-200",
  Queued: "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200",
  Processing: "bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200",
  Completed: "bg-emerald-100 text-emerald-800 dark:bg-emerald-900 dark:text-emerald-200",
  CompletedWithErrors: "bg-orange-100 text-orange-800 dark:bg-orange-900 dark:text-orange-200",
  Cancelled: "bg-muted text-muted-foreground",
  Failed: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
  QueueFailed: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
};

function statusBadge(status: CampaignStatus) {
  return (
    <Badge variant="secondary" className={cn(statusStyles[status])}>
      {statusLabels[status]}
    </Badge>
  );
}

function parseImportedRecipients(value: string): ImportedCampaignRecipientRequest[] {
  const lines = value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  if (lines.length === 0) return [];

  const first = splitRow(lines[0]);
  const hasHeader = first.some((cell) => ["phone", "telefono", "telÃ©fono"].includes(cell.toLowerCase()));
  const headers = hasHeader ? first.map((h) => h.trim()) : ["phone", "name"];
  const rows = hasHeader ? lines.slice(1) : lines;

  const result: ImportedCampaignRecipientRequest[] = [];
  rows.forEach((line) => {
    const cells = splitRow(line);
    const variables: Record<string, string> = {};
    headers.forEach((header, index) => {
      if (header) variables[header] = cells[index] ?? "";
    });

    const phoneIndex = headers.findIndex((h) => ["phone", "telefono", "teléfono"].includes(h.toLowerCase()));
    const nameIndex = headers.findIndex((h) => ["name", "nombre", "customerName"].includes(h));
    const phone = cells[phoneIndex >= 0 ? phoneIndex : 0]?.trim() ?? "";
    const customerName = cells[nameIndex >= 0 ? nameIndex : 1]?.trim() || null;

    if (phone) result.push({ phone, customerName, variables });
  });

  return result;
}

function splitRow(line: string) {
  return line.split(/[,\t;]/).map((cell) => cell.trim());
}

function normalizeImportHeader(value: string) {
  return value
    .trim()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[\s_-]+/g, "")
    .toLowerCase();
}

function resolveImportIndexes(headers: string[]) {
  const normalized = headers.map(normalizeImportHeader);
  const phoneAliases = new Set(["phone", "telefono", "tel", "celular", "whatsapp", "numero", "number"]);
  const nameAliases = new Set(["name", "nombre", "cliente", "customername", "customer"]);

  return {
    phoneIndex: normalized.findIndex((header) => phoneAliases.has(header)),
    nameIndex: normalized.findIndex((header) => nameAliases.has(header)),
  };
}

function rowsToImportedRecipients(rows: string[][]): ImportedCampaignRecipientRequest[] {
  const cleanedRows = rows
    .map((row) => row.map((cell) => String(cell ?? "").trim()))
    .filter((row) => row.some(Boolean));

  if (cleanedRows.length < 2) return [];

  const headers = cleanedRows[0];
  const { phoneIndex, nameIndex } = resolveImportIndexes(headers);
  if (phoneIndex < 0) {
    throw new Error("El Excel debe tener una columna phone o telefono.");
  }

  return cleanedRows.slice(1).reduce<ImportedCampaignRecipientRequest[]>((items, cells) => {
    const phone = cells[phoneIndex]?.trim() ?? "";
    if (!phone) return items;

    const variables: Record<string, string> = {};
    headers.forEach((header, index) => {
      if (header) variables[header] = cells[index] ?? "";
    });

    items.push({
      phone,
      customerName: nameIndex >= 0 ? cells[nameIndex]?.trim() || null : null,
      variables,
    });
    return items;
  }, []);
}

function importedRecipientsToTsv(recipients: ImportedCampaignRecipientRequest[]) {
  const variableKeys = Array.from(
    recipients.reduce((keys, recipient) => {
      Object.keys(recipient.variables ?? {}).forEach((key) => keys.add(key));
      return keys;
    }, new Set<string>())
  );

  const headers = ["phone", "name", ...variableKeys.filter((key) => !["phone", "name"].includes(key.toLowerCase()))];
  const rows = recipients.map((recipient) => headers.map((header) => {
    if (header === "phone") return recipient.phone;
    if (header === "name") return recipient.customerName ?? "";
    return recipient.variables?.[header] ?? "";
  }));

  return [headers, ...rows]
    .map((row) => row.map((cell) => String(cell).replace(/[\r\n\t]+/g, " ").trim()).join("\t"))
    .join("\n");
}

function getTemplateHeaders(parameterKeys: string) {
  const variables = parameterKeys
    .split(",")
    .map((key) => key.trim())
    .filter(Boolean)
    .filter((key) => !["phone", "name"].includes(key.toLowerCase()));

  return ["phone", "name", ...Array.from(new Set(variables))];
}

function getSampleValue(header: string) {
  const normalized = normalizeImportHeader(header);
  if (normalized === "phone") return "573001112233";
  if (normalized === "name" || normalized === "customername") return "Ana Perez";
  if (normalized.includes("date") || normalized.includes("fecha")) return "2026-07-05";
  return "Ejemplo";
}

function formatImportCell(value: unknown) {
  if (value instanceof Date) return value.toISOString().slice(0, 10);
  return String(value ?? "").trim();
}

function escapeXml(value: string) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

function columnName(index: number) {
  let name = "";
  let current = index + 1;
  while (current > 0) {
    const remainder = (current - 1) % 26;
    name = String.fromCharCode(65 + remainder) + name;
    current = Math.floor((current - 1) / 26);
  }
  return name;
}

function buildWorksheetXml(rows: string[][]) {
  const xmlRows = rows.map((row, rowIndex) => {
    const rowNumber = rowIndex + 1;
    const cells = row.map((cell, columnIndex) => {
      const ref = `${columnName(columnIndex)}${rowNumber}`;
      return `<c r="${ref}" t="inlineStr"><is><t>${escapeXml(cell)}</t></is></c>`;
    }).join("");
    return `<row r="${rowNumber}">${cells}</row>`;
  }).join("");

  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>${xmlRows}</sheetData></worksheet>`;
}

function downloadXlsxTemplate(filename: string, rows: string[][]) {
  const files: Record<string, Uint8Array> = {
    "[Content_Types].xml": strToU8(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>`),
    "_rels/.rels": strToU8(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`),
    "xl/workbook.xml": strToU8(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Destinatarios" sheetId="1" r:id="rId1"/></sheets></workbook>`),
    "xl/_rels/workbook.xml.rels": strToU8(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>`),
    "xl/worksheets/sheet1.xml": strToU8(buildWorksheetXml(rows)),
  };

  const blob = new Blob([zipSync(files)], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

const defaultParameterKeys = ["CustomerName", "LastReservationDate", "Phone"];

function buildDefaultParameterKeys(count: number) {
  return Array.from({ length: Math.max(count, 0) }, (_, index) => defaultParameterKeys[index] ?? `Param${index + 1}`).join(", ");
}

function normalizeTemplateCategory(category: WhatsAppTemplate["category"]): "Marketing" | "Utility" {
  return category?.toLowerCase() === "utility" ? "Utility" : "Marketing";
}

export default function CampaignsPage() {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [dialogOpen, setDialogOpen] = useState(false);
  const [name, setName] = useState("");
  const [sourceType, setSourceType] = useState<"Segment" | "Import">("Segment");
  const [segmentKey, setSegmentKey] = useState("reserved_no_return");
  const [inactiveDays, setInactiveDays] = useState(60);
  const [templateName, setTemplateName] = useState("");
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [languageCode, setLanguageCode] = useState("es_CO");
  const [templateCategory, setTemplateCategory] = useState<"Marketing" | "Utility">("Marketing");
  const [parameterKeys, setParameterKeys] = useState("CustomerName");
  const [scheduledAt, setScheduledAt] = useState("");
  const [importText, setImportText] = useState("");
  const [importFilename, setImportFilename] = useState("");

  const { data, isLoading, isError, refetch } = useCampaigns({
    page,
    pageSize,
    search: search || undefined,
  });
  const templatesQuery = useWhatsAppTemplates();
  const createCampaign = useCreateCampaign();
  const campaigns = data?.items ?? [];
  const templates = templatesQuery.data ?? [];

  const importedRecipients = useMemo(() => parseImportedRecipients(importText), [importText]);

  const stats = useMemo(() => {
    const active = campaigns.filter((c) => ["Scheduled", "Queued", "Processing"].includes(c.status)).length;
    const sent = campaigns.reduce((sum, c) => sum + c.sentCount, 0);
    const failed = campaigns.reduce((sum, c) => sum + c.failedCount, 0);
    return { total: data?.totalCount ?? campaigns.length, active, sent, failed };
  }, [campaigns, data?.totalCount]);

  const columns: ColumnDef<Campaign>[] = useMemo(() => [
    {
      accessorKey: "name",
      header: "CampaÃ±a",
      cell: ({ row }) => (
        <div>
          <div className="font-medium">{row.original.name}</div>
          <div className="text-xs text-muted-foreground">{row.original.templateName}</div>
        </div>
      ),
    },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => statusBadge(row.original.status),
    },
    {
      accessorKey: "recipientCount",
      header: "Destinatarios",
      cell: ({ row }) => row.original.recipientCount.toLocaleString("es-CO"),
    },
    {
      accessorKey: "sentCount",
      header: "Enviados",
      cell: ({ row }) => row.original.sentCount.toLocaleString("es-CO"),
    },
    {
      accessorKey: "failedCount",
      header: "Fallidos",
      cell: ({ row }) => row.original.failedCount.toLocaleString("es-CO"),
    },
    {
      accessorKey: "createdAt",
      header: "Creada",
      cell: ({ row }) => formatDateTime(row.original.createdAt),
    },
  ], []);

  const cardRenderer = (item: Campaign) => (
    <Card key={item.campaignId}>
      <CardContent className="pt-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h3 className="font-semibold">{item.name}</h3>
            <p className="text-sm text-muted-foreground">{item.templateName}</p>
          </div>
          {statusBadge(item.status)}
        </div>
        <div className="mt-4 grid grid-cols-3 gap-2 text-sm">
          <div><span className="text-muted-foreground">Total</span><div className="font-medium">{item.recipientCount}</div></div>
          <div><span className="text-muted-foreground">OK</span><div className="font-medium">{item.sentCount}</div></div>
          <div><span className="text-muted-foreground">Error</span><div className="font-medium">{item.failedCount}</div></div>
        </div>
        <p className="mt-3 text-xs text-muted-foreground">{formatDateTime(item.createdAt)}</p>
      </CardContent>
    </Card>
  );

  function resetForm() {
    setName("");
    setSourceType("Segment");
    setSegmentKey("reserved_no_return");
    setInactiveDays(60);
    setTemplateName("");
    setSelectedTemplateId("");
    setLanguageCode("es_CO");
    setTemplateCategory("Marketing");
    setParameterKeys("CustomerName");
    setScheduledAt("");
    setImportText("");
    setImportFilename("");
  }

  function applyTemplate(templateId: string) {
    setSelectedTemplateId(templateId);
    const template = templates.find((item) => item.id === templateId);
    if (!template) {
      setTemplateName("");
      return;
    }

    setTemplateName(template.name);
    setLanguageCode(template.language || "es_CO");
    setTemplateCategory(normalizeTemplateCategory(template.category));
    setParameterKeys(buildDefaultParameterKeys(template.bodyParameterCount));
  }

  async function handleExcelUpload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      const rows = await readSheet(file);
      const recipients = rowsToImportedRecipients(rows.map((row) => row.map(formatImportCell)));
      if (recipients.length === 0) throw new Error("El Excel no tiene destinatarios validos.");

      setImportFilename(file.name);
      setImportText(importedRecipientsToTsv(recipients));
      toast.success(`${recipients.length} destinatarios cargados desde Excel`);
    } catch (error) {
      setImportFilename("");
      setImportText("");
      toast.error(error instanceof Error ? error.message : "No se pudo leer el Excel");
    } finally {
      event.target.value = "";
    }
  }

  function downloadExcelTemplate() {
    const headers = getTemplateHeaders(parameterKeys);
    const rows = [headers, headers.map(getSampleValue)];
    downloadXlsxTemplate("plantilla-campana-whatsapp.xlsx", rows);
  }

  async function submitCampaign() {
    if (!selectedBusinessId) return;
    if (!name.trim() || !templateName.trim()) {
      toast.error("Completa nombre y plantilla");
      return;
    }

    if (sourceType === "Import" && importedRecipients.length === 0) {
      toast.error("Carga un Excel o pega destinatarios para importar");
      return;
    }

    const bodyParameterKeys = parameterKeys
      .split(",")
      .map((key) => key.trim())
      .filter(Boolean);

    const payload: CreateCampaignRequest = {
      businessId: selectedBusinessId,
      name,
      sourceType,
      templateName,
      languageCode,
      templateCategory,
      bodyParameterKeys,
      scheduledAtUtc: scheduledAt ? new Date(scheduledAt).toISOString() : null,
      audience: sourceType === "Import"
        ? { segmentKey: null, inactiveDays: null, importedRecipients }
        : { segmentKey, inactiveDays, importedRecipients: null },
    };

    try {
      await createCampaign.mutateAsync(payload);
      toast.success("CampaÃ±a encolada");
      setDialogOpen(false);
      resetForm();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo crear la campaÃ±a");
    }
  }

  if (!selectedBusinessId) return <PageError message="Selecciona un negocio para administrar campaÃ±as." />;
  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">CampaÃ±as</h1>
          <p className="text-muted-foreground">Mensajes masivos por WhatsApp</p>
        </div>
        <Button onClick={() => setDialogOpen(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Nueva campaÃ±a
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="CampaÃ±as" value={stats.total} icon={Megaphone} />
        <StatCard title="Activas" value={stats.active} icon={CalendarClock} />
        <StatCard title="Enviados" value={stats.sent} icon={Send} />
        <StatCard title="Fallidos" value={stats.failed} icon={Upload} />
      </div>

      <DataTable
        columns={columns}
        data={campaigns}
        searchKey="name"
        searchPlaceholder="Buscar campaÃ±a..."
        viewMode="table"
        cardRenderer={cardRenderer}
        enableRowSelection={false}
        page={page}
        pageSize={pageSize}
        pageCount={data?.totalPages}
        totalItems={data?.totalCount}
        onPaginationChange={(nextPage, nextPageSize) => {
          setPage(nextPageSize === pageSize ? nextPage : 1);
          setPageSize(nextPageSize);
        }}
        onSearch={(value) => {
          setSearch(value);
          setPage(1);
        }}
      />

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
          <DialogHeader>
            <DialogTitle>Nueva campaÃ±a</DialogTitle>
            <DialogDescription>Configura audiencia y plantilla</DialogDescription>
          </DialogHeader>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="campaign-name">Nombre</Label>
              <Input id="campaign-name" value={name} onChange={(e) => setName(e.target.value)} />
            </div>
            <div className="space-y-2">
              <Label>Origen</Label>
              <Select value={sourceType} onValueChange={(value) => setSourceType(value as "Segment" | "Import")}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Segment">Segmento</SelectItem>
                  <SelectItem value="Import">ImportaciÃ³n</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {sourceType === "Segment" ? (
              <>
                <div className="space-y-2">
                  <Label>Segmento</Label>
                  <Select value={segmentKey} onValueChange={setSegmentKey}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="reserved_no_return">Reservaron y no volvieron</SelectItem>
                      <SelectItem value="inactive_leads">Leads sin actividad</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="inactive-days">DÃ­as</Label>
                  <Input
                    id="inactive-days"
                    type="number"
                    min={1}
                    value={inactiveDays}
                    onChange={(e) => setInactiveDays(Number(e.target.value))}
                  />
                </div>
              </>
            ) : (
              <div className="space-y-3 sm:col-span-2">
                <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
                  <div className="space-y-2">
                    <Label htmlFor="import-file">Excel de destinatarios</Label>
                    <Input
                      id="import-file"
                      type="file"
                      accept=".xlsx"
                      onChange={handleExcelUpload}
                    />
                  </div>
                  <Button type="button" variant="outline" onClick={downloadExcelTemplate}>
                    <Download className="mr-2 h-4 w-4" />
                    Plantilla
                  </Button>
                </div>
                <Textarea
                  id="import-text"
                  value={importText}
                  onChange={(e) => {
                    setImportFilename("");
                    setImportText(e.target.value);
                  }}
                  rows={7}
                  placeholder="phone,name,CustomerName&#10;573001112233,Ana Perez,Ana Perez"
                />
                <p className="text-xs text-muted-foreground">
                  {importedRecipients.length} destinatarios detectados{importFilename ? ` desde ${importFilename}` : ""}
                </p>
              </div>
            )}

            <div className="space-y-2 sm:col-span-2">
              <Label>Plantilla de WhatsApp</Label>
              <Select
                value={selectedTemplateId}
                onValueChange={applyTemplate}
                disabled={templatesQuery.isLoading || templates.length === 0}
              >
                <SelectTrigger>
                  <SelectValue
                    placeholder={
                      templatesQuery.isLoading
                        ? "Cargando plantillas..."
                        : templates.length === 0
                          ? "Sin plantillas aprobadas"
                          : "Selecciona una plantilla"
                    }
                  />
                </SelectTrigger>
                <SelectContent>
                  {templates.map((template) => (
                    <SelectItem key={template.id} value={template.id}>
                      {template.name} - {template.language}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {templatesQuery.isError ? (
                <p className="text-xs text-destructive">No se pudieron cargar las plantillas del negocio.</p>
              ) : null}
            </div>
            <div className="space-y-2">
              <Label htmlFor="template-category">Categoria</Label>
              <Input id="template-category" value={templateCategory} readOnly />
            </div>
            <div className="space-y-2">
              <Label htmlFor="language-code">Idioma</Label>
              <Input id="language-code" value={languageCode} readOnly />
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="parameter-keys">Variables del body</Label>
              <Input id="parameter-keys" value={parameterKeys} onChange={(e) => setParameterKeys(e.target.value)} />
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="scheduled-at">ProgramaciÃ³n</Label>
              <Input
                id="scheduled-at"
                type="datetime-local"
                value={scheduledAt}
                onChange={(e) => setScheduledAt(e.target.value)}
              />
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setDialogOpen(false)}>
              Cancelar
            </Button>
            <Button type="button" onClick={submitCampaign} disabled={createCampaign.isPending}>
              <Send className="mr-2 h-4 w-4" />
              Encolar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}








