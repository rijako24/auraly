import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import vm from "node:vm";

const workerSource = await readFile(path.join(process.cwd(), "public", "app-sw.js"), "utf8");

function createPushHarness(visibilityStates) {
  const listeners = new Map();
  const postedMessages = [];
  const notifications = [];
  const clients = visibilityStates.map((visibilityState) => ({
    visibilityState,
    postMessage(message) { postedMessages.push({ visibilityState, message }); },
  }));
  const worker = {
    addEventListener(type, listener) { listeners.set(type, listener); },
    skipWaiting() {},
    clients: {
      claim: async () => undefined,
      matchAll: async () => clients,
      openWindow: async () => undefined,
    },
    registration: {
      async showNotification(title, options) { notifications.push({ title, options }); },
    },
    location: { origin: "https://auraly.test" },
  };
  vm.runInNewContext(workerSource, {
    self: worker,
    caches: { open: async () => ({ addAll: async () => undefined }), keys: async () => [] },
    fetch: async () => undefined,
    URL,
  });

  return {
    notifications,
    postedMessages,
    async push(data = {}) {
      let completion;
      listeners.get("push")({
        data: { json: () => data },
        waitUntil(promise) { completion = promise; },
      });
      await completion;
    },
  };
}

test("refreshes an open Auraly window without showing a system notification", async () => {
  const harness = createPushHarness(["visible"]);
  await harness.push({ title: "Autorización" });
  assert.equal(harness.notifications.length, 0);
  assert.equal(harness.postedMessages.length, 1);
  assert.equal(harness.postedMessages[0].visibilityState, "visible");
  assert.equal(harness.postedMessages[0].message.type, "auraly:pos-approvals-changed");
});

test("shows the system notification when Auraly is not visible", async () => {
  const harness = createPushHarness(["hidden"]);
  await harness.push({ title: "Autorización", body: "Revisar solicitud" });
  assert.equal(harness.notifications.length, 1);
  assert.equal(harness.notifications[0].title, "Autorización");
  assert.equal(harness.notifications[0].options.body, "Revisar solicitud");
});

test("shows the system notification when Auraly has no open window", async () => {
  const harness = createPushHarness([]);
  await harness.push();
  assert.equal(harness.notifications.length, 1);
});
