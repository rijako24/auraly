# Evidencia — catálogo y sincronización POS

Fecha: 2026-07-27

## Decisión comprobada

- `dbo.Products` se conserva como identidad única.
- El DACPAC no contiene `CatalogProducts`.
- Una prueba SQL crea por la API y comprueba exactamente una fila en
  `Products`, además de comprobar que `CatalogProducts` no existe.
- Los precios, códigos, proveedores/costos, impuestos y balanza están
  normalizados en tablas relacionadas.

## Escenarios automatizados

La suite de fundación cubre con SQLite físico:

- creación aditiva del esquema sin borrar datos POS existentes;
- staging no visible;
- checkpoint después de reiniciar;
- promoción atómica;
- búsqueda exacta y por nombre;
- código alterno;
- código de balanza y cantidad decimal;
- incremental repetido;
- tombstone que bloquea captura.

La suite SQL Server cubre:

- publicación real del DACPAC;
- JWT administrativo y permisos;
- creación y actualización de producto;
- filtro combinado;
- proveedor y costo en servidor;
- selección exclusiva del precio del canal de caja;
- bootstrap automático de POS Edge;
- búsqueda offline posterior;
- cambio incremental de precio y código de barras;
- desactivación/tombstone;
- código de barras duplicado;
- separación del tenant del body frente al autenticado;
- ausencia de costos/proveedores en SQLite;
- disponibilidad con negativos permitidos y bloqueados;
- conservación de las 15 pruebas del flujo fiscal/ventas anterior.

## Comandos de evidencia

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

Resultado final observado:

- solución Release: 0 errores, 0 advertencias;
- proyecto SQL Release: 0 errores, 0 advertencias;
- fundación: 30/30;
- integración SQL Server/DACPAC: 17/17.

## Fuera de esta rebanada

- interfaz visual POS;
- empaquetado PWA/escritorio;
- Web PubSub o SignalR;
- XML UBL, firma y transmisión DIAN;
- compras, cartera y reportes.

No se agregaron contratos vacíos para esas capacidades.
