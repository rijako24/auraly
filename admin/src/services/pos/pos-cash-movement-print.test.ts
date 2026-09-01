import assert from "node:assert/strict";
import test from "node:test";
import { cashMovementTicketHtml } from "./pos-cash-movement-print";

test("cash movement receipt is professional and omits optional blank fields", () => {
  const html = cashMovementTicketHtml({
    documentId: "movement-1", direction: "Out", reasonName: "Pago de transporte",
    amount: 125000, occurredAt: "2026-08-31T14:30:00-05:00",
    reference: null, notes: null, responsibleName: "Carol Cairo",
  }, "Empresa Uno");

  assert.match(html, /SALIDA DE DINERO/);
  assert.match(html, /Empresa Uno/);
  assert.match(html, /Carol Cairo/);
  assert.match(html, /Firma/);
  assert.doesNotMatch(html, /Referencia/);
  assert.doesNotMatch(html, /Observación/);
});

test("cash movement receipt escapes user-controlled content", () => {
  const html = cashMovementTicketHtml({
    documentId: "movement-2", direction: "In", reasonName: "<script>alert(1)</script>",
    amount: 1, occurredAt: "2026-08-31T14:30:00-05:00",
    reference: "REF<&>", notes: "Nota", responsibleName: "Cajero",
  }, "Empresa");

  assert.doesNotMatch(html, /<script>alert\(1\)<\/script>/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /REF&lt;&amp;&gt;/);
});
