"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ImagePlus, Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  productOffersApi,
  type ProductOffer,
  type SaveProductOfferRequest,
} from "@/services/api/product-offers";
import { useBusinessContextStore } from "@/stores/business-context-store";

const emptyOffer: SaveProductOfferRequest = {
  condition: "new",
  storageGb: 128,
  color: null,
  variantLabel: null,
  unitPrice: 0,
  currency: "COP",
  minimumBatteryHealthPercent: null,
  isAvailable: true,
  isActive: true,
  priceSourceUrl: null,
  priceObservedAtUtc: null,
};

export function ProductOffersSection({ productId }: { productId: string }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const queryKey = ["product-offers", businessId, productId];
  const imageKey = ["product-images", businessId, productId];
  const offersQuery = useQuery({
    queryKey,
    queryFn: () => productOffersApi.list(businessId!, productId),
    enabled: !!businessId,
  });
  const imagesQuery = useQuery({
    queryKey: imageKey,
    queryFn: () => productOffersApi.images(businessId!, productId),
    enabled: !!businessId,
  });
  const [drafts, setDrafts] = useState<Record<string, SaveProductOfferRequest>>({});
  const [newOffer, setNewOffer] = useState(emptyOffer);
  const [imageUrl, setImageUrl] = useState("");
  const [imageOfferId, setImageOfferId] = useState("");

  useEffect(() => {
    setDrafts(
      Object.fromEntries(
        (offersQuery.data ?? []).map((offer) => [
          offer.productOfferId,
          {
            condition: offer.condition,
            storageGb: offer.storageGb,
            color: offer.color,
            unitPrice: offer.unitPrice,
            currency: offer.currency,
            minimumBatteryHealthPercent: offer.minimumBatteryHealthPercent,
            isAvailable: offer.isAvailable,
            isActive: offer.isActive,
            priceSourceUrl: offer.priceSourceUrl,
            priceObservedAtUtc: offer.priceObservedAtUtc,
          },
        ])
      )
    );
  }, [offersQuery.data]);

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey }),
      queryClient.invalidateQueries({ queryKey: imageKey }),
    ]);
  };
  const saveOffer = useMutation({
    mutationFn: ({ id, request }: { id?: string; request: SaveProductOfferRequest }) =>
      id
        ? productOffersApi.update(businessId!, productId, id, request)
        : productOffersApi.create(businessId!, productId, request),
    onSuccess: async () => {
      await refresh();
      toast.success("Oferta guardada");
    },
    onError: () => toast.error("No se pudo guardar la oferta"),
  });
  const addImageUrl = useMutation({
    mutationFn: () =>
      productOffersApi.addImageUrl(businessId!, productId, {
        productOfferId: imageOfferId || null,
        mediaUrl: imageUrl,
        displayOrder: imagesQuery.data?.length ?? 0,
        isPrimary: true,
      }),
    onSuccess: async () => {
      setImageUrl("");
      await refresh();
      toast.success("Imagen agregada");
    },
    onError: () => toast.error("No se pudo agregar la imagen"),
  });
  const uploadImage = useMutation({
    mutationFn: (file: File) =>
      productOffersApi.uploadImage(
        businessId!,
        productId,
        file,
        imageOfferId || null,
        true
      ),
    onSuccess: async () => {
      await refresh();
      toast.success("Imagen subida");
    },
    onError: () => toast.error("No se pudo subir la imagen"),
  });
  const deleteImage = useMutation({
    mutationFn: (imageId: string) =>
      productOffersApi.deleteImage(businessId!, productId, imageId),
    onSuccess: refresh,
  });

  const updateDraft = (
    offer: ProductOffer,
    field: keyof SaveProductOfferRequest,
    value: string | number | boolean | null
  ) =>
    setDrafts((current) => ({
      ...current,
      [offer.productOfferId]: {
        ...(current[offer.productOfferId] ?? emptyOffer),
        [field]: value,
      },
    }));

  return (
    <section className="space-y-5 border-t pt-5">
      <div>
        <h3 className="font-medium">Ofertas e imágenes</h3>
        <p className="text-sm text-muted-foreground">
          Administra condición, capacidad, precio y las fotos que el agente envía.
        </p>
      </div>

      <div className="space-y-3">
        {(offersQuery.data ?? []).map((offer) => {
          const draft = drafts[offer.productOfferId];
          if (!draft) return null;
          return (
            <div key={offer.productOfferId} className="grid gap-2 rounded-lg border p-3 sm:grid-cols-7">
              <select
                className="h-9 rounded-md border bg-background px-2 text-sm"
                value={draft.condition}
                onChange={(event) => {
                  updateDraft(offer, "condition", event.target.value as ProductOffer["condition"]);
                }}
              >
                <option value="new">Nuevo</option>
                <option value="used">Usado</option>
                <option value="refurbished">Reacondicionado</option>
              </select>
              <Input
                type="number"
                value={draft.storageGb ?? ""}
                placeholder="GB"
                onChange={(event) => updateDraft(offer, "storageGb", Number(event.target.value))}
              />
              <Input
                value={draft.variantLabel ?? ""}
                placeholder="Variante: eSIM, color, CPO…"
                onChange={(event) => updateDraft(offer, "variantLabel", event.target.value)}
              />
              <Input
                type="number"
                value={draft.unitPrice}
                placeholder="Precio"
                onChange={(event) => updateDraft(offer, "unitPrice", Number(event.target.value))}
              />
              <Input
                value={draft.currency}
                maxLength={3}
                onChange={(event) => updateDraft(offer, "currency", event.target.value.toUpperCase())}
              />
              <Input
                type="number"
                min={0}
                max={100}
                value={draft.minimumBatteryHealthPercent ?? ""}
                placeholder="Batería %"
                onChange={(event) =>
                  updateDraft(offer, "minimumBatteryHealthPercent", Number(event.target.value))
                }
              />
              <Button
                size="sm"
                disabled={saveOffer.isPending}
                onClick={() => saveOffer.mutate({ id: offer.productOfferId, request: draft })}
              >
                <Save className="mr-2 h-4 w-4" /> Guardar
              </Button>
            </div>
          );
        })}
        <div className="grid gap-2 rounded-lg border border-dashed p-3 sm:grid-cols-7">
          <select
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={newOffer.condition}
            onChange={(event) => {
              setNewOffer((value) => ({
                ...value,
                condition: event.target.value as ProductOffer["condition"],
              }));
            }}
          >
            <option value="new">Nuevo</option>
            <option value="used">Usado</option>
            <option value="refurbished">Reacondicionado</option>
          </select>
          <Input type="number" value={newOffer.storageGb ?? ""} onChange={(event) => setNewOffer((value) => ({ ...value, storageGb: Number(event.target.value) }))} />
          <Input value={newOffer.variantLabel ?? ""} placeholder="Variante" onChange={(event) => setNewOffer((value) => ({ ...value, variantLabel: event.target.value }))} />
          <Input type="number" value={newOffer.unitPrice} onChange={(event) => setNewOffer((value) => ({ ...value, unitPrice: Number(event.target.value) }))} />
          <Input value={newOffer.currency} maxLength={3} onChange={(event) => setNewOffer((value) => ({ ...value, currency: event.target.value.toUpperCase() }))} />
          <Input type="number" min={0} max={100} value={newOffer.minimumBatteryHealthPercent ?? ""} onChange={(event) => setNewOffer((value) => ({ ...value, minimumBatteryHealthPercent: Number(event.target.value) }))} />
          <Button size="sm" variant="outline" onClick={() => saveOffer.mutate({ request: newOffer })}>
            <Plus className="mr-2 h-4 w-4" /> Agregar
          </Button>
        </div>
      </div>

      <div className="space-y-3">
        <Label>Galería</Label>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {(imagesQuery.data ?? []).map((image) => (
            <div key={image.productImageId} className="relative overflow-hidden rounded-lg border bg-muted">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={image.mediaUrl} alt={image.altText ?? "Producto"} className="aspect-square w-full object-contain" />
              <div className="flex items-center justify-between p-2">
                {image.isPrimary ? <Badge>Principal</Badge> : <span />}
                <Button size="icon" variant="ghost" onClick={() => deleteImage.mutate(image.productImageId)}>
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>
          ))}
        </div>
        <select
          className="h-9 w-full rounded-md border bg-background px-2 text-sm"
          value={imageOfferId}
          onChange={(event) => setImageOfferId(event.target.value)}
        >
          <option value="">Imagen general del producto</option>
          {(offersQuery.data ?? []).map((offer) => (
            <option key={offer.productOfferId} value={offer.productOfferId}>
              {offer.condition} · {offer.storageGb ?? "sin capacidad"} GB
            </option>
          ))}
        </select>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Input value={imageUrl} onChange={(event) => setImageUrl(event.target.value)} placeholder="https://... imagen externa" />
          <Button variant="outline" disabled={!imageUrl || addImageUrl.isPending} onClick={() => addImageUrl.mutate()}>
            <ImagePlus className="mr-2 h-4 w-4" /> Agregar URL
          </Button>
          <Label className="inline-flex h-9 cursor-pointer items-center justify-center rounded-md border px-3 text-sm">
            Subir archivo
            <Input
              className="sr-only"
              type="file"
              accept=".jpg,.jpeg,.png,.webp"
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file) uploadImage.mutate(file);
              }}
            />
          </Label>
        </div>
      </div>
    </section>
  );
}
