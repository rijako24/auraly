import type {SalesBreakdownRow} from "@/services/api/sales-reporting";

export interface PaymentMethodOption {
  code: string;
  label: string;
  sortOrder: number;
}

export type PaymentBreakdownRow = SalesBreakdownRow;

const emptyRow = (code: string, label: string): PaymentBreakdownRow => ({
  key: code,
  label,
  documentCount: 0,
  quantity: 0,
  grossSales: 0,
  discounts: 0,
  returns: 0,
  netUntaxedSales: 0,
  tax: 0,
  netSales: 0,
  recognizedCost: null,
  grossProfit: null,
  grossMarginPercent: null,
  participationPercent: 0,
});

export function completePaymentBreakdown(
  options: readonly PaymentMethodOption[],
  rows: readonly PaymentBreakdownRow[],
): PaymentBreakdownRow[] {
  const reported = new Map(rows.map((row) => [row.key.toLocaleLowerCase("es"), row]));
  const configured = [...options]
    .sort((left, right) => left.sortOrder - right.sortOrder)
    .map((option) => {
      const row = reported.get(option.code.toLocaleLowerCase("es"));
      if (!row) return emptyRow(option.code, option.label);
      reported.delete(option.code.toLocaleLowerCase("es"));
      return { ...row, label: option.label };
    });
  return [...configured, ...rows.filter((row) => reported.has(row.key.toLocaleLowerCase("es")))];
}
