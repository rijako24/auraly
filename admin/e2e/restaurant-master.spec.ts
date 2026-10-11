import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const productId = "44444444-4444-4444-4444-444444444444";
const permissions = ["dashboard.read", "business_config.read", "businesses.update"];

test.use({ serviceWorkers: "block" });

const json = (route: Route, value: unknown, status = 200) => route.fulfill({
  status, contentType: "application/json", body: JSON.stringify(value),
});

async function setup(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "restaurant-master-test",
    url: "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  const user = { userId, tenantId, tenantKey: "E2E", username: "restaurant-test",
    email: "restaurant@test.local", firstName: "Prueba", lastName: "Restaurante",
    roles: ["Administrador"], permissions };
  await page.addInitScript(({ user, tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("theme", "light");
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true, user }, version: 0 }));
  }, { user, tenantId, businessId });
  const rooms: Array<Record<string, unknown>> = [];
  const tables: Array<Record<string, unknown>> = [];
  const stations: Array<Record<string, unknown>> = [];
  let productRoute: Record<string, unknown> | null = null;
  let roomReads = 0;
  let tableReads = 0;
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    const method = route.request().method();
    if (path.endsWith("/auth/me")) return json(route, user);
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Empresa de prueba" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Restaurante prueba", pointOfSaleMode: "RestaurantBar" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: user.roles, permissions });
    if (path.endsWith(`/businesses/${businessId}`))
      return json(route, { businessId, tenantId, name: "Restaurante prueba",
        pointOfSaleMode: "RestaurantBar" });
    if (path.endsWith("/commerce/v1/restaurant/rooms")) {
      if (method === "POST") {
        const request = route.request().postDataJSON();
        const room = { ...request, isActive: true, rowVersion: "AAAAAAAAAAA=" };
        rooms.push(room);
        return json(route, room, 201);
      }
      roomReads++;
      return json(route, { items: rooms, page: 1, pageSize: 100, totalCount: rooms.length });
    }
    if (path.endsWith("/commerce/v1/restaurant/tables")) {
      if (method === "POST") {
        const request = route.request().postDataJSON();
        const table = { ...request, isActive: true, rowVersion: "AAAAAAAAAAA=" };
        tables.push(table);
        return json(route, table, 201);
      }
      tableReads++;
      return json(route, { items: tables, page: 1, pageSize: 100, totalCount: tables.length });
    }
    if (path.endsWith("/commerce/v1/restaurant/stations")) {
      if (method === "POST") {
        const station = { ...route.request().postDataJSON(), isActive: true,
          rowVersion: "AAAAAAAAAAA=" };
        stations.push(station);
        return json(route, station, 201);
      }
      return json(route, { items: stations, page: 1, pageSize: 100,
        totalCount: stations.length });
    }
    if (path.endsWith(`/businesses/${businessId}/products`)) return json(route, {
      items: [{ productId, businessId, name: "Hamburguesa", productCode: "HAM-01", isActive: true }],
      page: 1, pageSize: 20, totalCount: 1, totalPages: 1,
    });
    if (path.endsWith("/commerce/v1/restaurant/digital-menu") && method === "GET")
      return route.fulfill({ status: 204 });
    if (path.endsWith("/commerce/v1/restaurant/digital-menu/preview"))
      return json(route, { title: "Restaurante prueba", introduction: null,
        categories: [{ categoryId: "55555555-5555-5555-5555-555555555555",
          name: "Hamburguesas", productCount: 1 }] });
    if (path.endsWith("/commerce/v1/restaurant/digital-menu/preview/products"))
      return json(route, { items: [{ productId, name: "Hamburguesa",
        description: "Pan artesanal", price: 25000, currencyCode: "COP",
        imageUrl: null }], hasMore: false });
    if (path.endsWith("/commerce/v1/restaurant/product-routes")) return json(route, {
      items: productRoute ? [productRoute] : [], page: 1, pageSize: 25,
      totalCount: productRoute ? 1 : 0,
    });
    if (path.endsWith(`/commerce/v1/restaurant/product-routes/${productId}`)) {
      if (method === "PUT") {
        productRoute = { productId, ...route.request().postDataJSON(),
          productName: "Hamburguesa", productCode: "HAM-01", rowVersion: "AAAAAAAAAAE=" };
        return json(route, productRoute);
      }
      if (method === "DELETE") {
        const deleted = productRoute;
        productRoute = null;
        return json(route, deleted);
      }
      return json(route, productRoute);
    }
    if (path.includes("/commerce/v1/restaurant/tables/") && method === "PUT") {
      const request = route.request().postDataJSON();
      const table = tables.find((item) => item.tableId === path.split("/").at(-1));
      Object.assign(table!, request, { rowVersion: "AAAAAAAAAAE=" });
      return json(route, table);
    }
    return json(route, []);
  });
  return { rooms, tables, stations, route: () => productRoute,
    reads: () => ({ roomReads, tableReads }) };
}

