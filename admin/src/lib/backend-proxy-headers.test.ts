import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { buildBackendProxyHeaders } from "./backend-proxy-headers";

describe("buildBackendProxyHeaders", () => {
  it("forwards authentication, business and durable idempotency", () => {
    const source = new Headers({
      "Content-Type": "application/json",
      "X-Tenant-Id": "tenant-1",
      "X-Business-Id": "business-1",
      "Idempotency-Key": "online-sale-document-1",
      "X-Correlation-Id": "correlation-1",
      "X-Auraly-Draft-Id": "draft-1",
      "X-Auraly-Approval-Id": "approval-1",
      "X-Auraly-Operation-Id": "operation-1",
      Cookie: "must-not-leak=true",
    });

    assert.deepEqual(buildBackendProxyHeaders(source, "token"), {
      "Content-Type": "application/json",
      Authorization: "Bearer token",
      "X-Tenant-Id": "tenant-1",
      "X-Business-Id": "business-1",
      "Idempotency-Key": "online-sale-document-1",
      "X-Correlation-Id": "correlation-1",
      "X-Auraly-Draft-Id": "draft-1",
      "X-Auraly-Approval-Id": "approval-1",
      "X-Auraly-Operation-Id": "operation-1",
    });
  });

  it("does not fabricate optional headers", () => {
    assert.deepEqual(buildBackendProxyHeaders(new Headers()), {
      "Content-Type": "application/json",
    });
  });
});
