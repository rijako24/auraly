import assert from "node:assert/strict";
import test from "node:test";

import { posPublicError } from "./pos-public-error";

test("never exposes the configured API hostname or port", () => {
  const value = posPublicError(
    "Host desconocido. (api-auraly-dev-w5usmo6w.azurewebsites.net:443)",
  );

  assert.ok(value);
  assert.doesNotMatch(value, /azurewebsites|:443/i);
  assert.match(value, /Auraly/);
});

test("preserves actionable business messages", () => {
  assert.equal(
    posPublicError("El servidor rechazó la identidad de esta caja."),
    "El servidor rechazó la identidad de esta caja.",
  );
});
