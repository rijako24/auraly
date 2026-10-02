import assert from "node:assert/strict";
import test from "node:test";
import { OrderHttpError, orderHttpError, orderOperationErrorMessage } from "./order-http-error";

test("order errors preserve problem details returned as json", async () => {
  const error = await orderHttpError(new Response(
    JSON.stringify({ detail: "El pedido no existe." }),
    { status: 404, headers: { "content-type": "application/problem+json" } },
  ));
  assert.equal(error.message, "El pedido no existe.");
  assert.equal((error as OrderHttpError).status, 404);
});

test("inactive subscription keeps its user-facing reason and status", async () => {
  const error = await orderHttpError(new Response(
    JSON.stringify({ detail: "La suscripción no está activa. Revisa su estado." }),
    { status: 402, headers: { "content-type": "application/problem+json" } },
  ));
  assert.ok(error instanceof OrderHttpError);
  assert.equal(error.status, 402);
  assert.equal(error.message, "La suscripción no está activa. Revisa su estado.");
});

test("order errors never expose an html error page in the seller UI", async () => {
  const error = await orderHttpError(new Response(
    "<!doctype html><meta name=\"viewport\" content=\"width=device-width\"><h1>Internal Server Error</h1>",
    { status: 500, headers: { "content-type": "text/html" } },
  ));
  assert.equal(error.message, "El servidor no pudo consultar los pedidos. Intenta nuevamente.");
  assert.doesNotMatch(error.message, /html|device-width|internal/i);
});

test("order operations present a friendly message when the API cannot be reached", () => {
  assert.equal(
    orderOperationErrorMessage(new TypeError("Failed to fetch")),
    "No hay conexión con Auraly. Revisa la conexión e intenta nuevamente.",
  );
});
