import assert from "node:assert/strict";
import test from "node:test";

import { approvalRequestConfirmsExistingPermission } from "./pos-approval-permission";

test("recognizes an authoritative permission when the browser session is stale", () => {
  assert.equal(
    approvalRequestConfirmsExistingPermission(
      { message: "El usuario ya tiene el permiso solicitado.", status: 409, code: "PermissionAlreadyGranted" },
    ),
    true,
  );
});

test("does not bypass approval for unrelated failures", () => {
  assert.equal(
    approvalRequestConfirmsExistingPermission(
      { message: "No autorizado.", status: 403, code: "ApprovalRequired" },
    ),
    false,
  );
  assert.equal(approvalRequestConfirmsExistingPermission(new Error("network")), false);
});
