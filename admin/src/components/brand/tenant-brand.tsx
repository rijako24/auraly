"use client";

import Image from "next/image";
import { Building2 } from "lucide-react";

import { cn } from "@/lib/utils";

type Props = {
  displayName: string;
  logoUrl?: string | null;
  className?: string;
  imageClassName?: string;
  showName?: boolean;
};

export function TenantBrand({ displayName, logoUrl, className, imageClassName, showName = true }: Props) {
  return <span className={cn("flex min-w-0 items-center gap-3", className)}>
    <span className={cn("grid h-12 w-20 shrink-0 place-items-center overflow-hidden rounded-lg border bg-white", imageClassName)}>
      {logoUrl
        ? <Image loader={({ src }) => src} unoptimized src={logoUrl} width={160} height={96} alt={`Logo de ${displayName}`} className="h-full w-full object-contain p-1" />
        : <Building2 className="h-6 w-6 text-slate-400" aria-hidden="true" />}
    </span>
    {showName && <span className="truncate font-semibold">{displayName}</span>}
  </span>;
}
