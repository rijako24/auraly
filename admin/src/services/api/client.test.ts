import assert from "node:assert/strict";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import test from "node:test";
import { apiClient, fetchWithTimeout } from "./client";

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

test("GET forwards caller cancellation through response-body consumption", {timeout:5_000}, async (context) => {
  const caller = new AbortController();
  context.mock.method(globalThis, "fetch", async (_input: RequestInfo | URL, init?: RequestInit) => {
    assert.ok(init?.signal);
    const stream = new ReadableStream({start(controller) {
      controller.enqueue(new TextEncoder().encode('{"value":'));
      init.signal!.addEventListener("abort", () => controller.error(new DOMException("Cancelled", "AbortError")), {once:true});
    }});
    return new Response(stream, {status:200,headers:{"Content-Type":"application/json"}});
  });
  const pending = apiClient.get("/commerce/v1/receivables/report", undefined, {signal:caller.signal});
  caller.abort();
  await assert.rejects(pending, (error: Error) => error.name === "AbortError");
});


for (const body of ["proxy error", JSON.stringify({title:"Internal Server Error"}), JSON.stringify({detail:"Revisa el documento."})]) {
  test(`API public error keeps its status and Spanish message: ${body}`,async context=>{
    context.mock.method(globalThis,"fetch",async()=>new Response(body,{status:500,statusText:"Internal Server Error"}));
    await assert.rejects(apiClient.get("/test-error"),(error:Error&{statusCode:number})=>{
      assert.equal(error.statusCode,500);
      assert.equal(error.message,body.includes("detail")?"Revisa el documento.":"No fue posible completar la solicitud.");
      return true;
    });
  });
}


test("API network failure stays a transport error with its original diagnostic cause",async context=>{
  const cause=new TypeError("Failed to fetch");
  context.mock.method(globalThis,"fetch",async()=>{throw cause;});
  await assert.rejects(fetchWithTimeout("/test-offline",{}),(error:TypeError)=>{
    assert.ok(error instanceof TypeError);
    assert.equal(error.message,"No fue posible conectar con el servidor. Revisa la conexión.");
    assert.equal(error.cause,cause);
    return true;
  });
});

test("API keeps a caller's TypeError cancellation reason unchanged",async context=>{
  const caller=new AbortController(),reason=new TypeError("Cancelled by caller");
  caller.abort(reason);
  context.mock.method(globalThis,"fetch",async()=>{throw reason;});
  await assert.rejects(fetchWithTimeout("/test-cancelled",{signal:caller.signal}),(error:Error)=>error===reason);
});
