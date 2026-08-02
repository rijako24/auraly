# Evidencia del corte a sesiones de trabajo

Fecha de ejecución: 2026-08-02
Rama: `feature/auraly-commerce-work-session-cutover`
Base: `80c4289`

## Resultados

| Validación | Resultado |
|---|---:|
| `dotnet build Auraly.Commerce.sln --configuration Release --no-restore` | 0 errores, 0 advertencias |
| Build `Auraly.Database.sqlproj` | 0 errores, 0 advertencias |
| `Auraly.Foundation.Tests` | 142/142 |
| `Auraly.Pos.Edge.Host.Tests` | 15/15 |
| `Auraly.ServerSlice.IntegrationTests` con SQL Server real | 83/83 |
| TypeScript `--noEmit` | Correcto |
| `npm run test:pos` | 25/25 |
| `npm run build` | Correcto, 48 páginas y `/pos` generado |
| `git diff --check` | Sin errores de whitespace |

`npm run lint` no es automatizable actualmente: `next lint` abre el asistente porque
no existe una configuración ESLint. El build de Next ejecutó la comprobación de tipos.

## Prueba real de actualización SQL

Se desplegó el DACPAC del commit base en SQL Server `\.\LOCAL` sobre una base temporal.
Se sembraron una sede, bodega, caja histórica, dispositivo y serie offline. Después se
publicó el DACPAC nuevo.

La prueba encontró y corrigió tres defectos que no detectaba el build estático:

1. Referencias compiladas antes de crear columnas nuevas en predespliegue.
2. Llaves foráneas históricas que todavía poseían columnas que debían retirarse.
3. Reejecución de la migración después de una publicación interrumpida.

La publicación final con `BlockOnPossibleDataLoss=True` terminó correctamente. Se
comprobó que:

- `CashRegisters`, `CashSessions`, `CashMovements` y `PosDevices` ya no existen.
- El dispositivo histórico existe en `EnrolledDevices` con el tenant correcto.
- La serie histórica conserva prefijo, código, rango y queda asociada al dispositivo.
- No existen columnas `RegisterId`, `CashSessionId` o `CashierShiftId` en el alcance
  comercial migrado.
- `SupervisorCredentials` permanece disponible.
- Una repetición del predespliegue no intenta recompilar ni ejecutar el modelo anterior.

## Cobertura funcional conectada

- Selección segura de sede y bodega para venta online.
- Sesión de trabajo por usuario para online y Edge.
- Equipo opcional solo para operación offline.
- Enrolamiento y autenticación del dispositivo por tenant.
- Login offline durable y recuperación de la sesión local.
- Ventas, borradores, pedidos, devoluciones, inventario y fiscalidad sin `RegisterId`.
- Numeración online de servidor y numeración offline por dispositivo.
- Persistencia SQLite compatible con bases creadas por la versión anterior.
- API, SQL Server, POS Edge y frontend compilados y probados como una sola rebanada.

## Pendiente deliberado

- Configurar ESLint sin asistente interactivo.
- Construir la experiencia visual administrativa de credenciales/autorización de
  supervisor sobre el módulo Authorization. En esta entrega se preservó su dato, pero
  no se presenta como funcionalidad visual terminada.
- Ejecutar el procedimiento de corte con respaldo en un ambiente de ensayo que contenga
  un volumen representativo de datos productivos antes de desplegarlo en producción.
