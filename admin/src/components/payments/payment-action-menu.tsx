"use client";

import { ChevronDown, FileText, Loader2, Wallet } from "lucide-react";
import { Button, type ButtonProps } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";

// These are navigation actions. Payment forms still come from the tenant catalog.
export function PaymentActionMenu({ direction, onDirect, onVoucher, label = "Pagar", ariaLabel,
  disabled = false, busy = false, size = "default", variant = "default" }: {
  direction: "payable" | "receivable"; onDirect?: () => void; onVoucher?: () => void;
  label?: string; ariaLabel?: string; disabled?: boolean; busy?: boolean;
  size?: ButtonProps["size"]; variant?: ButtonProps["variant"];
}) {
  if (!onDirect && !onVoucher) return null;
  return <DropdownMenu>
    <DropdownMenuTrigger asChild>
      <Button type="button" size={size} variant={variant} disabled={disabled || busy} aria-label={ariaLabel}
        onClick={event => event.stopPropagation()} onKeyDown={event => event.stopPropagation()}>
        {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}{label}<ChevronDown className="ml-2 h-4 w-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" className="w-64 max-w-[calc(100vw-2rem)]"
      onClick={event => event.stopPropagation()} onKeyDown={event => event.stopPropagation()}>
      <DropdownMenuItem disabled={!onDirect} onSelect={onDirect}>
        <Wallet className="h-4 w-4" /><span>Pago directo</span>
      </DropdownMenuItem>
      <DropdownMenuItem disabled={!onVoucher} onSelect={onVoucher}>
        <FileText className="h-4 w-4" /><span>{direction === "receivable" ? "Recibo de caja" : "Comprobante de egreso"}</span>
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>;
}
