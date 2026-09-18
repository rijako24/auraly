import assert from "node:assert/strict";
import test from "node:test";

import { inventoryDraftKey } from "./operation-draft-store";

test("separa borradores locales por negocio y tipo de operación", () => {
  assert.notEqual(
    inventoryDraftKey("business-a", "adjustment"),
    inventoryDraftKey("business-a", "transfer"),
  );
  assert.notEqual(
    inventoryDraftKey("business-a", "adjustment"),
    inventoryDraftKey("business-b", "adjustment"),
  );
});
