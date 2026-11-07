"use client";

import { useState, type ComponentProps } from "react";
import { Eye, Loader2, Plus, UtensilsCrossed, type LucideIcon } from "lucide-react";
import type { PosCatalogProduct } from "@/services/pos/pos-edge-client";
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PosSaleItemCard({ children, className = "", ...props }: ComponentProps<"div">) {
  return <div {...props} className={`rounded-2xl border bg-background p-4 ${className}`}>{children}</div>;
}

export function PosCategoryPill({ active, label, count, icon: Icon, onClick }: {
  active: boolean; label: string; count?: number; icon: LucideIcon;
  onClick: () => void;
}) {
  return <button type="button" onClick={onClick} className={`flex min-h-14 shrink-0 items-center gap-2.5 rounded-2xl border px-3.5 py-2 text-sm font-semibold shadow-sm transition active:scale-[.98] ${active ? "border-primary bg-primary text-primary-foreground shadow-primary/20" : "bg-background hover:border-primary/60 hover:bg-primary/5 hover:shadow-md"}`}>
    <span className={`grid h-8 w-8 place-items-center rounded-xl ${active ? "bg-white/20" : "bg-primary/10 text-primary"}`}><Icon className="h-[18px] w-[18px]"/></span>
    <span>{label}</span>{count !== undefined && <span className={`rounded-lg px-2 py-0.5 text-xs ${active ? "bg-white/20" : "bg-muted text-muted-foreground"}`}>{count}</span>}
  </button>;
}

export function PosProductTile({ product, disabled, loading, onClick, onViewFamily, imageFit = "cover", fallbackIcon: FallbackIcon = UtensilsCrossed }: {
  product: Pick<PosCatalogProduct, "name" | "unitPrice" | "imageUrl">; imageFit?: "cover" | "contain"; fallbackIcon?: LucideIcon; disabled: boolean; loading: boolean; onClick: () => void; onViewFamily?: () => void;
}) {
  const [failedImage, setFailedImage] = useState<string | null>(null);
  return <div className="group relative min-h-52 overflow-hidden rounded-3xl border bg-background shadow-sm transition hover:-translate-y-0.5 hover:border-primary/70 hover:shadow-md">
    <button type="button" disabled={disabled} onClick={onClick} aria-label={`Agregar ${product.name}`}
      className="h-full min-h-52 w-full overflow-hidden rounded-3xl text-left focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-primary disabled:opacity-60">
    <div className="relative grid min-h-32 aspect-[4/3] place-items-center overflow-hidden bg-gradient-to-br from-emerald-50 via-teal-50 to-slate-100 text-primary dark:from-emerald-950 dark:via-teal-950 dark:to-slate-900">
      {product.imageUrl && failedImage !== product.imageUrl
        // eslint-disable-next-line @next/next/no-img-element
        ? <img src={product.imageUrl} alt="" className={`h-full w-full transition duration-300 group-hover:scale-105 ${imageFit === "contain" ? "object-contain p-3" : "object-cover"}`} onError={() => setFailedImage(product.imageUrl ?? null)}/>
        : <FallbackIcon className="h-14 w-14 opacity-50" aria-hidden="true"/>}
      <span className="absolute right-3 top-3 grid h-10 w-10 place-items-center rounded-full bg-background/90 text-primary shadow">{loading ? <Loader2 className="h-5 w-5 animate-spin"/> : <Plus className="h-5 w-5"/>}</span>
    </div><div className="p-4"><p className="line-clamp-2 min-h-11 text-sm font-bold sm:text-base">{product.name}</p><p className={`mt-2 text-lg font-extrabold text-primary ${onViewFamily ? "pr-11" : ""}`}>{money.format(product.unitPrice)}</p></div>
    </button>
    {onViewFamily && <button type="button" onClick={onViewFamily} aria-label={`Ver familia de ${product.name}`} title={`Ver familia de ${product.name}`}
      className="absolute bottom-2 right-3 grid h-11 w-11 place-items-center rounded-full text-primary transition hover:bg-primary/10 focus-visible:outline-2 focus-visible:outline-primary">
      <Eye className="h-5 w-5" aria-hidden="true" />
    </button>}
  </div>;
}
