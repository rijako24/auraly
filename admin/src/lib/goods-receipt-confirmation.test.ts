import assert from "node:assert/strict";
import test from "node:test";
import {
  discardGoodsReceiptDraft,
  goodsReceiptConfirmationReceivedAt,
} from "./goods-receipt-confirmation";

test("confirmation retries preserve the original receipt timestamp", () => {
  const capturedAt = "2026-09-18T10:15";

  assert.equal(
    goodsReceiptConfirmationReceivedAt(capturedAt),
    goodsReceiptConfirmationReceivedAt(capturedAt),
  );
});

test("discarding a local-only receipt clears IndexedDB without calling the server", async () => {
  const calls: string[] = [];

  await discardGoodsReceiptDraft(
    null,
    async () => { calls.push("server"); },
    async () => { calls.push("indexeddb"); },
  );

  assert.deepEqual(calls, ["indexeddb"]);
});

test("discarding a saved receipt removes the server draft before IndexedDB", async () => {
  const calls: string[] = [];

  await discardGoodsReceiptDraft(
    "row-version",
    async (token) => { calls.push(`server:${token}`); },
    async () => { calls.push("indexeddb"); },
  );

  assert.deepEqual(calls, ["server:row-version", "indexeddb"]);
});

test("a server delete conflict preserves the local recovery", async () => {
  const calls: string[] = [];

  await assert.rejects(() => discardGoodsReceiptDraft(
    "stale-row-version",
    async () => { throw new Error("conflict"); },
    async () => { calls.push("indexeddb"); },
  ));

  assert.deepEqual(calls, []);
});
