# Fase 0: línea base y evidencia

**Fecha de ejecución:** 27 de julio de 2026  
**Rama fuente:** `design/auraly-commerce-mvp` (`eedbeb4`)  
**Rama de implementación:** `feature/auraly-commerce-foundation`

## Protección del trabajo existente

El árbol de trabajo activo contenía modificaciones y archivos no rastreados del
usuario. La implementación se preparó desde los commits confirmados de la rama
de diseño mediante un árbol de trabajo y un índice Git aislados. No se ejecutó
`reset`, `checkout` destructivo ni `stash`, y ningún archivo modificado por el
usuario se incorporó a los commits de Auraly Commerce.

## Versiones encontradas

| Componente | Versión del repositorio o entorno |
|---|---|
| SDK .NET fijado | 8.0.405 |
| Target framework actual | .NET 8 |
| Azure Functions | v4, proceso aislado |
| Node ejecutado | 20.11.0 |
| Next.js actual | 14.2.21 |
| TypeScript | 5 |
| SQL project SDK | `Microsoft.Build.Sql` 0.1.10-preview |
| Destino SQL | Azure SQL Database v12 |

.NET 8, Node 20 y Next.js 14 requieren una actualización de plataforma antes de
producción. La actualización debe realizarse como una rebanada de ingeniería
separada y verificable; no se cambian targets sin disponer del SDK y runtime
correspondientes en CI, SaaS y on-premise.

## Línea base anterior a la implementación

Ejecutado sobre una copia limpia de `design/auraly-commerce-mvp`:

| Comando | Resultado |
|---|---|
| `dotnet build Auraly.sln --configuration Release` | Correcto, 0 errores, 0 advertencias |
| `dotnet test src/Tests/Auraly.Tests/Auraly.Tests.csproj --configuration Release` | Correcto, 693/693 |
| `dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release` | Correcto, DACPAC generado |
| `npm ci` en `admin` | Correcto con advertencias de motor y dependencias obsoletas |
| `npx tsc --noEmit` en `admin` | Correcto |
| `npm run lint` en `admin` | No automatizable: Next.js solicita configurar ESLint interactivamente |
| `npm run build` en `admin` | El proceso agotó 242 segundos; produjo artefactos, pero no se registra como exitoso |

La prueba original no estaba incluida en la solución anterior y requirió
restaurar/ejecutar su `.csproj` explícitamente.

## Fundación modular Auraly

Comandos:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj `
  --configuration Release --no-build
```

Resultado:

- 22 proyectos canónicos en la solución;
- build Release: 0 errores, 0 advertencias;
- pruebas: 21 correctas, 0 fallidas;
- reglas de arquitectura y conectividad activas;
- ningún proyecto canónico fuera de la solución o sin consumidor;
- búsqueda automatizada de nombres legacy dentro del alcance nuevo.

## Alcance conectado y comprobado

- UUIDv7 generado sin conexión;
- identificadores tipados de tenant, empresa, sede, bodega, caja, usuario,
  producto y documento;
- auditoría, idempotencia y outbox con reintentos;
- producto mínimo, múltiples códigos de barras y configuración de balanza;
- proyección del producto al contrato de catálogo POS;
- relación caja-bodega sin duplicar la política de negativos en caja;
- proyección del contexto de caja con la política heredada de la bodega;
- autorización backend para crear ventas y aplicar descuentos;
- serie fiscal exclusiva por caja, vigencia, rango y consumo concurrente;
- asignación fiscal consumida por el caso de uso de venta;
- snapshot fiscal inmutable;
- construcción canónica y cálculo SHA-384 de CUFE;
- payload textual para QR;
- confirmación offline que conecta catálogo, organización, permisos, fiscal,
  venta y outbox;
- motor de documentos con recibo idempotente;
- pruebas de dependencias permitidas entre módulos.

## Límites de la evidencia

Esta fundación todavía no demuestra:

- persistencia SQL o SQLite;
- sincronización paginada/incremental de catálogo;
- POS web/desktop;
- impresión física;
- XML UBL, firma o transmisión DIAN;
- cumplimiento de contingencia;
- rebanada E2E offline completa con reinicio de proceso.

Estos puntos pertenecen a las siguientes entregas de la rebanada vertical y no
se consideran terminados por la existencia de interfaces o modelos.

