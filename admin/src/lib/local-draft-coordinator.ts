export type LocalDraftStore<T> = {
  load: (key: string) => Promise<T | undefined>;
  save: (key: string, value: T) => Promise<void>;
  remove: (key: string) => Promise<void>;
};

type Pending<T> = { kind: "save"; value: T } | { kind: "remove" };
type Entry<T> = {
  pending?: Pending<T>;
  timer?: ReturnType<typeof setTimeout>;
  tail: Promise<void>;
  lastOperation: Promise<void>;
  latest?: T;
  hasLatest: boolean;
  revision: number;
};

// Owned by the page, so a dialog can close while its final IndexedDB write
// continues. Only one transaction per key runs at a time and newer edits replace
// snapshots that have not started writing yet.
export function createLocalDraftCoordinator<T>(
  store: LocalDraftStore<T>,
  onBackgroundError: (key: string, error: unknown) => void,
  delayMs = 250,
) {
  const entries = new Map<string, Entry<T>>();
  const entryFor = (key: string) => {
    let entry = entries.get(key);
    if (!entry) {
      entry = { tail: Promise.resolve(), lastOperation: Promise.resolve(), hasLatest: false, revision: 0 };
      entries.set(key, entry);
    }
    return entry;
  };
  const cancelTimer = (entry: Entry<T>) => {
    if (entry.timer !== undefined) clearTimeout(entry.timer);
    entry.timer = undefined;
  };
  const flush = (key: string): Promise<void> => {
    const entry = entryFor(key);
    cancelTimer(entry);
    const pending = entry.pending;
    if (!pending) return entry.lastOperation;
    const revision = entry.revision;
    entry.pending = undefined;
    const operation = entry.tail.then(() => pending.kind === "save"
      ? store.save(key, pending.value)
      : store.remove(key)).catch(error => {
      // A failed transaction stays available for the next explicit close/open.
      // Do not replace a newer edit that arrived while this write was running.
      if (!entry.pending && entry.hasLatest && entry.revision === revision)
        entry.pending = entry.latest === undefined
          ? { kind: "remove" }
          : { kind: "save", value: entry.latest };
      throw error;
    });
    entry.lastOperation = operation;
    entry.tail = operation.catch(() => undefined);
    return operation;
  };
  return {
    update(key: string, value: T | undefined) {
      const entry = entryFor(key);
      entry.latest = value;
      entry.hasLatest = true;
      entry.revision++;
      entry.pending = value === undefined ? { kind: "remove" } : { kind: "save", value };
      cancelTimer(entry);
      entry.timer = setTimeout(() => {
        void flush(key).catch(error => onBackgroundError(key, error));
      }, delayMs);
    },
    flush,
    async load(key: string) {
      const entry = entryFor(key);
      try {
        await flush(key);
      } catch (error) {
        if (entry.hasLatest && entry.latest !== undefined) return entry.latest;
        throw error;
      }
      if (entry.hasLatest) return entry.latest;
      return store.load(key);
    },
    remove(key: string) {
      const entry = entryFor(key);
      entry.latest = undefined;
      entry.hasLatest = true;
      entry.revision++;
      entry.pending = { kind: "remove" };
      return flush(key);
    },
    flushAll() {
      return Promise.all([...entries.keys()].map(key => flush(key)));
    },
  };
}

export type LocalDraftCoordinator<T> = ReturnType<typeof createLocalDraftCoordinator<T>>;
