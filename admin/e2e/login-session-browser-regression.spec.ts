import { expect, test } from "@playwright/test";

import { login } from "./support/auth";

test("el navegador conserva la sesión creada por el API y reenvía su ClientId", async ({
  context,
  page,
}) => {
  test.skip(
    process.env.AURALY_E2E_REAL_LOGIN !== "1",
    "Requires the local API and seeded authentication database.",
  );

  await login(page, {
    tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "@auraly",
    username: process.env.AURALY_E2E_USERNAME ?? "admin",
    password: process.env.AURALY_E2E_PASSWORD ?? "Admin123!",
  });

  const cookies = await context.cookies();
  for (const name of ["auth_token", "auth_refresh", "auraly_auth_client"]) {
    const cookie = cookies.find((candidate) => candidate.name === name);
    expect(cookie, `${name} must be issued by the login BFF`).toBeDefined();
    expect(cookie?.httpOnly).toBe(true);
  }

  const status = await page.evaluate(async () =>
    (await fetch("/api/auth/me", { credentials: "include" })).status,
  );
  expect(status).toBe(200);

  await page.reload();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/);
  const statusAfterReload = await page.evaluate(async () =>
    (await fetch("/api/auth/me", { credentials: "include" })).status,
  );
  expect(statusAfterReload).toBe(200);
});
