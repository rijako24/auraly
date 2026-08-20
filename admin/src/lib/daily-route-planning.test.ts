import assert from "node:assert/strict";
import test from "node:test";
import { canDismissWorkspaceDialog, firstPendingRouteStop, isoScheduleDay, pendingRouteStops, resolveSellerWorkspace } from "./daily-route-planning";

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

test("restores the seller warehouse remembered on this device", () => {
  const options = [
    { businessId: "business-1", warehouseId: "warehouse-1" },
    { businessId: "business-1", warehouseId: "warehouse-2" },
  ];
  assert.equal(resolveSellerWorkspace(options, "business-1", "business-1:warehouse-2")?.warehouseId, "warehouse-2");
});

test("automatically configures the only seller warehouse", () => {
  const options = [{ businessId: "business-1", warehouseId: "warehouse-1" }];
  assert.equal(resolveSellerWorkspace(options, "business-1", null)?.warehouseId, "warehouse-1");
});

test("requires the warehouse popup when several choices exist and none was remembered", () => {
  const options = [
    { businessId: "business-1", warehouseId: "warehouse-1" },
    { businessId: "business-1", warehouseId: "warehouse-2" },
  ];
  assert.equal(resolveSellerWorkspace(options, "business-1", null), null);
});

test("allows closing the required warehouse dialog when there are no choices", () => {
  assert.equal(canDismissWorkspaceDialog(true, 0), true);
  assert.equal(canDismissWorkspaceDialog(true, 2), false);
  assert.equal(canDismissWorkspaceDialog(false, 2), true);
});
