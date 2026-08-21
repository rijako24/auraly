import assert from "node:assert/strict";
import test from "node:test";

import { temporaryNameForCustomer } from "./pos-temporary-name";

test("pauses immediately with the selected customer name", () => {
  assert.equal(temporaryNameForCustomer({ name: "  Mercado Horizonte  " }), "Mercado Horizonte");
});

test("asks for a name when the sale has no selected customer", () => {
  assert.equal(temporaryNameForCustomer(null), null);
  assert.equal(temporaryNameForCustomer({ name: "   " }), null);
});
