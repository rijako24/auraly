export function canRequestOrderSave({
  connected,
  lineCount,
  busy,
}: {
  connected: boolean;
  lineCount: number;
  busy: boolean;
}) {
  return connected && lineCount > 0 && !busy;
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
