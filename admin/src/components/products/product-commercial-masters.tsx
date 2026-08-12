"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck, Plus, Ruler, Tags } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { productMerchandisingApi } from "@/services/api/product-merchandising";

export function ProductCommercialMasters({ canManage }: { canManage: boolean }) {
  const client = useQueryClient();
  const brands = useQuery({ queryKey: ["product-brands"], queryFn: productMerchandisingApi.brands });
  const units = useQuery({ queryKey: ["product-units"], queryFn: productMerchandisingApi.units });
  const [brand, setBrand] = useState("");
  const [unit, setUnit] = useState({ code: "", name: "", symbol: "", allowsFractionalQuantity: false, decimalPlaces: 0 });
  const createBrand = useMutation({ mutationFn: () => productMerchandisingApi.createBrand(brand), onSuccess: async () => { setBrand(""); await client.invalidateQueries({ queryKey: ["product-brands"] }); toast.success("Marca creada."); } });
  const createUnit = useMutation({ mutationFn: () => productMerchandisingApi.createUnit(unit), onSuccess: async () => { setUnit({ code: "", name: "", symbol: "", allowsFractionalQuantity: false, decimalPlaces: 0 }); await client.invalidateQueries({ queryKey: ["product-units"] }); toast.success("Unidad de venta creada."); } });

  return <div className="mt-5 grid gap-5 xl:grid-cols-2">
    <section className="rounded-2xl border bg-card p-5"><header className="mb-4 flex items-center gap-3"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Tags className="h-5 w-5" /></span><div><h3 className="font-semibold">Marcas</h3><p className="text-xs text-muted-foreground">Fabricante o marca comercial del producto.</p></div></header><div className="flex gap-2"><Input value={brand} onChange={event => setBrand(event.target.value)} placeholder="Ej. Samsung" disabled={!canManage} /><Button type="button" onClick={() => createBrand.mutate()} disabled={!canManage || !brand.trim()}><Plus className="mr-1 h-4 w-4" />Crear</Button></div><div className="mt-4 flex flex-wrap gap-2">{(brands.data ?? []).map(item => <span key={item.productBrandId} className="inline-flex items-center gap-1 rounded-full border px-3 py-1 text-sm"><BadgeCheck className="h-3.5 w-3.5 text-primary" />{item.name}</span>)}</div></section>
    <section className="rounded-2xl border bg-card p-5"><header className="mb-4 flex items-center gap-3"><span className="rounded-xl bg-primary/10 p-2 text-primary"><Ruler className="h-5 w-5" /></span><div><h3 className="font-semibold">Unidades de venta</h3><p className="text-xs text-muted-foreground">Cómo se expresa la cantidad: unidad, kg, m o L.</p></div></header><div className="grid gap-2 sm:grid-cols-3"><div><Label>Nombre</Label><Input value={unit.name} onChange={event => setUnit({ ...unit, name: event.target.value })} placeholder="Kilogramo" disabled={!canManage} /></div><div><Label>Código</Label><Input value={unit.code} onChange={event => setUnit({ ...unit, code: event.target.value.toUpperCase() })} placeholder="KG" disabled={!canManage} /></div><div><Label>Símbolo</Label><Input value={unit.symbol} onChange={event => setUnit({ ...unit, symbol: event.target.value })} placeholder="kg" disabled={!canManage} /></div></div><Button type="button" className="mt-3 w-full" onClick={() => createUnit.mutate()} disabled={!canManage || !unit.name || !unit.code || !unit.symbol}><Plus className="mr-1 h-4 w-4" />Crear unidad de venta</Button><div className="mt-4 grid gap-2 sm:grid-cols-2">{(units.data ?? []).map(item => <div key={item.productUnitId} className="rounded-lg border px-3 py-2"><p className="font-medium">{item.name} <span className="text-muted-foreground">({item.symbol})</span></p><p className="text-xs text-muted-foreground">Código interno: {item.code}</p></div>)}</div></section>
  </div>;
}
