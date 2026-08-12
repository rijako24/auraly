# Evidencia: inventario administrativo y averías

Fecha: 2026-08-09

## Resultado comprobado

La prueba `Damage_updates_inventory_once_and_queries_respect_cost_permission` despliega el DACPAC en SQL Server real y ejecuta:

1. saldo inicial de 10 unidades a costo promedio 5;
2. confirmación de una avería de 3 unidades;
3. reenvío del mismo `DocumentId` e `Idempotency-Key`;
4. verificación de saldo 7, costo promedio 5 y valorización 35;
5. verificación de un único `InventoryDamage`, una única outbox y un único procesamiento;
6. consulta autenticada de existencias y kárdex;
7. ocultamiento de costo y valorización sin `inventory.costs.read`.

Resultado: 1/1 aprobada. La prueba explícita `RabbitMqDocumentProcessingTests` también pasó contra el contenedor RabbitMQ real, validando orden, efectos únicos y dead letter sin polling. En la ejecución conjunta previa de la clase, los dos escenarios existentes de conteo, ajuste, traslado, conversión, rollback, reintento y dead letter también aprobaron.

## Builds

- `dotnet build src/API/Auraly.Api/Auraly.Api.csproj --configuration Release --no-restore`: 0 errores, 0 advertencias.
- `dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --no-restore`: 0 errores, 0 advertencias.
- `npx tsc --noEmit`: aprobado.
- `npm run build`: aprobado; UTF-8 verificado, compilación optimizada correcta y 57 páginas generadas, incluida `/dashboard/inventory`.

## Base local y servicios

El primer publish local detectó siete columnas preexistentes ausentes del SQL project y se detuvo por posible pérdida de datos. Las columnas se incorporaron al proyecto, el DACPAC se reconstruyó y después se publicó satisfactoriamente en `MimosBabySpa` con postdeployment completo. La API queda en `http://127.0.0.1:5097`, el admin en `http://127.0.0.1:3000` y RabbitMQ real procesa las señales sin polling.

La inspección visual automatizada no pudo iniciarse porque el controlador del navegador fue bloqueado por las ACL del entorno. La ruta sí está incluida en TypeScript, el servidor Next está activo y el acceso queda disponible para prueba manual autenticada.
## Evidencia de cierre de operaciones

- `npx tsc --noEmit`: correcto.
- `npm run check:encoding`: correcto; fuentes UTF-8 verificadas.
- `dotnet build Auraly.Commerce.sln --configuration Release --no-restore`: correcto, 0 errores y 0 advertencias.
- `InventoryOperationsVerticalSliceTests`: 3/3 correctas con SQL Server real.
- Consulta paginada de productos probada con saldo y costo; el costo se oculta sin permiso.
- `npm run build`: correcto; 57/57 páginas generadas y `/dashboard/inventory` incluida.

Capacidades conectadas: búsqueda de productos por bodega, conteo en dos pasos, ajuste, traslado, conversión, avería, historial, kárdex, permisos, idempotencia y procesamiento por el motor. No se agregó sondeo.