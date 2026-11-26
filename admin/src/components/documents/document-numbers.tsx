import { cn } from "@/lib/utils";

export function DocumentNumbers({ internalNumber, fiscalNumber, className }: {
  internalNumber: string | null | undefined;
  fiscalNumber?: string | null;
  className?: string;
}) {
  return <span className={cn("inline-flex min-w-0 flex-col gap-1 break-words", className)}>
    <span className="font-medium"><span className="sr-only">Número interno: </span><span>{internalNumber || "Sin número interno"}</span></span>
    {fiscalNumber && <span className="text-xs font-normal text-muted-foreground">DIAN {fiscalNumber}</span>}
  </span>;
}
