"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { partiesApi, type PartyRole, type PartyWorkspaceItem } from "@/services/api/parties";

const roleId = (item: PartyWorkspaceItem, role: PartyRole) => ({
  Customer: item.customerId,
  Supplier: item.supplierId,
  Seller: item.sellerId,
  Carrier: item.carrierId,
  Employee: item.employeeId,
  User: item.userId,
})[role];

type PartyRoleSelectProps = {
  role?: PartyRole;
  value: string;
  onChange: (value: string, party?: PartyWorkspaceItem) => void;
  onResolved?: (party: PartyWorkspaceItem) => void;
  selectedOption?: PagedEntityOption | null;
  leadingOptions?: PagedEntityOption[];
  placeholder?: string;
  disabled?: boolean;
  includePartyId?: boolean;
  preload?: boolean;
};

export function PartyRoleSelect({ role, value, onChange, onResolved, selectedOption, leadingOptions, placeholder, disabled, includePartyId = false, preload = false }: PartyRoleSelectProps) {
  const [picked, setPicked] = useState<PagedEntityOption | null>(null);
  const getOption = useCallback((item: PartyWorkspaceItem) => {
    const id = includePartyId || !role ? item.partyId : roleId(item, role);
    return id ? { value: id, label: item.displayName, description: item.identification ?? item.email ?? null } : null;
  }, [includePartyId, role]);
  useEffect(() => { if (picked && picked.value !== value) setPicked(null); }, [picked, value]);
  const selectedQuery = useQuery({
    queryKey: ["party-role-select-value", role ?? "Any", includePartyId, value],
    queryFn: async () => (await partiesApi.page({ page: 1, pageSize: 1, ...(role ? { role } : {}), isActive: true, ...((includePartyId || !role) ? { partyId: value } : { roleId: value }) })).items[0] ?? null,
    enabled: Boolean(value) && !selectedOption && picked?.value !== value && !leadingOptions?.some(option => option.value === value),
    staleTime: 5 * 60 * 1000,
  });
  const resolvedSelected = selectedOption ?? (picked?.value === value ? picked : null) ?? (selectedQuery.data ? getOption(selectedQuery.data) : null);
  const onResolvedRef = useRef(onResolved);
  useEffect(() => { onResolvedRef.current = onResolved; }, [onResolved]);
  useEffect(() => { if (selectedQuery.data) onResolvedRef.current?.(selectedQuery.data); }, [selectedQuery.data]);
  return <PagedEntitySelect
    queryKey={["party-role-select", role ?? "Any", includePartyId]}
    value={value}
    onChange={(id, option, item) => {
      if (item) setPicked(option);
      onChange(id, item);
    }}
    loadPage={async (search, page, pageSize) => {
      const result = await partiesApi.page({ page, pageSize, ...(role ? { role } : {}), isActive: true, search: search || undefined });
      return result;
    }}
    getOption={getOption}
    selectedOption={resolvedSelected}
    leadingOptions={leadingOptions}
    placeholder={placeholder}
    ariaLabel={role ? `Seleccionar ${role.toLocaleLowerCase("es-CO")}` : "Seleccionar tercero"}
    disabled={disabled}
    preload={preload}
  />;
}

export function PartySelect(props: Omit<PartyRoleSelectProps, "role" | "includePartyId">) {
  return <PartyRoleSelect {...props} includePartyId/>;
}
