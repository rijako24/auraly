import assert from "node:assert/strict";
import test from "node:test";

import { formatWorkSessionCountInput, normalizeWorkSessionCountInput, workSessionCloseRequest, workSessionClosureHtml, workSessionClosurePreviewRequest, workSessionPaymentMethodName, workSessionPaymentMethodRequiresCount } from "./pos-work-session-close";

test("online closure uses the authenticated server work-session endpoints", () => {
  const preview = workSessionClosurePreviewRequest("session/1", "draft-1", "approval-1", "operation-1");
  assert.equal(preview.path, "/api/commerce/v1/work-sessions/session%2F1/closure-preview");
  assert.deepEqual(preview.init.headers, { "X-Auraly-Draft-Id": "draft-1", "X-Auraly-Approval-Id": "approval-1", "X-Auraly-Operation-Id": "operation-1" });
  const request = workSessionCloseRequest("session-1", "operation-1", "draft-1", "approval-1", 120000, [{ paymentMethodCode: "Cash", countedAmount: 120000 }], "Conteo");
  assert.equal(request.path, "/api/commerce/v1/work-sessions/session-1/close");
  assert.deepEqual(request.init.headers, { "Idempotency-Key": "operation-1", "X-Auraly-Draft-Id": "draft-1", "X-Auraly-Approval-Id": "approval-1" });
  assert.deepEqual(JSON.parse(String(request.init.body)), { countedCash: 120000, paymentCounts: [{ paymentMethodCode: "Cash", countedAmount: 120000 }], note: "Conteo" });
});

test("closure print view is a receipt with user and every payment breakdown", () => {
  const html = workSessionClosureHtml({ workSessionClosureId: "close-1", companyName: "Comercializadora & Uno", logoUrl: "https://media.test/logo.png", businessName: "Sede <Uno>", warehouseName: "Principal", userName: "Ana", openedAt: "2026-08-23T10:00:00Z", closedAt: "2026-08-23T12:00:00Z", totalSales: 140, totalRefunds: 0, totalOther: 0, netAmount: 140, expectedCash: 100, countedCash: 100, cashDifference: 0, note: "<script>alert(1)</script>", paymentTotals: [{ paymentMethodCode: "Cash", salesAmount: 110, refundAmount: 10, otherAmount: 0, netAmount: 100, countedAmount: 100, difference: 0 }, { paymentMethodCode: "Transfer", salesAmount: 40, refundAmount: 0, otherAmount: 0, netAmount: 40 }] });
  assert.match(html, /Comercializadora &amp; Uno/);
  assert.match(html, /Sede: Sede &lt;Uno&gt; · Principal/);
  assert.match(html, /https:\/\/media\.test\/logo\.png/);
  assert.match(html, /ARQUEO DE CAJA · CIERRE CONFIRMADO/);
  assert.match(html, /Usuario que trabajó: <strong>Ana<\/strong>/);
  assert.match(html, /Detalle por medio de pago/);
  assert.match(html, /Transferencia/);
  assert.match(html, /Devoluciones/);
  assert.match(html, /Entradas de caja/);
  assert.match(html, /Salidas de caja/);
  assert.doesNotMatch(html, /Movimiento neto total/);
  assert.match(html, /Cuadra/);
  assert.doesNotMatch(html, /Diferencia de efectivo/);
  assert.doesNotMatch(html, /close-1/);
  assert.match(html, /size:80mm auto/);
  assert.match(html, /Apertura:/);
  assert.match(html, /Cierre:/);
  assert.match(html, /Duración:/);
  assert.match(html, /class="payment" data-payment-method="Cash"/);
  const cash = html.slice(html.indexOf("data-payment-method=\"Cash\""), html.indexOf("</section>", html.indexOf("data-payment-method=\"Cash\"")));
  assert.doesNotMatch(cash, />Esperado</);
  assert.doesNotMatch(cash, />Contado</);
  const transfer = html.slice(html.indexOf("<h3>Transferencia</h3>"), html.indexOf("<h2>Totales del turno</h2>"));
  assert.doesNotMatch(transfer, />Entradas</);
  assert.doesNotMatch(transfer, />Salidas</);
  assert.doesNotMatch(transfer, />Esperado</);
  assert.doesNotMatch(transfer, />Contado</);
  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>alert\(1\)<\/script>/);
  assert.doesNotMatch(html, /window\.print/);
});

test("closure count rules require cash, card and transfer", () => {
  assert.equal(workSessionPaymentMethodName("Transfer"), "Transferencia");
  assert.equal(workSessionPaymentMethodName("Withholding"), "Retención");
  assert.equal(workSessionPaymentMethodRequiresCount("Transfer"), true);
  assert.equal(workSessionPaymentMethodRequiresCount("Cash"), true);
  assert.equal(workSessionPaymentMethodRequiresCount("Card"), true);
});

test("closure money inputs format Colombian thousands while typing", () => {
  assert.equal(normalizeWorkSessionCountInput("$ 1.250.000"), "1250000");
  assert.equal(formatWorkSessionCountInput("1250000"), "1.250.000");
  assert.equal(formatWorkSessionCountInput(""), "");
});
