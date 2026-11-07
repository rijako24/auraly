"use client";

import { useState } from "react";
import { Package } from "lucide-react";

export function ProductCover({ imageUrl, name, gallery = false }: { imageUrl?: string | null; name: string; gallery?: boolean }) {
  const [failedImage, setFailedImage] = useState<string | null>(null);
  return <div className={`grid w-full place-items-center overflow-hidden rounded-xl bg-muted/35 ${gallery ? "aspect-square" : "h-40"}`}>
    {imageUrl && failedImage !== imageUrl
      // eslint-disable-next-line @next/next/no-img-element
      ? <img src={imageUrl} alt={name} loading="lazy" decoding="async" className="h-full w-full object-contain p-3" onError={() => setFailedImage(imageUrl)}/>
      : <div className="flex flex-col items-center gap-2 text-muted-foreground"><Package className="h-10 w-10 opacity-50"/><span className="text-xs">Sin imagen</span></div>}
  </div>;
}
