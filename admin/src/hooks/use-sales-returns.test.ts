import assert from "node:assert/strict";
import test from "node:test";

import { resolveSalesReturnBusinessId } from "../lib/sales-return-business-context";

test("the POS embedded returns view uses the workstation business explicitly", () => {
  assert.equal(resolveSalesReturnBusinessId("pos-business", null), "pos-business");
  assert.equal(
    resolveSalesReturnBusinessId("pos-business", "stale-dashboard-business"),
    "pos-business",
  );
});

test("the administrative returns view keeps using the selected dashboard business", () => {
  assert.equal(resolveSalesReturnBusinessId(undefined, "admin-business"), "admin-business");
});
