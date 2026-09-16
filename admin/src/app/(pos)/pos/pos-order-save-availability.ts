export function canRequestOrderSave({
  lineCount,
  busy,
}: {
  lineCount: number;
  busy: boolean;
}) {
  return lineCount > 0 && !busy;
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
