import Image from "next/image";
import { cn } from "@/lib/utils";

export function AuralyMark({ className }: { className?: string }) {
  return (
    <Image src="/brand/auraly-mark.png" width={909} height={731} alt="" aria-hidden="true" className={cn("shrink-0 object-contain", className)} />
  );
}
