"use client";

import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { focusPaymentEvidence } from "./use-payment-evidence-focus";
import { paymentMethodsApi, type PaymentMethod, type PaymentMethodQuery } from "@/services/api/payment-methods";

export function PaymentMethodSelect({ tenantId, businessId, usage, value, name, accountLabel,
  disabled, ariaLabel, onChange, methodCode, excludeMethodCode, autoSelectSingle = false, className, wrapLabels = false }: {
  tenantId: string | null; businessId: string; usage: NonNullable<PaymentMethodQuery["usage"]>;
  value: string; name?: string | null; accountLabel?: string | null;
  methodCode?: string; excludeMethodCode?: string;
  autoSelectSingle?: boolean;
  className?: string; wrapLabels?: boolean;
  disabled?: boolean; ariaLabel: string; onChange: (method: PaymentMethod) => void;
}) {
  return <PagedEntitySelect<PaymentMethod> value={value}
    className={className} wrapLabels={wrapLabels}
    queryKey={["payment-method-choices", tenantId, businessId, usage, methodCode, excludeMethodCode]} disabled={disabled}
    preload={autoSelectSingle} autoSelectSingle={autoSelectSingle}
    ariaLabel={ariaLabel} placeholder="Selecciona una forma de pago" searchPlaceholder="Buscar código o nombre…"
    emptyMessage="No hay formas habilitadas. Revisa el catálogo de formas de pago."
    selectedOption={value ? { value, label: name ?? "", description: accountLabel } : null}
    loadPage={async (search, page, pageSize) => {
      const result = await paymentMethodsApi.list({ search, page, pageSize, usage, methodCode, excludeMethodCode });
      return { ...result, totalPages: Math.ceil(result.totalCount / result.pageSize) };
    }}
    getOption={method => ({ value: method.paymentMethodId, label: method.name,
      description: `${method.code} · ${method.accountCode} · ${method.accountName}` })}
    onSelectionComplete={trigger => focusPaymentEvidence(trigger.closest("[role='dialog']"))}
    onChange={(_id, _option, method) => { if (method) onChange(method); }} />;
}
