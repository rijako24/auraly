"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, ArrowUpRight, Check, Copy, Eye, Monitor, QrCode,
  Save, Smartphone } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { PublicRestaurantMenu } from "@/components/restaurant/public-restaurant-menu";
import { businessesApi } from "@/services/api/businesses";
import { restaurantDigitalMenuApi } from "@/services/api/restaurant-digital-menu";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function RestaurantDigitalMenuSettingsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const business = useQuery({
    queryKey: ["restaurant-digital-menu-business", businessId],
    queryFn: () => businessesApi.getById(businessId!),
    enabled: !!businessId,
  });
  const isRestaurant = business.data?.pointOfSaleMode === "RestaurantBar";
  const query = useQuery({
    queryKey: ["restaurant-digital-menu", businessId],
    queryFn: restaurantDigitalMenuApi.configuration,
    enabled: !!businessId && isRestaurant,
  });
  const [title, setTitle] = useState("");
  const [introduction, setIntroduction] = useState("");
  const [published, setPublished] = useState(false);
  const [saving, setSaving] = useState(false);
  const [copied, setCopied] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewMode, setPreviewMode] = useState<"phone" | "desktop">("phone");
  useEffect(() => setPreviewOpen(false), [businessId]);
  useEffect(() => {
    if (!isRestaurant || !query.isFetched) return;
    setTitle(query.data?.title ?? "Mi restaurante");
    setIntroduction(query.data?.introduction ?? "");
    setPublished(query.data?.isPublished ?? false);
  }, [isRestaurant, query.isFetched, query.data]);
  const link = query.data?.publicCode && typeof window !== "undefined"
    ? `${window.location.origin}/menu/${query.data.publicCode}` : "";

  async function save() {
    if (!businessId || !isRestaurant || !title.trim() || saving) return;
    setSaving(true);
    try {
      const next = await restaurantDigitalMenuApi.save({
        title: title.trim(), introduction: introduction.trim() || null,
        isPublished: published, rowVersion: query.data?.rowVersion ?? null,
      });
      query.refetch();
      toast.success(next.isPublished ? "Menú publicado" : "Menú guardado como privado");
    } catch (error) {
      toast.error(error instanceof Error ? error.message :
        "No se pudo guardar el menú.");
    } finally { setSaving(false); }
  }

  return <div className="mx-auto max-w-5xl space-y-6 pb-12">
    <Link href="/dashboard/restaurant" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary"><ArrowLeft className="h-4 w-4"/>Restaurante y Bar</Link>
    <header><p className="text-xs font-bold uppercase tracking-[.2em] text-primary">Tu escaparate público</p>
      <h1 className="mt-2 text-3xl font-bold">Menú digital</h1>
      <p className="mt-2 text-muted-foreground">Tus categorías, productos, fotos y precios se actualizan desde el catálogo de esta sede.</p></header>
    {!businessId && <p className="rounded-2xl border p-5">Selecciona una sede para configurar su menú.</p>}
    {businessId && business.isLoading && <div className="h-96 animate-pulse rounded-[2rem] bg-muted"/>}
    {businessId && business.isError && <div role="alert" className="rounded-2xl border p-5">No pudimos verificar la sede seleccionada. <Button variant="link" onClick={() => void business.refetch()}>Reintentar</Button></div>}
    {businessId && business.data && !isRestaurant && <div role="alert" className="rounded-2xl border border-amber-300/60 bg-amber-50 p-6 text-amber-950 dark:bg-amber-950/30 dark:text-amber-100"><h2 className="text-lg font-bold">Selecciona una sede Restaurante y Bar</h2><p className="mt-2 text-sm">{business.data.name} está configurada como Venta directa. Cambia la sede en el selector de arriba para diseñar y previsualizar su menú digital.</p></div>}
    {businessId && isRestaurant && query.isLoading && <div className="h-96 animate-pulse rounded-[2rem] bg-muted"/>}
    {businessId && isRestaurant && query.isError && <div role="alert" className="rounded-2xl border p-5">No pudimos cargar el menú. <Button variant="link" onClick={() => void query.refetch()}>Reintentar</Button></div>}
    {businessId && isRestaurant && !query.isLoading && !query.isError && <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
      <section className="rounded-[2rem] border bg-card p-5 shadow-sm sm:p-7">
        <h2 className="text-xl font-semibold">Presentación</h2>
        <label className="mt-6 block text-sm font-medium">Nombre del menú<Input className="mt-2 h-12" maxLength={100} value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Ej. La Casona · Cocina y Bar"/></label>
        <label className="mt-5 block text-sm font-medium">Mensaje de bienvenida<textarea className="mt-2 min-h-28 w-full resize-y rounded-xl border bg-background p-3 text-base outline-none focus-visible:ring-2 focus-visible:ring-primary" maxLength={500} value={introduction} onChange={(event) => setIntroduction(event.target.value)} placeholder="Cuéntales qué hace especial tu cocina."/></label>
        <label className="mt-6 flex cursor-pointer items-center justify-between gap-4 rounded-2xl border bg-muted/30 p-4"><div><strong className="block">Publicar el menú</strong><span className="text-sm text-muted-foreground">Al activarlo, cualquier persona con el enlace o QR podrá verlo.</span></div><input type="checkbox" className="h-6 w-6 accent-teal-600" checked={published} onChange={(event) => setPublished(event.target.checked)}/></label>
        <div className="mt-6 flex flex-wrap gap-2"><Button className="min-h-12 px-6" disabled={!title.trim() || saving} onClick={() => void save()}><Save className="mr-2 h-4 w-4"/>{saving ? "Guardando…" : "Guardar menú"}</Button>
          <Button variant="outline" className="min-h-12 px-5" onClick={() => setPreviewOpen(true)}><Eye className="mr-2 h-4 w-4"/>Vista previa</Button></div>
      </section>
      <aside className="rounded-[2rem] border bg-card p-5 text-center shadow-sm sm:p-7">
        <span className="mx-auto grid h-14 w-14 place-items-center rounded-2xl bg-primary/10 text-primary"><QrCode className="h-7 w-7"/></span>
        <h2 className="mt-4 text-xl font-semibold">Enlace y QR</h2>
        {link ? <><div className="mx-auto mt-5 grid h-[246px] w-[246px] place-items-center rounded-3xl border bg-white p-5">{query.data?.isPublished
          // The SVG is generated by the existing server-side QR renderer.
          // eslint-disable-next-line @next/next/no-img-element
          ? <img src={`/api/public/v1/restaurant-menus/${query.data.publicCode}/qr?url=${encodeURIComponent(link)}`} alt="QR del menú digital" className="h-full w-full"/>
          : <QrCode className="h-20 w-20 text-slate-300"/>}</div>
          <p className="mt-4 break-all text-xs text-muted-foreground">{link}</p>
          <div className="mt-5 flex justify-center gap-2"><Button variant="outline" onClick={async () => { await navigator.clipboard.writeText(link); setCopied(true); toast.success("Enlace copiado"); }}><span>{copied ? <Check className="mr-2 inline h-4 w-4"/> : <Copy className="mr-2 inline h-4 w-4"/>}</span>Copiar</Button>{query.data?.isPublished && <Button asChild variant="outline"><a href={link} target="_blank" rel="noreferrer"><ArrowUpRight className="mr-2 h-4 w-4"/>Ver</a></Button>}</div>
          {!query.data?.isPublished && <p className="mt-4 text-xs text-amber-700 dark:text-amber-300">El QR estará disponible al publicar el menú.</p>}</>
          : <p className="mt-6 text-sm text-muted-foreground">Guarda el menú para crear su enlace y QR permanentes.</p>}
      </aside>
    </div>}
    <Dialog open={previewOpen} onOpenChange={setPreviewOpen}>
      <DialogContent className="flex h-[min(92vh,1000px)] w-[min(96vw,1120px)] max-w-none flex-col gap-0 overflow-hidden rounded-3xl p-0" aria-label="Vista previa del menú digital">
        <DialogHeader className="flex-row items-center justify-between gap-3 border-b px-6 py-4 pr-12 text-left"><div><DialogTitle>Vista previa del menú</DialogTitle><p className="mt-1 text-xs text-muted-foreground">Así lo verá el cliente. Esta vista no publica tus cambios.</p></div>
          <div className="flex gap-1 rounded-xl border bg-muted/40 p-1"><Button size="sm" variant={previewMode === "phone" ? "default" : "ghost"} onClick={() => setPreviewMode("phone")} aria-label="Vista de celular"><Smartphone className="mr-1.5 h-4 w-4"/>Celular</Button><Button size="sm" variant={previewMode === "desktop" ? "default" : "ghost"} onClick={() => setPreviewMode("desktop")} aria-label="Vista de computador"><Monitor className="mr-1.5 h-4 w-4"/>Computador</Button></div></DialogHeader>
        <div className="min-h-0 flex-1 overflow-auto bg-stone-200/70 p-3 dark:bg-slate-950 sm:p-6"><div className={`mx-auto overflow-hidden bg-background shadow-2xl transition-[max-width,border-radius] duration-300 ${previewMode === "phone" ? "max-w-[390px] rounded-[2rem] border-[8px] border-slate-800" : "max-w-[960px] rounded-2xl border"}`}>
          {previewOpen && <PublicRestaurantMenu key={businessId} preview previewPhone={previewMode === "phone"} previewTitle={title} previewIntroduction={introduction}/>}</div></div>
      </DialogContent>
    </Dialog>
  </div>;
}
