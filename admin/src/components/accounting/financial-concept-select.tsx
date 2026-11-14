"use client";

import { PagedEntitySelect, type PagedEntityOption } from "@/components/forms/paged-entity-select";
import { financialConceptsApi, type FinancialConcept, type FinancialConceptQuery } from "@/services/api/financial-concepts";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function FinancialConceptSelect({ value, usage, selectedOption, disabled, onChange, allowClear=true }: {
  value: string; usage: NonNullable<FinancialConceptQuery["usage"]>; selectedOption?: PagedEntityOption | null;
  disabled?: boolean; onChange: (concept: FinancialConcept | null) => void; allowClear?:boolean;
}) {
  const tenant = useTenantContextStore(state => state.selectedTenantId);
  const business = useBusinessContextStore(state => state.selectedBusinessId);
  return <PagedEntitySelect<FinancialConcept> value={value} selectedOption={selectedOption}
    queryKey={["financial-concept-options", tenant, business, usage]} disabled={disabled || !tenant || !business}
    ariaLabel="Concepto financiero" placeholder="Seleccionar concepto" searchPlaceholder="Buscar código o nombre del concepto…"
    emptyMessage="No hay conceptos configurados para este uso."
    onClear={allowClear?() => onChange(null):undefined} onChange={(_, __, concept) => { if (concept) onChange(concept); }}
    loadPage={async (search, page, pageSize) => { const result = await financialConceptsApi.list({ search, page, pageSize, usage }); return { ...result, totalPages: Math.ceil(result.totalCount / result.pageSize) }; }}
    getOption={concept => ({ value: concept.financialConceptId, label: `${concept.code} · ${concept.name}`,
      description: `${concept.accountCode} · ${concept.accountName} · ${concept.sideLabel}${concept.isActive ? "" : " · Inactivo"}` })} />;
}
