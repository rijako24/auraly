"use client";

import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { paymentMethodsApi, type PaymentMethod, type PaymentMethodQuery } from "@/services/api/payment-methods";

export function PaymentMethodSelect({ tenantId, businessId, usage, value, name, accountLabel,
  disabled, ariaLabel, onChange }: {
  tenantId: string | null; businessId: string; usage: NonNullable<PaymentMethodQuery["usage"]>;
  value: string; name?: string | null; accountLabel?: string | null;
  disabled?: boolean; ariaLabel: string; onChange: (method: PaymentMethod) => void;
}) {
  return <PagedEntitySelect<PaymentMethod> value={value}
    queryKey={["payment-method-choices", tenantId, businessId, usage]} disabled={disabled}
    ariaLabel={ariaLabel} placeholder="Selecciona una forma de pago" searchPlaceholder="Buscar código o nombre…"
    emptyMessage="No hay formas habilitadas. Revisa el catálogo de formas de pago."
    selectedOption={value ? { value, label: name ?? "", description: accountLabel } : null}
    loadPage={async (search, page, pageSize) => {
      const result = await paymentMethodsApi.list({ search, page, pageSize, usage });
      return { ...result, totalPages: Math.ceil(result.totalCount / result.pageSize) };
    }}
    getOption={method => ({ value: method.paymentMethodId, label: method.name,
      description: `${method.code} · ${method.accountCode} · ${method.accountName}` })}
    onChange={(_id, _option, method) => { if (method) onChange(method); }} />;
}
