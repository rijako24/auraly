"use client";

import { FormEvent, useCallback, useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Edit, MoreHorizontal, Plus, PowerOff, RotateCcw } from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
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
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { formatDate } from "@/lib/utils";
import {
  useAgents,
  useBusinessInboundContacts,
  useCreateBusinessInboundContact,
  useDeleteBusinessInboundContact,
  useUpdateBusinessInboundContact,
} from "@/hooks/use-agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { BusinessInboundContact, Agent } from "@/types/entities";
import type { BusinessInboundContactPayload } from "@/services/api/agents";

type ContactFormState = {
  type: string;
  key: string;
  name: string;
  role: string;
  phoneNumber: string;
  inboundAgentId: string;
  capabilitiesJson: string;
  isActive: boolean;
};

const contactTypeLabels: Record<string, string> = {
  operations: "Operacion",
  delivery: "Domicilios",
};

const defaultForm = (agents: Agent[], type = "operations"): ContactFormState => ({
  type,
  key: "",
  name: "",
  role: type,
  phoneNumber: "",
  inboundAgentId: agents.find((agent) => agent.kind === type)?.agentId ?? "",
  capabilitiesJson: "",
  isActive: true,
});

function toForm(contact: BusinessInboundContact): ContactFormState {
  return {
    type: contact.type || "operations",
    key: contact.key ?? "",
    name: contact.name ?? "",
    role: contact.role ?? "",
    phoneNumber: contact.phoneNumber ?? "",
    inboundAgentId: contact.inboundAgentId ?? "",
    capabilitiesJson: contact.capabilitiesJson ?? "",
    isActive: contact.isActive,
  };
}

function toPayload(form: ContactFormState): BusinessInboundContactPayload {
  return {
    type: form.type.trim(),
    key: form.key.trim() || undefined,
    name: form.name.trim(),
    role: form.role.trim() || undefined,
    phoneNumber: form.phoneNumber.trim(),
    inboundAgentId: form.inboundAgentId,
    capabilitiesJson: form.capabilitiesJson.trim() || null,
    isActive: form.isActive,
  };
}

