import assert from "node:assert/strict";
import test from "node:test";
import { supportsManagedSites } from "./party-site-management";

test("customers and suppliers can manage party sites", () => {
  assert.equal(supportsManagedSites({ customer: {}, supplier: null }), true);
  assert.equal(supportsManagedSites({ customer: null, supplier: {} }), true);
  assert.equal(supportsManagedSites({ customer: {}, supplier: {} }), true);
});

test("roles without customer or supplier ownership do not manage party sites", () => {
  assert.equal(supportsManagedSites({ customer: null, supplier: null }), false);
});
