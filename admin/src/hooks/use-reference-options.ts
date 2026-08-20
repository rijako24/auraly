"use client";

import { useQuery } from "@tanstack/react-query";
import { referenceOptionsApi } from "@/services/api/reference-options";

export const referenceOptionKeys = {
  catalog: (catalogCode: string) => ["reference-options", catalogCode] as const,
};

export function useReferenceOptions(catalogCode: string, enabled = true) {
  return useQuery({
    queryKey: referenceOptionKeys.catalog(catalogCode),
    queryFn: () => referenceOptionsApi.list(catalogCode),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}
