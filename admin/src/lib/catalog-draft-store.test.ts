import assert from "node:assert/strict";
import test from "node:test";

import { catalogDraftKey } from "./catalog-draft-store";

test("scopes catalog drafts by operation, user, business and entity", () => {
  assert.equal(catalogDraftKey("product-create", "user-1", "business-1"),
    "product-create:user-1:business-1:new");
  assert.notEqual(
    catalogDraftKey("party-edit", "user-1", "business-1", "party-1"),
    catalogDraftKey("party-edit", "user-2", "business-1", "party-1"),
  );
});
