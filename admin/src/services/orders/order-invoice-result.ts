export type OrderInvoiceResultSummary = {
  orderNumber: string;
  status: string;
  error?: string | null;
};

export function orderInvoiceFailureDetails(
  results: readonly OrderInvoiceResultSummary[] | null | undefined,
) {
  return results
    ?.filter((result) => result.status !== "Invoiced" && result.error?.trim())
    .map((result) => `${result.orderNumber}: ${result.error!.trim()}`) ?? [];
}
