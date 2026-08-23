# Certificación de integridad de motores: operación, contabilidad y reporting

Fecha: 2026-08-22
Estado: evidencia local aprobada; certificación DEV y cierre arquitectónico pendientes

## Suite permanente y reproducible

La certificación dejó de ser una corrida manual. El comando canónico es:

```powershell
./scripts/Test-AuralyEngines.ps1
```

El script compila el DACPAC y la solución, crea una base SQL Server real por
suite, ejecuta los contratos de arquitectura y los motores operacional,
contable, reporting y extremo a extremo, y guarda un TRX independiente por
grupo bajo `artifacts/test-results/engine-certification`.

Para aislar un motor durante desarrollo:

```powershell
./scripts/Test-AuralyEngines.ps1 -Suite Operational
./scripts/Test-AuralyEngines.ps1 -Suite Accounting
./scripts/Test-AuralyEngines.ps1 -Suite Fiscal
./scripts/Test-AuralyEngines.ps1 -Suite Reporting
./scripts/Test-AuralyEngines.ps1 -Suite EndToEnd
./scripts/Test-AuralyEngines.ps1 -Suite Architecture
```

Los casos se seleccionan por el trait xUnit `EngineCertification`; agregar un
nuevo tipo documental obliga a incorporarlo al grupo propietario y, si cruza
motores, también a `EndToEnd`. La ejecución `All` incluye además
`Auraly.Foundation.Tests`, donde viven los ratchets de escritores únicos,
transacciones compartidas y deuda de acceso a datos.

## Veredicto

La regresión funcional sobre SQL Server real y el nuevo escenario integrado de
diez ventas son correctos. Todavía no se puede declarar que los motores están
certificados al 100 % porque la auditoría encontró tres desviaciones de la
arquitectura acordada y la corrida remota sobre DEV requiere una sesión de demo
y conciliación de solo lectura contra su base o telemetría.

Esta distinción es obligatoria: una prueba local con base efímera demuestra el
comportamiento del código y del esquema, pero no demuestra el transporte, la
configuración ni el release que está desplegado en DEV.

## Evidencia automatizada local

La base se crea desde el DACPAC en una instancia SQL Server real y se elimina al
terminar. No se sustituyeron repositorios ni transacciones por mocks.

Corrida canónica final del 2026-08-23:

| Grupo `EngineCertification` | Resultado |
| --- | ---: |
| Architecture | 1/1 |
| Operational | 15/15 |
| Accounting | 7/7 |
| Fiscal | 8/8 |
| Reporting | 1/1 |
| EndToEnd | 3/3 |
| Ratchets de código y arquitectura | 233/233 |

No hubo pruebas omitidas. El build del DACPAC y de la solución terminó con cero
errores y cero advertencias. En la corrida final, el lote de diez ventas observó
máximos de `121,454 ms` operacional, `129,646 ms` contable y `153,791 ms` de
reporting, todos por debajo del presupuesto local de 2.000 ms.

| Suite o escenario | Resultado | Duración observada |
| --- | ---: | ---: |
| Server slice completo | 148/148 | 4 min 02 s |
| Server slice final, con lote integrado incluido | 148/148 | 2 min 46 s |
| Platform | 744/744 | 11 s |
| Foundation | 232/232 | 10 s |
| Flujo contable y comercial integrado, incluido lote de 10 | 1/1 | 27,538 s dentro de la prueba |

Archivos TRX:

- `artifacts/test-results/engine-certification-demo-20260822/engine-certification-demo-20260822.trx`
- `artifacts/test-results/engine-certification-demo-20260822/engine-code-audit-platform.trx`
- `artifacts/test-results/engine-certification-demo-20260822/engine-code-audit-foundation.trx`
- `artifacts/test-results/engine-certification-demo-20260822/engine-full-pipeline-10-local-with-timings.trx`
- `artifacts/test-results/engine-certification-demo-20260822/engine-certification-after-pipeline-10.trx`

## Flujo integrado de diez ventas

La prueba `AccountingVerticalSliceTests` crea diez facturas confirmadas y usa el
mismo `DocumentId` como llave de conciliación entre los tres motores. Exige:

- diez `DocumentProcessingJobs` terminados;
- diez `InventoryMovements`, cada uno por `-1`, encadenados sin saltos entre
  `QuantityBefore` y `QuantityAfter`;
- disminución final exacta de `10` unidades en `InventoryBalances`;
- diez `AccountingPostingJobs` publicados y diez `AccountingEntries`;
- cero asientos desbalanceados;
- débito acumulado de caja por `119.000`, crédito de ventas por `100.000` y
  crédito de IVA generado por `19.000`;
