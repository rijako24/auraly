import assert from "node:assert/strict";
import test from "node:test";
import { firstPendingRouteStop, isoScheduleDay, pendingRouteStops } from "./daily-route-planning";

const stops = ["first", "second", "third"].map((routeStopId) => ({ routeStopId }));

test("maps browser Sunday to ISO Sunday and preserves Monday through Saturday", () => {
  assert.equal(isoScheduleDay(0), 7);
  assert.equal(isoScheduleDay(1), 1);
  assert.equal(isoScheduleDay(6), 6);
});

test("advances to the next configured customer after a completed order", () => {
  assert.equal(firstPendingRouteStop(stops, [{ routeStopId: "first" }])?.routeStopId, "second");
});

test("a no-purchase visit also advances while preserving configured order", () => {
  assert.deepEqual(pendingRouteStops(stops, [{ routeStopId: "first" }]).map((stop) => stop.routeStopId), ["second", "third"]);
});

test("an out-of-order map visit returns to the earliest configured pending customer", () => {
  assert.equal(firstPendingRouteStop(stops, [{ routeStopId: "third" }])?.routeStopId, "first");
});

test("returns no next customer when the route is complete", () => {
  assert.equal(firstPendingRouteStop(stops, stops), null);
});
