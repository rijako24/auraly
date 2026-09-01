import Image from "next/image";
import { cn } from "@/lib/utils";

export function AuralyMark({ className }: { className?: string }) {
  return (
    <Image src="/brand/auraly-icon-v5.png" width={512} height={512} alt="" aria-hidden="true" className={cn("shrink-0 object-contain", className)} />
  );
}
