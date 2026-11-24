"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { accountingApi, type AccountingCostCenter, type AccountingCostCenterQuery, type AccountingCostCenterPage } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";

const freshness = 300_000;

// One caller per document/form resolves its selected identities in bounded
// batches. Individual line selectors never fetch their selected row separately.
export function useCostCenterLabels(selectedIds: readonly (string | null | undefined)[]) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const client = useQueryClient();
  const ids = [...new Set(selectedIds.filter((id): id is string => !!id))].sort();
  const query = useQuery({
    queryKey: ["accounting-cost-center-labels", tenantId, ids],
    enabled: !!tenantId && ids.length > 0,
    staleTime: freshness,
    queryFn: async () => {
      if (ids.length > 500) throw new Error("El documento supera el límite de centros seleccionados.");
      const found = new Map<string, AccountingCostCenter>();
      const missing = ids.filter(id => {
        const key = ["accounting-cost-center", tenantId, id];
        const row = client.getQueryData<AccountingCostCenter>(key);
        if (!row || Date.now() - (client.getQueryState(key)?.dataUpdatedAt ?? 0) >= freshness) return true;
        found.set(id, row);
        return false;
      });
      const batches: string[][] = [];
      for (let offset = 0; offset < missing.length; offset += 100) batches.push(missing.slice(offset, offset + 100));
      const pages = await Promise.all(batches.map(ids => accountingApi.costCenterPage({ ids, pageSize: 100 })));
      for (const page of pages) for (const row of page.items) {
        found.set(row.costCenterId, row);
        client.setQueryData(["accounting-cost-center", tenantId, row.costCenterId], row);
      }
      return found;
    },
  });
  return query;
}

export function CostCenterSelect({ value, onChange, selected, selectedName, ariaLabel, disabled = false,
  includeInactive = false, excludeId, emptyLabel = "Automático", tenantIdOverride, loadPage, wrapLabels = false, className }: {
  value: string | null;
  onChange: (id: string | null, row?: AccountingCostCenter) => void;
  selected?: AccountingCostCenter;
  selectedName?: string | null;
  ariaLabel: string;
  disabled?: boolean;
  includeInactive?: boolean;
  excludeId?: string;
  emptyLabel?: string;
  tenantIdOverride?: string | null;
  wrapLabels?: boolean;
  className?: string;
  loadPage?: (query: AccountingCostCenterQuery) => Promise<AccountingCostCenterPage>;
}) {
  const contextTenantId = useTenantContextStore(state => state.selectedTenantId);
  const tenantId = tenantIdOverride === undefined ? contextTenantId : tenantIdOverride;
  const client = useQueryClient();
  const option = (row: AccountingCostCenter) => ({ value: row.costCenterId, label: `${row.code} · ${row.name}${row.isActive ? "" : " (inactivo)"}` });
  return <PagedEntitySelect<AccountingCostCenter>
    queryKey={["accounting-cost-center-options", tenantId, includeInactive]}
    value={value ?? "__automatic__"}
    selectedOption={selected ? option(selected) : value ? { value, label: selectedName ?? "Consultando centro seleccionado…" } : null}
    leadingOptions={[{ value: "__automatic__", label: emptyLabel }]}
    disabled={disabled || !tenantId}
    wrapLabels={wrapLabels}
    className={className}
    ariaLabel={ariaLabel}
    placeholder={emptyLabel}
    searchPlaceholder="Buscar centro por código o nombre…"
    onChange={(id, _, row) => {
      if (row) client.setQueryData(["accounting-cost-center", tenantId, row.costCenterId], row);
      onChange(id === "__automatic__" ? null : id, row);
    }}
    loadPage={(search, page, pageSize) => (loadPage ?? accountingApi.costCenterPage)({ search, page, pageSize, isActive: includeInactive ? undefined : true })}
    getOption={row => row.costCenterId === excludeId ? null : option(row)}
  />;
}
