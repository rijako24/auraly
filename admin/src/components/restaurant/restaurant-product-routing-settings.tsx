"use client";

import { useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { ProductPicker } from "@/components/products/product-picker";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { restaurantMasterApi, type RestaurantProductRoute, type RestaurantStation } from "@/services/api/restaurant-master";

type RouteEditor = {
  productId: string; productName: string; productCode: string | null;
  route: RestaurantProductRoute | null; needsLookup: boolean;
};

export function RestaurantProductRoutingSettings({ businessId, stations, canConfigure }: {
  businessId: string; stations: RestaurantStation[]; canConfigure: boolean;
}) {
  const [page, setPage] = useState(1);
  const [editor, setEditor] = useState<RouteEditor | null>(null);
  const [stationId, setStationId] = useState<string | null>(null);
  const [shortName, setShortName] = useState<string | null>(null);
  const [removing, setRemoving] = useState<RestaurantProductRoute | null>(null);
  const returnToProductSearch = useRef(false);
  const selectedProductIds = useMemo(() => new Set(editor ? [editor.productId] : []), [editor]);
  const queryClient = useQueryClient();
  const listKey = ["restaurant", businessId, "product-routes"] as const;
  const routes = useQuery({ queryKey: [...listKey, page], queryFn: () => restaurantMasterApi.productRoutes(page) });
  const lookup = useQuery({ queryKey: ["restaurant", businessId, "product-route", editor?.productId],
    queryFn: () => restaurantMasterApi.productRoute(editor!.productId), enabled: !!editor?.needsLookup });
  const currentRoute = editor?.needsLookup ? lookup.data : editor?.route;
  const chosenStation = stationId ?? currentRoute?.stationId ?? "";
  const activeStations = stations.filter((station) => station.isActive);

  const save = useMutation({
    mutationFn: (value: { productId: string; stationId: string; shortName: string | null; rowVersion: string | null }) =>
      restaurantMasterApi.setProductRoute(value.productId, { businessId, stationId: value.stationId,
        shortName: value.shortName, rowVersion: value.rowVersion }),
    onSuccess: (saved, value) => {
      queryClient.setQueryData(["restaurant", businessId, "product-route", value.productId], saved);
      // Altas y cambios pueden desplazar filas entre páginas; releer solo la página destino.
      const targetPage = value.rowVersion ? page : 1;
      void queryClient.invalidateQueries({ queryKey: listKey, refetchType: "none" });
      void queryClient.refetchQueries({ queryKey: [...listKey, targetPage], exact: true });
      if (targetPage !== page) setPage(targetPage);
      returnToProductSearch.current = true;
      setEditor(null);
      toast.success(value.rowVersion ? "Destino actualizado." : "Producto agregado a los destinos.");
    },
    onError: () => toast.error("No se pudo guardar el destino. Revisa si otro usuario lo cambió."),
  });
  const remove = useMutation({
    mutationFn: (route: RestaurantProductRoute) => restaurantMasterApi.deleteProductRoute(route.productId, route.rowVersion),
    onSuccess: (deleted) => {
      queryClient.setQueryData(["restaurant", businessId, "product-route", deleted.productId], null);
      const targetPage = page > 1 && routes.data?.items.length === 1 ? page - 1 : page;
      void queryClient.invalidateQueries({ queryKey: listKey, refetchType: "none" });
      void queryClient.refetchQueries({ queryKey: [...listKey, targetPage], exact: true });
      if (targetPage !== page) setPage(targetPage);
      setRemoving(null);
      toast.success("Destino eliminado.");
    },
    onError: () => toast.error("No se pudo eliminar el destino. Recarga la lista e inténtalo de nuevo."),
  });
  function openEditor(value: RouteEditor) {
    setStationId(null);
    setShortName(null);
    setEditor(value);
  }

  return <section className="space-y-5 rounded-3xl border bg-card p-5" aria-label="Destinos de productos">
    <div><h2 className="text-lg font-semibold">Destino de cada producto</h2>
      <p className="text-sm text-muted-foreground">El producto pertenece al catálogo de toda la organización. Aquí eliges a qué estación de esta sede llegan sus comandas.</p></div>
    <div className="max-w-xl"><ProductPicker businessId={businessId} selectedProductIds={selectedProductIds}
      disabled={!canConfigure || save.isPending || remove.isPending} label="Buscar y agregar producto"
      inputId="restaurant-route-product-search" onSelect={(product) => {
        const listed = routes.data?.items.find((item) => item.productId === product.productId);
        openEditor({ productId: product.productId, productName: product.productName,
          productCode: product.productCode || null, route: listed ?? null, needsLookup: !listed });
      }}/></div>

    <div className="overflow-hidden rounded-2xl border">
      <div className="flex items-center justify-between gap-3 border-b bg-muted/30 px-4 py-3">
        <h3 className="font-semibold">Productos con destino</h3>
        <span className="text-sm text-muted-foreground">{routes.data?.totalCount ?? 0} asignados</span>
      </div>
      {routes.isLoading && <p className="p-5 text-sm text-muted-foreground">Cargando destinos…</p>}
      {routes.isError && <p role="alert" className="p-5 text-sm text-destructive">No se pudieron cargar los destinos. <Button variant="link" onClick={() => void routes.refetch()}>Reintentar</Button></p>}
      {!routes.isLoading && !routes.isError && !routes.data?.items.length &&
        <p className="p-6 text-sm text-muted-foreground">Aún no hay productos asignados. Busca uno arriba para agregarlo.</p>}
      {!!routes.data?.items.length && <div className="divide-y"><div className="hidden grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1fr)_auto] gap-3 bg-muted/20 px-4 py-2 text-xs font-bold uppercase tracking-wide text-muted-foreground sm:grid"><span>Producto</span><span>Estación</span><span>En comanda</span><span>Acciones</span></div>{routes.data.items.map((route) =>
        <div key={route.productId} className="grid gap-2 px-4 py-3 sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-center sm:gap-3">
          <div className="min-w-0"><p className="font-medium">{route.productName ?? route.productId}</p>
            <p className="text-xs text-muted-foreground">{route.productCode ?? "Sin código"}</p></div>
          <div><span className="inline-flex rounded-full bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary">{stations.find((station) => station.stationId === route.stationId)?.name ?? "Estación no disponible"}</span></div>
          <p className="text-sm text-muted-foreground">{route.shortName ? `En comanda: ${route.shortName}` : "Nombre del producto"}</p>
          <div className="flex items-center gap-2">
            {canConfigure && <><Button size="icon" variant="outline" aria-label={`Editar destino de ${route.productName ?? "producto"}`} onClick={() => openEditor({ productId: route.productId, productName: route.productName ?? route.productId, productCode: route.productCode, route, needsLookup: false })}><Pencil className="h-4 w-4"/></Button>
              <Button size="icon" variant="outline" aria-label={`Eliminar destino de ${route.productName ?? "producto"}`} onClick={() => setRemoving(route)}><Trash2 className="h-4 w-4"/></Button></>}</div>
        </div>)}</div>}
    </div>
    {(routes.data?.totalCount ?? 0) > 25 && <div className="flex items-center justify-end gap-3">
      <Button variant="outline" disabled={page === 1} onClick={() => setPage(page - 1)}>Anterior</Button>
      <span className="text-sm text-muted-foreground">Página {page}</span>
      <Button variant="outline" disabled={page * 25 >= (routes.data?.totalCount ?? 0)} onClick={() => setPage(page + 1)}>Siguiente</Button>
    </div>}

    <Dialog open={!!editor} onOpenChange={(open) => { if (!open && !save.isPending) setEditor(null); }}>
      <DialogContent onCloseAutoFocus={(event) => {
        if (!returnToProductSearch.current) return;
        event.preventDefault();
        returnToProductSearch.current = false;
        document.getElementById("restaurant-route-product-search")?.focus();
      }}><DialogHeader><DialogTitle>Destino de {editor?.productName}</DialogTitle></DialogHeader>
        {editor?.needsLookup && lookup.isFetching ? <p className="text-sm text-muted-foreground">Consultando destino actual…</p> :
          editor?.needsLookup && lookup.isError ? <p role="alert" className="text-sm text-destructive">No se pudo consultar el producto. <Button variant="link" onClick={() => void lookup.refetch()}>Reintentar</Button></p> :
          <div className="space-y-4"><div className="grid gap-2"><Label>Estación</Label>
            <Select value={chosenStation} onValueChange={setStationId}><SelectTrigger><SelectValue placeholder="Elige una estación"/></SelectTrigger>
              <SelectContent>{activeStations.map((station) => <SelectItem key={station.stationId} value={station.stationId}>{station.name} · {station.kind === "Preparation" ? "Preparación" : "Retiro"}</SelectItem>)}</SelectContent></Select></div>
            <div className="grid gap-2"><Label htmlFor="route-short-name">Nombre corto en comanda</Label><Input id="route-short-name" maxLength={100} value={shortName ?? currentRoute?.shortName ?? ""} onChange={(event) => setShortName(event.target.value)} placeholder="Opcional"/></div>
            <Button className="w-full" disabled={!editor || !chosenStation || save.isPending || !activeStations.some((station) => station.stationId === chosenStation)}
              onClick={() => editor && save.mutate({ productId: editor.productId, stationId: chosenStation,
                shortName: (shortName ?? currentRoute?.shortName ?? "").trim() || null,
                rowVersion: currentRoute?.rowVersion ?? null })}>{save.isPending ? "Guardando…" : currentRoute ? "Guardar cambios" : "Agregar a la lista"}</Button></div>}
      </DialogContent>
    </Dialog>
    <Dialog open={!!removing} onOpenChange={(open) => { if (!open && !remove.isPending) setRemoving(null); }}>
      <DialogContent><DialogHeader><DialogTitle>Eliminar destino</DialogTitle></DialogHeader>
        <p className="text-sm text-muted-foreground">{removing?.productName ?? "Este producto"} dejará de tener estación para nuevas comandas de esta sede.</p>
        <div className="flex justify-end gap-2"><Button variant="outline" disabled={remove.isPending} onClick={() => setRemoving(null)}>Volver</Button>
          <Button variant="destructive" disabled={remove.isPending} onClick={() => removing && remove.mutate(removing)}>{remove.isPending ? "Eliminando…" : "Eliminar destino"}</Button></div>
      </DialogContent>
    </Dialog>
  </section>;
}
