"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Armchair, ArrowLeft, Copy, DoorOpen, Grip, LayoutGrid, Redo2, Save, Send, Sofa, Trash2, Trees, Undo2, Utensils } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { restaurantMasterApi, type RestaurantFloorPlan, type RestaurantFloorPlanObject } from "@/services/api/restaurant-master";
import { floorPlanShapeRadius, floorPlanTextColor } from "@/lib/restaurant-floorplan-appearance";
import { FloorPlanObjectAppearance, FloorPlanTableSeats } from "@/components/restaurant/floor-plan-object-appearance";
import { useBusinessContextStore } from "@/stores/business-context-store";

const dimensions = { width: 1000, height: 700 };
const kindLabels: Record<RestaurantFloorPlanObject["kind"], string> = {
  Table: "Mesa", Chair: "Silla", Bench: "Mueble", Wall: "Pared", Door: "Puerta", Counter: "Barra",
  Plant: "Planta", Label: "Texto", Window: "Ventana", Column: "Columna",
  Zone: "Zona", Kitchen: "Cocina", Bathroom: "Baño", Entrance: "Entrada",
};
const shapeLabels = { Round: "Circular", Oval: "Ovalada", Square: "Cuadrada",
  Rectangle: "Rectangular", Rounded: "Esquinas redondeadas" } as const;

function makeSeat(kind: "Chair" | "Bench", table: RestaurantFloorPlanObject,
  index: number): RestaurantFloorPlanObject {
  const width = kind === "Chair" ? 38 : 110;
  const height = kind === "Chair" ? 38 : 42;
  const gap = 14;
  const positions = [
    { x: table.x + (table.width - width) / 2, y: table.y - height - gap },
    { x: table.x + table.width + gap, y: table.y + (table.height - height) / 2 },
    { x: table.x + (table.width - width) / 2, y: table.y + table.height + gap },
    { x: table.x - width - gap, y: table.y + (table.height - height) / 2 },
  ];
  const side = positions[index % positions.length];
  const spread = Math.floor(index / positions.length);
  const offset = spread === 0 ? 0 : Math.ceil(spread / 2) * 44 * (spread % 2 ? 1 : -1);
  const horizontal = index % 2 === 0;
  return {
    objectId: crypto.randomUUID(), kind, tableId: table.tableId,
    x: Math.max(0, Math.min(dimensions.width - width, Math.round(side.x + (horizontal ? offset : 0)))),
    y: Math.max(0, Math.min(dimensions.height - height, Math.round(side.y + (horizontal ? 0 : offset)))),
    width, height, rotation: 0, label: null, shape: null, color: null, chairs: null,
  };
}

