# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: masters-browser-regression.spec.ts >> maestros administrables desde la interfaz >> ubicación y clasificación comparten explorador, búsqueda, edición y estado
- Location: e2e\masters-browser-regression.spec.ts:18:7

# Error details

```
Test timeout of 120000ms exceeded.
```

```
Error: locator.fill: Test timeout of 120000ms exceeded.
Call log:
  - waiting for getByRole('dialog').getByLabel(/C.digo/)

```

# Page snapshot

```yaml
- generic:
  - generic:
    - complementary:
      - generic:
        - link:
          - /url: /dashboard
          - generic: AURALY
      - generic:
        - generic:
          - generic:
            - button [expanded]: Principal
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /dashboard
                    - generic: Dashboard
                - generic:
                  - link:
                    - /url: /dashboard/analytics
                    - generic: Analytics
          - generic:
            - button [expanded]: Negocio
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /dashboard/services
                    - generic: Servicios
                - generic:
                  - link:
                    - /url: /dashboard/products
                    - generic: Productos
                - generic:
                  - link:
                    - /url: /dashboard/parties
                    - generic: Terceros
                - generic:
                  - link:
                    - /url: /dashboard/routes
                    - generic: Rutas comerciales
                - generic:
                  - link:
                    - /url: /dashboard/products/pricing
                    - generic: Precios y rentabilidad
                - generic:
                  - link:
                    - /url: /dashboard/promotions
                    - generic: Promociones
                - generic:
                  - link:
                    - /url: /dashboard/employees
                    - generic: Empleados
          - generic:
            - button [expanded]: Operaciones
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /pos
                    - generic: Punto de venta
                - generic:
                  - link:
                    - /url: /dashboard/inventory
                    - generic: Inventario
                - generic:
                  - link:
                    - /url: /dashboard/purchasing/goods-receipts
                    - generic: Recepción de mercancía
                - generic:
                  - link:
                    - /url: /dashboard/purchasing/purchase-returns
                    - generic: Devoluciones a proveedores
                - generic:
                  - link:
                    - /url: /dashboard/sales-returns
                    - generic: Devoluciones de venta
                - generic:
                  - link:
                    - /url: /dashboard/reservations
                    - generic: Reservaciones
                - generic:
                  - link:
                    - /url: /dashboard/reservations/calendar
                    - generic: Calendario
                - generic:
                  - link:
                    - /url: /dashboard/agents
                    - generic: Agente IA
                - generic:
                  - link:
                    - /url: /dashboard/channels
                    - generic: Canales
                - generic:
                  - link:
                    - /url: /dashboard/agents/inbound-contacts
                    - generic: Contactos inbound
                - generic:
                  - link:
                    - /url: /dashboard/conversations
                    - generic: Conversaciones
                - generic:
                  - link:
                    - /url: /dashboard/leads
                    - generic: Leads
                - generic:
                  - link:
                    - /url: /dashboard/campaigns
                    - generic: Campanas
          - generic:
            - button [expanded]: Finanzas
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /dashboard/subscription
                    - generic: Suscripcion
                - generic:
                  - link:
                    - /url: /dashboard/orders
                    - generic: Pedidos
                - generic:
                  - link:
                    - /url: /dashboard/payments
                    - generic: Pagos
                - generic:
                  - link:
                    - /url: /dashboard/payables
                    - generic: Cuentas por pagar
                - generic:
                  - link:
                    - /url: /dashboard/receivables
                    - generic: Cuentas por cobrar
          - generic:
            - button [expanded]: Administracion
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /dashboard/tenants
                    - generic: Tenants
                - generic:
                  - link:
                    - /url: /dashboard/businesses
                    - generic: Negocios
                - generic:
                  - link:
                    - /url: /dashboard/users
                    - generic: Usuarios
                - generic:
                  - link:
                    - /url: /dashboard/roles
                    - generic: Roles
                - generic:
                  - link:
                    - /url: /dashboard/audit-logs
                    - generic: Auditoria
          - generic:
            - button [expanded]: Configuracion
            - generic:
              - generic:
                - generic:
                  - link:
                    - /url: /dashboard/settings/masters
                    - generic: Maestros
                - generic:
                  - link:
                    - /url: /dashboard/settings
                    - generic: Configuracion
      - generic:
        - generic:
          - generic: AN
          - generic:
            - paragraph: Admin Negocio 2222
            - paragraph: admin2222@mimosbabyspa.com
      - generic:
        - button
    - generic:
      - banner:
        - combobox:
          - generic: AURALY
        - navigation:
          - list:
            - listitem:
              - link:
                - /url: /dashboard
                - text: Dashboard
            - listitem
            - listitem:
              - link:
                - /url: /dashboard/settings
                - text: Configuración
            - listitem
            - listitem:
              - link [disabled]: Masters
        - generic:
          - button
          - button
          - button
          - button:
            - generic: AN
      - main:
        - generic:
          - generic:
            - paragraph: Configuración central
            - heading [level=1]: Maestros de Auraly
            - paragraph: Configura datos compartidos sin navegar por decenas de pantallas. Cada sección muestra qué utiliza y permite crear o editar donde corresponde.
          - navigation:
            - button:
              - generic:
                - generic: Ubicación
                - text: Países, departamentos y ciudades
            - button:
              - generic:
                - generic: Productos
                - text: Áreas, líneas, grupos y subgrupos
            - button:
              - generic:
                - generic: Bodegas
                - text: Existencias, negativos y costos
            - button:
              - generic:
                - generic: Motivos
                - text: Conteos, ajustes y movimientos
            - button:
              - generic:
                - generic: IVA
                - text: Tarifas de compra y venta
            - button:
              - generic:
                - generic: Fiscal
                - text: Resolución y numeración DIAN
          - generic:
            - generic:
              - generic:
                - generic:
                  - generic:
                    - heading [level=2]: Ubicación geográfica
                    - paragraph: Países, departamentos y ciudades en un solo árbol. Busca en toda la estructura, expande lo necesario y administra cada registro en contexto.
                - button: Nuevo país
              - generic:
                - generic:
                  - textbox:
                    - /placeholder: Buscar en toda la jerarquía de ubicación geográfica
                - generic:
                  - generic: Mostrar inactivos
                  - switch
                - button: Expandir todo
            - generic:
              - generic:
                - generic: "1"
                - text: País
              - generic:
                - generic: "2"
                - text: Departamento
              - generic:
                - generic: "3"
                - text: Ciudad
            - generic:
              - paragraph: Aún no hay registros.
  - region "Notifications alt+T"
  - alert
  - dialog [ref=f1e2]:
    - heading "Crear país" [level=2] [ref=f1e4]
    - generic [ref=f1e5]:
      - generic [ref=f1e6]:
        - text: Código
        - textbox [ref=f1e7]
      - generic [ref=f1e8]:
        - text: Nombre
        - textbox [active] [ref=f1e9]
    - generic [ref=f1e10]:
      - generic [ref=f1e11]:
        - strong [ref=f1e12]: Registro activo
        - text: Al desactivarlo deja de estar disponible para nuevas selecciones.
      - switch "Registro activo Al desactivarlo deja de estar disponible para nuevas selecciones." [checked] [ref=f1e13] [cursor=pointer]
    - generic [ref=f1e14]:
      - button "Cancelar" [ref=f1e15] [cursor=pointer]
      - button "Guardar" [disabled]
    - button "Close" [ref=f1e16] [cursor=pointer]
```

