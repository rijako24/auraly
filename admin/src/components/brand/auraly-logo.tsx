"use client";

import { cn } from "@/lib/utils";
import { AuralyMark } from "./auraly-mark";

type AuralyLogoProps = {
  collapsed?: boolean;
  className?: string;
  markClassName?: string;
  priority?: boolean;
};

export function AuralyLogo({
  collapsed = false,
  className,
  markClassName,
  priority = false,
}: AuralyLogoProps) {
  return (
    <span className={cn("flex items-center gap-2.5", className)} aria-label="AURALY">
      <AuralyMark className={cn("h-8 w-10", markClassName)} priority={priority} />
      {!collapsed && (
        <span className="text-sm font-semibold uppercase tracking-[0.28em] text-sidebar-primary">
          AURALY
        </span>
      )}
    </span>
  );
}
