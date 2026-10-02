import { expect, test, type Page } from "@playwright/test";
import { login } from "./support/auth";

async function nativeStatus(page: Page, status: string) {
  await page.evaluate(status => {
    window.dispatchEvent(new CustomEvent("test-desktop-status", { detail: status }));
  }, status);
}

test("application update icon downloads, retries, defers and offers the saved download again", async ({ page }) => {
  test.setTimeout(150_000);
  let manifestReads = 0;
  await page.route("**/api/commerce/v1/pos/installer", route => {
    manifestReads++;
    return route.fulfill({ json: { downloadUrl: "/api/commerce/v1/pos/installer/download",
      version: "0.1.0-rc235", sha256: "A".repeat(64), tenantPreconfigured: false } });
  });
  await page.addInitScript(() => {
    type Message = { type: string };
    const events = new EventTarget();
    const emit = (status: string) => {
      if (status === "ready") localStorage.setItem("test-update-downloaded", "yes");
      events.dispatchEvent(new MessageEvent("message", { data: {
        type: "auraly-pos-update-status", status, version: "0.1.0-rc235",
        progress: status === "downloading" ? 35 : status === "ready" ? 100 : null,
        message: status === "downloading" ? "Descargando actualización… 35%" : "Actualización de prueba",
      } }));
    };
    const native = {
      addEventListener: events.addEventListener.bind(events),
      removeEventListener: events.removeEventListener.bind(events),
      postMessage: (message: Message) => {
        if (message.type === "auraly-pos-update-check") {
          emit(localStorage.getItem("test-update-downloaded") ? "ready" : "idle");
        } else if (message.type === "auraly-pos-update-discovered") emit("available");
        else if (message.type === "auraly-pos-update-download") {
          localStorage.setItem("test-download-requests", String(Number(localStorage.getItem("test-download-requests") ?? 0) + 1));
          emit("downloading");
        } else if (message.type === "auraly-pos-update-restart") {
          localStorage.setItem("test-restart-requested", "yes");
          emit("restarting");
        }
      },
    };
    Object.defineProperty(window, "chrome", { configurable: true, value: { webview: native } });
    window.addEventListener("test-desktop-status", event => emit((event as CustomEvent<string>).detail));
  });
  await login(page, undefined, 60_000);
  const download = page.getByRole("button", { name: "Descargar nueva versión 0.1.0-rc235" });
  await expect(download).toBeVisible({ timeout: 30_000 });
  await expect(download).toHaveCount(1);
  await expect(download).toHaveClass(/text-sky-700/);
  expect(manifestReads).toBe(1);
  await page.screenshot({ path: "../tmp/desktop-update-icon.png" });
  await page.getByRole("link", { name: "Terceros", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/parties/, { timeout: 60_000 });
  await expect(download).toHaveCount(1);
  expect(manifestReads).toBe(1);
  await download.click();
  const dialog = page.getByRole("dialog", { name: "Actualización de Auraly · 0.1.0-rc235" });
  await expect(dialog.getByRole("progressbar", { name: "Progreso de descarga" })).toBeVisible();
  await nativeStatus(page, "error");
  await dialog.getByRole("button", { name: "Reintentar descarga" }).click();
  await expect.poll(() => manifestReads).toBe(2);
  await expect(dialog.getByRole("progressbar", { name: "Progreso de descarga" })).toBeVisible();
  await nativeStatus(page, "ready");
  await dialog.getByRole("button", { name: "Más tarde" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("button", { name: "Actualización 0.1.0-rc235 lista para instalar" })).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem("test-restart-requested"))).toBeNull();
  await page.reload();
  await expect(dialog).toBeVisible({ timeout: 30_000 });
  expect(manifestReads).toBe(2);
  expect(await page.evaluate(() => localStorage.getItem("test-download-requests"))).toBe("2");
  await dialog.getByRole("button", { name: "Reiniciar ahora" }).click();
  await expect(dialog.getByRole("button", { name: "Abriendo instalador…" })).toBeDisabled();
  await nativeStatus(page, "restart-error");
  await expect(dialog.getByRole("button", { name: "Reiniciar ahora" })).toBeEnabled();
  await expect(dialog.getByRole("button", { name: "Más tarde" })).toBeEnabled();
  await dialog.getByRole("button", { name: "Más tarde" }).click();
  await page.getByRole("button", { name: "Actualización 0.1.0-rc235 lista para instalar" }).click();
  await expect(dialog).toBeVisible();
  expect(manifestReads).toBe(2);
});

test("a normal browser does not check or display the native updater", async ({ page }) => {
  let manifestReads = 0;
  await page.route("**/api/commerce/v1/pos/installer", route => {
    manifestReads++;
    return route.fulfill({ status: 500 });
  });
  await login(page, undefined, 60_000);
  await expect(page.locator("header").first()).toBeVisible();
  await page.clock.install();
  await page.clock.runFor(4000);
  await expect(page.getByRole("button", { name: /Descargar nueva versión|lista para instalar|Revisar actualizaciones/ })).toHaveCount(0);
  expect(manifestReads).toBe(0);
});
