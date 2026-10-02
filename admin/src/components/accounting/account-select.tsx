"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { accountingApi, type AccountingAccount } from "@/services/api/accounting";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function AccountSelect({ value, accounts, onChange, expenseOnly = false }: {
  value: string;
  accounts?: Array<{ accountId: string; code: string; name: string }>;
  onChange: (value: string, account?: Pick<AccountingAccount, "accountId" | "code" | "name">) => void;
  expenseOnly?: boolean;
}) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  const [picked, setPicked] = useState<{ accountId: string; code: string; name: string } | null>(null);
  const selected = accounts?.find(account => account.accountId === value) ?? (picked?.accountId === value ? picked : null);
  const resolved = useQuery({
    queryKey: ["account-option", tenantId, value, expenseOnly],
    queryFn: () => accountingApi.accountOptions({ page: 1, pageSize: 1, accountId: value, expenseOnly }),
    enabled: !!tenantId && !!value && !selected,
    staleTime: 5 * 60 * 1000,
  });
  const current = selected ?? resolved.data?.items[0];
  return <PagedEntitySelect<AccountingAccount>
    queryKey={["account-options", tenantId, expenseOnly]}
    value={value}
    onChange={(id, option, account) => { if (account) setPicked(account); onChange(id, account); }}
    loadPage={(search, page, pageSize) => accountingApi.accountOptions({ search, page, pageSize, expenseOnly })}
    getOption={account => ({ value: account.accountId, label: `${account.code} · ${account.name}` })}
    selectedOption={current ? { value: current.accountId, label: `${current.code} · ${current.name}` } : null}
    placeholder="Seleccionar cuenta"
    searchPlaceholder="Buscar por código o nombre…"
    ariaLabel="Seleccionar cuenta contable"
    disabled={!tenantId}
  />;
}
