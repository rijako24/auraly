# Cuentas bancarias y conciliación mínima

**Estado:** diseño funcional aprobado como extensión del módulo contable
**Fecha:** 2026-09-01

## Decisión

La cuenta bancaria es un maestro contable del tenant. Cada cuenta enlaza un
único auxiliar activo y contabilizable del PUC. La cuenta principal sirve solo
como valor inicial en la captura: el usuario puede cambiarla en cada operación.

Una venta por transferencia conserva cuenta, referencia, nota y valor. Una
devolución por transferencia conserva la misma evidencia y acredita la cuenta
seleccionada. Si `AccountingTenantSettings.Status` no es `Ready`, cuenta no se
muestra ni se exige; referencia sí es obligatoria y nota es opcional. La
activación posterior no reasigna operaciones históricas ni inventa una cuenta.

La conciliación pertenece al módulo Accounting. No modifica ventas,
devoluciones o extractos y no escribe directamente en `AccountingEntries`. Los
ajustes confirmados entran como documentos inmutables al procesador contable
canónico y reutilizan sus periodos, validación de balance, idempotencia,
numeración, observabilidad y writer.

## Periodicidad

La frecuencia inicial es mensual, alineada con el extracto y el periodo
contable. No se codifica una supuesta obligación trimestral general.

- se puede importar y trabajar cualquier rango de fechas;
- solo se cierra una conciliación que cubra un periodo sin solapamientos;
- un tablero puede advertir cuentas cuyo último mes sigue abierto;
- no hay timer, cierre automático ni asiento automático por antigüedad;
- una reapertura exige permiso, motivo y auditoría.

## Alcance funcional inicial

1. Seleccionar cuenta y periodo.
2. Importar CSV o XLSX mediante un perfil de columnas guardado por banco:
   fecha de transacción, fecha valor opcional, descripción, referencia,
   débito, crédito y saldo opcional.
3. Guardar archivo, hash, moneda, saldos inicial/final y líneas normalizadas. El
   mismo hash no se importa dos veces para la misma cuenta.
4. Presentar al lado izquierdo las líneas del extracto y al derecho los
   movimientos de `AccountingEntryLines` del auxiliar bancario.
5. Sugerir cruces por cuenta, naturaleza, valor exacto, referencia normalizada
   y proximidad de fecha. Una sugerencia nunca concilia por sí sola.
6. Permitir confirmar, deshacer y repartir manualmente un cruce. Las
   asignaciones conservan cuánto de cada línea bancaria y contable fue usado,
   por lo que soportan depósitos agrupados sin duplicar documentos.
7. Clasificar una línea que solo existe en el banco y confirmar su ajuste.
8. Cerrar mostrando saldo del extracto, saldo en libros, partidas pendientes y
   diferencia. Una diferencia distinta de cero bloquea el cierre; no se crea
   una partida de cuadratura automática.

No forman parte del primer alcance: conexión directa con bancos, OCR, reglas de
IA, conciliación automática irreversible, transferencias interbancarias
avanzadas ni formatos propios para cada banco escritos en código.

## Partidas bancarias y asiento

La clasificación es explícita y sus cuentas se resuelven mediante categorías
del perfil contable, nunca mediante IDs o SQL quemados:

| Partida del extracto | Débito | Crédito | Regla |
|---|---|---|---|
| Comisión bancaria | gasto bancario y, si aplica, IVA descontable | cuenta bancaria | base e IVA se capturan por separado; no se infieren |
| GMF | gasto GMF | cuenta bancaria | se registra el valor realmente debitado por el banco |
| Rendimiento con retención | banco por el neto + retención a favor | ingreso financiero bruto | la suma debe cuadrar con el rendimiento |
| Rendimiento sin retención | banco | ingreso financiero | valor real del extracto |
| Otro ajuste soportado | cuenta PUC elegida | banco, o inverso | exige concepto, soporte y permiso contable |

El GMF no se calcula sobre todas las salidas. La entidad financiera determina
el débito real y las exenciones. Se guarda el valor total contable y evidencia;
el atributo fiscal permite reportar la porción deducible vigente sin partir ni
alterar el movimiento bancario.

## Modelo durable propuesto

- `BankStatementImports`: cuenta, rango, saldos, archivo/hash, perfil y estado.
- `BankStatementLines`: secuencia estable, fechas, descripción, referencia,
  débito, crédito, saldo e integridad de origen.
- `BankReconciliations`: cuenta, periodo, saldos, estado, versión y auditoría.
- `BankReconciliationAllocations`: línea bancaria, línea contable, valor
  asignado, actor y fecha.
- `BankStatementAdjustments`: clasificación y evidencia de una línea sin libro,
  con el identificador del documento contable canónico cuando se confirma.

Importación y conciliación usan concurrencia optimista. Cerrar toma bloqueos del
encabezado y vuelve a calcular todos los saldos dentro de una transacción. Una
línea no puede asignarse por encima de su valor ni participar en dos
conciliaciones cerradas.

## Seguridad y operación

- `accounting.bank-reconciliation.read`: consultar e importar.
- `accounting.bank-reconciliation.manage`: confirmar/deshacer cruces y ajustes.
- `accounting.bank-reconciliation.close`: cerrar y reabrir con motivo.
- quien concilia queda auditado independientemente de quien originó pagos;
- tenant, cuenta y auxiliar PUC se validan en cada escritura;
- referencia y nota ayudan a identificar, pero nunca autorizan ni deciden un
  cruce por sí solas.

## Base normativa considerada

- DIAN, GMF: el hecho se causa al disponer recursos, la base es la transacción
  real y la tarifa general es 4 x 1.000:
  https://www.dian.gov.co/impuestos/personas/Paginas/gravamen_movimientos_financieros.aspx
- DIAN, artículo 115: el 50% del GMF efectivamente pagado puede ser deducible
  con certificación del agente retenedor:
  https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_1787_2015.htm
- DIAN, Concepto Unificado 1466 de 2017: una comisión bancaria puede causar IVA
  y su débito también puede coexistir con GMF:
  https://www.dian.gov.co/normatividad/Documents/Concepto%20General%20Unificado%20-%20No1466%20-%2029122017.pdf
- MinCIT, marco técnico: las conciliaciones deben ser periódicas, revisadas, con
  segregación de funciones y depuración de partidas:
  https://www.mincit.gov.co/normatividad/proyectos-de-normatividad/proyectos-de-decretos-2019/16-09-2019-anexo-tecnico-compilatorio-y-actializad.aspx
