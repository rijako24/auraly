import assert from "node:assert/strict";
import test from "node:test";
import { readPosResponse } from "./pos-response";

test("POS accepts a successful reprint audit with no response body", async () => {
  const result = await readPosResponse<void>(new Response(null, { status: 204 }));
  assert.equal(result, undefined);
});

test("POS still parses JSON responses and rejects an unexpectedly empty JSON response", async () => {
  assert.deepEqual(
    await readPosResponse<{ documentNumber: string }>(
      Response.json({ documentNumber: "CVI00-00000004" }),
    ),
    { documentNumber: "CVI00-00000004" },
  );
  await assert.rejects(
    readPosResponse(new Response("", { status: 200 })),
    SyntaxError,
  );
});
