import assert from "node:assert/strict";
import test from "node:test";

import {
  isApprovalSynchronizationMessage,
  shouldMaintainApprovalRealtimeConnection,
} from "./pos-approval-synchronization";

test("recognizes the PascalCase invalidation emitted by the API", () => {
  assert.equal(isApprovalSynchronizationMessage(JSON.stringify({
    type: "message",
    data: { Stream: "Approvals", NotificationId: "approval-1" },
  })), true);
});

test("recognizes string and camelCase payloads without treating protocol frames as approvals", () => {
  assert.equal(isApprovalSynchronizationMessage(JSON.stringify({
    type: "message",
    data: JSON.stringify({ stream: "Approvals" }),
  })), true);
  assert.equal(isApprovalSynchronizationMessage(JSON.stringify({ type: "system", event: "connected" })), false);
  assert.equal(isApprovalSynchronizationMessage("not-json"), false);
});

test("keeps realtime presence only while the approval app is visible", () => {
  assert.equal(shouldMaintainApprovalRealtimeConnection("visible"), true);
  assert.equal(shouldMaintainApprovalRealtimeConnection("hidden"), false);
});
