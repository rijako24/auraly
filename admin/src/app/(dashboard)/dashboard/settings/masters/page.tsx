"use client";

import { useState } from "react";
import { BookOpen, Globe2, Settings2, Warehouse } from "lucide-react";
import { GeographyMaster } from "@/components/masters/geography-master";
import { WarehouseMaster } from "@/components/inventory/warehouse-master";
import { InventoryReasonMaster } from "@/components/inventory/inventory-reason-master";
import { VatMaster } from "@/components/products/vat-master";
import { ProductCategoriesMaster } from "@/components/products/product-categories-master";
import { useAuthStore } from "@/stores/auth-store";

type Section = "geography" | "products" | "warehouses" | "reasons" | "taxes";
const sections = [
  { id: "geography", label: "Ubicación", detail: "Países, departamentos y ciudades", icon: Globe2 },
  { id: "products", label: "Productos", detail: "Áreas, líneas, grupos y subgrupos", icon: BookOpen },
  { id: "warehouses", label: "Bodegas", detail: "Existencias, negativos y costos", icon: Warehouse },
  { id: "reasons", label: "Motivos", detail: "Conteos, ajustes y movimientos", icon: Settings2 },
  { id: "taxes", label: "IVA", detail: "Tarifas de compra y venta", icon: Settings2 },
] as const;

export default function MastersPage() {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const [section, setSection] = useState<Section>("geography");
  return <div className="mx-auto max-w-7xl space-y-6">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 to-teal-950 p-7 text-white"><p className="text-sm font-bold uppercase tracking-[.15em] text-teal-300">Configuración central</p><h1 className="mt-2 text-3xl font-black">Maestros de Auraly</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">Configura catálogos compartidos. La facturación electrónica tiene su propio módulo por su vigencia y asignación a equipos.</p></header>
    <nav className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">{sections.map((item) => <button key={item.id} onClick={() => setSection(item.id)} className={`flex items-center gap-3 rounded-2xl border p-4 text-left transition ${section === item.id ? "border-primary bg-primary/5 shadow-sm" : "bg-card hover:bg-muted/40"}`}><span className="rounded-xl bg-primary/10 p-2 text-primary"><item.icon className="h-5 w-5" /></span><span><b className="block">{item.label}</b><small className="text-muted-foreground">{item.detail}</small></span></button>)}</nav>
    {section === "geography" && <GeographyMaster canManage={permissions.has("masters.geography.manage")} />}
    {section === "products" && <ProductCategoriesMaster canManage={permissions.has("products.update")} />}
    {section === "warehouses" && <WarehouseMaster canManage={permissions.has("inventory.warehouses.manage")} />}
    {section === "reasons" && <InventoryReasonMaster canManage={permissions.has("inventory.reasons.manage")} />}
    {section === "taxes" && <VatMaster canManage={permissions.has("catalog.update") || permissions.has("products.update")} />}
  </div>;
}
