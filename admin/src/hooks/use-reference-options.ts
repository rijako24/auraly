"use client";

import { useQuery } from "@tanstack/react-query";
import { referenceOptionsApi } from "@/services/api/reference-options";
import {useBusinessContextStore} from "@/stores/business-context-store";
import {useTenantContextStore} from "@/stores/tenant-context-store";

export const referenceOptionKeys = {
  catalog: (catalogCode: string) => ["reference-options", catalogCode] as const,
};

export function useReferenceOptions(catalogCode: string, enabled = true,context?:{from?:string;to?:string}) {
  const tenant=useTenantContextStore(state=>state.selectedTenantId);
  const business=useBusinessContextStore(state=>state.selectedBusinessId);
  return useQuery({
    queryKey: [...referenceOptionKeys.catalog(catalogCode),tenant,business,context??null],
    queryFn: () => referenceOptionsApi.list(catalogCode,context?{contextual:true,...context}:undefined),
    enabled:enabled&&(!context||Boolean(tenant&&business)),
    staleTime: 5 * 60 * 1000,
  });
}
