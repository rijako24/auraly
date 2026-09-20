import type { WorkSessionInvoiceCharge } from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });

export function InvoiceChargeSummary({ charges, paymentMethod }: {
  charges: WorkSessionInvoiceCharge[]; paymentMethod?: string;
}) {
  const rows = paymentMethod
    ? charges.flatMap(charge => charge.payments.filter(payment => payment.paymentMethodCode === paymentMethod)
      .map(payment => ({ charge, amount: payment.amount, key: `${charge.appliedChargeId}:${payment.paymentNumber}` })))
    : charges.map(charge => ({ charge, amount: charge.amount, key: charge.appliedChargeId }));
  if (paymentMethod && rows.length === 0) return null;
  return <section className="m-4 overflow-hidden rounded-xl border border-teal-100 bg-teal-50/40">
    <h3 className="border-b border-teal-100 px-4 py-3 text-sm font-semibold text-teal-950">
      {paymentMethod ? "Cargos incluidos en este medio" : "Cargos de facturación"}
    </h3>
    <div className="divide-y divide-teal-100">
      {rows.map(({ charge, amount, key }) => <div key={key} className="flex items-start justify-between gap-4 px-4 py-3 text-sm">
        <div className="min-w-0"><strong>{charge.name}</strong><p className="break-words text-xs text-muted-foreground">{charge.documentNumber} · {charge.supplierName}</p>
          {!paymentMethod && <p className="mt-1 text-xs text-teal-800">{charge.invoicedAmount > 0 ? "Incluido en factura" : "Registrado como gasto"}</p>}
        </div><strong className="shrink-0 tabular-nums">{money.format(amount)}</strong>
      </div>)}
      {rows.length === 0 && <p className="px-4 py-3 text-sm text-muted-foreground">Sin cargos en este cierre.</p>}
    </div>
    {!paymentMethod && rows.length > 0 && <div className="grid gap-2 border-t border-teal-100 px-4 py-3 text-sm sm:grid-cols-2">
      <p>Incluido en facturas <strong className="tabular-nums">{money.format(charges.reduce((sum, charge) => sum + charge.invoicedAmount, 0))}</strong></p>
      <p>Registrado como gasto <strong className="tabular-nums">{money.format(charges.reduce((sum, charge) => sum + charge.expenseAmount, 0))}</strong></p>
    </div>}
  </section>;
}
