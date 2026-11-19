"use client";

import { PagedEntitySelect } from "@/components/forms/paged-entity-select";
import { DatePicker } from "@/components/ui/date-picker";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { addCalendarDays } from "@/lib/accounting-document-date";
import type { AccountingOpeningInvoice } from "@/services/api/accounting";
import { partiesApi, type PartySiteRoleOption } from "@/services/api/parties";

// Capture only. Saving, validating, approving and posting still belong to the
// existing opening aggregate. A closed selector performs no directory I/O.
export function OpeningInvoiceFields({ businessId, partyId, kind, lineNumber, invoice, siteName,
  effectiveOn, amount, disabled, onInvoice, onAmount }: {
  businessId: string; partyId: string | null; kind: "Payable" | "Receivable"; lineNumber: number;
  invoice: AccountingOpeningInvoice; siteName?: string | null; effectiveOn: string; amount: number;
  disabled: boolean; onInvoice: (value: AccountingOpeningInvoice, siteName?: string | null) => void;
  onAmount: (value: number) => void;
}) {
  const prefix = `opening-invoice-${lineNumber}`;
  return <section aria-label={`Factura inicial de la partida ${lineNumber}`}
    className="grid min-w-0 gap-3 rounded-xl border border-teal-200 bg-teal-50/30 p-4 sm:grid-cols-2 lg:grid-cols-3">
    <div className="space-y-1"><Label htmlFor={`${prefix}-number`}>Número de factura original</Label>
      <Input id={`${prefix}-number`} disabled={disabled} value={invoice.documentNumber} maxLength={kind === "Receivable" ? 64 : 80}
        onChange={event => onInvoice({ ...invoice, documentNumber: event.target.value }, siteName)} />
    </div>
    <div className="space-y-1"><Label>Sede del {kind === "Receivable" ? "cliente" : "proveedor"}</Label>
      <PagedEntitySelect<PartySiteRoleOption> queryKey={["opening-invoice-site", businessId, kind, partyId]}
        value={invoice.partySiteId} disabled={disabled || !partyId} ariaLabel={`Sede de la factura inicial ${lineNumber}`}
        selectedOption={invoice.partySiteId ? { value: invoice.partySiteId, label: siteName ?? "Sede seleccionada" } : null}
        placeholder="Seleccionar sede" searchPlaceholder="Buscar sede…" emptyMessage="El tercero no tiene una sede activa para este rol."
        loadPage={(search, page, pageSize) => partiesApi.portfolioSiteOptions({ role: kind === "Receivable" ? "Customer" : "Supplier", partyId: partyId!, search, page, pageSize })}
        getOption={item => ({ value: item.partySiteId, label: item.siteName, description: item.displayName })}
        onChange={(id, _, item) => onInvoice({ ...invoice, partySiteId: id }, item?.siteName)} />
    </div>
    <div className="space-y-1"><Label htmlFor={`${prefix}-issued`}>Fecha de emisión original</Label>
      <DatePicker id={`${prefix}-issued`} today={effectiveOn} value={invoice.issuedOn} disabled={disabled}
        max={effectiveOn ? addCalendarDays(effectiveOn, -1) : undefined}
        onChange={issuedOn => onInvoice({ ...invoice, issuedOn }, siteName)} />
    </div>
    <div className="space-y-1"><Label htmlFor={`${prefix}-due`}>Fecha de vencimiento</Label>
      <DatePicker id={`${prefix}-due`} today={effectiveOn} value={invoice.dueOn} disabled={disabled} min={invoice.issuedOn || undefined}
        onChange={dueOn => onInvoice({ ...invoice, dueOn }, siteName)} />
    </div>
    <div className="space-y-1"><Label>Saldo pendiente al corte · COP</Label>
      <FormattedNumberInput kind="currency" ariaLabel={`Saldo pendiente de la factura inicial ${lineNumber}`}
        value={amount || ""} disabled={disabled} onValueChange={value => onAmount(value ?? 0)} />
    </div>
    <p className="text-xs text-muted-foreground sm:col-span-2 lg:col-span-3">
      Ingresa lo que queda por cobrar o pagar, después de los abonos anteriores al corte.
      Esta apertura conserva la referencia de la factura y no vuelve a facturar la operación.
    </p>
  </section>;
}
