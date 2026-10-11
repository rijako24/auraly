"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Armchair, DoorOpen, LayoutGrid, Pencil, Plus, Users } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { restaurantMasterApi, type RestaurantRoom, type RestaurantTable,
  type RestaurantMasterPage } from "@/services/api/restaurant-master";
import { RestaurantStationsSettings } from "@/components/restaurant/restaurant-stations-settings";

type TableDraft = { id: string; code: string; name: string; capacity: string; roomId: string; isActive: boolean; rowVersion?: string };
type RoomDraft = { id: string; name: string; sortOrder: number; isActive: boolean; rowVersion?: string };
const emptyTable = (): TableDraft => ({ id: crypto.randomUUID(), code: "", name: "", capacity: "", roomId: "none", isActive: true });
const emptyRoom = (): RoomDraft => ({ id: crypto.randomUUID(), name: "", sortOrder: 0, isActive: true });

export default function RestaurantSettingsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canConfigure = useAuthStore((state) => state.user?.permissions.includes("businesses.update") ?? false);
  const [tablePage, setTablePage] = useState(1);
  const [roomPage, setRoomPage] = useState(1);
  const [tableDraft, setTableDraft] = useState<TableDraft | null>(null);
  const [roomDraft, setRoomDraft] = useState<RoomDraft | null>(null);
  const queryClient = useQueryClient();
  const roomKey = ["restaurant", businessId, "rooms", roomPage] as const;
  const tableKey = ["restaurant", businessId, "tables", tablePage] as const;
  const rooms = useQuery({ queryKey: roomKey, queryFn: () => restaurantMasterApi.rooms(roomPage), enabled: !!businessId });
  const tables = useQuery({ queryKey: tableKey, queryFn: () => restaurantMasterApi.tables(tablePage), enabled: !!businessId });
  const roomNames = useMemo(() => new Map((rooms.data?.items ?? []).map((room) => [room.roomId, room.name])), [rooms.data]);
  const saveRoom = useMutation({
    mutationFn: (draft: RoomDraft) => draft.rowVersion
      ? restaurantMasterApi.updateRoom(draft.id, { name: draft.name.trim(),
          sortOrder: draft.sortOrder, isActive: draft.isActive, rowVersion: draft.rowVersion })
      : restaurantMasterApi.createRoom({ roomId: draft.id, businessId: businessId!,
          name: draft.name.trim(), sortOrder: draft.sortOrder }),
    onSuccess: (room, draft) => {
      queryClient.setQueryData<RestaurantMasterPage<RestaurantRoom>>(roomKey, (page) =>
        page ? { ...page, totalCount: page.totalCount + (draft.rowVersion ? 0 : 1),
          items: [...page.items.filter((item) => item.roomId !== room.roomId), room]
            .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)).slice(0, page.pageSize) } : page);
      setRoomDraft(null);
      toast.success(draft.rowVersion ? "Salón actualizado." : "Salón creado.");
    },
    onError: () => toast.error("No fue posible guardar el salón. Revisa los cambios y vuelve a intentarlo."),
  });
  const saveTable = useMutation({
    mutationFn: (draft: TableDraft) => {
      const values = { code: draft.code.trim(), name: draft.name.trim() || null,
        capacity: draft.capacity ? Number(draft.capacity) : null,
        roomId: draft.roomId === "none" ? null : draft.roomId };
      return draft.rowVersion
        ? restaurantMasterApi.updateTable(draft.id, { ...values,
            isActive: draft.isActive, rowVersion: draft.rowVersion })
        : restaurantMasterApi.createTable({ ...values, tableId: draft.id,
            businessId: businessId! });
    },
    onSuccess: (table, draft) => {
      queryClient.setQueryData<RestaurantMasterPage<RestaurantTable>>(tableKey, (page) =>
        page ? { ...page, totalCount: page.totalCount + (draft.rowVersion ? 0 : 1),
          items: [...page.items.filter((item) => item.tableId !== table.tableId), table]
            .sort((a, b) => a.code.localeCompare(b.code)).slice(0, page.pageSize) } : page);
      setTableDraft(null);
      toast.success(draft.rowVersion ? "Mesa actualizada." : "Mesa creada. Ya aparece en la lista del restaurante.");
    },
    onError: () => toast.error("No fue posible guardar la mesa. Revisa los datos y vuelve a intentarlo."),
  });

  if (!businessId) return <div className="rounded-3xl border bg-card p-8 text-center text-muted-foreground">Selecciona una sede para configurar sus salones y mesas.</div>;
  return <div className="space-y-7 pb-12">
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-[0.2em] text-primary">Restaurante</p>
        <h1 className="mt-1 text-3xl font-bold tracking-tight">Salones, mesas y estaciones</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">Crea tus mesas una sola vez. Aparecen en la vista de atención aunque todavía no hayas dibujado el plano.</p></div>
      {canConfigure && <div className="flex gap-2"><Button variant="outline" onClick={() => setRoomDraft(emptyRoom())}><DoorOpen className="mr-2 h-4 w-4"/>Nuevo salón</Button>
        <Button onClick={() => setTableDraft(emptyTable())}><Plus className="mr-2 h-4 w-4"/>Nueva mesa</Button></div>}
    </header>
    <section className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-card p-4">
      <div><h2 className="font-semibold">Atención de meseros</h2><p className="text-sm text-muted-foreground">La sede define si se pide «Tu clave» y si una mesa queda reservada a su mesero responsable.</p></div>
      <Button variant="outline" asChild><Link href={`/dashboard/businesses/${businessId}`}>Configurar en la sede</Link></Button>
    </section>
    <section id="salones" className="space-y-3" aria-label="Salones">
      <div className="flex items-center justify-between"><h2 className="text-lg font-semibold">Salones</h2><span className="text-xs text-muted-foreground">{rooms.data?.totalCount ?? 0} en esta sede</span></div>
      {rooms.isError ? <LoadError onRetry={() => void rooms.refetch()} /> : rooms.isLoading ? <div className="h-16 animate-pulse rounded-2xl bg-muted"/> :
        <div className="flex flex-wrap gap-2">{rooms.data?.items.length ? rooms.data.items.map((room) =>
          <div key={room.roomId} className="flex min-h-12 items-center gap-2 rounded-2xl border bg-card px-4 text-sm font-medium shadow-sm"><DoorOpen className="h-4 w-4 text-primary"/>{room.name}{!room.isActive && <span className="text-xs text-muted-foreground">Inactivo</span>}{canConfigure && <Button variant="ghost" size="icon" className="ml-1 h-8 w-8" aria-label={`Editar salón ${room.name}`} onClick={() => setRoomDraft({ id: room.roomId, name: room.name, sortOrder: room.sortOrder, isActive: room.isActive, rowVersion: room.rowVersion })}><Pencil className="h-4 w-4"/></Button>}</div>) :
          <p className="rounded-2xl border border-dashed px-5 py-4 text-sm text-muted-foreground">Puedes crear mesas sin salón y organizarlas después.</p>}</div>}
      <Pager page={roomPage} total={rooms.data?.totalCount ?? 0} onChange={setRoomPage}/>
    </section>
    <section className="space-y-3" aria-label="Mesas">
      <div className="flex items-center justify-between"><h2 className="text-lg font-semibold">Mesas</h2><span className="text-xs text-muted-foreground">{tables.data?.totalCount ?? 0} creadas</span></div>
      {tables.isError ? <LoadError onRetry={() => void tables.refetch()} /> : tables.isLoading ? <div className="h-44 animate-pulse rounded-2xl bg-muted"/> :
        tables.data?.items.length ? <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{tables.data.items.map((table) =>
          <Card key={table.tableId} className="rounded-3xl border-border/70 shadow-sm"><CardContent className="flex min-h-40 flex-col justify-between p-5">
            <div className="flex items-start justify-between"><div className="grid h-12 w-12 place-items-center rounded-2xl bg-primary/10 text-primary"><Armchair className="h-6 w-6"/></div><span className={`rounded-full px-3 py-1 text-xs font-semibold ${table.isActive ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300" : "bg-muted text-muted-foreground"}`}>{table.isActive ? "Activa" : "Inactiva"}</span></div>
            <div><h3 className="mt-4 text-2xl font-bold">{table.code}</h3><p className="text-sm text-muted-foreground">{table.name || (table.roomId ? roomNames.get(table.roomId) ?? "Salón" : "Sin ubicar")}</p>
              {table.capacity && <p className="mt-2 flex items-center gap-1 text-xs text-muted-foreground"><Users className="h-3.5 w-3.5"/>{table.capacity} personas</p>}</div>
            {canConfigure && <Button variant="outline" size="sm" className="mt-4 w-full" onClick={() => setTableDraft({ id: table.tableId, code: table.code, name: table.name ?? "", capacity: table.capacity?.toString() ?? "", roomId: table.roomId ?? "none", isActive: table.isActive, rowVersion: table.rowVersion })}><Pencil className="mr-2 h-3.5 w-3.5"/>Editar mesa</Button>}
          </CardContent></Card>)}</div> :
          <div className="grid min-h-52 place-items-center rounded-3xl border border-dashed bg-muted/20 p-6 text-center"><div><LayoutGrid className="mx-auto h-9 w-9 text-primary/60"/><h3 className="mt-3 font-semibold">Tu salón empieza con la primera mesa</h3><p className="mt-1 text-sm text-muted-foreground">No necesitas crear un plano para comenzar a organizarlas.</p>{canConfigure && <Button className="mt-4" onClick={() => setTableDraft(emptyTable())}>Crear mesa</Button>}</div></div>}
      <Pager page={tablePage} total={tables.data?.totalCount ?? 0} onChange={setTablePage}/>
    </section>
    <div id="estaciones"><RestaurantStationsSettings key={businessId} businessId={businessId} canConfigure={canConfigure} /></div>
    <Dialog open={!!roomDraft} onOpenChange={(open) => { if (!open && !saveRoom.isPending) setRoomDraft(null); }}>
      <DialogContent><DialogHeader><DialogTitle>{roomDraft?.rowVersion ? "Editar salón" : "Nuevo salón"}</DialogTitle></DialogHeader><div className="space-y-4"><Label htmlFor="restaurant-room-name">Nombre</Label><Input id="restaurant-room-name" maxLength={100} value={roomDraft?.name ?? ""} onChange={(event) => setRoomDraft((draft) => draft && { ...draft, name: event.target.value })} placeholder="Terraza, primer piso, salón principal…"/>
        {roomDraft?.rowVersion && <div className="flex items-center justify-between rounded-xl border p-3"><Label htmlFor="restaurant-room-active">Salón activo</Label><Switch id="restaurant-room-active" checked={roomDraft.isActive} onCheckedChange={(checked) => setRoomDraft((draft) => draft && { ...draft, isActive: checked })}/></div>}
        <Button className="w-full" disabled={!roomDraft?.name.trim() || saveRoom.isPending} onClick={() => roomDraft && saveRoom.mutate(roomDraft)}>{saveRoom.isPending ? "Guardando…" : roomDraft?.rowVersion ? "Guardar salón" : "Crear salón"}</Button></div></DialogContent>
    </Dialog>
    <Dialog open={!!tableDraft} onOpenChange={(open) => { if (!open && !saveTable.isPending) setTableDraft(null); }}>
      <DialogContent><DialogHeader><DialogTitle>{tableDraft?.rowVersion ? "Editar mesa" : "Nueva mesa"}</DialogTitle></DialogHeader><div className="grid gap-4"><div className="grid gap-2"><Label htmlFor="restaurant-table-code">Número o código</Label><Input id="restaurant-table-code" maxLength={32} value={tableDraft?.code ?? ""} onChange={(event) => setTableDraft((draft) => draft && { ...draft, code: event.target.value })} placeholder="01, 02, Barra 1…"/></div>
        <div className="grid gap-2"><Label htmlFor="restaurant-table-name">Nombre opcional</Label><Input id="restaurant-table-name" maxLength={100} value={tableDraft?.name ?? ""} onChange={(event) => setTableDraft((draft) => draft && { ...draft, name: event.target.value })} placeholder="Junto a la ventana"/></div>
        <div className="grid grid-cols-2 gap-3"><div className="grid gap-2"><Label htmlFor="restaurant-table-capacity">Capacidad</Label><Input id="restaurant-table-capacity" type="number" min={1} max={32767} value={tableDraft?.capacity ?? ""} onChange={(event) => setTableDraft((draft) => draft && { ...draft, capacity: event.target.value })}/></div>
          <div className="grid gap-2"><Label>Salón</Label><Select value={tableDraft?.roomId ?? "none"} onValueChange={(value) => setTableDraft((draft) => draft && { ...draft, roomId: value })}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="none">Sin ubicar</SelectItem>{(rooms.data?.items ?? []).filter((room) => room.isActive).map((room) => <SelectItem key={room.roomId} value={room.roomId}>{room.name}</SelectItem>)}</SelectContent></Select></div></div>
        {tableDraft?.rowVersion && <div className="flex items-center justify-between rounded-xl border p-3"><Label htmlFor="restaurant-table-active">Mesa activa</Label><Switch id="restaurant-table-active" checked={tableDraft.isActive} onCheckedChange={(checked) => setTableDraft((draft) => draft && { ...draft, isActive: checked })}/></div>}
        <Button className="mt-1 w-full" disabled={!tableDraft?.code.trim() || saveTable.isPending || (tableDraft.capacity !== "" && (!Number.isInteger(Number(tableDraft.capacity)) || Number(tableDraft.capacity) < 1 || Number(tableDraft.capacity) > 32767))} onClick={() => tableDraft && saveTable.mutate(tableDraft)}>{saveTable.isPending ? "Guardando…" : tableDraft?.rowVersion ? "Guardar mesa" : "Crear mesa"}</Button></div></DialogContent>
    </Dialog>
  </div>;
}

function Pager({ page, total, onChange }: { page: number; total: number; onChange: (page: number) => void }) {
  if (total <= 100) return null;
  return <div className="flex items-center justify-end gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>Anterior</Button><span className="text-xs text-muted-foreground">Página {page}</span><Button variant="outline" size="sm" disabled={page * 100 >= total} onClick={() => onChange(page + 1)}>Siguiente</Button></div>;
}

function LoadError({ onRetry }: { onRetry: () => void }) {
  return <div role="alert" className="rounded-2xl border border-destructive/30 bg-destructive/5 p-4 text-sm">No fue posible cargar esta información. <Button variant="link" size="sm" onClick={onRetry}>Reintentar</Button></div>;
}
