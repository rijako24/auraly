import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { readBackendProxyBody } from "./backend-proxy-body";

describe("readBackendProxyBody", () => {
  it("preserves multipart binary bytes without UTF-8 conversion", async () => {
    const expected = Uint8Array.from([
      0x2d, 0x2d, 0x62, 0x6f, 0x75, 0x6e, 0x64, 0x61, 0x72, 0x79,
      0x0d, 0x0a, 0xff, 0x00, 0xc3, 0x28, 0x80, 0x0d, 0x0a,
    ]);
    const request = new Request("https://example.test/upload", {
      method: "POST",
      body: expected,
    });

    const actual = await readBackendProxyBody(request, "POST");

    assert.ok(actual);
    assert.deepEqual(new Uint8Array(actual), expected);
  });

  it("does not consume a body for GET", async () => {
    const request = {
      arrayBuffer: () => {
        throw new Error("GET body must not be read");
      },
    };

    assert.equal(await readBackendProxyBody(request, "GET"), undefined);
  });
});
