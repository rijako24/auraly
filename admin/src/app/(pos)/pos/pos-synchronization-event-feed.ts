export type SynchronizationFeedEvent = {
  sequence: number;
  category: string;
};

const hiddenCategories = new Set(["Synchronization", "Push"]);

export function nextSynchronizationEventFeed<TEvent extends SynchronizationFeedEvent>(
  events: readonly TEvent[],
  seenSequences: ReadonlySet<number>,
  initialized: boolean,
  maximumVisible = 4,
): { events: TEvent[]; seenSequences: Set<number> } {
  const nextSeen = new Set(seenSequences);
  events.forEach((event) => nextSeen.add(event.sequence));

  if (!initialized) return { events: [], seenSequences: nextSeen };

  return {
    events: events
      .filter((event) => !hiddenCategories.has(event.category))
      .filter((event) => !seenSequences.has(event.sequence))
      .sort((left, right) => right.sequence - left.sequence)
      .slice(0, maximumVisible),
    seenSequences: nextSeen,
  };
}
