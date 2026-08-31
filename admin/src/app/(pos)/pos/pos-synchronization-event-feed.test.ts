import assert from "node:assert/strict";
import test from "node:test";
import { nextSynchronizationEventFeed } from "./pos-synchronization-event-feed";

test("opening the synchronization monitor seeds history without replaying it", () => {
  const history = [
    { sequence: 3, category: "Cliente" },
    { sequence: 2, category: "Cliente" },
  ];

  const initial = nextSynchronizationEventFeed(history, new Set(), false);
  assert.deepEqual(initial.events, []);

  const reopened = nextSynchronizationEventFeed(history, initial.seenSequences, true);
  assert.deepEqual(reopened.events, []);
});

test("the open monitor shows only new business changes and hides transport noise", () => {
  const next = nextSynchronizationEventFeed([
    { sequence: 5, category: "Cliente" },
    { sequence: 4, category: "Push" },
    { sequence: 3, category: "Producto" },
  ], new Set([3]), true);

  assert.deepEqual(next.events, [{ sequence: 5, category: "Cliente" }]);
});
