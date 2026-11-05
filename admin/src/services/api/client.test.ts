import assert from "node:assert/strict";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import test from "node:test";
import { fetchWithTimeout } from "./client";

for (const cancelBy of ["caller", "deadline"] as const) {
  test(`API cancellation by ${cancelBy} still aborts after headers arrive`, { timeout: 5_000 }, async (context) => {
    const caller = new AbortController();
    const deadline = new AbortController();
    context.mock.method(AbortSignal, "timeout", (milliseconds: number) => {
      assert.equal(milliseconds, 45_000);
      return deadline.signal;
    });
    const server = createServer((_request, response) => {
      response.writeHead(200, { "Content-Type": "application/json" });
      response.write('{"value":');
    });
    context.after(() => {
      server.closeAllConnections();
      return new Promise<void>((resolve) => server.close(() => resolve()));
    });
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
    const response = await fetchWithTimeout(url, { signal: caller.signal });
    (cancelBy === "caller" ? caller : deadline).abort();
    await assert.rejects(response.text(), (error: Error) => error.name === "AbortError");
  });
}

test("API honors a caller that was already cancelled", async () => {
  const caller = new AbortController();
  caller.abort();
  await assert.rejects(fetchWithTimeout("http://127.0.0.1:1", { signal: caller.signal }),
    (error: Error) => error.name === "AbortError");
});
