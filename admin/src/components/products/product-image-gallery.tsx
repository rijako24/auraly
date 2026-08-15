"use client";

import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ImageIcon, Images, Loader2, Star, Trash2, UploadCloud } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { productOffersApi } from "@/services/api/product-offers";
import { useBusinessContextStore } from "@/stores/business-context-store";

export interface PendingProductImage {
  id: string;
  file: File;
  previewUrl: string;
  isPrimary: boolean;
}

export function PendingProductImagePicker({ images, onChange }: {
  images: PendingProductImage[];
  onChange: (images: PendingProductImage[]) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  function addFiles(files: FileList | null) {
    if (!files) return;
    const accepted = validImageFiles(Array.from(files));
    const additions = accepted.map((file, index) => ({
      id: crypto.randomUUID(),
      file,
      previewUrl: URL.createObjectURL(file),
      isPrimary: images.length === 0 && index === 0,
    }));
    onChange([...images, ...additions]);
  }

  function remove(id: string) {
    const removed = images.find((image) => image.id === id);
    if (removed) URL.revokeObjectURL(removed.previewUrl);
    const remaining = images.filter((image) => image.id !== id);
    if (removed?.isPrimary && remaining.length) remaining[0] = { ...remaining[0], isPrimary: true };
    onChange(remaining);
  }

  return <ProductImagePanel
    images={images.map((image) => ({ id: image.id, url: image.previewUrl, isPrimary: image.isPrimary, alt: image.file.name }))}
    emptyAction={() => inputRef.current?.click()}
    actions={(image) => <>
      {!image.isPrimary && <IconButton label="Marcar como principal" onClick={() => onChange(images.map((item) => ({ ...item, isPrimary: item.id === image.id })))}><Star className="h-4 w-4" /></IconButton>}
      <IconButton label="Eliminar imagen" danger onClick={() => remove(image.id)}><Trash2 className="h-4 w-4" /></IconButton>
    </>}
    footer={<>
      <input ref={inputRef} className="sr-only" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { addFiles(event.target.files); event.currentTarget.value = ""; }} />
      <Button type="button" variant="outline" onClick={() => inputRef.current?.click()}><UploadCloud className="mr-2 h-4 w-4" />{images.length ? "Agregar más imágenes" : "Seleccionar imágenes"}</Button>
      <p className="text-xs text-muted-foreground">JPG, PNG o WEBP · máximo 8 MB por imagen. Se guardarán al crear el producto.</p>
    </>}
  />;
}

export interface ProductImageEditorHandle {
  save: () => Promise<void>;
}

