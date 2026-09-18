"use client";

import { LayoutGrid, LogOut } from "lucide-react";
import { useRouter } from "next/navigation";

type Props = {
  target: string;
  localOnly: boolean;
  disabled: boolean;
  onLocalLogout: () => void;
};

export function PosExitMenuButton({
  target,
  localOnly,
  disabled,
  onLocalLogout,
}: Props) {
  const router = useRouter();
  const title = localOnly
    ? "Cerrar la sesión local del cajero"
    : "Salir de facturación y abrir el menú";

  return (
    <button
      type="button"
      onMouseEnter={() => { if (!localOnly) router.prefetch(target); }}
      onFocus={() => { if (!localOnly) router.prefetch(target); }}
      onClick={() => localOnly ? onLocalLogout() : router.push(target)}
      disabled={disabled}
      title={title}
      aria-label={title}
      className="flex h-8 items-center gap-2 rounded-lg border border-white/10 px-2.5 text-xs font-semibold text-auraly-secondary transition hover:bg-white/10 hover:text-white disabled:opacity-40"
    >
      {localOnly ? <LogOut className="h-4 w-4" /> : <LayoutGrid className="h-4 w-4" />}
      <span className="hidden sm:inline">{localOnly ? "Cerrar sesión" : "Menú"}</span>
    </button>
  );
}
