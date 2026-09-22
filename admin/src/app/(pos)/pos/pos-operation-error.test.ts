import assert from "node:assert/strict";
import test from "node:test";

import { posOperationErrorMessage } from "./pos-operation-error";

test("un error normal del flujo web nunca se presenta como una falla local", () => {
  assert.equal(
    posOperationErrorMessage("online", new Error("No fue posible recuperar el pedido.")),
    "No fue posible recuperar el pedido.",
  );
});

test("un fallo de transporte web menciona Auraly y no los servicios locales", () => {
  const message = posOperationErrorMessage("online", new TypeError("Failed to fetch"));
  assert.match(message, /conexión con Auraly/i);
  assert.doesNotMatch(message, /servicios locales/i);
});
