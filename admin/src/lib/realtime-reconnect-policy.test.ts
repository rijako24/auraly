import assert from "node:assert/strict";
import test from "node:test";

import {
  realtimeReconnectDelay,
  wasRealtimeConnectionStable,
} from "./realtime-reconnect-policy";

test("bounds realtime reconnections instead of turning failures into polling", () => {
  assert.deepEqual(
    [0, 1, 2, 3, 4].map(realtimeReconnectDelay),
    [1_000, 2_000, 4_000, 8_000, null],
  );
  assert.equal(realtimeReconnectDelay(-1), null);
});

test("only a stable connection renews the finite reconnect budget", () => {
  assert.equal(wasRealtimeConnectionStable(null, 50_000), false);
  assert.equal(wasRealtimeConnectionStable(1_000, 30_999), false);
  assert.equal(wasRealtimeConnectionStable(1_000, 31_000), true);
});
