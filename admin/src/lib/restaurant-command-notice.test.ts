import assert from "node:assert/strict";
import test from "node:test";

import { hasNewRestaurantCommandArrival, isRestaurantCommandNotice } from "./restaurant-command-notice";

test("recognizes the restaurant stream in Azure Web PubSub messages", () => {
  assert.equal(isRestaurantCommandNotice(JSON.stringify({
    type: "message", data: { Stream: "RestaurantCommands" },
  })), true);
  assert.equal(isRestaurantCommandNotice(JSON.stringify({
    type: "message", data: JSON.stringify({ stream: "RestaurantCommands" }),
  })), true);
});

test("ignores other streams and protocol frames", () => {
  assert.equal(isRestaurantCommandNotice(JSON.stringify({
    type: "message", data: { Stream: "FiscalStatus" },
  })), false);
  assert.equal(isRestaurantCommandNotice(JSON.stringify({
    type: "system", data: { Stream: "RestaurantCommands" },
  })), false);
  assert.equal(isRestaurantCommandNotice("not-json"), false);
});

test("sounds for a newly received command, not for an old card entering the page", () => {
  const earlier = Date.parse("2026-10-10T12:00:00Z");
  const known = new Set(["existing"]);
  assert.equal(hasNewRestaurantCommandArrival([
    { cardId: "new", state: "Pending", receivedAt: "2026-10-10T12:00:01Z" },
  ], known, earlier, 1), true);
  assert.equal(hasNewRestaurantCommandArrival([
    { cardId: "older", state: "Pending", receivedAt: "2026-10-10T11:59:59Z" },
  ], known, earlier, 1), false);
  assert.equal(hasNewRestaurantCommandArrival([
    { cardId: "moved", state: "Preparing", receivedAt: "2026-10-10T12:00:01Z" },
  ], known, earlier, 1), false);
  assert.equal(hasNewRestaurantCommandArrival([
    { cardId: "new", state: "Pending", receivedAt: "2026-10-10T12:00:01Z" },
  ], known, earlier, 2), false);
});
