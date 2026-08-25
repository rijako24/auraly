import assert from "node:assert/strict";
import test from "node:test";

import { pushApplicationServerKeyMatches } from "./pos-approval-push-key";

test("keeps a push subscription only when it uses the current VAPID key", () => {
  const current = Uint8Array.from([4, 10, 20, 30]);
  assert.equal(
    pushApplicationServerKeyMatches(current.buffer, Uint8Array.from([4, 10, 20, 30])),
    true,
  );
  assert.equal(
    pushApplicationServerKeyMatches(current.buffer, Uint8Array.from([4, 10, 20, 31])),
    false,
  );
  assert.equal(
    pushApplicationServerKeyMatches(current.buffer, Uint8Array.from([4, 10, 20])),
    false,
  );
  assert.equal(pushApplicationServerKeyMatches(null, current), false);
});
