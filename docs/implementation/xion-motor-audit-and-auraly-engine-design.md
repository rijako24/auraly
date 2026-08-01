# Auditoría del motor de Xion y diseño definitivo del motor Auraly

**Fecha:** 31 de julio de 2026  
**Estado:** auditoría histórica; sus propuestas de implementación fueron
reemplazadas por `docs/decision-motor-documental-ordenado-y-efectos-intrinsecos.md`.
No usar este archivo para implementar transporte, orden, polling, drenado,
recibos ni manejo de errores.
**Alcance:** Auraly Commerce SaaS y on-premise

## 1. Resultado ejecutivo

Auraly tendrá un solo motor lógico de documentos. Todos los documentos definitivos que alteren el estado operativo de un negocio se registran en una cola durable y se procesan estrictamente uno por uno dentro de su `BusinessId`.

Negocios distintos pueden avanzar en paralelo. Un único error crítico bloquea solamente el negocio afectado y no permite saltar al documento siguiente. No habrá una cola global para todos los clientes ni motores independientes que puedan aplicar inventario, caja o cartera fuera de orden.

El motor completo tiene dos etapas conectadas:

1. **Publicación operativa crítica:** documento, inventario, costo, pagos, caja, cartera, resúmenes tributarios y vínculos operativos se confirman atómicamente.
2. **Procesamiento derivado durable:** contabilidad, fiscal, estadísticas, consolidaciones, reportes, integraciones y notificaciones consumen eventos creados por la etapa crítica.

Ambas etapas pertenecen al motor Auraly. La separación no deja componentes desconectados: evita que recalcular un informe o esperar a la DIAN mantenga bloqueada la transacción que protege el inventario.

## 2. Qué hace Xion actualmente

### 2.1 Motor del servidor

`MovimientoxProcesar` registra el tipo, documento, empresa, sucursal, bodega, equipo, usuario y estados de procesamiento. `MotorService.GetObtenerMovimientoxProcesar()` toma el primer registro no procesado ordenado por `Id`.

El formulario `MotorPrincipal/Formulario/FrmMotor.cs` ejecuta un ciclo continuo. Procesa el primer pendiente, vuelve a consultar el primero y continúa hasta vaciar la cola. Si el documento de cabecera falla, permanece pendiente y vuelve a ser el primero: en la práctica bloquea los posteriores.

Para una factura, `MotorService.ProcesarDocumentos` coordina dentro de una transacción:

- kardex y existencias;
- puntos;
- cartera;
- bonos;
- arqueo;
- última compra del cliente;
- estados del documento y de la cola;
- estadísticas y rotación.

Para una entrada de mercancía coordina:

- kardex y existencias;
- costos y productos pendientes del proveedor;
- cuenta por pagar;
- rotación y última compra;
- estadísticas de compra;
- estados del documento y de la cola.

Inventarios, traslados, averías y conversiones también modifican existencias mediante el mismo servicio central de kardex.

### 2.2 Motor local de caja

`SMovimientoxProcesar` diferencia `ProcesadaLocal` y `ProcesadaServidor`. El motor local toma primero el menor `Id` no procesado localmente y después el menor `Id` procesado localmente pero pendiente de servidor.

Esto conserva orden local y de carga, pero depende de un proceso Windows Forms que sondea conexión, cola y cambios aproximadamente cada 500 ms.

### 2.3 Capacidades que se conservan

- Documento y trabajo pendiente se registran de manera durable.
- La unidad de proceso es el documento completo.
- Existe un único orden para los efectos operativos.
- El documento no se marca procesado antes de completar sus efectos.
- Inventario, cartera, caja y estadísticas nacen del documento, no de acciones manuales separadas.
- Una falla de cabecera impide adelantar documentos dependientes.

### 2.4 Problemas que no se migran

- Worker acoplado a Windows Forms.
- Sondeo fijo de alta frecuencia como único mecanismo de activación.
- Orden global que puede bloquear negocios independientes.
- `Id` numérico como orden implícito y sin secuencia explícita por negocio.
- Estadísticas y procedimientos pesados dentro de la transacción crítica.
- Consultas y `switch` central que conocen las tablas de todos los documentos.
- Ausencia de leases robustos para múltiples instancias.
- Clasificación limitada de errores, reintentos e intervención.
- Acoplamiento entre sincronización de caja y procesamiento del servidor.

## 3. Topología definitiva de Auraly