test("crea salón y mesa, edita sin perder el formulario y funciona en claro/oscuro", async ({ page }) => {
  const data = await setup(page);
  await page.goto("/dashboard/settings/restaurant");
  await expect(page.getByRole("heading", { name: "Salones, mesas y estaciones" })).toBeVisible();
  await page.getByRole("button", { name: "Nuevo salón" }).click();
  await page.getByLabel("Nombre", { exact: true }).fill("Terraza");
  await page.getByRole("button", { name: "Crear salón" }).click();
  await expect(page.getByText("Terraza", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Nueva mesa" }).click();
  await page.getByLabel("Número o código").fill("T-01");
  await page.getByLabel("Nombre opcional").fill("Junto a la ventana");
  await page.getByLabel("Capacidad").fill("4");
  await page.getByRole("dialog").getByRole("combobox").click();
  await page.getByRole("option", { name: "Terraza" }).click();
  await page.getByRole("button", { name: "Crear mesa" }).click();
  await expect(page.getByRole("heading", { name: "T-01" })).toBeVisible();
  await page.getByRole("button", { name: "Editar mesa" }).click();
  await page.getByLabel("Capacidad").fill("6");
  await page.getByRole("button", { name: "Guardar mesa" }).click();
  await expect(page.getByText("6 personas")).toBeVisible();
  expect(data.rooms).toHaveLength(1);
  expect(data.tables).toHaveLength(1);
  expect(data.tables[0].capacity).toBe(6);
  await page.getByRole("button", { name: "Nueva estación" }).click();
  await page.getByLabel("Código", { exact: true }).fill("COCINA");
  await page.getByLabel("Nombre", { exact: true }).fill("Cocina principal");
  await page.getByRole("button", { name: "Guardar estación" }).click();
  await expect(page.getByText("Cocina principal")).toBeVisible();
  expect(data.stations).toHaveLength(1);
  const routing = page.getByRole("region", { name: "Destinos de productos" });
  await routing.getByLabel("Buscar y agregar producto").fill("Hamburguesa");
  await routing.getByRole("option", { name: /Hamburguesa/ }).click();
  const destination = page.getByRole("dialog", { name: "Destino de Hamburguesa" });
  await destination.getByRole("combobox").click();
  await page.getByRole("option", { name: /Cocina principal/ }).click();
  await destination.getByRole("button", { name: "Agregar a la lista" }).click();
  await expect(routing.getByText("Hamburguesa", { exact: true })).toBeVisible();
  await expect(routing.getByText("Cocina principal", { exact: true })).toBeVisible();
  await expect(routing.getByLabel("Buscar y agregar producto")).toBeFocused();
  expect(data.route()?.stationId).toBe(data.stations[0].stationId);
  expect(data.reads()).toEqual({ roomReads: 1, tableReads: 1 });
  await page.screenshot({ path: "../tmp/restaurant-master-light.png", fullPage: true });
  await page.evaluate(() => document.documentElement.classList.add("dark"));
  await page.screenshot({ path: "../tmp/restaurant-master-dark.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "../tmp/restaurant-master-mobile.png", fullPage: true });
  await routing.getByRole("button", { name: "Editar destino de Hamburguesa" }).click();
  await destination.getByLabel("Nombre corto en comanda").fill("Burger");
  await destination.getByRole("button", { name: "Guardar cambios" }).click();
  await expect(routing.getByText(/En comanda: Burger/)).toBeVisible();
  await routing.getByRole("button", { name: "Eliminar destino de Hamburguesa" }).click();
  await page.getByRole("dialog", { name: "Eliminar destino" })
    .getByRole("button", { name: "Eliminar destino" }).click();
  await expect(routing.getByText("Aún no hay productos asignados.", { exact: false })).toBeVisible();
});

test("agrega una mesa existente y permite mover sus sillas y agregar muebles", async ({ page }) => {
  const data = await setup(page);
  const roomId = "66666666-6666-6666-6666-666666666666";
  const tableId = "77777777-7777-7777-7777-777777777777";
  data.rooms.push({ roomId, businessId, name: "Salón principal", sortOrder: 0,
    isActive: true, rowVersion: "AAAAAAAAAAA=" });
  data.tables.push({ tableId, businessId, roomId, code: "M-01", name: null,
    capacity: 4, isActive: true, rowVersion: "AAAAAAAAAAA=" });
  let objects: Array<Record<string, unknown>> = [];
  await page.route(`**/api/commerce/v1/restaurant/rooms/${roomId}/floorplan**`, (route) => {
    if (route.request().method() === "PUT") {
      objects = route.request().postDataJSON().objects;
      return json(route, { roomId, roomName: "Salón principal", rowVersion: "AAAAAAAAAAE=",
        hasCustomLayout: true, hasUnpublishedChanges: true, objects, tables: data.tables });
    }
    return json(route, { roomId, roomName: "Salón principal", rowVersion: "AAAAAAAAAAA=",
      hasCustomLayout: false, hasUnpublishedChanges: false, objects, tables: data.tables });
  });
  await page.goto("/dashboard/restaurant/floorplan");
  await expect(page.getByRole("heading", { name: "Diseña tu restaurante" })).toBeVisible();
  await expect(page.getByText("Agregar al plano", { exact: true })).toHaveCount(0);
  await page.getByRole("combobox", { name: "Elegir mesa creada para agregar al plano" }).click();
  await page.getByRole("option", { name: "Mesa M-01" }).click();
  await page.getByRole("button", { name: "Mesa M-01" }).click();
  await page.getByRole("button", { name: "Mover las 4 sillas actuales" }).click();
  await expect(page.locator('[data-floor-kind="Chair"]')).toHaveCount(4);
  const chair = page.locator('[data-floor-kind="Chair"]').first();
  const initialLeft = await chair.evaluate((element) => element.style.left);
  const box = await chair.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await page.mouse.down();
  await page.mouse.move(box!.x + box!.width / 2 + 80, box!.y + box!.height / 2 + 30);
  await page.mouse.up();
  await expect.poll(() => chair.evaluate((element) => element.style.left)).not.toBe(initialLeft);
  await page.getByRole("button", { name: "Mueble", exact: true }).click();
  await expect(page.locator('[data-floor-kind="Bench"]')).toHaveCount(1);
  await page.getByRole("button", { name: "Guardar plano" }).click();
  await expect.poll(() => objects.length).toBe(6);
  expect(objects.filter((item) => item.kind === "Chair")).toHaveLength(4);
  expect(objects.filter((item) => item.kind === "Bench")).toHaveLength(1);
});

test("el menú digital sin configurar abre su formulario inicial", async ({ page }) => {
  await setup(page);
  await page.goto("/dashboard/restaurant/menu");
  await expect(page.getByRole("heading", { name: "Presentación" })).toBeVisible();
  await expect(page.getByLabel("Nombre del menú")).toHaveValue("Mi restaurante");
  await expect(page.getByText("No pudimos cargar el menú.")).toHaveCount(0);
  await page.getByLabel("Nombre del menú").fill("La Casona");
  await page.getByRole("button", { name: "Vista previa" }).click();
  const preview = page.getByRole("dialog", { name: "Vista previa del menú" });
  await expect(preview.getByRole("heading", { name: "La Casona" })).toBeVisible();
  await expect(preview.getByText("Hamburguesa", { exact: true }).first()).toBeVisible();
  await preview.getByRole("button", { name: "Vista de computador" }).click();
  await expect(preview.getByText(/25\.000/).first()).toBeVisible();
});