- diez `SalesReportDocuments` y diez `SalesReportLineFacts`;
- cantidad reportada `10` y venta total reportada `119.000`;
- incremento exacto del consolidado diario por producto;
- replay de las diez facturas sin cambiar ninguna de las magnitudes anteriores.

Máximos del lote local, medidos desde las columnas durables de cada motor:

| Motor | Máximo |
| --- | ---: |
| Operacional SQL | 188,339 ms |
| Contable, desde creación del job hasta contabilización | 241,779 ms |
| Reporting, desde cierre operacional hasta proyección | 236,188 ms |

Los tres valores están por debajo del umbral local de 2.000 ms. El tiempo de
reporting incluye el intervalo posterior al cierre operacional y por ello es
una medida extremo a extremo más exigente que el tiempo interno del `INSERT`.
En la corrida final completa los máximos fueron, respectivamente, `109,089 ms`,
`113,357 ms` y `122,260 ms`; se conservan arriba los valores mayores de la
corrida dirigida como cota observada más conservadora.

## Acumulación contable por código

La suite contable incluye además un lote de 30 documentos: diez ventas a
crédito de base `$100.000` e IVA `$19.000`, diez devoluciones de base `$10.000`
e IVA `$1.900`, y diez notas crédito financieras de `$7.100`. La aritmética
esperada, verificada directamente sobre líneas del mayor, es:

| Código | Débito | Crédito | Saldo del lote |
| --- | ---: | ---: | ---: |
| `130505` Clientes | $1.190.000 | $190.000 | $1.000.000 débito |
| `413595` Ingresos por ventas | $0 | $1.000.000 | $1.000.000 crédito |
| `240805` IVA generado | $19.000 | $190.000 | $171.000 crédito |
| `417595` Devoluciones en ventas | $171.000 | $0 | $171.000 débito |
| `143505` Inventarios | costo devuelto | costo vendido | espejo de `613595` |
| `613595` Costo de ventas | costo vendido | costo devuelto | espejo de `143505` |
| Total de los 30 asientos | $1.380.000 + débitos de costo/inventario | igual al débito | $0 |

La suma de las diez cuentas por cobrar debe terminar exactamente en
`$1.000.000`. También se exigen 30 fuentes, 30 jobs y 30 asientos, cero asientos
desbalanceados, solo 20 jobs operacionales, 20 movimientos de inventario y 20
jobs de reporting. El lote completo se reproduce y todas las magnitudes deben
permanecer idénticas.

## Cobertura funcional comprobada

Inventario:

- venta secuencial e idempotente;
- entrada de mercancía;
- conteo de una y varias líneas;
- ajuste de entrada y salida;
- traslado y reverso;
- conversión y reverso;
- avería y autorización;
- rechazo de conversión sin existencia;
- devolución de venta;
- devolución a proveedor.

Contabilidad y submayores:

- venta de contado y a crédito;
- CxC y CxP;
- recibos y pagos;
- entrada de mercancía con IVA y retenciones;
- gasto;
- devolución de venta;
- devolución a proveedor;
- nota débito y nota crédito de cliente y proveedor;
- comprobante manual;
- periodos y reportes financieros;
- idempotencia de reintentos.

Reporting:

- documento, detalle y pago proyectados;
- consolidado diario;
- filtros y desgloses sin joins a tablas operacionales durante la consulta;
- ventas y devoluciones firmadas;
- replay sin duplicar hechos ni totales.

## Auditoría transaccional

### Lo que sí cumple

- El procesamiento operacional exitoso conserva una transacción SQL
  `Serializable` desde la toma del job hasta kardex, saldo, efectos del documento,
  outbox, jobs derivados y marca `Completed`.
- Los nueve handlers operacionales usan la conexión y transacción de la sesión
  compartida; no abren una transacción propia.
- El procesador contable contabiliza efectos financieros, asiento, líneas y
  estado del job dentro de una transacción SQL `Serializable`.
- El procesador de reporting escribe todas las proyecciones y el checkpoint
  dentro de una transacción SQL `Serializable`.
- Service Bus completa el mensaje y RabbitMQ hace `ack` solamente después de que
  el procesador termina.

### Desviación: SQL y broker no forman una sola transacción

Después del commit operacional, el consumidor publica fiscal, contabilidad y
reporting y luego confirma el mensaje original. Un fallo entre esos pasos se
recupera mediante redelivery e idempotencia, pero no existe una transacción
atómica SQL + broker. Además, un fallo operacional revierte la transacción de
negocio y abre otra transacción para guardar reintento o intervención.

