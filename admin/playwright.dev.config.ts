import { defineConfig } from "@playwright/test";
import base from "./playwright.config";

// Every journey uses realAccountingContext, which rejects non-DEV cloud targets
// and limits writes to the explicitly authorized QA tenant and its three sites.
export default defineConfig(base, {
  webServer: undefined,
  testMatch: [
    "accounting-real-local.spec.ts",
    "goods-receipt-browser-regression.spec.ts",
    "inventory-count-and-detail.spec.ts",
    "orders-real-local.spec.ts",
    "payroll-real-dev.spec.ts",
    "portfolio-payment-entry-real.spec.ts",
    "pos-browser-regression.spec.ts",
    "pricing-publication-real-dev.spec.ts",
    "sales-modes-real.spec.ts",
  ],
  use: {
    baseURL: "https://proud-moss-0d9cf540f.7.azurestaticapps.net",
    channel: undefined,
    viewport: { width: 1366, height: 768 },
    actionTimeout: 30_000,
    navigationTimeout: 60_000,
    // Authentication requests must never be included in retained network traces.
    trace: "off",
    serviceWorkers: "block",
  },
});
