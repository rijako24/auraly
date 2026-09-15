import { realtimeReconnectDelay } from "../../lib/realtime-reconnect-policy";

type PosStateNotificationScheduler = (notify: () => void) => () => void;

export function isChangedPosStateEvent(event: string) {
  return event.split("\n").some((line) => line.trim() === "data: changed");
}

export function posStateStreamReconnectDelay(attempt: number) {
  return realtimeReconnectDelay(attempt);
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
