import { defineConfig } from "@playwright/test";

const manageServers = process.env.AURALY_E2E_MANAGE_SERVERS === "1";
const manageApi = process.env.AURALY_E2E_MANAGE_API !== "0";

export default defineConfig({
  webServer: manageServers ? [
    ...(manageApi ? [{
      command: "dotnet ../src/API/Auraly.Api/bin/Release/net8.0/Auraly.Api.dll --urls http://127.0.0.1:5097",
      url: "http://127.0.0.1:5097/health",
      reuseExistingServer: false,
      timeout: 120_000,
    }] : []),
    {
      command: "node .next/standalone/server.js",
      url: "http://127.0.0.1:3000/login",
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ] : undefined,
  testDir: "./e2e",
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    baseURL: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
    channel: "msedge",
    headless: process.env.AURALY_E2E_HEADED !== "1",
    viewport: { width: 1440, height: 1000 },
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
    video: "off",
  },
  outputDir: "test-results",
});
