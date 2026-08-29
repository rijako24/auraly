"use client";

import { useQuery } from "@tanstack/react-query";
import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { payrollApi, type PayrollEmployment } from "@/services/api/payroll";

export function PayrollEmploymentSelect({ value, onChange, selectedOption, placeholder = "Buscar trabajador", includeInactive = false }: {
  value: string;
  onChange: (value: string, employment?: PayrollEmployment) => void;
  selectedOption?: PagedEntityOption | null;
  placeholder?: string;
  includeInactive?: boolean;
}) {
  const selected = useQuery({
    queryKey: ["payroll-employment-select-value", value],
    queryFn: async () => (await payrollApi.employments({ page:1, pageSize:1, employmentId:value })).items[0] ?? null,
    enabled: Boolean(value) && !selectedOption,
  });
  const resolved = selectedOption ?? (selected.data ? option(selected.data) : null);
  return <PagedEntitySelect
    queryKey={["payroll-employment-select", includeInactive]}
    value={value}
    onChange={(id, _option, item) => onChange(id, item)}
    loadPage={(search,page,pageSize) => payrollApi.employments({ page, pageSize, search:search||undefined, isActive:includeInactive?undefined:true })}
    getOption={option}
    selectedOption={resolved}
    placeholder={placeholder}
  />;
}

function option(item: PayrollEmployment): PagedEntityOption {
  return { value:item.employmentId, label:item.employeeName, description:`Contrato ${item.contractNumber}` };
}
