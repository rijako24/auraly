"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { accountingApi, type AccountingAccount } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function AccountSelect({ value, accounts, onChange, expenseOnly = false, liabilityOnly = false, disabled = false, resolveMissing = true }: {
  value: string;
  accounts?: Array<{ accountId: string; code: string; name: string }>;
  onChange: (value: string, account?: Pick<AccountingAccount, "accountId" | "code" | "name">) => void;
  expenseOnly?: boolean;
  liabilityOnly?: boolean;
  disabled?: boolean;
  resolveMissing?: boolean;
}) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [picked, setPicked] = useState<{ accountId: string; code: string; name: string } | null>(null);
  const selected = accounts?.find(account => account.accountId === value) ?? (picked?.accountId === value ? picked : null);
  const resolved = useQuery({
    queryKey: ["account-option", tenantId, businessId, value, expenseOnly, liabilityOnly],
    queryFn: () => accountingApi.accountOptions({ page: 1, pageSize: 1, accountId: value, expenseOnly, liabilityOnly }),
    // Prepared grids own their batch metadata, including unavailable accounts.
    enabled: resolveMissing && !!tenantId && !!value && !selected,
    staleTime: 5 * 60 * 1000,
  });
  const current = selected ?? (resolveMissing ? resolved.data?.items[0] : undefined);
  return <PagedEntitySelect<AccountingAccount>
    queryKey={["account-options", tenantId, businessId, expenseOnly, liabilityOnly]}
    value={value}
    onChange={(id, option, account) => { if (account) setPicked(account); onChange(id, account); }}
    loadPage={(search, page, pageSize) => accountingApi.accountOptions({ search, page, pageSize, expenseOnly, liabilityOnly })}
    getOption={account => ({ value: account.accountId, label: `${account.code} · ${account.name}` })}
    selectedOption={current ? { value: current.accountId, label: `${current.code} · ${current.name}` } : null}
    placeholder="Seleccionar cuenta"
    searchPlaceholder="Buscar por código o nombre…"
    ariaLabel="Seleccionar cuenta contable"
    openOnTyping
    disabled={!tenantId || disabled}
  />;
}
