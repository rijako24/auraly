"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { partiesApi, type PartyRole, type PartyRoleOption } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

export type PartyRoleSelection = PartyRoleOption & {
  customerId: string | null; supplierId: string | null; sellerId: string | null;
  carrierId: string | null; employeeId: string | null; userId: string | null;
};

const toSelection = (item: PartyRoleOption): PartyRoleSelection => ({
  ...item,
  customerId: item.role === "Customer" ? item.roleId : null,
  supplierId: item.role === "Supplier" ? item.roleId : null,
  sellerId: item.role === "Seller" ? item.roleId : null,
  carrierId: item.role === "Carrier" ? item.roleId : null,
  employeeId: item.role === "Employee" ? item.roleId : null,
  userId: item.role === "User" ? item.roleId : null,
});

type PartyRoleSelectProps = {
  role?: PartyRole;
  value: string;
  onChange: (value: string, party?: PartyRoleSelection) => void;
  onResolved?: (party: PartyRoleSelection) => void;
  selectedOption?: PagedEntityOption | null;
  leadingOptions?: PagedEntityOption[];
  placeholder?: string;
  disabled?: boolean;
  includePartyId?: boolean;
  preload?: boolean;
};

export function PartyRoleSelect({ role, value, onChange, onResolved, selectedOption, leadingOptions, placeholder, disabled, includePartyId = false, preload = false }: PartyRoleSelectProps) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [picked, setPicked] = useState<PagedEntityOption | null>(null);
  const getOption = useCallback((item: PartyRoleSelection) => ({
    value: includePartyId || !role ? item.partyId : item.roleId,
    label: item.displayName,
    description: item.identification,
  }), [includePartyId, role]);
  useEffect(() => { if (picked && picked.value !== value) setPicked(null); }, [picked, value]);
  const selectedQuery = useQuery({
    queryKey: ["party-role-select-value", businessId, role ?? "Any", includePartyId, value],
    queryFn: async () => {
      const item = (await partiesApi.roleOptions({
        page: 1, pageSize: 1, role: role ?? "Any",
        ...(includePartyId || !role ? { partyId: value } : { roleId: value }),
      })).items[0];
      return item ? toSelection(item) : null;
    },
    enabled: Boolean(businessId) && Boolean(value) && !selectedOption && picked?.value !== value && !leadingOptions?.some(option => option.value === value),
    staleTime: 5 * 60 * 1000,
  });
  const resolvedSelected = selectedOption ?? (picked?.value === value ? picked : null) ?? (selectedQuery.data ? getOption(selectedQuery.data) : null);
  const onResolvedRef = useRef(onResolved);
  useEffect(() => { onResolvedRef.current = onResolved; }, [onResolved]);
  useEffect(() => { if (selectedQuery.data) onResolvedRef.current?.(selectedQuery.data); }, [selectedQuery.data]);
  return <PagedEntitySelect
    queryKey={["party-role-select", businessId, role ?? "Any", includePartyId]}
    value={value}
    onChange={(id, option, item) => {
      if (item) setPicked(option);
      onChange(id, item);
    }}
    loadPage={async (search, page, pageSize) => {
      const result = await partiesApi.roleOptions({
        page, pageSize, role: role ?? "Any", search: search || undefined,
      });
      return { ...result, items: result.items.map(toSelection) };
    }}
    getOption={getOption}
    selectedOption={resolvedSelected}
    leadingOptions={leadingOptions}
    placeholder={placeholder}
    ariaLabel={role ? `Seleccionar ${role.toLocaleLowerCase("es-CO")}` : "Seleccionar tercero"}
    disabled={disabled || !businessId}
    preload={preload}
  />;
}

export function PartySelect(props: Omit<PartyRoleSelectProps, "role" | "includePartyId">) {
  return <PartyRoleSelect {...props} includePartyId/>;
}