export default function InboundContactsPage() {
  const selectedBusinessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const [editing, setEditing] = useState<BusinessInboundContact | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [form, setForm] = useState<ContactFormState>(() => defaultForm([]));
  const [formError, setFormError] = useState<string | null>(null);

  const contactsQuery = useBusinessInboundContacts({ includeInactive: true });
  const agentsQuery = useAgents();
  const createContact = useCreateBusinessInboundContact();
  const updateContact = useUpdateBusinessInboundContact();
  const deleteContact = useDeleteBusinessInboundContact();

  const contacts = contactsQuery.data ?? [];
  const agents = useMemo(() => agentsQuery.data ?? [], [agentsQuery.data]);
  const inboundAgents = useMemo(
    () => agents.filter((agent) => agent.kind !== "customer"),
    [agents]
  );
  const agentsForType = inboundAgents.filter((agent) => agent.kind === form.type);

  const openCreate = () => {
    const next = defaultForm(inboundAgents);
    setEditing(null);
    setForm(next);
    setFormError(null);
    setDialogOpen(true);
  };

  const openEdit = (contact: BusinessInboundContact) => {
    setEditing(contact);
    setForm(toForm(contact));
    setFormError(null);
    setDialogOpen(true);
  };

  const changeType = (type: string) => {
    const nextAgent = inboundAgents.find((agent) => agent.kind === type)?.agentId ?? "";
    setForm((current) => ({
      ...current,
      type,
      role: current.role || type,
      inboundAgentId: inboundAgents.some((agent) => agent.agentId === current.inboundAgentId && agent.kind === type)
        ? current.inboundAgentId
        : nextAgent,
    }));
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setFormError(null);

    try {
      const payload = toPayload(form);
      if (editing) {
        await updateContact.mutateAsync({ contactId: editing.businessInboundContactId, payload });
      } else {
        await createContact.mutateAsync(payload);
      }
      setDialogOpen(false);
      setEditing(null);
    } catch (error) {
      setFormError(error instanceof Error ? error.message : "No se pudo guardar el contacto.");
    }
  };

  const toggleActive = useCallback(async (contact: BusinessInboundContact) => {
    if (contact.isActive) {
      await deleteContact.mutateAsync(contact.businessInboundContactId);
      return;
    }

    await updateContact.mutateAsync({
      contactId: contact.businessInboundContactId,
      payload: { isActive: true },
    });
  }, [deleteContact, updateContact]);

  const columns: ColumnDef<BusinessInboundContact>[] = useMemo(
    () => [
      {
        accessorKey: "name",
        header: "Contacto",
        cell: ({ row }) => (
          <div className="min-w-0">
            <div className="font-medium">{row.original.name}</div>
            <div className="text-xs text-muted-foreground">{row.original.key}</div>
          </div>
        ),
      },
      {
        accessorKey: "type",
        header: "Tipo",
        cell: ({ row }) => (
          <Badge variant="outline">{contactTypeLabels[row.original.type] ?? row.original.type}</Badge>
        ),
      },
      {
        accessorKey: "phoneNumber",
        header: "Telefono",
      },
      {
        accessorKey: "inboundAgentName",
        header: "Agente",
        cell: ({ row }) => row.original.inboundAgentName || row.original.inboundAgentId,
      },
      {
        accessorKey: "isActive",
        header: "Estado",
        cell: ({ row }) => (
          <Badge variant={row.original.isActive ? "default" : "secondary"}>
            {row.original.isActive ? "Activo" : "Inactivo"}
          </Badge>
        ),
      },
      {
        accessorKey: "createdAt",
        header: "Creado",
        cell: ({ row }) => formatDate(row.original.createdAt),
      },
      {
        id: "actions",
        cell: ({ row }) => {
          const contact = row.original;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-8 w-8">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={() => openEdit(contact)}>
                  <Edit className="mr-2 h-4 w-4" />
                  Editar
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  className={contact.isActive ? "text-destructive" : undefined}
                  onClick={() => toggleActive(contact)}
                >
                  {contact.isActive ? (
                    <PowerOff className="mr-2 h-4 w-4" />
                  ) : (
                    <RotateCcw className="mr-2 h-4 w-4" />
                  )}
                  {contact.isActive ? "Desactivar" : "Reactivar"}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          );
        },
      },
    ],
    [toggleActive]
  );

  if (!selectedBusinessId) {
    return <PageError message="Selecciona un negocio para administrar contactos inbound." />;
  }

  if (contactsQuery.isLoading || agentsQuery.isLoading) return <PageLoading cards={0} />;
  if (contactsQuery.isError || agentsQuery.isError) {
    return <PageError onRetry={() => { contactsQuery.refetch(); agentsQuery.refetch(); }} />;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Contactos inbound</h1>
          <p className="text-muted-foreground">Numeros internos que enrutan a agentes de operaciones o domicilios</p>
        </div>
        <Button onClick={openCreate} disabled={!selectedBusinessId || agentsQuery.isLoading}>
          <Plus className="mr-2 h-4 w-4" />
          Nuevo contacto
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={contacts}
        searchKey="name"
        searchPlaceholder="Buscar contacto..."
        facetedFilters={[
          {
            column: "type",
            title: "Tipo",
            options: Object.entries(contactTypeLabels).map(([value, label]) => ({ value, label })),
          },
        ]}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        enableRowSelection={false}
      />

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{editing ? "Editar contacto inbound" : "Nuevo contacto inbound"}</DialogTitle>
          </DialogHeader>
          <form className="space-y-4" onSubmit={submit}>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label>Tipo</Label>
                <Select value={form.type} onValueChange={changeType}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="operations">Operacion</SelectItem>
                    <SelectItem value="delivery">Domicilios</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Agente</Label>
                <Select
                  value={form.inboundAgentId}
                  onValueChange={(inboundAgentId) => setForm((current) => ({ ...current, inboundAgentId }))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Selecciona agente" />
                  </SelectTrigger>
                  <SelectContent>
                    {agentsForType.map((agent) => (
                      <SelectItem key={agent.agentId} value={agent.agentId}>
                        {agent.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Nombre</Label>
                <Input
                  value={form.name}
                  onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label>Telefono</Label>
                <Input
                  value={form.phoneNumber}
                  onChange={(event) => setForm((current) => ({ ...current, phoneNumber: event.target.value }))}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label>Clave</Label>
                <Input
                  value={form.key}
                  onChange={(event) => setForm((current) => ({ ...current, key: event.target.value }))}
                  placeholder="Se genera automaticamente"
                />
              </div>
              <div className="space-y-2">
                <Label>Rol</Label>
                <Input
                  value={form.role}
                  onChange={(event) => setForm((current) => ({ ...current, role: event.target.value }))}
                />
              </div>
            </div>

            <div className="space-y-2">
              <Label>Capabilities JSON</Label>
              <Textarea
                value={form.capabilitiesJson}
                onChange={(event) => setForm((current) => ({ ...current, capabilitiesJson: event.target.value }))}
                rows={4}
                placeholder='{"scope":"operations"}'
              />
            </div>

            <div className="flex items-center justify-between rounded-md border px-3 py-2">
              <Label>Activo</Label>
              <Switch
                checked={form.isActive}
                onCheckedChange={(isActive) => setForm((current) => ({ ...current, isActive }))}
              />
            </div>

            {formError && <p className="text-sm text-destructive">{formError}</p>}

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setDialogOpen(false)}>
                Cancelar
              </Button>
              <Button
                type="submit"
                disabled={
                  createContact.isPending ||
                  updateContact.isPending ||
                  !form.inboundAgentId ||
                  agentsForType.length === 0
                }
              >
                Guardar
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}