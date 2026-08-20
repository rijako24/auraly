import assert from "node:assert/strict";
import test from "node:test";
import { shouldOfferPwaInstall } from "./pwa-install-visibility";

test("does not offer installation before authentication", () => {
  assert.equal(shouldOfferPwaInstall(false, "/login"), false);
  assert.equal(shouldOfferPwaInstall(false, "/dashboard"), false);
});

test("offers installation only inside the authenticated dashboard", () => {
  assert.equal(shouldOfferPwaInstall(true, "/login"), false);
  assert.equal(shouldOfferPwaInstall(true, "/register"), false);
  assert.equal(shouldOfferPwaInstall(true, "/dashboard"), true);
  assert.equal(shouldOfferPwaInstall(true, "/dashboard/orders"), true);
});
