"use client";

import { useState } from "react";
import { Edit2, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { TimePicker } from "@/components/ui/time-picker";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { useEmployees } from "@/hooks/use-employees";
import {
  useBusinessAvailabilityBlocks,
  useCreateBusinessAvailabilityBlock,
  useDeleteBusinessAvailabilityBlock,
  useUpdateBusinessAvailabilityBlock,
} from "@/hooks/use-working-hours";
import type { BusinessAvailabilityBlock, BusinessAvailabilityBlockPayload } from "@/types/entities";

const ALL_EMPLOYEES = "__all";

const emptyForm = (): BusinessAvailabilityBlockPayload => ({
  employeeId: null,
  date: todayInputValue(),
  startTime: null,
  endTime: null,
  reason: "",
  isActive: true,
});

export function AvailabilityBlocksEditor() {
  const { data, isLoading } = useBusinessAvailabilityBlocks();
  const { data: employeesData } = useEmployees({ page: 1, pageSize: 500 });
  const createBlock = useCreateBusinessAvailabilityBlock();
  const updateBlock = useUpdateBusinessAvailabilityBlock();
  const deleteBlock = useDeleteBusinessAvailabilityBlock();
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<BusinessAvailabilityBlock | null>(null);
  const [form, setForm] = useState<BusinessAvailabilityBlockPayload>(emptyForm);

  const blocks = data ?? [];
  const employees = employeesData?.items ?? [];
  const isSaving = createBlock.isPending || updateBlock.isPending;

  const beginCreate = () => {
    setEditing(null);
    setForm(emptyForm());
    setOpen(true);
  };

  const beginEdit = (block: BusinessAvailabilityBlock) => {
    setEditing(block);
    setForm({
      employeeId: block.employeeId,
      date: block.date,
      startTime: block.startTime,
      endTime: block.endTime,
      reason: block.reason,
      isActive: block.isActive,
    });
    setOpen(true);
  };

  const submit = async () => {
    const payload = normalizePayload(form);
    try {
      if (editing) {
        await updateBlock.mutateAsync({ blockId: editing.businessAvailabilityBlockId, payload });
        toast.success("Bloqueo actualizado");
      } else {
        await createBlock.mutateAsync(payload);
        toast.success("Bloqueo creado");
      }
      setOpen(false);
    } catch {
      toast.error("No se pudo guardar el bloqueo");
    }
  };

  const remove = async (block: BusinessAvailabilityBlock) => {
    if (!window.confirm("Eliminar este bloqueo?")) return;
    try {
      await deleteBlock.mutateAsync(block.businessAvailabilityBlockId);
      toast.success("Bloqueo eliminado");
    } catch {
      toast.error("No se pudo eliminar el bloqueo");
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <Button type="button" size="sm" onClick={beginCreate}>
          <Plus className="mr-2 h-4 w-4" />
          Agregar bloqueo
        </Button>
      </div>

      <div className="overflow-x-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Horario</TableHead>
              <TableHead>Empleado</TableHead>
              <TableHead>Motivo</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="w-20" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {blocks.map((block) => (
              <TableRow key={block.businessAvailabilityBlockId}>
                <TableCell>{block.date}</TableCell>
                <TableCell>{formatRange(block.startTime, block.endTime)}</TableCell>
                <TableCell>{block.employeeName ?? "Todo el negocio"}</TableCell>
                <TableCell className="max-w-[260px] truncate">{block.reason}</TableCell>
                <TableCell>{block.isActive ? "Activo" : "Inactivo"}</TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
                    <Button type="button" variant="ghost" size="icon" onClick={() => beginEdit(block)}>
                      <Edit2 className="h-4 w-4" />
                    </Button>
                    <Button type="button" variant="ghost" size="icon" onClick={() => remove(block)} disabled={deleteBlock.isPending}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {!isLoading && blocks.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="text-center text-muted-foreground">
                  Sin bloqueos configurados
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? "Editar bloqueo" : "Nuevo bloqueo"}</DialogTitle>
            <DialogDescription>Bloquea una fecha completa o un rango horario especifico.</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-2">
            <div className="grid gap-2">
              <Label>Empleado</Label>
              <Select value={form.employeeId ?? ALL_EMPLOYEES} onValueChange={(value) => setForm((current) => ({ ...current, employeeId: value === ALL_EMPLOYEES ? null : value }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_EMPLOYEES}>Todo el negocio</SelectItem>
                  {employees.map((employee) => (
                    <SelectItem key={employee.employeeId} value={employee.employeeId}>{employee.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="grid gap-2"><Label>Fecha</Label><DatePicker value={form.date} onChange={(date) => setForm((current) => ({ ...current, date }))} /></div>
              <div className="grid gap-2"><Label>Inicio</Label><TimePicker value={form.startTime} onChange={(startTime) => setForm((current) => ({ ...current, startTime }))} /></div>
              <div className="grid gap-2"><Label>Fin</Label><TimePicker value={form.endTime} onChange={(endTime) => setForm((current) => ({ ...current, endTime }))} /></div>
            </div>
            <div className="grid gap-2"><Label>Motivo</Label><Textarea value={form.reason} onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))} /></div>
            <div className="flex items-center gap-2"><Switch checked={form.isActive} onCheckedChange={(isActive) => setForm((current) => ({ ...current, isActive }))} /><Label>Activo</Label></div>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>Cancelar</Button>
            <Button type="button" onClick={submit} disabled={isSaving}>Guardar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function normalizePayload(payload: BusinessAvailabilityBlockPayload): BusinessAvailabilityBlockPayload {
  return {
    ...payload,
    startTime: payload.startTime || null,
    endTime: payload.endTime || null,
    reason: payload.reason.trim() || "Bloqueo manual",
    isActive: true,
  };
}

function formatRange(startTime: string | null, endTime: string | null) {
  if (!startTime && !endTime) return "Todo el dia";
  return `${startTime ?? "--:--"} - ${endTime ?? "--:--"}`;
}

function todayInputValue() {
  const date = new Date();
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 10);
}

