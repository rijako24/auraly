"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { type PartyRoleSelection } from "@/components/parties/party-role-select";
import { partiesApi, type PartySiteRoleOption } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

export type SupplierSiteSelection = PartyRoleSelection & {
  partySiteId: string;
  siteName: string;
  isPrimary: boolean;
  supplierId: string;
  identification: string;
};

const toSelection = (item: PartySiteRoleOption): SupplierSiteSelection => ({
  ...item,
  role: "Supplier",
  customerId: null,
  supplierId: item.roleId,
  sellerId: null,
  carrierId: null,
  employeeId: null,
  userId: null,
});

const toOption = (item: PartySiteRoleOption): PagedEntityOption => ({
  value: item.partySiteId,
  label: `${item.displayName} · ${item.siteName}`,
  description: item.identification,
});

export function SupplierSiteSelect({ value, onChange, onResolved, roleId, selectedOption,
  disabled = false, preload = false, placeholder = "Buscar proveedor y sede", sourceKey = "supplier-sites" }: {
  value: string;
  onChange: (siteId: string, supplier?: SupplierSiteSelection) => void;
  onResolved?: (supplier: SupplierSiteSelection) => void;
  roleId?: string;
  selectedOption?: PagedEntityOption | null;
  disabled?: boolean;
  preload?: boolean;
  placeholder?: string;
  sourceKey?: string;
}) {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [picked, setPicked] = useState<{ site: SupplierSiteSelection; option: PagedEntityOption } | null>(null);
  useEffect(() => { if (picked && picked.site.partySiteId !== value) setPicked(null); }, [picked, value]);
  const resolved = useQuery({
    queryKey: ["supplier-site-select-value", businessId, value],
    queryFn: async () => {
      const page = await partiesApi.portfolioSiteOptions({ role: "Supplier", partySiteId: value, page: 1, pageSize: 1 });
      return page.items[0] ? toSelection(page.items[0]) : null;
    },
    enabled: !!businessId && !!value && picked?.site.partySiteId !== value && !selectedOption,
    staleTime: 5 * 60 * 1000,
  });
  const onResolvedRef = useRef(onResolved);
  useEffect(() => { onResolvedRef.current = onResolved; }, [onResolved]);
  useEffect(() => { if (resolved.data) onResolvedRef.current?.(resolved.data); }, [resolved.data]);
  return <PagedEntitySelect<PartySiteRoleOption>
    queryKey={[sourceKey, businessId, roleId ?? "all"]}
    value={value}
    selectedOption={selectedOption ?? (picked?.site.partySiteId === value ? picked.option : null) ??
      (resolved.data ? toOption(resolved.data) : null)}
    loadPage={(search, page, pageSize) => partiesApi.portfolioSiteOptions({
      role: "Supplier", roleId, search: search || undefined, page, pageSize,
    })}
    getOption={toOption}
    onChange={(siteId, option, item) => {
      if (!item) return;
      const supplier = toSelection(item);
      setPicked({ site: supplier, option });
      onChange(siteId, supplier);
    }}
    onClear={value ? () => { setPicked(null); onChange(""); } : undefined}
    placeholder={placeholder}
    ariaLabel="Seleccionar proveedor y sede"
    emptyMessage="No hay proveedores con sedes activas para esta búsqueda."
    disabled={disabled || !businessId}
    preload={preload}
  />;
}