export const ProductImageEditor = forwardRef<ProductImageEditorHandle, { productId: string }>(
  function ProductImageEditor({ productId }, ref) {
    const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
    const queryClient = useQueryClient();
    const inputRef = useRef<HTMLInputElement>(null);
    const pendingRef = useRef<PendingProductImage[]>([]);
    const queryKey = useMemo(() => ["product-images", businessId, productId], [businessId, productId]);
    const query = useQuery({
      queryKey,
      queryFn: () => productOffersApi.images(businessId!, productId),
      enabled: !!businessId,
    });
    const [pending, setPending] = useState<PendingProductImage[]>([]);
    const [removedIds, setRemovedIds] = useState<string[]>([]);
    const [primaryId, setPrimaryId] = useState<string>();
    pendingRef.current = pending;

    useEffect(() => {
      if (!pending.length && !removedIds.length) {
        setPrimaryId(query.data?.find((image) => image.isPrimary)?.productImageId);
      }
    }, [pending.length, query.data, removedIds.length]);

    useEffect(() => () => {
      pendingRef.current.forEach((image) => URL.revokeObjectURL(image.previewUrl));
    }, []);

    const remoteImages = useMemo(
      () => [...(query.data ?? [])]
        .filter((image) => !removedIds.includes(image.productImageId))
        .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.displayOrder - b.displayOrder),
      [query.data, removedIds],
    );
    const displayImages = useMemo<DisplayImage[]>(() => [
      ...remoteImages.map((image) => ({
        id: image.productImageId,
        url: image.mediaUrl,
        isPrimary: image.productImageId === primaryId,
        alt: image.altText ?? "Imagen del producto",
      })),
      ...pending.map((image) => ({
        id: image.id,
        url: image.previewUrl,
        isPrimary: image.id === primaryId,
        alt: image.file.name,
      })),
    ], [pending, primaryId, remoteImages]);

    function addFiles(files: FileList | null) {
      if (!files) return;
      const accepted = validImageFiles(Array.from(files));
      const additions = accepted.map((file) => ({
        id: crypto.randomUUID(),
        file,
        previewUrl: URL.createObjectURL(file),
        isPrimary: false,
      }));
      if (!additions.length) return;
      const nextPrimary = primaryId ?? displayImages[0]?.id ?? additions[0].id;
      setPending((current) => [...current, ...additions]);
      setPrimaryId(nextPrimary);
    }

    function removeImage(id: string) {
      const pendingImage = pending.find((image) => image.id === id);
      if (pendingImage) {
        URL.revokeObjectURL(pendingImage.previewUrl);
        setPending((current) => current.filter((image) => image.id !== id));
      } else {
        setRemovedIds((current) => current.includes(id) ? current : [...current, id]);
      }
      if (primaryId === id) {
        setPrimaryId(displayImages.find((image) => image.id !== id)?.id);
      }
    }

    useImperativeHandle(ref, () => ({
      save: async () => {
        if (!businessId) throw new Error("Selecciona un negocio antes de guardar las imágenes.");
        let savedPrimaryId = primaryId;
        for (const imageId of removedIds) {
          await productOffersApi.deleteImage(businessId, productId, imageId);
        }
        for (const image of pending) {
          const uploaded = await productOffersApi.uploadImage(
            businessId,
            productId,
            image.file,
            null,
            image.id === primaryId,
          );
          if (image.id === primaryId) savedPrimaryId = uploaded.productImageId;
        }
        if (savedPrimaryId && remoteImages.some((image) => image.productImageId === savedPrimaryId)) {
          await productOffersApi.setPrimaryImage(businessId, productId, savedPrimaryId);
        }
        pending.forEach((image) => URL.revokeObjectURL(image.previewUrl));
        setPending([]);
        setRemovedIds([]);
        setPrimaryId(savedPrimaryId);
        await queryClient.invalidateQueries({ queryKey });
      },
    }), [businessId, pending, primaryId, productId, queryClient, queryKey, remoteImages, removedIds]);

    if (query.isLoading) return <div className="grid min-h-48 place-items-center rounded-2xl border bg-muted/10"><Loader2 className="h-6 w-6 animate-spin text-primary" /></div>;

    return <ProductImagePanel
      images={displayImages}
      emptyAction={() => inputRef.current?.click()}
      actions={(image) => <>
        {!image.isPrimary && <IconButton label="Marcar como principal" onClick={() => setPrimaryId(image.id)}><Star className="h-4 w-4" /></IconButton>}
        <IconButton label="Quitar imagen" danger onClick={() => removeImage(image.id)}><Trash2 className="h-4 w-4" /></IconButton>
      </>}
      footer={<>
        <input ref={inputRef} className="sr-only" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { addFiles(event.target.files); event.currentTarget.value = ""; }} />
        <Button type="button" variant="outline" onClick={() => inputRef.current?.click()}><UploadCloud className="mr-2 h-4 w-4" />{displayImages.length ? "Agregar más imágenes" : "Seleccionar imágenes"}</Button>
        <p className="text-xs text-muted-foreground">Vista previa local. Los cambios se enviarán al Blob Storage de este negocio únicamente al guardar el producto.</p>
      </>}
    />;
  },
);

export function ProductImageGallery({ productId, readOnly = false }: { productId: string; readOnly?: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const queryKey = ["product-images", businessId, productId];
  const query = useQuery({ queryKey, queryFn: () => productOffersApi.images(businessId!, productId), enabled: !!businessId });
  const refresh = () => queryClient.invalidateQueries({ queryKey });
  const upload = useMutation({
    mutationFn: async (files: File[]) => {
      const hasPrimary = (query.data ?? []).some((image) => image.isPrimary);
      for (let index = 0; index < files.length; index += 1) {
        await productOffersApi.uploadImage(businessId!, productId, files[index], null, !hasPrimary && index === 0);
      }
    },
    onSuccess: async () => { await refresh(); toast.success("Imágenes guardadas en el almacenamiento del negocio"); },
    onError: () => toast.error("No fue posible subir las imágenes"),
  });
  const setPrimary = useMutation({
    mutationFn: (imageId: string) => productOffersApi.setPrimaryImage(businessId!, productId, imageId),
    onSuccess: async () => { await refresh(); toast.success("Imagen principal actualizada"); },
    onError: () => toast.error("No fue posible cambiar la imagen principal"),
  });
  const remove = useMutation({
    mutationFn: (imageId: string) => productOffersApi.deleteImage(businessId!, productId, imageId),
    onSuccess: refresh,
    onError: () => toast.error("No fue posible eliminar la imagen"),
  });
  const ordered = useMemo(() => [...(query.data ?? [])].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.displayOrder - b.displayOrder), [query.data]);

  if (query.isLoading) return <div className="grid min-h-48 place-items-center rounded-2xl border bg-muted/10"><Loader2 className="h-6 w-6 animate-spin text-primary" /></div>;

  return <ProductImagePanel
    images={ordered.map((image) => ({ id: image.productImageId, url: image.mediaUrl, isPrimary: image.isPrimary, alt: image.altText ?? "Imagen del producto" }))}
    emptyAction={readOnly ? undefined : () => inputRef.current?.click()}
    actions={readOnly ? undefined : (image) => <>
      {!image.isPrimary && <IconButton label="Marcar como principal" onClick={() => setPrimary.mutate(image.id)}><Star className="h-4 w-4" /></IconButton>}
      <IconButton label="Eliminar imagen" danger onClick={() => remove.mutate(image.id)}><Trash2 className="h-4 w-4" /></IconButton>
    </>}
    footer={readOnly ? undefined : <>
      <input ref={inputRef} className="sr-only" type="file" accept="image/jpeg,image/png,image/webp" multiple onChange={(event) => { const files = Array.from(event.target.files ?? []); if (files.length) upload.mutate(files); event.currentTarget.value = ""; }} />
      <Button type="button" variant="outline" disabled={upload.isPending} onClick={() => inputRef.current?.click()}><UploadCloud className="mr-2 h-4 w-4" />{upload.isPending ? "Subiendo…" : "Agregar imágenes"}</Button>
      <p className="text-xs text-muted-foreground">Los archivos se almacenan de forma aislada en el Blob Storage de este negocio.</p>
    </>}
  />;
}

