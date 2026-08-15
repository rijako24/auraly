# Evidencia de la segunda rebanada

Fecha de ejecución: 2026-07-27

Rama aislada: `feature/auraly-commerce-server-slice`

Base: `a1eed12aeebc47bf3a7f37e2d2c372e67af691fc`

## Aislamiento

La implementación se realizó en
`<workspace>\.tmp\auraly-edit` con índice Git separado. El
workspace principal permaneció en su rama original, con sus cambios locales
intactos; no se hizo `reset`, `stash` ni cambio de rama en ese workspace.

## Resultados

| Verificación | Resultado |
|---|---:|
| Build Release de `Auraly.Commerce.sln` | 0 errores, 0 advertencias |
| Pruebas de fundación | 27 aprobadas |
| Pruebas de integración servidor | 15 aprobadas |
| Build Release de `Auraly.Database.sqlproj` | 0 errores, 0 advertencias |
| Despliegue de DACPAC en SQL Server real | aprobado |
| Tablas Commerce desplegadas | 14 |

La instancia usada fue SQL Server Express 14.0 en `.\LOCAL`, con autenticación
integrada. El fixture creó una base con UUID, desplegó el DACPAC completo,
ejecutó pre/postdeployment, sembró un contexto aislado y eliminó únicamente esa
base al finalizar.

## Cobertura comprobada

- API autenticada y permiso backend.
- Denegación por permiso.
- Denegación por tenant o contexto de caja incorrecto.
- Igualdad del CUFE de POS Edge y servidor.
- `FiscalIntegrityConflict` al alterar número, fecha, cliente, cantidad,
  precio, descuento, impuesto, total, prefijo o autorización.
- Dos cargas concurrentes del mismo `DocumentId`.
- Un solo documento, pago, movimiento y evento servidor.
- Repetición exacta sin efectos adicionales.
- SQLite físico, cierre, reapertura y conservación de dos ventas.
- Timeout con backoff y conservación del pendiente.
- Respuesta sin recibo durable que no completa la outbox.
- Conflicto fiscal sin pérdida ni renumeración local.
- Actualización de una base SQLite del esquema anterior sin perder serie ni
  siguiente consecutivo.
- Proyectos canónicos presentes en la solución y con consumidores reales.
- Ausencia de referencias legacy y componentes vacíos en el alcance nuevo.

## E2E principal

`Physical_sqlite_restarts_uploads_once_and_preserves_conflict` demuestra:

1. creación de SQLite físico;
2. provisión de serie exclusiva;
3. emisión de dos facturas offline;
4. reinicio de POS Edge;
5. upload HTTP real al host de pruebas;
6. mismo CUFE recibido y recalculado;
7. una venta, un pago, un movimiento y un evento;
8. repetición reconocida como `AlreadyProcessed`;
9. alteración de una copia del segundo snapshot;
10. `FiscalIntegrityConflict` durable;
11. cero pago y cero movimiento para el documento alterado;
12. conservación del payload y factura local.

## Comandos de evidencia

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

## Pendiente deliberado

No se afirma cumplimiento DIAN en esta entrega. Permanecen fuera del alcance el
XML UBL, firma digital, certificado, transmisión, estados DIAN reales, UI POS,
impresión física y sincronización completa del catálogo.

