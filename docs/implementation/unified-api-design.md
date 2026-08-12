# API única de Auraly

Fecha: 2026-08-05

## Decisión

`Auraly.Api` es el único host ASP.NET Core (`Microsoft.NET.Sdk.Web`) para la aplicación administrativa, Auraly Commerce y POS. No existe una API administrativa separada ni un host Commerce adicional.

Los controladores administrativos viven físicamente en:

- `src/API/Auraly.Api/Controllers`

Las librerías de dominio, aplicación e infraestructura no exponen controladores. `Auraly.Api` actúa como raíz de composición y los controladores delegan en los servicios de aplicación correspondientes.

El proyecto Azure Functions existente continúa siendo un worker de integraciones y tareas asíncronas; no contiene controladores MVC ni reemplaza a `Auraly.Api` como API de la aplicación. La extracción posterior de workers no modifica los contratos HTTP de la aplicación.

## Versionado HTTP

Las rutas públicas quedan organizadas así:

- Administración y autenticación: `/api/v1/...`
- Commerce: `/api/commerce/v1/...`
- POS y dispositivos: `/api/pos/v1/...`
- POS Edge local: `/edge/v1/...`
- Salud del único host: `/health`

Todos los atributos MVC que comienzan en `api/` deben incluir una versión. Una prueba de arquitectura recorre cada `*Controller.cs` y falla si encuentra un controlador fuera de `Auraly.Api` o una ruta MVC sin `vN`.

## Frontend

El frontend usa una sola variable de destino:

- `NEXT_PUBLIC_API_URL`

Su valor termina en `/api`, por ejemplo `http://127.0.0.1:5097/api`. El BFF de Next traduce las rutas administrativas internas a `/api/v1/...` y conserva las rutas que ya incluyen la versión de Commerce o POS. No existe `AURALY_COMMERCE_API_URL` ni una segunda URL de identidad.

## Evidencia ejecutada

- `dotnet build Auraly.Commerce.sln --configuration Release --no-restore`: 0 errores, 0 advertencias.
- `Auraly.Foundation.Tests`: 154/154.
- `Auraly.ServerSlice.IntegrationTests`: 106/106 con SQL Server real y DACPAC.
- Regresión administrativa existente: 701/701.
- `Auraly.Pos.Edge.Host.Tests`: 20/20.
- Pruebas web: 32/32.
- `npx tsc --noEmit`: aprobado.
- `npm run build`: aprobado, 56 páginas generadas.

La prueba `UnifiedApiHostTests` autentica un único cliente y consulta en el mismo host tanto `/api/v1/businesses` como `/api/commerce/v1/products`.