type DisplayImage = { id: string; url: string; isPrimary: boolean; alt: string };

function ProductImagePanel({ images, actions, footer, emptyAction }: {
  images: DisplayImage[];
  actions?: (image: DisplayImage) => React.ReactNode;
  footer?: React.ReactNode;
  emptyAction?: () => void;
}) {
  const [selectedId, setSelectedId] = useState<string>();
  const selected = images.find((image) => image.id === selectedId) ?? images.find((image) => image.isPrimary) ?? images[0];

  return <div className="overflow-hidden rounded-2xl border bg-gradient-to-br from-background to-muted/20">
    <div className="grid gap-0 lg:grid-cols-[minmax(0,1.3fr)_minmax(260px,.7fr)]">
      <div className="relative grid min-h-72 place-items-center overflow-hidden bg-[radial-gradient(circle_at_center,hsl(var(--muted))_0,transparent_68%)] p-6">
        {selected ? <>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={selected.url} alt={selected.alt} className="max-h-[28rem] w-full rounded-xl object-contain drop-shadow-xl" />
          {selected.isPrimary && <Badge className="absolute left-4 top-4 gap-1 bg-slate-950/85 text-white"><Star className="h-3.5 w-3.5 fill-current" />Principal</Badge>}
        </> : <button type="button" onClick={emptyAction} disabled={!emptyAction} className="flex max-w-sm flex-col items-center rounded-2xl border border-dashed bg-background/80 px-8 py-10 text-center transition hover:border-primary/50 hover:bg-background disabled:cursor-default">
          <span className="mb-4 grid h-16 w-16 place-items-center rounded-2xl bg-primary/10 text-primary"><ImageIcon className="h-8 w-8" /></span>
          <strong>Añade una imagen clara del producto</strong>
          <span className="mt-2 text-sm text-muted-foreground">La principal será la portada; puedes añadir varias vistas y cambiarla después.</span>
        </button>}
      </div>
      <div className="border-t p-4 lg:border-l lg:border-t-0">
        <div className="mb-3 flex items-center justify-between"><div><p className="flex items-center gap-2 font-semibold"><Images className="h-4 w-4 text-primary" />Galería</p><p className="text-xs text-muted-foreground">{images.length} {images.length === 1 ? "imagen" : "imágenes"}</p></div>{selected && actions && <div className="flex gap-1">{actions(selected)}</div>}</div>
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-3">
          {images.map((image) => <button key={image.id} type="button" onClick={() => setSelectedId(image.id)} className={`relative overflow-hidden rounded-xl border-2 bg-muted/30 transition ${selected?.id === image.id ? "border-primary shadow-sm" : "border-transparent hover:border-primary/30"}`}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={image.url} alt={image.alt} className="aspect-square w-full object-cover" />
            {image.isPrimary && <span className="absolute right-1 top-1 grid h-5 w-5 place-items-center rounded-full bg-primary text-primary-foreground"><Check className="h-3 w-3" /></span>}
          </button>)}
        </div>
      </div>
    </div>
    {footer && <div className="flex flex-col gap-3 border-t bg-background/80 px-4 py-4 sm:flex-row sm:items-center">{footer}</div>}
  </div>;
}

const allowedImageTypes = new Set(["image/jpeg", "image/png", "image/webp"]);
const maximumImageBytes = 8 * 1024 * 1024;

function validImageFiles(files: File[]) {
  const accepted = files.filter((file) => allowedImageTypes.has(file.type) && file.size <= maximumImageBytes);
  if (accepted.length !== files.length) {
    toast.error(
      "Solo se admiten imágenes JPG, PNG o WEBP de máximo 8 MB por archivo.",
    );
  }
  return accepted;
}

function IconButton({ label, danger = false, onClick, children }: { label: string; danger?: boolean; onClick: () => void; children: React.ReactNode }) {
  return <Button type="button" size="icon" variant="outline" aria-label={label} title={label} className={danger ? "text-destructive hover:text-destructive" : ""} onClick={onClick}>{children}</Button>;
}
