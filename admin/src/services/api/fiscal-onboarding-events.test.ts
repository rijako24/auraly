import assert from "node:assert/strict";
import test from "node:test";

import {
  habilitationFeedbackKind,
  isFiscalStatusSynchronizationMessage,
} from "./fiscal-onboarding-events";

test("recognizes fiscal invalidations emitted through Azure Web PubSub", () => {
  assert.equal(isFiscalStatusSynchronizationMessage(JSON.stringify({
    type: "message",
    data: { Stream: "FiscalStatus", NotificationId: "fiscal-1" },
  })), true);
  assert.equal(isFiscalStatusSynchronizationMessage(JSON.stringify({
    type: "message",
    data: JSON.stringify({ stream: "Approvals" }),
  })), false);
});

test("a terminal fiscal failure is rendered as an error instead of an indefinite loader", () => {
  assert.equal(habilitationFeedbackKind({
    documentId: "document-1",
    status: "SignatureFailed",
    isTerminalFailure: true,
    errorCode: "FiscalSignatureFailed",
    errorMessage: "El certificado no identifica al emisor.",
    updatedAt: "2026-08-25T20:11:42Z",
  }), "failure");
});
