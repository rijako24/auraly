"use client";

import Link from "next/link";
import { ArrowUpRight, ClipboardList, HandCoins, LayoutGrid, QrCode, Settings2, Store } from "lucide-react";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function RestaurantWorkspacePage() {
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canConfigure = permissions.includes("businesses.update");
  const canAttend = permissions.includes("restaurant.attend") || permissions.includes("sales.create");
  const cards = [
    ...(canAttend ? [{ title: "Mesas y pedidos", description: "Abre mesas y toma pedidos desde la pantalla táctil.", href: "/pos", icon: Store }] : []),
    ...(permissions.includes("restaurant.commands.read") ? [{ title: "Comandas", description: "Abre la pantalla completa de cocina o barra.", href: "/restaurant/commands", icon: ClipboardList }] : []),
    ...(permissions.includes("cash.close") ? [{ title: "Liquidar propinas", description: "Reparte y registra las propinas pendientes de entregar al personal.", href: "/dashboard/restaurant/tips", icon: HandCoins }] : []),
    ...(canConfigure ? [
      { title: "Configurar restaurante", description: "Administra salones, mesas, estaciones y destinos de productos.", href: "/dashboard/restaurant/setup", icon: Settings2 },
      { title: "Plano del restaurante", description: "Ubica mesas, paredes y espacios con un editor táctil.", href: "/dashboard/restaurant/floorplan", icon: LayoutGrid },
      { title: "Menú digital", description: "Publica tus categorías, fotos y precios en un enlace con QR.", href: "/dashboard/restaurant/menu", icon: QrCode },
    ] : []),
  ];

  return <div className="space-y-8 pb-10">
    <header className="rounded-[2rem] border bg-card p-6 shadow-sm sm:p-8">
      <p className="text-xs font-bold uppercase tracking-[.2em] text-primary">Operación gastronómica</p>
      <h1 className="mt-2 text-3xl font-bold tracking-tight sm:text-4xl">Restaurante y Bar</h1>
      <p className="mt-3 max-w-2xl text-muted-foreground">Mesas, salones, estaciones y comandas en un solo lugar. La sede seleccionada determina qué datos ves.</p>
      {!businessId && <p className="mt-5 rounded-xl border border-amber-300/50 bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950/30 dark:text-amber-200">Selecciona una sede para configurar el restaurante.</p>}
    </header>
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {cards.map(({ title, description, href, icon: Icon }) =>
        <Link key={href} href={href} className="group flex min-h-44 flex-col rounded-[1.75rem] border bg-card p-5 shadow-sm transition hover:-translate-y-0.5 hover:border-primary/50 hover:shadow-md focus-visible:outline-2 focus-visible:outline-primary">
          <div className="flex items-start justify-between"><span className="grid h-12 w-12 place-items-center rounded-2xl bg-primary/10 text-primary"><Icon className="h-6 w-6"/></span><ArrowUpRight className="h-5 w-5 text-muted-foreground transition group-hover:translate-x-0.5 group-hover:-translate-y-0.5 group-hover:text-primary"/></div>
          <h2 className="mt-5 text-lg font-semibold">{title}</h2><p className="mt-1 text-sm text-muted-foreground">{description}</p>
        </Link>)}
    </div>
  </div>;
}
