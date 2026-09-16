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

test("decodes a UTF-8 permission error that arrived packed as UTF-16 characters", () => {
  assert.equal(
    posPublicError("敐浲獩楳湯✠慳敬⹳敢潬⵷潣瑳‧獩爠煥極敲⹤"),
    "Esta venta queda por debajo del costo y requiere autorización de un usuario con ese permiso.",
  );
});

test("never exposes internal permission identifiers", () => {
  assert.equal(
    posPublicError("Permission 'sales.below-cost' is required."),
    "Esta venta queda por debajo del costo y requiere autorización de un usuario con ese permiso.",
  );
});
