type PosStateNotificationScheduler = (notify: () => void) => () => void;

export function isChangedPosStateEvent(event: string) {
  return event.split("\n").some((line) => line.trim() === "data: changed");
}

export function posStateStreamReconnectDelay(attempt: number) {
  const safeAttempt = Math.max(0, Math.floor(attempt));
  return Math.min(5_000, 500 * (2 ** Math.min(safeAttempt, 4)));
}

export function createPosStateInvalidationNotifier(
  onStateChanged: () => void,
  schedule: PosStateNotificationScheduler,
) {
  let cancelScheduled: (() => void) | null = null;
  return {
    notify() {
      if (cancelScheduled !== null) return;
      cancelScheduled = schedule(() => {
        cancelScheduled = null;
        onStateChanged();
      });
    },
    dispose() {
      cancelScheduled?.();
      cancelScheduled = null;
    },
  };
}
