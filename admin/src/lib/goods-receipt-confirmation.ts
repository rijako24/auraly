export function goodsReceiptConfirmationReceivedAt(value: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    throw new Error("La fecha de recepción no es válida.");
  }
  return parsed.toISOString();
}

export async function discardGoodsReceiptDraft(
  concurrencyToken: string | null,
  removeServerDraft: (token: string) => Promise<unknown>,
  removeLocalDraft: () => Promise<void>,
): Promise<void> {
  if (concurrencyToken) await removeServerDraft(concurrencyToken);
  await removeLocalDraft();
}