```text
API / POS Edge / módulos web
          |
          v
validar y congelar documento
          |
          v
DocumentProcessingJobs (secuencia por BusinessId)
          |
          v
Coordinador crítico por negocio
          |
          +--> manejador Venta
          +--> manejador Entrada de mercancía
          +--> manejador Conteo/Ajuste
          +--> manejador Traslado
          +--> manejador Conversión
          +--> manejador Avería
          +--> manejador Devolución/Nota crédito
          +--> manejador Caja/Tesorería
          |
          v
commit único de efectos críticos + outbox
          |
          +--> carril fiscal
          +--> carril contable
          +--> carril analítico y de reportes
          +--> integraciones y notificaciones
```

No se crean motores funcionales separados. Los manejadores son extensiones del mismo motor y solo pueden escribir mediante los contratos públicos de los módulos propietarios.

## 4. Orden y concurrencia

Al aceptar un documento, el servidor asigna `ProcessingSequence` dentro de su `BusinessId`. El motor solamente adquiere:

```text
ProcessingSequence = LastCompletedSequence + 1
```

Reglas:

- máximo un documento crítico en ejecución por negocio;
- varios negocios pueden ejecutarse simultáneamente;
- el orden es el de aceptación durable del servidor;
- la fecha comercial o la fecha local no reordena efectos ya publicados;
- un documento offline recibe secuencia al ser aceptado por el servidor;
- documentos duplicados reutilizan el mismo trabajo y resultado;
- `NeedsIntervention` no avanza el cursor;
- no existe una operación genérica para omitir un documento.

Una cola única global degradaría todo el SaaS: un dato defectuoso de un cliente detendría a todos. El aislamiento correcto es secuencial por `BusinessId` y paralelo entre negocios.

## 5. Contrato de ingreso genérico

Antes de agregar entradas, inventarios o conversiones se debe desacoplar la fuente de trabajo de `SalesDocuments` y `FiscalSnapshots`.

Cada documento confirmado debe tener un sobre inmutable común:

- `DocumentId`;
- `TenantId` derivable y validado desde el negocio;
- `BusinessId`;
- `DocumentType`;
- `DocumentVersion`;
- `OccurredAt`;
- `AcceptedAt`;
- `SourceMode`;
- `PayloadReference` o payload canónico;
- `PayloadHash`;
- `ProcessingSequence`;
- estado operativo y trazabilidad.

El sobre no es una tabla gigante con los campos de todos los documentos. Cada módulo conserva sus tablas y snapshot canónico. Un cargador registrado por `DocumentType` reconstruye el comando inmutable que consume su manejador.

`SqlDocumentProcessingWorkSource` no puede continuar unido exclusivamente a ventas y snapshots fiscales cuando se incorporen otros documentos.

## 6. Transacción operativa crítica

El manejador procesa el documento completo en la misma transacción SQL que mantiene el turno del negocio. Según el tipo documental incluye solamente los efectos autoritativos que deben ser indivisibles:

- estado y líneas definitivas del documento;
- saldo y kardex de inventario;
- fotografías de cantidad y costo anterior/posterior;
- costo promedio y costo reconocido;
- pagos y movimiento de caja/tesorería;
- obligación o aplicación de cartera por cobrar o pagar;
- resumen de impuestos por tarifa y naturaleza;
- vínculo con pedido, devolución, traslado o documento origen;
- asiento pendiente contable con datos fuente inmutables;
- eventos de outbox para todos los carriles derivados;
- movimiento idempotente, estado del trabajo y cursor del negocio.

Si cualquiera de estos efectos falla, se revierte todo el documento y no avanza el cursor.

## 7. Inventario, valor y tipos de documento

Todos los documentos que cambian existencias usan el mismo kernel de inventario:

```text
BusinessId + WarehouseId + ProductId
```

El kernel bloquea saldos en orden canónico y produce movimientos inmutables. Los manejadores expresan la intención; no editan el saldo directamente.

| Documento | Efecto mínimo |
|---|---|
| Venta | salida, costo de venta congelado |
| Entrada de mercancía | entrada, nuevo promedio, costo de proveedor y cuenta por pagar |
| Devolución de venta / nota crédito | entrada física cuando corresponda y reversión financiera vinculada |
| Devolución de compra | salida y reversión de cuenta por pagar |
| Conteo | diferencia contra la base del conteo, nunca reemplazo ciego |
| Traslado | salida y entrada atómicas entre bodegas |
| Conversión | consumos y productos resultantes, valor y merma explícitos |
| Avería | traslado o salida según disposición configurada |

El saldo materializado permite disponibilidad rápida; el kardex sigue siendo la evidencia conciliable. Cantidad y valor deben poder reconstruirse desde movimientos.

## 8. Carriles derivados del mismo motor

### 8.1 Contabilidad

