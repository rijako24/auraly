import assert from "node:assert/strict";
import test from "node:test";

import { formatWorkSessionCountInput, normalizeWorkSessionCountInput, workSessionCloseRequest, workSessionClosureHtml, workSessionClosurePreviewPath, workSessionPaymentMethodName, workSessionPaymentMethodRequiresCount } from "./pos-work-session-close";

test("online closure uses the authenticated server work-session endpoints", () => {
  assert.equal(workSessionClosurePreviewPath("session/1"), "/api/commerce/v1/work-sessions/session%2F1/closure-preview");
  const request = workSessionCloseRequest("session-1", "operation-1", "draft-1", "approval-1", 120000, [{ paymentMethodCode: "Cash", countedAmount: 120000 }], "Conteo");
  assert.equal(request.path, "/api/commerce/v1/work-sessions/session-1/close");
  assert.deepEqual(request.init.headers, { "Idempotency-Key": "operation-1", "X-Auraly-Draft-Id": "draft-1", "X-Auraly-Approval-Id": "approval-1" });
  assert.deepEqual(JSON.parse(String(request.init.body)), { countedCash: 120000, paymentCounts: [{ paymentMethodCode: "Cash", countedAmount: 120000 }], note: "Conteo" });
});

test("closure print view contains the tenant business and escapes external text", () => {
  const html = workSessionClosureHtml({ logoUrl: "https://media.test/logo.png", businessName: "Empresa <Uno>", warehouseName: "Principal", userName: "Ana", openedAt: "2026-08-23T10:00:00Z", closedAt: "2026-08-23T12:00:00Z", totalSales: 140, totalRefunds: 0, totalOther: 0, netAmount: 140, expectedCash: 100, countedCash: 100, cashDifference: 0, note: "<script>alert(1)</script>", paymentTotals: [{ paymentMethodCode: "Cash", netAmount: 100, countedAmount: 100, difference: 0 }, { paymentMethodCode: "Transfer", netAmount: 40 }] });
  assert.match(html, /Empresa &lt;Uno&gt;/);
  assert.match(html, /https:\/\/media\.test\/logo\.png/);
  assert.match(html, /Transferencia conciliación/);
  assert.match(html, /Automática/);
  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>alert\(1\)<\/script>/);
  assert.doesNotMatch(html, /window\.print/);
});

test("closure count rules keep transfer automatic", () => {
  assert.equal(workSessionPaymentMethodName("Transfer"), "Transferencia");
  assert.equal(workSessionPaymentMethodRequiresCount("Transfer"), false);
  assert.equal(workSessionPaymentMethodRequiresCount("Cash"), true);
});

test("closure money inputs format Colombian thousands while typing", () => {
  assert.equal(normalizeWorkSessionCountInput("$ 1.250.000"), "1250000");
  assert.equal(formatWorkSessionCountInput("1250000"), "1.250.000");
  assert.equal(formatWorkSessionCountInput(""), "");
});