export default function RestaurantFloorPlanPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const cache = useQueryClient();
  const [roomId, setRoomId] = useState("");
  const [objects, setObjects] = useState<RestaurantFloorPlanObject[]>([]);
  const [history, setHistory] = useState<RestaurantFloorPlanObject[][]>([]);
  const [future, setFuture] = useState<RestaurantFloorPlanObject[][]>([]);
  const [version, setVersion] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const surface = useRef<HTMLDivElement>(null);
  const drag = useRef<{ id: string; pointerX: number; pointerY: number; x: number; y: number; remembered: boolean } | null>(null);
  const resize = useRef<{ id: string; pointerX: number; pointerY: number; width: number; height: number; remembered: boolean } | null>(null);
  const rooms = useQuery({ queryKey: ["restaurant-floor-rooms", businessId],
    queryFn: () => restaurantMasterApi.rooms() });
  useEffect(() => {
    if (!roomId && rooms.data?.items.length) setRoomId(rooms.data.items[0].roomId);
  }, [roomId, rooms.data]);
  const plan = useQuery({ queryKey: ["restaurant-floorplan", businessId, roomId],
    queryFn: () => restaurantMasterApi.floorPlan(roomId, true), enabled: !!roomId });
  useEffect(() => {
    if (!plan.data || dirty) return;
    setObjects(plan.data.objects);
    setHistory([]);
    setFuture([]);
    setVersion(plan.data.rowVersion);
    setSelected(null);
  }, [plan.data, dirty]);

  const active = objects.find((item) => item.objectId === selected);
  const activeTableId = active?.kind === "Table" ? active.tableId :
    active?.kind === "Chair" || active?.kind === "Bench" ? active.tableId : null;
  const customSeating = activeTableId ? objects.filter((item) =>
    item.tableId === activeTableId && (item.kind === "Chair" || item.kind === "Bench")) : [];
  const seatingByTable = new Map<string, { chairs: number; furniture: number }>();
  for (const item of objects) {
    if (!item.tableId || (item.kind !== "Chair" && item.kind !== "Bench")) continue;
    const counts = seatingByTable.get(item.tableId) ?? { chairs: 0, furniture: 0 };
    if (item.kind === "Chair") counts.chairs++;
    else counts.furniture++;
    seatingByTable.set(item.tableId, counts);
  }
  const tableById = new Map((plan.data?.tables ?? [])
    .map((table) => [table.tableId, table]));
  const placedTables = new Set(objects.filter((item) => item.kind === "Table").map((item) => item.tableId));
  const availableTables = [...tableById.values()].filter((table) => !placedTables.has(table.tableId));

  function remember() {
    setHistory((current) => [...current.slice(-29), objects.map((item) => ({ ...item }))]);
    setFuture([]);
  }

  function add(kind: RestaurantFloorPlanObject["kind"], tableId: string | null = null) {
    const tableObject = tableId && kind !== "Table" ? objects.find((item) =>
      item.kind === "Table" && item.tableId === tableId) : undefined;
    if ((kind === "Chair" || kind === "Bench") && tableObject) {
      const existing = objects.filter((item) => item.tableId === tableId &&
        (item.kind === "Chair" || item.kind === "Bench"));
      const automatic = existing.length === 0 ? Array.from(
        { length: Math.min(Math.max(tableObject.chairs ?? 0, 0), 16) }, (_, index) =>
          makeSeat("Chair", tableObject, index)) : [];
      const next = makeSeat(kind, tableObject, existing.length + automatic.length);
      remember();
      setObjects((current) => [...current.map((item) => automatic.length &&
        item.objectId === tableObject.objectId ? { ...item, chairs: 0 } : item),
        ...automatic, next]);
      setSelected(next.objectId);
      setDirty(true);
      return;
    }
    remember();
    const objectId = crypto.randomUUID();
    const sizes: Record<RestaurantFloorPlanObject["kind"],
      { width: number; height: number }> = {
      Table: { width: 110, height: 100 },
      Chair: { width: 38, height: 38 },
      Bench: { width: 110, height: 42 },
      Wall: { width: 200, height: 16 },
      Window: { width: 130, height: 24 },
      Door: { width: 80, height: 80 },
      Entrance: { width: 100, height: 80 },
      Counter: { width: 180, height: 60 },
      Plant: { width: 70, height: 70 },
      Column: { width: 55, height: 55 },
      Kitchen: { width: 140, height: 130 },
      Bathroom: { width: 110, height: 110 },
      Zone: { width: 160, height: 120 },
      Label: { width: 160, height: 40 },
    };
    const size = sizes[kind];
    const proposedX = 100 + (objects.length % 5) * 90;
    const proposedY = 100 + Math.floor(objects.length / 5) * 90;
    const next: RestaurantFloorPlanObject = { objectId, kind, tableId,
      x: Math.min(1000 - size.width, proposedX),
      y: Math.min(700 - size.height, proposedY),
      ...sizes[kind],
      rotation: 0, label: kind === "Label" ? "Nueva zona" : null,
      shape: kind === "Table" ? "Rounded" : null,
      color: null, chairs: kind === "Table" ? 4 : null };
    setObjects((current) => [...current, next]);
    setSelected(objectId);
    setDirty(true);
  }

  function makeAutomaticSeatsMovable(table: RestaurantFloorPlanObject) {
    if (!table.tableId || seatingByTable.has(table.tableId)) return;
    const count = Math.min(Math.max(table.chairs ?? 0, 0), 16);
    if (!count) return;
    const seats = Array.from({ length: count }, (_, index) => makeSeat("Chair", table, index));
    remember();
    setObjects((current) => [...current.map((item) => item.objectId === table.objectId ?
      { ...item, chairs: 0 } : item), ...seats]);
    setSelected(seats[0].objectId);
    setDirty(true);
  }

  function change(id: string, patch: Partial<RestaurantFloorPlanObject>) {
    setObjects((current) => current.map((item) => item.objectId === id ? { ...item, ...patch } : item));
    setDirty(true);
  }

  async function save() {
    if (!roomId || !dirty || saving) return;
    setSaving(true);
    try {
      const result = await restaurantMasterApi.saveFloorPlan(roomId, { rowVersion: version, objects });
      setVersion(result.rowVersion);
      setDirty(false);
      setHistory([]);
      setFuture([]);
      cache.setQueryData(["restaurant-floorplan", businessId, roomId], result);
      toast.success("Borrador guardado. Publícalo cuando esté listo para el punto de venta.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo guardar el plano.");
    } finally { setSaving(false); }
  }

  async function publish() {
    if (!roomId || dirty || saving || !plan.data?.hasUnpublishedChanges) return;
    setSaving(true);
    try {
      const published = await restaurantMasterApi.publishFloorPlan(roomId, version);
      setVersion(published.rowVersion);
      setHistory([]);
      setFuture([]);
      cache.setQueryData(["restaurant-floorplan", businessId, roomId], published);
      cache.setQueriesData<RestaurantFloorPlan>({ predicate: (query) =>
        query.queryKey.includes("floorplan") &&
        query.queryKey.includes(businessId) &&
        query.queryKey.includes(roomId) &&
        query.queryKey[0] !== "restaurant-floorplan" }, published);
      toast.success("Plano publicado. Ya se ve en el punto de venta.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No se pudo publicar el plano.");
    } finally { setSaving(false); }
  }

  return <main className="space-y-5 pb-12">
    <div><Link href="/dashboard/restaurant" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4"/>Volver</Link>
      <h1 className="mt-3 text-3xl font-bold">Diseña tu restaurante</h1>
      <p className="mt-1 text-muted-foreground">Arrastra mesas, paredes y detalles. Guarda un borrador y publícalo para mostrarlo en el punto de venta.</p></div>
    <div className="flex flex-wrap items-center gap-2">
      {(rooms.data?.items ?? []).filter((room) => room.isActive).map((room) =>
        <Button key={room.roomId} variant={roomId === room.roomId ? "default" : "outline"}
          disabled={dirty && roomId !== room.roomId} onClick={() => setRoomId(room.roomId)}>{room.name}</Button>)}
      <Button variant="outline" disabled={!history.length || saving} onClick={() => {
        const previous = history.at(-1);
        if (!previous) return;
        setObjects(previous);
        setHistory(history.slice(0, -1));
        setFuture((current) => [...current, objects.map((item) => ({ ...item }))]);
        setSelected(null);
        setDirty(true);
      }}><Undo2 className="mr-2 h-4 w-4"/>Deshacer</Button>
      <Button variant="outline" disabled={!future.length || saving} onClick={() => {
        const next = future.at(-1);
        if (!next) return;
        setHistory((current) => [...current, objects.map((item) => ({ ...item }))]);
        setObjects(next);
        setFuture(future.slice(0, -1));
        setSelected(null);
        setDirty(true);
      }}><Redo2 className="mr-2 h-4 w-4"/>Rehacer</Button>
      <Button className="ml-auto" disabled={!dirty || saving} onClick={() => void save()}><Save className="mr-2 h-4 w-4"/>{saving ? "Guardando…" : "Guardar plano"}</Button>
      <Button variant="outline" disabled={dirty || saving || !plan.data?.hasUnpublishedChanges} onClick={() => void publish()}><Send className="mr-2 h-4 w-4"/>Publicar plano</Button>
    </div>
    {dirty && <p className="text-sm text-amber-700 dark:text-amber-300">Guarda el plano antes de cambiar de salón o publicarlo.</p>}
    {!dirty && plan.data?.hasUnpublishedChanges && <p className="text-sm text-primary">Tienes un borrador guardado. El punto de venta seguirá mostrando el plano anterior hasta que publiques.</p>}
    {plan.isError && <div role="alert" className="rounded-xl border border-destructive/40 p-4">No pudimos cargar el plano. <Button variant="link" onClick={() => void plan.refetch()}>Reintentar</Button></div>}
    {!rooms.isLoading && !rooms.data?.items.length && <p>Crea un salón y sus mesas en <Link className="underline" href="/dashboard/restaurant/setup#salones">Salones y mesas</Link>.</p>}
    {plan.data && <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_280px]">
      <div className="overflow-hidden rounded-3xl border bg-card p-3 shadow-sm sm:p-5">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Select key={objects.length} disabled={availableTables.length === 0} onValueChange={(tableId) => add("Table", tableId)}>
            <SelectTrigger aria-label="Elegir mesa creada para agregar al plano" className="h-11 min-w-52 rounded-xl"><SelectValue placeholder={availableTables.length ? "Elegir mesa creada…" : "Sin mesas disponibles"}/></SelectTrigger>
            <SelectContent>{availableTables.map((table) => <SelectItem key={table.tableId} value={table.tableId}>Mesa {table.code}</SelectItem>)}</SelectContent>
          </Select>
          <Link href="/dashboard/restaurant/setup#salones" className="text-sm font-medium text-primary underline-offset-4 hover:underline">Administrar mesas</Link>
          <Button variant="outline" onClick={() => add("Wall")}><LayoutGrid className="mr-2 h-4 w-4"/>Pared</Button>
          <Button variant="outline" onClick={() => add("Door")}><DoorOpen className="mr-2 h-4 w-4"/>Puerta / entrada</Button>
          <Button variant="outline" disabled={!activeTableId} title={activeTableId ? "Agregar silla a esta mesa" : "Selecciona una mesa primero"} onClick={() => { if (activeTableId) add("Chair", activeTableId); }}><Armchair className="mr-2 h-4 w-4"/>Silla</Button>
          <Button variant="outline" disabled={!activeTableId} title={activeTableId ? "Agregar mueble a esta mesa" : "Selecciona una mesa primero"} onClick={() => { if (activeTableId) add("Bench", activeTableId); }}><Sofa className="mr-2 h-4 w-4"/>Mueble</Button>
          <Button variant="outline" onClick={() => add("Counter")}><Utensils className="mr-2 h-4 w-4"/>Barra</Button>
          <Button variant="outline" onClick={() => add("Plant")}><Trees className="mr-2 h-4 w-4"/>Planta</Button>
          <Button variant="outline" onClick={() => add("Label")}>+ Texto</Button>
          <Button variant="outline" onClick={() => add("Window")}>+ Ventana</Button>
          <Button variant="outline" onClick={() => add("Column")}>+ Columna</Button>
          <Button variant="outline" onClick={() => add("Zone")}>+ Zona</Button>
          <Button variant="outline" onClick={() => add("Kitchen")}>+ Cocina</Button>
          <Button variant="outline" onClick={() => add("Bathroom")}>+ Baño</Button>
        </div>
        <div ref={surface} className="relative aspect-[10/7] w-full touch-none overflow-hidden rounded-2xl border-2 border-dashed border-primary/25 bg-[#f8f5ed] bg-[radial-gradient(circle_at_center,#b9b19d55_1px,transparent_1px)] bg-[length:20px_20px] dark:bg-[#202d29]"
          onPointerMove={(event) => {
            if (!surface.current) return;
            const scaleX = dimensions.width / surface.current.clientWidth;
            const scaleY = dimensions.height / surface.current.clientHeight;
            if (resize.current) {
              const item = objects.find((candidate) => candidate.objectId === resize.current?.id);
              if (!item) return;
              if (!resize.current.remembered) {
                if (event.clientX === resize.current.pointerX && event.clientY === resize.current.pointerY) return;
                remember();
                resize.current.remembered = true;
              }
              change(item.objectId, {
                width: Math.max(item.kind === "Wall" ? 8 : 20, Math.min(1000 - item.x, Math.round(resize.current.width + (event.clientX - resize.current.pointerX) * scaleX))),
                height: Math.max(item.kind === "Wall" ? 8 : 20, Math.min(700 - item.y, Math.round(resize.current.height + (event.clientY - resize.current.pointerY) * scaleY))),
              });
              return;
            }
            if (!drag.current) return;
            const item = objects.find((candidate) => candidate.objectId === drag.current?.id);
            if (!item) return;
            if (!drag.current.remembered) {
              if (event.clientX === drag.current.pointerX && event.clientY === drag.current.pointerY) return;
              remember();
              drag.current.remembered = true;
            }
            change(item.objectId, { x: Math.max(0, Math.min(1000 - item.width, Math.round(drag.current.x + (event.clientX - drag.current.pointerX) * scaleX))),
              y: Math.max(0, Math.min(700 - item.height, Math.round(drag.current.y + (event.clientY - drag.current.pointerY) * scaleY))) });
          }} onPointerUp={() => { drag.current = null; resize.current = null; }} onPointerCancel={() => { drag.current = null; resize.current = null; }}>
          {objects.map((item) => {
            const table = item.tableId ? tableById.get(item.tableId) : undefined;
            return <button key={item.objectId} type="button" data-floor-kind={item.kind} aria-label={item.kind === "Table" ? `Mesa ${table?.code ?? "sin código"}` : `${kindLabels[item.kind]}${table ? ` de mesa ${table.code}` : ""}`} onPointerDown={(event) => { event.currentTarget.setPointerCapture(event.pointerId); drag.current = { id: item.objectId, pointerX: event.clientX, pointerY: event.clientY, x: item.x, y: item.y, remembered: false }; setSelected(item.objectId); }}
              onClick={() => setSelected(item.objectId)}
              className={`absolute flex touch-none select-none items-center justify-center text-center text-xs font-bold transition-colors ${item.kind === "Table" ? "rounded-2xl border-2 border-teal-500 bg-teal-100 text-teal-950 shadow-md dark:bg-teal-800 dark:text-white" : item.kind === "Chair" || item.kind === "Bench" ? "overflow-visible rounded-xl" : "overflow-hidden rounded-xl"} ${selected === item.objectId ? "ring-2 ring-primary ring-offset-2" : ""}`}
              style={{ left: `${item.x / 10}%`, top: `${item.y / 7}%`, width: `${item.width / 10}%`, height: `${item.height / 7}%`, rotate: `${item.rotation}deg`,
                borderRadius: item.kind === "Table" ? floorPlanShapeRadius(item.shape) : undefined,
                backgroundColor: item.kind === "Table" ? item.color ?? undefined : undefined,
                color: item.kind === "Table" && item.color ? floorPlanTextColor(item.color) : undefined }}>
              {item.kind === "Table" ? <><FloorPlanTableSeats chairs={item.tableId && seatingByTable.has(item.tableId) ? 0 : item.chairs}/><span className="relative z-10">Mesa {table?.code ?? "?"}{item.tableId && seatingByTable.has(item.tableId) ? <small className="block font-normal">{seatingByTable.get(item.tableId)?.chairs ?? 0} sillas · {seatingByTable.get(item.tableId)?.furniture ?? 0} muebles</small> : item.chairs ? <small className="block font-normal">{item.chairs} sillas</small> : null}</span></> : <FloorPlanObjectAppearance object={item}/>}
              {selected === item.objectId && <span aria-hidden="true" className={`absolute flex touch-none cursor-nwse-resize items-center justify-center bg-primary text-primary-foreground ${item.kind === "Chair" || item.kind === "Bench" ? "-bottom-5 -right-5 h-6 w-6 rounded-lg shadow-md" : "bottom-0 right-0 h-8 w-8 rounded-tl-xl"}`}
                onPointerDown={(event) => { event.preventDefault(); event.stopPropagation(); event.currentTarget.setPointerCapture(event.pointerId); resize.current = { id: item.objectId, pointerX: event.clientX, pointerY: event.clientY, width: item.width, height: item.height, remembered: false }; }}><Grip className="h-4 w-4"/></span>}
            </button>;
          })}
        </div>
      </div>
      <aside className="h-fit rounded-3xl border bg-card p-5 shadow-sm">
        <h2 className="flex items-center gap-2 text-lg font-semibold"><Grip className="h-5 w-5"/>Propiedades</h2>
        {!active && <p className="mt-3 text-sm text-muted-foreground">Toca un elemento para moverlo o cambiar sus detalles.</p>}
        {active && <div className="mt-4 space-y-4"><p className="font-medium">{kindLabels[active.kind]}{active.tableId ? ` · ${tableById.get(active.tableId)?.code ?? ""}` : ""}</p>
          {active.kind === "Table" && <div className="space-y-3 rounded-2xl border border-primary/20 bg-primary/5 p-3">
            <div><p className="text-sm font-semibold">Sillas y muebles de esta mesa</p><p className="mt-1 text-xs text-muted-foreground">Agrega piezas y arrástralas a donde quieras alrededor de la mesa. Selecciona cada una para cambiar su tamaño o girarla.</p></div>
            <p className="text-xs text-muted-foreground">Usa Silla o Mueble en la barra de herramientas para añadir más piezas a esta mesa.</p>
            {customSeating.length === 0 && (active.chairs ?? 0) > 0 && <Button variant="ghost" className="w-full text-xs" onClick={() => makeAutomaticSeatsMovable(active)}>Mover las {active.chairs} sillas actuales</Button>}
          </div>}
          {active.kind === "Table" && customSeating.length > 0 && <p className="text-xs text-muted-foreground">{customSeating.filter((item) => item.kind === "Chair").length} sillas · {customSeating.filter((item) => item.kind === "Bench").length} muebles. Los asientos automáticos se ocultan mientras uses piezas a medida.</p>}
          {active.kind !== "Table" && <label className="block text-sm">Nombre<Input className="mt-1" value={active.label ?? ""} maxLength={80} onFocus={remember} onChange={(event) => change(active.objectId, { label: event.target.value })}/></label>}
          {active.kind === "Table" && <div className="space-y-1.5"><Label htmlFor="floor-table-shape">Forma</Label><Select value={active.shape ?? "Rounded"} onValueChange={(value) => { remember(); change(active.objectId, { shape: value as RestaurantFloorPlanObject["shape"] }); }}><SelectTrigger id="floor-table-shape" className="h-11 rounded-xl bg-background"><SelectValue/></SelectTrigger><SelectContent>{Object.entries(shapeLabels).map(([code, name]) => <SelectItem key={code} value={code}>{name}</SelectItem>)}</SelectContent></Select></div>}
          {active.kind === "Table" && customSeating.length === 0 && <label className="block text-sm">Sillas automáticas<Input className="mt-1" type="number" min={0} max={16} value={active.chairs ?? ""} placeholder="0" onFocus={remember} onChange={(event) => change(active.objectId, { chairs: event.target.value === "" ? null : Number(event.target.value) })}/></label>}
          <label className="block text-sm">Color<input aria-label="Color del elemento" className="mt-1 block h-11 w-full cursor-pointer rounded-xl border bg-background p-1" type="color" value={active.color ?? "#0d9488"} onFocus={remember} onChange={(event) => change(active.objectId, { color: event.target.value })}/></label>
          <label className="block text-sm">Ancho<input aria-label="Ancho" type="range" min={active.kind === "Wall" ? 8 : 20} max={Math.min(400, 1000 - active.x)} value={active.width} onFocus={remember} onChange={(event) => change(active.objectId, { width: Number(event.target.value) })} className="mt-2 w-full"/></label>
          <label className="block text-sm">Alto<input aria-label="Alto" type="range" min={active.kind === "Wall" ? 8 : 20} max={Math.min(300, 700 - active.y)} value={active.height} onFocus={remember} onChange={(event) => change(active.objectId, { height: Number(event.target.value) })} className="mt-2 w-full"/></label>
          <label className="block text-sm">Rotación<input aria-label="Rotación" type="range" min="0" max="359" value={active.rotation} onFocus={remember} onChange={(event) => change(active.objectId, { rotation: Number(event.target.value) })} className="mt-2 w-full"/></label>
          {active.kind !== "Table" && <Button variant="outline" className="w-full" onClick={() => { remember(); const duplicate = { ...active, objectId: crypto.randomUUID(), x: Math.min(1000 - active.width, active.x + 24), y: Math.min(700 - active.height, active.y + 24) }; setObjects((current) => [...current, duplicate]); setSelected(duplicate.objectId); setDirty(true); }}><Copy className="mr-2 h-4 w-4"/>Duplicar elemento</Button>}
          <Button variant="destructive" className="w-full" onClick={() => { remember(); setObjects((current) => current.filter((item) => item.objectId !== active.objectId && !(active.kind === "Table" && item.tableId === active.tableId && (item.kind === "Chair" || item.kind === "Bench")))); setSelected(null); setDirty(true); }}><Trash2 className="mr-2 h-4 w-4"/>Quitar del plano</Button>
        </div>}
      </aside>
    </div>}
  </main>;
}