Consume una solicitud contable durable generada en el commit crítico. Crea asientos balanceados, por periodo, entidad legal y centro de costo. Una configuración faltante no reaplica inventario: deja el trabajo contable bloqueado, impide cerrar el periodo y muestra una alerta accionable.

### 8.2 Fiscal

Consume el snapshot fiscal ya verificado. Genera, firma y transmite sin modificar documento, número ni CUFE. Un fallo DIAN no revierte una venta ya publicada ni bloquea la cola operativa.

### 8.3 Estadísticas, consolidaciones y reportes

Consume eventos inmutables como `SalePosted`, `PurchasePosted`, `InventoryAdjusted` y `PaymentPosted`. Actualiza proyecciones idempotentes para ventas, compras, utilidad, impuestos, rotación, arqueos y cartera.

Las proyecciones pueden reconstruirse desde las fuentes autoritativas. Un error en una proyección detiene su cursor y genera alerta, pero no revierte ni repite el documento operativo. Esta es la mejora deliberada frente a `APL_ConsolidarEstadistica` dentro de la transacción de Xion.

Los informes operativos que requieren consistencia inmediata pueden leer las tablas autoritativas. Las vistas consolidadas deben mostrar su `ProjectedThroughSequence` para evidenciar si están al día.

### 8.4 Integraciones y notificaciones

Publican cambios de catálogo, estados fiscales y mensajes externos desde outbox. Una notificación solo despierta consumidores; SQL Server conserva la autoridad durable.

## 9. Manejo de errores

### Error crítico antes del commit

- rollback completo;
- `RetryScheduled` si es técnico y recuperable;
- `NeedsIntervention` después del umbral o ante error determinístico;
- cursor del negocio sin avanzar;
- documentos posteriores del mismo negocio bloqueados;
- otros negocios continúan.

### Error de carril derivado

- no toca el cursor operativo ya completado;
- no reaplica inventario, caja ni cartera;
- conserva su propio cursor, intentos y último error;
- bloquea el cierre o la función que requiere esa proyección cuando corresponda;
- permite reconstrucción idempotente.

Esta distinción no permite ignorar errores: cambia el alcance correcto del bloqueo.

## 10. Activación y despliegue

El motor se aloja inicialmente como workers de `Auraly.Api`, pero su núcleo no depende del host. Puede ejecutarse en:

- proceso .NET en SaaS;
- Azure Functions o WebJob;
- servicio Windows;
- contenedor on-premise.

Cada documento publica un mensaje durable. El consumidor procesa exclusivamente el `MovementId` recibido. No existe escaneo SQL, Timer Function, polling ni drenado de pendientes.

## 11. Orden de implementación

1. Generalizar el sobre y la fuente de trabajos, actualmente acoplados a ventas.
2. Completar kernel de saldo, kardex y costo con venta como primer consumidor real.
3. Implementar entrada de mercancía y cuenta por pagar sobre el mismo kernel.
4. Implementar conteos, traslados, conversiones, averías y devoluciones.
5. Implementar solicitudes y worker contable con centros de costo.
6. Implementar proyecciones de estadísticas e informes con cursor reconstruible.
7. Completar nota crédito fiscal y, solamente con caso de uso confirmado, nota débito.

No se construirá una proyección, interfaz o tabla sin un documento productor y un consumidor probado.

## 12. Pruebas de aceptación del motor

Además de las pruebas existentes, cada manejador debe demostrar con SQL Server real:

- documento, trabajo y secuencia atómicos;
- orden estricto por negocio;
- paralelismo seguro entre negocios;
- documento multilínea sin efectos parciales;
- fallo en cualquier línea con rollback completo;
- reinicio con trabajo pendiente;
- lease vencido y recuperación;
- idempotencia después de timeout;
- mismo documento concurrente una sola vez;
- saldo, kardex, costo, pago y cartera sin duplicados;
- error crítico bloqueando el siguiente documento;
- evento derivado creado dentro del mismo commit;
- error derivado sin reaplicar efectos críticos;
- reconstrucción de estadísticas desde eventos;
- conciliación de saldos, movimientos, valor y contabilidad.

## 13. Decisión sobre el trabajo actual

Las tablas `BusinessProcessingCursors` y `DocumentProcessingJobs` y el bloqueo transaccional existente son una base válida. Antes de conectar entradas y conversiones se debe corregir el acoplamiento de `SqlDocumentProcessingWorkSource` con `SalesDocuments` y `FiscalSnapshots`.

`InventoryBalances` y las fotografías de costo/movimiento son compatibles con este diseño, pero solo se consideran implementadas cuando el manejador de venta las actualice en la misma transacción y las pruebas verifiquen cantidad, valor, idempotencia y orden.
