const BRANDING_CACHE_MS = 10 * 60 * 1000;

export function createTenantPrintBrandingCache<T>(
  load: () => Promise<T>,
  now: () => number = Date.now,
  dispose?: (value: T) => void,
) {
  let current: { session: string; expiresAt: number; promise: Promise<T>; value?: T } | null = null;
  const release = () => {
    if (current && dispose) void current.promise.then(dispose, () => undefined);
    current = null;
  };
  return {
    get(session: string): Promise<T> {
      if (current?.session === session && current.expiresAt > now()) return current.promise;
      release();
      const promise = load();
      current = { session, expiresAt: now() + BRANDING_CACHE_MS, promise };
      void promise.then(value => {
        if (current?.promise === promise) current.value = value;
      }, () => undefined);
      void promise.catch(() => {
        if (current?.promise === promise) current = null;
      });
      return promise;
    },
    peek(session: string): T | undefined {
      return current?.session === session ? current.value : undefined;
    },
    clear: release,
  };
}
