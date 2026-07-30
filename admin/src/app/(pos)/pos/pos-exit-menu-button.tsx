"use client";

import { LayoutGrid } from "lucide-react";

export function PosExitMenuButton() {
  return (
    <button
      type="button"
      onClick={() => window.location.assign("/dashboard")}
      title="Salir de facturación y abrir el menú"
      className="flex h-8 items-center gap-2 rounded-lg border border-white/10 px-2.5 text-xs font-semibold text-auraly-secondary transition hover:bg-white/10 hover:text-white"
    >
      <LayoutGrid className="h-4 w-4" />
      <span className="hidden sm:inline">Menú</span>
    </button>
  );
}
