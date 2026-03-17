"use client";

import { useQuery } from "@tanstack/react-query";
import { rolesApi } from "@/services/api";
import type { PagedRequest } from "@/types/api";

export const roleKeys = {
  all: ["roles"] as const,
  lists: () => [...roleKeys.all, "list"] as const,
  list: (params?: Partial<PagedRequest>) => [...roleKeys.lists(), params] as const,
  details: () => [...roleKeys.all, "detail"] as const,
  detail: (id: string) => [...roleKeys.details(), id] as const,
};

export function useRoles(params?: Partial<PagedRequest>) {
  return useQuery({
    queryKey: roleKeys.list(params),
    queryFn: () => rolesApi.list(params),
  });
}

export function useRole(id: string) {
  return useQuery({
    queryKey: roleKeys.detail(id),
    queryFn: () => rolesApi.getById(id),
    enabled: !!id,
  });
}
