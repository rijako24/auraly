"use client";

import { useQuery } from "@tanstack/react-query";

import type { PosClient } from "@/services/pos/pos-edge-client";

export type PosReferenceOptionsClient = Pick<
  PosClient,
  "mode" | "referenceOptions" | "printCashDenominationCount"
>;

export function usePosReferenceOptions(
  client: PosReferenceOptionsClient,
  catalogCode: string,
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-reference-options", client.mode, catalogCode],
    queryFn: () => client.referenceOptions(catalogCode),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}
