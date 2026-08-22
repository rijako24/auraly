# Implementación: motor contable mínimo y reporting de ventas

## Decisión vigente

Esta rebanada implementa la primera cobertura ejecutable definida en
`decision-contabilidad-minima-colombia-y-cumplimiento.md` y conserva como
contrato de evolución todo el alcance fiscal y de información exógena de esa
decisión. No mezcla presentación DIAN, contabilidad, inventario ni analítica.

Los cuatro motores durables son:

1. operación, propietario exclusivo de los efectos físicos de inventario;
2. contabilidad, propietario exclusivo de cartera, pagos, caja y asientos;
3. fiscal, propietario de generación, firma y transmisión DIAN;
4. reporting, propietario de proyecciones analíticas reconstruibles.

En Service Bus cada cola exige sesiones y usa `SessionId = BusinessId`. La
configuración contable de una empresa debe estar en estado `Ready` y la fecha
del documento debe ser igual o posterior a `EffectiveFrom`; antes de eso no se
crea `AccountingPostingJob` ni se publica una señal contable.

## Documento contable manual

La aceptación manual conserva JSON y SHA-256 inmutables, asigna secuencia y
crea un único trabajo contable. Repetir el identificador con el mismo contenido
es idempotente; repetirlo con contenido distinto es conflicto.

Hay dos contratos distintos:

- `AccountAdjustment`: nota de CxC o CxP con `Increase` o `Decrease`. Cambia el
  submayor y genera el asiento contra la cuenta escogida. Una disminución no
  puede producir saldo negativo.
- `ManualVoucher`: comprobante de reclasificación. Exige dos o más líneas,
  exactamente un débito o crédito positivo por línea y totales iguales. No
  altera CxC/CxP por inferencia.

Los documentos manuales no atraviesan un handler operacional. Para mantener la
secuencia compartida segura, su fuente completada solo se acepta cuando el
cursor operacional del negocio está al día; el alta incrementa de manera
atómica `LastAssignedSequence` y `LastCompletedSequence`. Así no se salta un
documento físico pendiente ni se crea un hueco que bloquee la sede.

## Salidas contables implementadas

La API y la vista de Contabilidad exponen:

- balance de comprobación y auxiliar por cuenta con drill-down;
- libro diario;
- mayor y balances con saldo inicial, débitos, créditos y saldo final;
- estado de situación financiera;
- estado de resultados;
- documentos pendientes o con excepción;
- captura de notas de cartera y comprobantes manuales.

Todos esos informes leen `AccountingEntries` y `AccountingEntryLines`. No
reconstruyen cifras consultando facturas, compras o movimientos operacionales.
El diseño completo de auxiliares por tercero/centro, cambios en patrimonio,
flujo de efectivo, bases fiscales y exógena versionada permanece obligatorio en
la decisión contable; una salida regulatoria solo podrá declararse disponible
cuando exista su definición por autoridad, año, formato, versión y resolución,
y tenga pruebas contra fuentes conciliadas.

## Reporting de ventas y patrón reproducible

Reporting usa una granularidad híbrida:

- identidad mínima por documento para paginación y salto al origen;
- hechos delgados y firmados por línea, pago e impuesto;
- totales diarios y totales diarios por dimensión para lectura macro rápida;
- checkpoint por negocio para reconstrucción e idempotencia.

No se copia el payload fiscal ni el detalle operacional completo. El detalle
analítico necesario queda desacoplado; cuando el usuario pide el comprobante
operacional de un día o documento se navega a su dueño, no se ensancha el
consolidado.

Para reproducir el patrón en otro proceso:

1. definir primero las preguntas y dimensiones que justifican una proyección;
2. publicar una señal después del commit del documento fuente;
3. particionar por `BusinessId` y hacer el proyector idempotente;
4. guardar hechos mínimos firmados y agregados a la granularidad de consulta;
5. conservar checkpoint y una operación explícita de reconstrucción;
6. comparar proyección contra fuente mediante pruebas de conciliación;
7. no crear consolidados para datos pequeños sin una consulta costosa real.

## Pruebas de cierre

La rebanada se cierra únicamente si pasan sobre SQL Server real:

- empresa sin configuración: cero trabajo y cero señal contable;
- activación `Ready`: documentos posteriores producen trabajo y asiento;
- ventas, devoluciones, compras, devoluciones de compra, gastos, recaudos,
  pagos y caja contabilizan balanceados e idempotentes;
- notas débito/crédito de CxC y CxP modifican saldo, guardan transacción firmada
  y producen la naturaleza contable esperada;
- comprobante manual válido contabiliza una vez y uno desbalanceado se rechaza;
- diario, mayor, estados, balance de prueba y excepciones concilian;
- reporting de venta/devolución concilia macro, dimensiones y detalle.
