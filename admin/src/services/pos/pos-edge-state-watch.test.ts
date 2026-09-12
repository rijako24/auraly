import assert from "node:assert/strict";
import test from "node:test";

import {
  createPosStateInvalidationNotifier,
  isChangedPosStateEvent,
  posStateStreamReconnectDelay,
} from "./pos-state-invalidation";

test("local state stream ignores its ready handshake", () => {
  assert.equal(isChangedPosStateEvent("event: state\ndata: ready"), false);
  assert.equal(isChangedPosStateEvent("event: state\ndata: changed"), true);
});

test("local state invalidations coalesce until the scheduled refresh runs", () => {
  let scheduled: (() => void) | null = null;
  let notifications = 0;
  const notifier = createPosStateInvalidationNotifier(
    () => { notifications += 1; },
    (notify) => {
      scheduled = notify;
      return () => { scheduled = null; };
    },
  );

  for (let index = 0; index < 100; index += 1) notifier.notify();

  assert.equal(notifications, 0);
  assert.ok(scheduled);
  const runScheduled = scheduled as () => void;
  runScheduled();
  assert.equal(notifications, 1);

  notifier.dispose();
});

test("local state stream reconnects with bounded backoff", () => {
  assert.deepEqual(
    [0, 1, 2, 3, 4, 5, 20].map(posStateStreamReconnectDelay),
    [500, 1_000, 2_000, 4_000, 5_000, 5_000, 5_000],
  );
});
