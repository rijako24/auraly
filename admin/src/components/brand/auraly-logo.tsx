"use client";

import Image from "next/image";

import { cn } from "@/lib/utils";

type AuralyLogoProps = {
  collapsed?: boolean;
  className?: string;
  markClassName?: string;
};

export function AuralyLogo({
  collapsed = false,
  className,
  markClassName,
}: AuralyLogoProps) {
  return (
    <span className={cn("flex items-center gap-2.5", className)} aria-label="AURALY">
      <Image
        src="/brand/auraly-mark.png"
        width={40}
        height={32}
        alt=""
        aria-hidden="true"
        className={cn("h-8 w-10 shrink-0 object-contain", markClassName)}
      />
      {!collapsed && (
        <span className="text-sm font-semibold uppercase tracking-[0.28em] text-sidebar-primary">
          AURALY
        </span>
      )}
    </span>
  );
}
