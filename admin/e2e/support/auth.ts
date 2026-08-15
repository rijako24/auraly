import { expect, type Page } from "@playwright/test";

export type E2eLoginCredentials = {
  tenantKey: string;
  username: string;
  password: string;
};

const defaultCredentials: E2eLoginCredentials = {
  tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "",
  username: process.env.AURALY_E2E_USERNAME ?? "admin",
  password: process.env.AURALY_E2E_PASSWORD ?? "Admin123!",
};

export async function login(
  page: Page,
  credentials: E2eLoginCredentials = defaultCredentials,
  timeout = 20_000,
) {
  if (!credentials.tenantKey.trim()) {
    throw new Error("AURALY_E2E_TENANT_KEY is required for the generic login flow.");
  }

  const tenantKey = credentials.tenantKey.trim();
  await page.goto(`/login?tenant=${encodeURIComponent(tenantKey)}`);
  const tenantInput = page.locator("#tenantKey");
  await expect(tenantInput).toHaveValue(tenantKey);
  await expect(tenantInput).toBeDisabled();
  await page.locator("#username").fill(credentials.username);
  await page.locator("#password").fill(credentials.password);

  const loginResponse = page.waitForResponse((response) =>
    response.request().method() === "POST" && response.url().includes("/api/auth/login"),
  );
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  const response = await loginResponse;
  expect(response.ok(), await response.text()).toBe(true);
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout });
}
