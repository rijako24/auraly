import assert from "node:assert/strict";
import test from "node:test";

import {
  isWorkspacePolicySynchronizationMessage,
  shouldReconnectWorkspacePolicy,
} from "./pos-workspace-synchronization";

test("recognizes only configuration invalidations", () => {
  assert.equal(isWorkspacePolicySynchronizationMessage(JSON.stringify({
    type: "message",
    data: { stream: "Configuration" },
  })), true);
  assert.equal(isWorkspacePolicySynchronizationMessage(JSON.stringify({
    type: "message",
    data: { stream: "Catalog" },
  })), false);
});

test("does not retry workspace negotiation after authentication is rejected", () => {
  assert.equal(shouldReconnectWorkspacePolicy(401), false);
  assert.equal(shouldReconnectWorkspacePolicy(403), false);
  assert.equal(shouldReconnectWorkspacePolicy(503), true);
  assert.equal(shouldReconnectWorkspacePolicy(), true);
});
