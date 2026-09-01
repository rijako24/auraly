import test from "node:test";
import assert from "node:assert/strict";
import { dailyRouteSnapshotKey, sellerLocalModeKey, sellerOfflinePreparationKey } from "./seller-offline-scope";

test("prepared seller data is isolated by authenticated user", () => {
  assert.notEqual(
    dailyRouteSnapshotKey("user-a", "business", "warehouse", "2026-08-31", "route"),
    dailyRouteSnapshotKey("user-b", "business", "warehouse", "2026-08-31", "route"),
  );
  assert.notEqual(
    sellerOfflinePreparationKey("user-a", "business", "warehouse"),
    sellerOfflinePreparationKey("user-b", "business", "warehouse"),
  );
  assert.equal(
    sellerLocalModeKey("user-a", "business", "warehouse"),
    "auraly:seller-local-mode:user-a:business:warehouse",
  );
});
