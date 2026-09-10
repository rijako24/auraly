import assert from "node:assert/strict";
import test from "node:test";
import { cashDenominationCountHtml, formatWorkSessionCountInput, normalizeWorkSessionCountInput, workSessionCloseRequest, workSessionClosurePreviewRequest, workSessionClosureReceiptRequest, workSessionPaymentMethodName, workSessionPaymentMethodRequiresCount } from "./pos-work-session-close";

test("builds work-session closure requests", () => {
  const preview = workSessionClosurePreviewRequest("session/1", "draft-1", "approval-1", "operation-1");
  assert.equal(preview.path, "/api/commerce/v1/work-sessions/session%2F1/closure-preview");
  assert.deepEqual(preview.init.headers, { "X-Auraly-Draft-Id": "draft-1", "X-Auraly-Approval-Id": "approval-1", "X-Auraly-Operation-Id": "operation-1" });

  const close = workSessionCloseRequest("session/1", "operation-1", "draft-1", undefined, 100, [{ paymentMethodCode: "Cash", countedAmount: 100 }], "ok");
  assert.equal(close.path, "/api/commerce/v1/work-sessions/session%2F1/close");
  assert.equal(close.init.method, "POST");

  const receipt = workSessionClosureReceiptRequest("session/1", "Compañía", "logo", 58);
  assert.equal(receipt.path, "/api/commerce/v1/work-sessions/session%2F1/closure-receipt");
  assert.deepEqual(JSON.parse(String(receipt.init.body)), { companyName: "Compañía", companyLogoSource: "logo", paperWidthMillimeters: 58 });
});

test("formats closure inputs and payment labels", () => {
  assert.equal(normalizeWorkSessionCountInput("$ 001.234"), "1234");
  assert.equal(formatWorkSessionCountInput("1234"), "1.234");
  assert.equal(workSessionPaymentMethodName("Transfer"), "Transferencia");
  assert.equal(workSessionPaymentMethodRequiresCount("Cash"), true);
  assert.equal(workSessionPaymentMethodRequiresCount("Credit"), false);
});

test("keeps cash denomination browser fallback", () => {
  const html = cashDenominationCountHtml({ businessName: "Auraly", userName: "Ana", countedAt: "2026-08-23T12:00:00Z", lines: [{ label: "$ 50.000", value: 50000, quantity: 2, subtotal: 100000 }], total: 100000 });
  assert.match(html, /Conteo de efectivo/);
  assert.match(html, /100\.000/);
});
