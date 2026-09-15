const realtimeReconnectDelays = [1_000, 2_000, 4_000, 8_000] as const;
const stableConnectionMilliseconds = 30_000;

export function realtimeReconnectDelay(failedAttempt: number): number | null {
  if (!Number.isInteger(failedAttempt) || failedAttempt < 0)
    return null;
  return realtimeReconnectDelays[failedAttempt] ?? null;
}

export function wasRealtimeConnectionStable(openedAt: number | null, closedAt: number): boolean {
  return openedAt !== null && closedAt - openedAt >= stableConnectionMilliseconds;
}
