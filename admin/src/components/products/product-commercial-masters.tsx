"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ruler, Tags } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { MasterListPanel } from "@/components/masters/master-list-panel";
import { productMerchandisingApi, type ProductBrand, type ProductUnit } from "@/services/api/product-merchandising";

type BrandEditor = { item: ProductBrand | null; name: string; active: boolean };
type UnitEditor = { item: ProductUnit | null; code: string; name: string; symbol: string; active: boolean };

export function ProductCommercialMasters({ canManage }: { canManage: boolean }) {
  const client = useQueryClient();
  const brands = useQuery({ queryKey: ["product-brands", true], queryFn: () => productMerchandisingApi.allBrands() });
  const units = useQuery({ queryKey: ["product-units", true], queryFn: () => productMerchandisingApi.allUnits() });
  const [brand, setBrand] = useState<BrandEditor | null>(null);
  const [unit, setUnit] = useState<UnitEditor | null>(null);
  const saveBrand = useMutation({ mutationFn: () => brand!.item ? productMerchandisingApi.saveBrand(brand!.item.productBrandId, brand!.name, brand!.active) : productMerchandisingApi.createBrand(brand!.name), onSuccess: async () => { await client.invalidateQueries({ queryKey: ["product-brands"] }); toast.success("Marca guardada"); setBrand(null); }, onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la marca") });
  const saveUnit = useMutation({ mutationFn: () => {
    const request = { code: unit!.code.trim().toUpperCase(), name: unit!.name.trim(), symbol: unit!.symbol.trim(), allowsFractionalQuantity: false, decimalPlaces: 0, isActive: unit!.active };
    return unit!.item ? productMerchandisingApi.saveUnit(unit!.item.productUnitId, request) : productMerchandisingApi.createUnit(request);
  }, onSuccess: async () => { await client.invalidateQueries({ queryKey: ["product-units"] }); toast.success("Unidad de venta guardada"); setUnit(null); }, onError: (error) => toast.error(error instanceof Error ? error.message : "No fue posible guardar la unidad") });
  return <div className="grid gap-5 xl:grid-cols-2">
    <MasterListPanel title="Marcas" description="Fabricante o marca comercial del producto." createLabel="Nueva marca" rows={(brands.data ?? []).map((item) => ({ id: item.productBrandId, name: item.name, active: item.isActive }))} canManage={canManage} icon={<Tags className="h-5 w-5" />} onCreate={() => setBrand({ item: null, name: "", active: true })} onEdit={(id) => { const item = brands.data!.find((candidate) => candidate.productBrandId === id)!; setBrand({ item, name: item.name, active: item.isActive }); }} />
    <MasterListPanel title="Unidades en que se vende" description="Expresa la cantidad del producto: unidad, kg, metro o litro." createLabel="Nueva unidad" rows={(units.data ?? []).map((item) => ({ id: item.productUnitId, name: item.name, detail: `${item.code} · ${item.symbol}`, active: item.isActive }))} canManage={canManage} icon={<Ruler className="h-5 w-5" />} onCreate={() => setUnit({ item: null, code: "", name: "", symbol: "", active: true })} onEdit={(id) => { const item = units.data!.find((candidate) => candidate.productUnitId === id)!; setUnit({ item, code: item.code, name: item.name, symbol: item.symbol, active: item.isActive }); }} />
    <Dialog open={!!brand} onOpenChange={(open) => !open && setBrand(null)}><DialogContent><DialogHeader><DialogTitle>{brand?.item ? "Editar" : "Nueva"} marca</DialogTitle></DialogHeader><div className="space-y-2"><Label htmlFor="brand-name">Nombre</Label><Input id="brand-name" autoFocus value={brand?.name ?? ""} onChange={(event) => brand && setBrand({ ...brand, name: event.target.value })} /></div><Status active={brand?.active ?? true} onChange={(active) => brand && setBrand({ ...brand, active })} label="Marca activa" /><DialogFooter><Button variant="outline" onClick={() => setBrand(null)}>Cancelar</Button><Button disabled={!brand?.name.trim() || saveBrand.isPending} onClick={() => saveBrand.mutate()}>Guardar</Button></DialogFooter></DialogContent></Dialog>
    <Dialog open={!!unit} onOpenChange={(open) => !open && setUnit(null)}><DialogContent><DialogHeader><DialogTitle>{unit?.item ? "Editar" : "Nueva"} unidad en que se vende</DialogTitle></DialogHeader><div className="grid gap-4 sm:grid-cols-3"><div className="space-y-2"><Label htmlFor="unit-name">Nombre</Label><Input id="unit-name" autoFocus value={unit?.name ?? ""} onChange={(event) => unit && setUnit({ ...unit, name: event.target.value })} placeholder="Kilogramo" /></div><div className="space-y-2"><Label htmlFor="unit-code">Código</Label><Input id="unit-code" value={unit?.code ?? ""} onChange={(event) => unit && setUnit({ ...unit, code: event.target.value.toUpperCase() })} placeholder="KG" /></div><div className="space-y-2"><Label htmlFor="unit-symbol">Símbolo</Label><Input id="unit-symbol" value={unit?.symbol ?? ""} onChange={(event) => unit && setUnit({ ...unit, symbol: event.target.value })} placeholder="kg" /></div></div><p className="rounded-xl bg-muted p-3 text-xs text-muted-foreground">La posibilidad de vender decimales pertenece al producto, no a este maestro.</p><Status active={unit?.active ?? true} onChange={(active) => unit && setUnit({ ...unit, active })} label="Unidad activa" /><DialogFooter><Button variant="outline" onClick={() => setUnit(null)}>Cancelar</Button><Button disabled={!unit?.name.trim() || !unit?.code.trim() || !unit?.symbol.trim() || saveUnit.isPending} onClick={() => saveUnit.mutate()}>Guardar</Button></DialogFooter></DialogContent></Dialog>
  </div>;
}

function Status({ active, onChange, label }: { active: boolean; onChange: (active: boolean) => void; label: string }) { return <label className="flex items-center justify-between rounded-xl border p-3"><span><strong className="block text-sm">{label}</strong><small className="text-muted-foreground">Los registros inactivos conservan su historial.</small></span><Switch checked={active} onCheckedChange={onChange} /></label>; }
