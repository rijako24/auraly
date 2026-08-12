# Contexto de ejecución web y POS

Fecha: 2026-08-05

## Decisión

El inicio de sesión autentica exclusivamente al usuario. No solicita tenant, negocio, sede, bodega ni dispositivo.

`AppUsers.TenantId` identifica el tenant de origen de la cuenta y de la sesión autenticada; no concede por sí solo acceso operativo a todos sus datos. El alcance de trabajo se obtiene de las asignaciones vigentes `UserRoles -> AppRoles.TenantId` y, cuando existe, `UserRoles.BusinessId`. Los permisos determinan acciones; las membresías determinan alcance.

## Web

1. Después del login, `GET /api/v1/execution-context/tenants` obtiene únicamente los tenants autorizados.
2. Con un solo tenant se selecciona automáticamente y no se muestra un control innecesario.
3. Con varios tenants, el selector vive en el menú del usuario junto a perfil y cierre de sesión.
4. Se restaura el último tenant local solamente si el servidor todavía lo devuelve como autorizado.
5. `GET /api/v1/execution-context/businesses`, con `X-Tenant-Id`, obtiene los negocios autorizados.
6. El negocio permanece visible en el encabezado porque cambia con mayor frecuencia y afecta precios, inventario y facturación.
7. Cada solicitud operativa envía `X-Tenant-Id` y `X-Business-Id` por el BFF.

Cambiar tenant invalida el negocio seleccionado y obliga a escoger automáticamente un negocio válido del nuevo tenant. Una preferencia de navegador nunca concede acceso.

## Aplicación instalada

El tenant proviene del paquete firmado de enrolamiento y queda fijado al dispositivo. El usuario no ve un selector de tenant. El negocio continúa siendo seleccionable únicamente entre los permitidos para ese tenant y usuario.

El bootstrap online del POS devuelve el `TenantId` efectivo para que todos los llamados posteriores utilicen el mismo contexto. El modo offline usa el tenant persistido por POS Edge durante el enrolamiento.

## Validación del servidor

`ExecutionContextMiddleware` es el único componente que resuelve el contexto HTTP. Reemplaza los middlewares anteriores que interpretaban `tenants.read` como acceso directo a cualquier negocio.

Cuando se envía un contexto explícito, el middleware:

- valida usuario activo, tenant activo y negocio activo;
- valida la membresía de tenant y negocio;
- vuelve a cargar roles y permisos aplicables al contexto;
- reemplaza los claims operativos `tenant_id`, `business_id`, roles y permisos;
- configura `ITenantContext` para los componentes existentes;
- responde 403 si el contexto dejó de estar autorizado.

`tenants.read` conserva el caso administrativo global existente, pero no sustituye la selección explícita de tenant ni permite mezclar negocios de distintos tenants en una consulta.

## Persistencia de preferencia

`selected_tenant_id` conserva el último tenant en el navegador. Al iniciar:

- si sigue autorizado, se restaura;
- si fue revocado, se usa el primer tenant autorizado;
- si solo existe uno, se activa automáticamente;
- si no hay ninguno, se muestra un error de acceso y no se abre el panel.

La validación efectiva siempre está en SQL Server y se ejecuta en la API.

## Pruebas

La cobertura incluye:

- membresía del mismo usuario en dos tenants;
- listado de negocios limitado al tenant seleccionado;
- rechazo de tenant no asignado;
- recálculo de permisos al seleccionar contexto;
- rechazo de permisos presentes solo en el token de login;
- restauración del último tenant todavía autorizado;
- descarte seguro de una preferencia revocada;
- propagación de `X-Tenant-Id` y `X-Business-Id` a través del BFF.