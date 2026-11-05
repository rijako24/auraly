import assert from "node:assert/strict";
import test from "node:test";

import {
  isWorkspacePolicySynchronizationMessage,
  shouldReconnectWorkspacePolicy,
  workspaceSynchronizationStream,
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

test("catalog changes use the shared workspace channel with either payload encoding", () => {
  assert.equal(workspaceSynchronizationStream(JSON.stringify({ type: "message", data: { Stream: "Catalog" } })), "Catalog");
  assert.equal(workspaceSynchronizationStream(JSON.stringify({ type: "message", data: JSON.stringify({ stream: "Catalog" }) })), "Catalog");
  assert.equal(workspaceSynchronizationStream(JSON.stringify({ type: "connected", data: { stream: "Catalog" } })), null);
  assert.equal(workspaceSynchronizationStream("invalid"), null);
});
