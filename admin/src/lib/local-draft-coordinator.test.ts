import assert from "node:assert/strict";
import test from "node:test";

import { createLocalDraftCoordinator } from "./local-draft-coordinator";

test("coalesces edits and flushes the final snapshot when the editor closes", async () => {
  const stored = new Map<string, string>();
  const writes: string[] = [];
  const drafts = createLocalDraftCoordinator<string>({
    load: async key => stored.get(key),
    save: async (key, value) => { writes.push(value); stored.set(key, value); },
    remove: async key => { stored.delete(key); },
  }, () => assert.fail("No background write should fail."), 250);

  drafts.update("receipt", "first");
  drafts.update("receipt", "second");
  drafts.update("receipt", "final");
  await drafts.flush("receipt");

  assert.deepEqual(writes, ["final"]);
  assert.equal(await drafts.load("receipt"), "final");
});

test("a pending save cannot recreate a draft after discard", async () => {
  const stored = new Map<string, string>();
  const operations: string[] = [];
  let releaseSave!: () => void;
  const blockedSave = new Promise<void>(resolve => { releaseSave = resolve; });
  const drafts = createLocalDraftCoordinator<string>({
    load: async key => stored.get(key),
    save: async (key, value) => {
      operations.push("save-start");
      await blockedSave;
      stored.set(key, value);
      operations.push("save-end");
    },
    remove: async key => { stored.delete(key); operations.push("remove"); },
  }, () => assert.fail("No background write should fail."));

  drafts.update("count", "outdated");
  const save = drafts.flush("count");
  await Promise.resolve();
  const discard = drafts.remove("count");
  releaseSave();
  await Promise.all([save, discard]);

  assert.deepEqual(operations, ["save-start", "save-end", "remove"]);
  assert.equal(stored.has("count"), false);
});

test("reopening after a failed local write keeps the latest snapshot in memory", async () => {
  const stored = new Map<string, string>();
  const drafts = createLocalDraftCoordinator<string>({
    load: async key => stored.get(key),
    save: async () => { throw new Error("IndexedDB unavailable"); },
    remove: async key => { stored.delete(key); },
  }, () => assert.fail("The foreground caller handles this failure."));

  drafts.update("order", "latest");
  await assert.rejects(drafts.flush("order"));
  assert.equal(await drafts.load("order"), "latest");
});

test("a transient IndexedDB failure retries the latest capture on the next close", async () => {
  const stored = new Map<string, string>();
  let attempts = 0;
  const drafts = createLocalDraftCoordinator<string>({
    load: async key => stored.get(key),
    save: async (key, value) => {
      if (++attempts === 1) throw new Error("Transient failure");
      stored.set(key, value);
    },
    remove: async key => { stored.delete(key); },
  }, () => assert.fail("The foreground caller handles this failure."));

  drafts.update("receipt", "final");
  await assert.rejects(drafts.flush("receipt"));
  await drafts.flush("receipt");

  assert.equal(attempts, 2);
  assert.equal(stored.get("receipt"), "final");
});

test("keeps separate draft keys isolated", async () => {
  const stored = new Map<string, string>();
  const drafts = createLocalDraftCoordinator<string>({
    load: async key => stored.get(key),
    save: async (key, value) => { stored.set(key, value); },
    remove: async key => { stored.delete(key); },
  }, () => assert.fail("No background write should fail."));

  drafts.update("user-a:business-a", "a");
  drafts.update("user-b:business-a", "b");
  await drafts.flushAll();

  assert.equal(await drafts.load("user-a:business-a"), "a");
  assert.equal(await drafts.load("user-b:business-a"), "b");
});
