export function isRestaurantCommandNotice(raw: string): boolean {
  try {
    const envelope = JSON.parse(raw);
    const data = typeof envelope.data === "string"
      ? JSON.parse(envelope.data) : envelope.data;
    return envelope.type === "message" &&
      (data?.stream ?? data?.Stream) === "RestaurantCommands";
  } catch {
    return false;
  }
}

export function hasNewRestaurantCommandArrival(
  cards: ReadonlyArray<{ cardId: string; state: string; receivedAt: string }>,
  knownIds: ReadonlySet<string> | null,
  latestArrival: number | null,
  page: number,
): boolean {
  if (!knownIds || page !== 1) return false;
  return cards.some((card) =>
    !knownIds.has(card.cardId) &&
    (card.state === "Pending" || card.state === "ToDeliver") &&
    (Date.parse(card.receivedAt) || 0) > (latestArrival ?? 0));
}
