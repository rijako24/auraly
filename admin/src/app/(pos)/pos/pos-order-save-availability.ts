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