# Test source

```ts
  1   | import { expect, test, type Page } from "@playwright/test";
  2   | 
  3   | const user = process.env.AURALY_E2E_USERNAME ?? "admin2222";
  4   | const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";
  5   | 
  6   | async function login(page: Page) {
  7   |   await page.goto("/login");
  8   |   await page.locator("#username").fill(user);
  9   |   await page.locator("#password").fill(password);
  10  |   await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  11  |   await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
  12  | }
  13  | 
  14  | test.describe.serial("maestros administrables desde la interfaz", () => {
  15  |   test.setTimeout(120_000);
  16  |   test.beforeEach(async ({ page }) => login(page));
  17  | 
  18  |   test("ubicación y clasificación comparten explorador, búsqueda, edición y estado", async ({ page }) => {
  19  |     const suffix = String(Date.now()).slice(-7);
  20  |     const countryName = `País UI ${suffix}`;
  21  |     const areaName = `Área UI ${suffix}`;
  22  |     const lineName = `Línea UI ${suffix}`;
  23  | 
  24  |     await page.goto("/dashboard/settings/masters");
  25  |     const hierarchySearch = page.getByPlaceholder(/Buscar en toda la jerarqu.a/);
  26  |     await expect(hierarchySearch).toBeVisible({ timeout: 20_000 });
  27  |     await page.getByRole("button", { name: /Nuevo pa.s/ }).click();
  28  |     let dialog = page.getByRole("dialog");
> 29  |     await dialog.getByLabel(/C.digo/).fill(`P${suffix}`);
      |                                       ^ Error: locator.fill: Test timeout of 120000ms exceeded.
  30  |     await dialog.getByLabel("Nombre").fill(countryName);
  31  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  32  |     await expect(dialog).toBeHidden({ timeout: 20_000 });
  33  | 
  34  |     await hierarchySearch.fill(countryName);
  35  |     await expect(page.getByText(countryName, { exact: true })).toBeVisible();
  36  |     await page.getByRole("button", { name: `Editar ${countryName}` }).click();
  37  |     dialog = page.getByRole("dialog");
  38  |     await dialog.getByRole("switch").click();
  39  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  40  |     await expect(page.getByText(countryName, { exact: true })).toBeHidden();
  41  |     await page.getByText("Mostrar inactivos", { exact: true }).locator("..").getByRole("switch").click();
  42  |     await expect(page.getByText(countryName, { exact: true })).toBeVisible();
  43  |     await expect(page.getByText("Inactivo", { exact: true })).toBeVisible();
  44  | 
  45  |     await page.getByRole("button", { name: /^Productos/ }).click();
  46  |     const productHierarchySearch = page.getByPlaceholder(/Buscar en toda la jerarqu.a/);
  47  |     await page.getByRole("button", { name: /Nueva .rea/ }).click();
  48  |     dialog = page.getByRole("dialog");
  49  |     await dialog.getByLabel("Nombre").fill(areaName);
  50  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  51  |     await productHierarchySearch.fill(areaName);
  52  |     await page.getByRole("button", { name: `Crear línea en ${areaName}` }).click();
  53  |     dialog = page.getByRole("dialog");
  54  |     await dialog.getByLabel("Nombre").fill(lineName);
  55  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  56  |     await productHierarchySearch.fill(lineName);
  57  |     await expect(page.getByText(areaName, { exact: true })).toBeVisible();
  58  |     await expect(page.getByText(lineName, { exact: true })).toBeVisible();
  59  |   });
  60  | 
  61  |   test("marca, unidad e IVA permiten buscar, editar e inactivar", async ({ page }) => {
  62  |     const suffix = String(Date.now()).slice(-7);
  63  |     await page.goto("/dashboard/settings/masters");
  64  |     await page.getByRole("button", { name: /^Productos/ }).click();
  65  | 
  66  |     const brandName = `Marca UI ${suffix}`;
  67  |     await page.getByRole("button", { name: "Nueva marca" }).click();
  68  |     let dialog = page.getByRole("dialog");
  69  |     await dialog.getByLabel("Nombre").fill(brandName);
  70  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  71  |     const brandSearch = page.getByPlaceholder("Buscar marcas");
  72  |     await brandSearch.fill(brandName);
  73  |     await page.getByText(brandName, { exact: true }).click();
  74  |     dialog = page.getByRole("dialog");
  75  |     await dialog.getByLabel("Nombre").fill(`${brandName} editada`);
  76  |     await dialog.getByRole("switch").click();
  77  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  78  |     await expect(page.getByText(`${brandName} editada`, { exact: true })).toBeHidden();
  79  | 
  80  |     const unitName = `Unidad UI ${suffix}`;
  81  |     await page.getByRole("button", { name: "Nueva unidad" }).click();
  82  |     dialog = page.getByRole("dialog");
  83  |     await dialog.getByLabel("Nombre").fill(unitName);
  84  |     await dialog.getByLabel(/C.digo/).fill(`U${suffix}`);
  85  |     await dialog.getByLabel(/S.mbolo/).fill(`u${suffix}`);
  86  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  87  |     await page.getByPlaceholder(/Buscar unidades/).fill(unitName);
  88  |     await expect(page.getByText(unitName, { exact: true })).toBeVisible();
  89  | 
  90  |     await page.getByRole("button", { name: /^IVA/ }).click();
  91  |     const vatName = `IVA UI ${suffix}`;
  92  |     await page.getByRole("button", { name: "Nuevo IVA" }).click();
  93  |     dialog = page.getByRole("dialog");
  94  |     await dialog.getByLabel(/C.digo interno/).fill(`IVA-${suffix}`);
  95  |     await dialog.getByLabel(/Tarifa/).fill("7");
  96  |     await dialog.getByLabel("Nombre").fill(vatName);
  97  |     await dialog.getByRole("button", { name: "Guardar", exact: true }).click();
  98  |     await page.getByPlaceholder(/Buscar tarifas de IVA/).fill(vatName);
  99  |     await expect(page.getByText(vatName, { exact: true })).toBeVisible();
  100 |   });
  101 | });
  102 | 
```