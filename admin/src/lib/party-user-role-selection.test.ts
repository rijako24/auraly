import assert from "node:assert/strict";
import test from "node:test";
import {
  configuredPasswordMask,
  effectiveUserRoleAssignments,
  snapshotSaveHandlers,
} from "./party-user-role-selection";

const assignment = (roleId: string, businessId: string | null) => ({
  roleId,
  roleName: roleId,
  businessId,
  assignedAt: "2026-08-25T00:00:00Z",
});

test("carga roles globales y del negocio sin depender del casing del GUID", () => {
  const effective = effectiveUserRoleAssignments(
    [assignment("global", null), assignment("local", "ABC-123"), assignment("other", "DEF-456")],
    "abc-123",
  );
  assert.deepEqual(effective.map((item) => item.roleId), ["global", "local"]);
});

test("la contraseña configurada se representa con una máscara, no con su valor", () => {
  assert.equal(configuredPasswordMask, "••••••••••");
});

test("el guardado conserva los handlers aunque una recarga desmonte los paneles", async () => {
  let saved = false;
  const handlers = new Map([["user", async () => { saved = true; }]]);
  const snapshot = snapshotSaveHandlers(handlers);
  handlers.clear();
  await Promise.all(snapshot.map((handler) => handler()));
  assert.equal(saved, true);
});
