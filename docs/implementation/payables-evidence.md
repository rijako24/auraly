# Evidencia de cuentas por pagar

Fecha: 2 de agosto de 2026

## Resultado

La rebanada está conectada desde la entrada de mercancía hasta la interfaz, SQL
Server, RabbitMQ y contabilidad. No se considera terminada por existir solamente
el modelo.

La prueba vertical verificó:

1. una entrada a crédito crea la obligación y su movimiento inicial;
2. la consulta está autenticada, paginada y aislada por Business;
3. el detalle conserva origen y movimientos;
4. un abono por transferencia se acepta con numeración `PGP`;
5. el motor contable canónico aplica el pago una sola vez;
6. el saldo queda parcial;
7. existe una sola aplicación y una sola transacción de cartera;
8. existe un solo evento de outbox;
9. existe un solo asiento;
10. el asiento debita proveedores y acredita bancos;
11. el replay devuelve la misma aceptación sin duplicar;
12. un sobrepago concurrentemente reservable es rechazado;
13. permisos y Business incorrectos son rechazados.

La prueba RabbitMQ usa el publicador y consumidor productivos, `prefetch=1`, ack
manual y una cola efímera. Procesó un abono en efectivo y republicó el mismo
mensaje para demostrar efectos exactamente una vez.

## Comandos y resultados ejecutados

```text
dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
0 errores, 0 advertencias; incluye Auraly.Database.dacpac
```

```text
dotnet build tests/Auraly.ServerSlice.IntegrationTests/... --configuration Release
0 errores, 0 advertencias
```

```text
dotnet test tests/Auraly.Foundation.Tests/... --configuration Release
146 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.Pos.Edge.Host.Tests/... --configuration Release
15 aprobadas, 0 fallidas
```

```text
dotnet test tests/Auraly.ServerSlice.IntegrationTests/... --configuration Release
85 aprobadas, 0 fallidas
```

```text
AURALY_REQUIRE_RABBITMQ_TEST=1
dotnet test ... --filter FullyQualifiedName~PayablesRabbitMqIntegrationTests
1 aprobada, 0 fallidas; RabbitMQ 4.1 local real
```

```text
npx tsc --noEmit
aprobado
```

```text
npm run build
aprobado; 48 páginas; /dashboard/payables generada
```

El build de Next.js compiló, verificó tipos y generó las 48 páginas estáticas o
dinámicas correspondientes. La ruta nueva reportó 7,4 kB y formó parte del
artefacto de producción.

`npm run lint` no se registra como aprobado: el repositorio no tiene ESLint
configurado y el comando abre el asistente interactivo de Next.js. Configurarlo
es una decisión transversal separada; no se simuló ni se aceptó automáticamente
una configuración en esta rebanada.

## Hallazgos durante la regresión

La primera ejecución completa encontró que la prueba de cartera reutilizaba el
producto global de ventas. Si ese producto acumulaba inventario negativo, una
recepción posterior podía calcular un costo promedio negativo y violar la
restricción SQL. Esto bloqueaba correctamente el turno documental y hacía fallar
los documentos siguientes.

La prueba se corrigió creando su propio producto, precio y asociación de
proveedor. Así mide cartera sin depender del orden de pruebas. No se alteró el
motor con una regla improvisada. El tratamiento contable del costo promedio al
cubrir existencias negativas queda como decisión requerida antes de completar
la rebanada avanzada de inventario y costos.

También se detectó y corrigió que el procesador contable no incluía la fecha de
`SupplierPayments` al reconstruir la fuente inmutable. La prueba SQL fue la que
impidió declarar una integración incompleta.

## Evidencia visual

El frontend superó la verificación estática y el build de producción. La
aplicación local se levantó en el puerto 3000, pero el controlador externo usado
para la inspección visual no pudo iniciar por un fallo de permisos de su entorno.
No se registra una validación visual interactiva como aprobada en esta ejecución.

## Pendiente deliberado

- selección visual masiva de obligaciones para un mismo pago;
- anticipos y saldos a favor del proveedor;
- reversos y anulaciones mediante documento compensatorio;
- retenciones practicadas;
- conciliación bancaria;
- asignación contable del tercero después de converger Supplier a Party;
- política contable explícita para costo promedio con inventario negativo;
- actualización push de la vista administrativa después de procesamiento
  asíncrono, cuando exista el consumidor canónico correspondiente.
