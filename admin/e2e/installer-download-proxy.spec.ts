import { createServer, type Server } from "node:http";
import { expect, test } from "@playwright/test";

let upstream: Server;
let fileReads = 0;
const fileLocation = "http://127.0.0.1:5097/installer-file?sp=r&sig=test";

test.beforeAll(async () => {
  upstream = createServer((request, response) => {
    if (request.url === "/api/commerce/v1/pos/installer/download") {
      response.writeHead(302, { location: fileLocation, "cache-control": "no-store", "content-length": "0" });
      response.end();
    } else if (request.url?.startsWith("/installer-file")) {
      fileReads++;
      response.writeHead(200, { "content-type": "application/octet-stream" });
      response.end("MZ");
    } else if (request.url === "/api/v1/ordinary-redirect") {
      response.writeHead(302, { location: "/ordinary-result" });
      response.end();
    } else if (request.url === "/ordinary-result") {
      response.end("ordinary response");
    } else {
      response.writeHead(404); response.end();
    }
  });
  await new Promise<void>((resolve, reject) => {
    upstream.once("error", reject);
    upstream.listen(5097, "127.0.0.1", resolve);
  });
});

test.afterAll(async () => {
  upstream?.closeAllConnections();
  if (upstream) await new Promise<void>((resolve, reject) => upstream.close(error => error ? reject(error) : resolve()));
});

test("el instalador conserva la redirección firmada sin descargar el ejecutable en el proxy", async ({ request }) => {
  const response = await request.get("/api/commerce/v1/pos/installer/download", {
    maxRedirects: 0, headers: { cookie: "auth_token=test; auraly_auth_client=test-client" },
  });
  expect(response.status()).toBe(302);
  expect(response.headers().location).toBe(fileLocation);
  expect(response.headers()["cache-control"]).toBe("no-store");
  expect(fileReads).toBe(0);
});

test("las demás rutas conservan su comportamiento de redirección", async ({ request }) => {
  const response = await request.get("/api/ordinary-redirect", { headers: { cookie: "auth_token=test" } });
  expect(response.status()).toBe(200);
  expect(await response.text()).toBe("ordinary response");
});
