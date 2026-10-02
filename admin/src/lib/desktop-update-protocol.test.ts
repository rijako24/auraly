import assert from "node:assert/strict";
import test from "node:test";

import { desktopUpdateAction, isDesktopUpdateStatus } from "./desktop-update-protocol";

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
  assert.equal(desktopUpdateAction("check"), "auraly-pos-update-check");
});

test("understands restored downloads and retry states with the native JSON property names", () => {
  for (const status of ["idle", "ready", "error", "check-error", "restarting", "restart-error"]) {
    assert.equal(isDesktopUpdateStatus({ type: "auraly-pos-update-status", status,
      version: "0.1.0-rc235", progress: null, message: "Actualización" }), true);
  }
  assert.equal(isDesktopUpdateStatus({ Type: "auraly-pos-update-status", Status: "available",
    Version: "0.1.0-rc235", Progress: null, Message: "Actualización" }), false);
});