Por tanto, la afirmación estricta «un mensaje, una sola transacción desde el
broker hasta todos los destinos» no se cumple. El patrón reproducible para
cerrarlo es outbox transaccional: la primera transacción solo guarda efectos y
mensajes de salida; un despachador publica la outbox con identidad estable; los
consumidores destino conservan su propia transacción e idempotencia.

## Una tabla de procesamiento por motor

| Motor | Tabla durable de trabajo | Resultado |
| --- | --- | --- |
| Operacional | `dbo.DocumentProcessingJobs` | Cumple |
| Contable | `dbo.AccountingPostingJobs` | Cumple |
| Fiscal | `dbo.FiscalDocumentProcesses` | Cumple |
| Reporting | `reporting.SalesReportingJobs` | Cumple |

`DocumentProcessingPayloads` es payload inmutable y
`BusinessProcessingCursors` es secuenciación, no son segundas colas de trabajo.
El checkpoint agregado es un cursor de lectura y no una segunda cola. Cada
documento proyectable tiene ahora una fila en `reporting.SalesReportingJobs`
con estado, intento, error, hash y tiempos. La proyección y la transición final
del job se confirman en la misma transacción serializable.

## Documentos exclusivamente contables

La regla acordada es:

```text
API financiera -> cola contable -> transacción contable -> CxC/CxP y asiento
```

Una nota débito/crédito, un comprobante manual o un efecto financiero sin
existencias no debe entrar a la cola operacional ni alterar su cursor.

Los ajustes de cuenta y comprobantes manuales ya guardan su fuente en
`AccountingSourceDocuments` y su único trabajo en `AccountingPostingJobs`; no
crean `DocumentProcessingJobs`, no insertan `DocumentProcessingPayloads` y no
avanzan `BusinessProcessingCursors`. El FK del job contable hacia el job
operacional fue eliminado. La suite comprueba esta ausencia de forma explícita.

Pagos, gastos y movimientos de caja conservan por ahora su aceptación histórica
por el carril documental. Aunque no alteran el kardex, todavía deben migrarse al
mismo origen contable neutral antes de declarar cerrada toda la frontera de
documentos exclusivamente financieros.

## Acceso a datos

No se encontró Entity Framework en los caminos críticos de los tres motores.
Sin embargo, el requisito «sin query quemada ni stored procedures» no se cumple:

- operación contiene decenas de `SqlCommand` y bloques SQL embebidos;
- contabilidad contiene decenas de `SqlCommand` y bloques SQL embebidos;
- reporting contiene trece comandos y trece bloques SQL embebidos;
- el proyecto de base contiene 25 stored procedures;
- el motor de inventario consume procedimientos de búsqueda, secuencia, payload
  y cierre documental.

Eliminar EF no equivale a eliminar SQL embebido. Antes de cambiarlo debe
cerrarse una política única: SQL en archivos/procedimientos versionados o un
repositorio de consultas tipadas. Mezclar ambos sin regla aumenta la deriva y
dificulta auditar transacciones.

## DEV

La salud pública de `api-auraly-dev-w5usmo6w.azurewebsites.net/health` respondió
`200 Healthy`. Esto no certifica motores. El cierre remoto exige:

1. iniciar sesión con el negocio demo;
2. registrar identificadores y saldos anteriores;
3. ejecutar el lote por la API de DEV;
4. esperar las colas reales de DEV;
5. conciliar jobs, movimientos, asientos, hechos y acumulados con acceso de solo
   lectura o telemetría correlacionada;
6. repetir exactamente los mismos mensajes;
7. comprobar que todas las cardinalidades y saldos permanecen invariables;
8. registrar tiempos de cola, procesamiento y extremo a extremo por motor.

DEV usa Service Bus según el contrato de despliegue. RabbitMQ corresponde al
perfil on-premise y debe certificarse separadamente. Las pruebas RabbitMQ de la
corrida actual se omitieron porque no estaba definida `AURALY_TEST_RABBITMQ`;
un test omitido por configuración no constituye evidencia aprobatoria.

## Puerta de cierre

No declarar «100 % certificado» hasta completar, en este orden:

1. migrar pagos, gastos y caja exclusivamente financieros al origen contable;
2. aplicar el patrón outbox para la frontera SQL/broker;
3. cerrar la política de SQL embebido y stored procedures;
4. aprobar el mismo lote integrado en DEV con Service Bus y conciliación;
5. aprobar el perfil on-premise con RabbitMQ real;
6. ejecutar `Test-AuralyEngines.ps1` y conservar los TRX del release definitivo.
