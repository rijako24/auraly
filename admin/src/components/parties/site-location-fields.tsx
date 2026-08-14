"use client";

import { LocateFixed, MapPinned, Navigation } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export interface SiteLocationValue {
  googleMapsUrl: string;
  googlePlaceId: string;
  latitude: string;
  longitude: string;
}

function valid(latitude: number, longitude: number) {
  return Number.isFinite(latitude) && Number.isFinite(longitude) &&
    latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;
}

export function coordinatesFromMapsValue(value: string): { latitude: number; longitude: number } | null {
  const decoded = decodeURIComponent(value.trim());
  const patterns = [
    /@(-?\d{1,2}(?:\.\d+)?),(-?\d{1,3}(?:\.\d+)?)/,
    /[?&](?:query|q)=(-?\d{1,2}(?:\.\d+)?)[,;](-?\d{1,3}(?:\.\d+)?)/i,
    /^(-?\d{1,2}(?:[.,]\d+)?)[;,]\s*(-?\d{1,3}(?:[.,]\d+)?)$/,
  ];
  for (const pattern of patterns) {
    const match = decoded.match(pattern);
    if (!match) continue;
    const latitude = Number(match[1].replace(",", "."));
    const longitude = Number(match[2].replace(",", "."));
    if (valid(latitude, longitude)) return { latitude, longitude };
  }
  return null;
}

export function mapsHref(value: Pick<SiteLocationValue, "googleMapsUrl"|"latitude"|"longitude">) {
  if (value.googleMapsUrl.trim()) return value.googleMapsUrl.trim();
  const latitude = Number(value.latitude), longitude = Number(value.longitude);
  return valid(latitude, longitude)
    ? `https://www.google.com/maps/search/?api=1&query=${latitude},${longitude}`
    : null;
}

export function SiteLocationFields({ value, onChange }: {
  value: SiteLocationValue;
  onChange: (value: SiteLocationValue) => void;
}) {
  const located = valid(Number(value.latitude), Number(value.longitude));
  const setUrl = (googleMapsUrl: string) => {
    const coordinates = coordinatesFromMapsValue(googleMapsUrl);
    onChange({
      ...value,
      googleMapsUrl,
      latitude: coordinates ? String(coordinates.latitude) : value.latitude,
      longitude: coordinates ? String(coordinates.longitude) : value.longitude,
    });
  };
  const capture = () => {
    if (!navigator.geolocation) return toast.error("Este dispositivo no permite capturar la ubicación.");
    navigator.geolocation.getCurrentPosition(
      position => {
        const latitude = Number(position.coords.latitude.toFixed(6));
        const longitude = Number(position.coords.longitude.toFixed(6));
        onChange({
          ...value,
          latitude: String(latitude),
          longitude: String(longitude),
          googleMapsUrl: `https://www.google.com/maps/search/?api=1&query=${latitude},${longitude}`,
        });
        toast.success("Ubicación capturada");
      },
      () => toast.error("No fue posible obtener la ubicación. Revisa el permiso del navegador."),
      { enableHighAccuracy: true, timeout: 15_000, maximumAge: 30_000 },
    );
  };
  const href = mapsHref(value);
  return <div className="col-span-full rounded-2xl border bg-muted/20 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><Label>Ubicación exacta</Label><p className="mt-1 text-xs text-muted-foreground">Pega un enlace completo de Google Maps o captura la ubicación del dispositivo.</p></div>
      <Badge variant={located ? "secondary" : "outline"}>{located ? "Ubicada" : "Pendiente de ubicar"}</Badge>
    </div>
    <div className="mt-3 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
      <Input value={value.googleMapsUrl} onChange={event => setUrl(event.target.value)} onBlur={event => setUrl(event.target.value)} placeholder="https://maps.google.com/... o coordenadas latitud;longitud" />
      <Button type="button" variant="outline" onClick={capture}><LocateFixed className="mr-2 h-4 w-4"/>Usar mi ubicación</Button>
    </div>
    <div className="mt-3 flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
      {located ? <span className="inline-flex items-center gap-1"><MapPinned className="h-4 w-4 text-emerald-600"/>{Number(value.latitude).toFixed(6)}, {Number(value.longitude).toFixed(6)}</span> : <span>La dirección puede guardarse, pero no se inventará un pin hasta tener coordenadas.</span>}
      {href && <a href={href} target="_blank" rel="noreferrer" className="inline-flex items-center font-semibold text-teal-700"><Navigation className="mr-1 h-4 w-4"/>Verificar en Maps</a>}
    </div>
  </div>;
}
