"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ContactRound, Home, Menu, ShoppingCart, Truck } from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import { useSidebarStore } from "@/stores/sidebar-store";

const items = [
  { label: "Inicio", href: "/dashboard", icon: Home, permission: "dashboard.read", exact: true },
  { label: "Pedidos", href: "/dashboard/orders", icon: ShoppingCart, permission: "orders.read", exact: false },
  { label: "Despachos", href: "/dashboard/dispatches", icon: Truck, permission: "dispatches.read", exact: false },
  { label: "Terceros", href: "/dashboard/parties", icon: ContactRound, permission: "parties.read", exact: false },
] as const;

export function MobileBottomNav() {
  const pathname = usePathname();
  const permissions = new Set(useAuthStore((state) => state.user?.permissions ?? []));
  const setOpen = useSidebarStore((state) => state.setOpen);
  const visible = items.filter((item) => permissions.has(item.permission)).slice(0, 4);
  return <nav aria-label="Navegación principal" className="fixed inset-x-0 bottom-0 z-50 border-t border-border/80 bg-card/95 pb-[env(safe-area-inset-bottom)] shadow-[0_-10px_35px_-24px_rgba(15,23,42,.65)] backdrop-blur lg:hidden">
    <div className="mx-auto grid h-16 max-w-xl grid-flow-col auto-cols-fr px-1">
      {visible.map((item) => { const active=item.exact?pathname===item.href:pathname===item.href||pathname.startsWith(item.href+"/"); return <Link key={item.href} href={item.href} aria-current={active?"page":undefined} className={cn("relative flex min-w-0 flex-col items-center justify-center gap-1 rounded-2xl text-[10px] font-semibold transition",active?"text-teal-700":"text-muted-foreground")}>{active&&<span className="absolute top-1 h-1 w-8 rounded-full bg-teal-600"/>}<item.icon className={cn("h-5 w-5",active&&"stroke-[2.5]")}/><span className="truncate">{item.label}</span></Link>; })}
      <button type="button" onClick={()=>setOpen(true)} className="flex min-w-0 flex-col items-center justify-center gap-1 rounded-2xl text-[10px] font-semibold text-muted-foreground" aria-label="Abrir todos los módulos"><Menu className="h-5 w-5"/><span>Menú</span></button>
    </div>
  </nav>;
}
