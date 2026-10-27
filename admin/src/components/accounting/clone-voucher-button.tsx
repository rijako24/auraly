"use client";

import { useRef } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Copy, Loader2 } from "lucide-react";
import { accountingApi, type VoucherDraft } from "@/services/api/accounting";
import { proposedAccountingDate } from "@/lib/accounting-document-date";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { Button } from "@/components/ui/button";

export function CloneVoucherButton({ documentId, disabled, onCloned }: {
  documentId: string; disabled?: boolean; onCloned?: (value: VoucherDraft) => void;
}) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const router = useRouter();
  const command = useRef<{ sourceId: string; documentId: string; occurredAt: string } | null>(null);
  const running = useRef(false);
  const clone = useMutation({
    mutationFn: () => {
      if (command.current?.sourceId !== documentId)
        command.current = { sourceId: documentId, documentId: crypto.randomUUID(), occurredAt: proposedAccountingDate() };
      return accountingApi.cloneVoucher(command.current.sourceId, command.current.documentId, command.current.occurredAt);
    },
  });
  return <div className="space-y-1">
    <Button variant="outline" disabled={disabled || clone.isPending} onClick={() => {
      if (running.current) return;
      running.current = true;
      clone.mutate(undefined, { onSuccess: value => {
        if (onCloned) onCloned(value);
        else {
          queryClient.setQueryData(["voucher-draft", tenantId, businessId, value.documentId], value);
          router.push(`/dashboard/financial-traceability?document=${value.documentId}`);
        }
      }, onSettled: () => { running.current = false; } });
    }}>
      {clone.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Copy className="mr-2 h-4 w-4" />}
      {clone.isPending ? "Creando copia…" : "Clonar comprobante"}
    </Button>
    {clone.isError && <p role="alert" className="max-w-md text-xs text-destructive">{clone.error.message}</p>}
  </div>;
}
