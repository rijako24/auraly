export function canRequestOrderSave({
  lineCount,
  busy,
}: {
  lineCount: number;
  busy: boolean;
}) {
  return lineCount > 0 && !busy;
}

export function orderSaveRequiresCustomerSelection(customerId: string | null | undefined) {
  return !customerId;
}

export function shouldSaveOrderAfterCustomerSelection({
  pendingOrderSave,
  selectedCustomerId,
}: {
  pendingOrderSave: boolean;
  selectedCustomerId: string | null | undefined;
}) {
  return pendingOrderSave && Boolean(selectedCustomerId);
}

export function removingLastRecoveredOrderLineCancelsOrder({
  sourceOrderId,
  lineCount,
}: {
  sourceOrderId: string | null | undefined;
  lineCount: number;
}) {
  return Boolean(sourceOrderId) && lineCount === 1;
}
