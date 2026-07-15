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
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import {
  useCreateEmployeeScheduleException,
  useDeleteEmployeeScheduleException,
  useEmployeeScheduleExceptions,
  useUpdateEmployeeScheduleException,
} from "@/hooks/use-working-hours";
import type { EmployeeScheduleException, EmployeeScheduleExceptionPayload } from "@/types/entities";

interface ScheduleExceptionsEditorProps {
  employeeId: string;
}

const emptyForm = (): EmployeeScheduleExceptionPayload => ({
  date: todayInputValue(),
  openTime: "08:00",
  closeTime: "12:00",
  isClosed: false,
  reason: "",
});

export function ScheduleExceptionsEditor({ employeeId }: ScheduleExceptionsEditorProps) {
  const { data, isLoading } = useEmployeeScheduleExceptions(employeeId);
  const createException = useCreateEmployeeScheduleException(employeeId);
  const updateException = useUpdateEmployeeScheduleException(employeeId);
  const deleteException = useDeleteEmployeeScheduleException(employeeId);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<EmployeeScheduleException | null>(null);
  const [form, setForm] = useState<EmployeeScheduleExceptionPayload>(emptyForm);

  const exceptions = data ?? [];
  const isSaving = createException.isPending || updateException.isPending;

  const beginCreate = () => {
    setEditing(null);
    setForm(emptyForm());
    setOpen(true);
  };

  const beginEdit = (exception: EmployeeScheduleException) => {
    setEditing(exception);
    setForm({
      date: exception.date,
      openTime: exception.openTime,
      closeTime: exception.closeTime,
      isClosed: exception.isClosed,
      reason: exception.reason ?? "",
    });
    setOpen(true);
  };

  const submit = async () => {
    const payload = normalizePayload(form);
    try {
      if (editing) {
        await updateException.mutateAsync({ exceptionId: editing.employeeScheduleExceptionId, payload });
        toast.success("Excepcion actualizada");
      } else {
        await createException.mutateAsync(payload);
        toast.success("Excepcion creada");
      }
      setOpen(false);
    } catch {
      toast.error("No se pudo guardar la excepcion");
    }
  };

  const remove = async (exception: EmployeeScheduleException) => {
    if (!window.confirm("Eliminar esta excepcion?")) return;
    try {
      await deleteException.mutateAsync(exception.employeeScheduleExceptionId);
      toast.success("Excepcion eliminada");
    } catch {
      toast.error("No se pudo eliminar la excepcion");
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <Button type="button" size="sm" onClick={beginCreate}>
          <Plus className="mr-2 h-4 w-4" />
          Agregar excepcion
        </Button>
      </div>

      <div className="overflow-x-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Horario</TableHead>
              <TableHead>Motivo</TableHead>
              <TableHead className="w-20" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {exceptions.map((exception) => (
              <TableRow key={exception.employeeScheduleExceptionId}>
                <TableCell>{exception.date}</TableCell>
                <TableCell>{exception.isClosed ? "Cerrado" : formatRange(exception.openTime, exception.closeTime)}</TableCell>
                <TableCell className="max-w-[320px] truncate">{exception.reason ?? "-"}</TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
                    <Button type="button" variant="ghost" size="icon" onClick={() => beginEdit(exception)}>
                      <Edit2 className="h-4 w-4" />
                    </Button>
                    <Button type="button" variant="ghost" size="icon" onClick={() => remove(exception)} disabled={deleteException.isPending}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
            {!isLoading && exceptions.length === 0 && (
              <TableRow>
                <TableCell colSpan={4} className="text-center text-muted-foreground">
                  Sin excepciones configuradas
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? "Editar excepcion" : "Nueva excepcion"}</DialogTitle>
            <DialogDescription>Ajusta una fecha puntual para este empleado.</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-2">
            <div className="grid gap-2"><Label>Fecha</Label><DatePicker value={form.date} onChange={(date) => setForm((current) => ({ ...current, date }))} /></div>
            <div className="flex items-center gap-2"><Switch checked={form.isClosed} onCheckedChange={(isClosed) => setForm((current) => ({ ...current, isClosed }))} /><Label>Cerrado todo el dia</Label></div>
            {!form.isClosed && (
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="grid gap-2"><Label>Apertura</Label><TimePicker value={form.openTime} onChange={(openTime) => setForm((current) => ({ ...current, openTime }))} /></div>
                <div className="grid gap-2"><Label>Cierre</Label><TimePicker value={form.closeTime} onChange={(closeTime) => setForm((current) => ({ ...current, closeTime }))} /></div>
              </div>
            )}
            <div className="grid gap-2"><Label>Motivo</Label><Textarea value={form.reason ?? ""} onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))} /></div>
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

function normalizePayload(payload: EmployeeScheduleExceptionPayload): EmployeeScheduleExceptionPayload {
  return {
    ...payload,
    openTime: payload.isClosed ? null : payload.openTime,
    closeTime: payload.isClosed ? null : payload.closeTime,
    reason: payload.reason?.trim() || null,
  };
}

function formatRange(openTime: string | null, closeTime: string | null) {
  return `${openTime ?? "--:--"} - ${closeTime ?? "--:--"}`;
}

function todayInputValue() {
  const date = new Date();
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 10);
}
