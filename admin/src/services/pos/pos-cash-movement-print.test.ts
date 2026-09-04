import assert from "node:assert/strict";
import test from "node:test";
import { cashMovementTicketHtml, printCashMovementTicket } from "./pos-cash-movement-print";

test("cash movement receipt is professional and omits optional blank fields", () => {
  const html = cashMovementTicketHtml({
    documentId: "movement-1", direction: "Out", reasonName: "Pago de transporte",
    amount: 125000, occurredAt: "2026-08-31T14:30:00-05:00",
    reference: null, notes: null, responsibleName: "Carol Cairo",
  }, "Empresa Uno", "Sede Norte", "Bodega principal");

  assert.match(html, /Salida de dinero/);
  assert.doesNotMatch(html, /body\s*\{[^}]*text-transform:\s*uppercase/);
  assert.match(html, /header, footer \{ text-align: center/);
  assert.match(html, /Empresa Uno/);
  assert.match(html, /Sede: Sede Norte · Bodega principal/);
  assert.match(html, /Carol Cairo/);
  assert.match(html, /Firma/);
  assert.doesNotMatch(html, /Referencia/);
  assert.doesNotMatch(html, /Observación/);
  assert.doesNotMatch(html, /movement-1/);
  assert.ok(html.indexOf('class="details"') < html.indexOf('class="amount"'));
  assert.ok(html.indexOf('class="amount"') < html.indexOf('class="signature"'));
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

test("web cash movements use an internal print frame instead of a popup", async () => {
  let printCalls = 0;
  let popupCalls = 0;
  const frame = {
    setAttribute() {},
    style: {},
    remove() {},
    onload: null as null | (() => void),
    srcdoc: "",
    contentWindow: {
      addEventListener() {},
      focus() {},
      print() { printCalls += 1; },
    },
  };
  const originalWindow = globalThis.window;
  const originalDocument = globalThis.document;
  Object.assign(globalThis, {
    window: {
      open() { popupCalls += 1; },
      setTimeout(callback: () => void) { callback(); return 1; },
    },
    document: {
      createElement() { return frame; },
      body: { appendChild() { frame.onload?.(); } },
    },
  });
  try {
    await printCashMovementTicket("<html><body>ticket</body></html>");
    assert.equal(printCalls, 1);
    assert.equal(popupCalls, 0);
    assert.match(frame.srcdoc, /ticket/);
  } finally {
    Object.assign(globalThis, { window: originalWindow, document: originalDocument });
  }
});
