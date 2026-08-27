import assert from "node:assert/strict";
import test from "node:test";

import { desktopUpdateAction, isDesktopUpdateStatus } from "./pos-desktop-update-protocol";

test("accepts only desktop update status messages", () => {
  assert.equal(isDesktopUpdateStatus({
    type: "auraly-pos-update-status",
    status: "downloading",
    version: "2.1.0",
    progress: 42,
    message: "Descargando…",
  }), true);
  assert.equal(isDesktopUpdateStatus({
    type: "auraly-pos-update-download",
    status: "ready",
    version: "2.1.0",
    progress: 100,
    message: "Lista",
  }), false);
});

test("maps user decisions to the native desktop protocol", () => {
  assert.equal(desktopUpdateAction("download"), "auraly-pos-update-download");
  assert.equal(desktopUpdateAction("restart"), "auraly-pos-update-restart");
  assert.equal(desktopUpdateAction("later"), "auraly-pos-update-later");
});
