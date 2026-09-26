# Diseño: cierres, informes de cartera y cruce de cuentas

Fecha: 2026-09-25. Estado: diseño funcional y contable cerrado para implementación.

## Cierre de sesión

- La sesión operativa canónica abre o se retoma cuando el usuario inicia una operación que maneja dinero. Un abono o pago a proveedor puede ser la primera operación; no depende de una venta previa. La vista de cierres no abre sesiones al consultarse.
- En el historial, la fecha de **cierre** es la principal y la de **inicio** aparece debajo. El filtro de conciliación inicia en `Pending` y permite `Partial`, `Reconciled`, `ReconciledWithDifferences` y todos. La API existente aplica el filtro y la paginación en SQL.
- Los importes y campos de conteo usan formato colombiano con separación de miles; el dato enviado sigue siendo decimal sin separadores. La tirilla vigente ya usa `es-CO` y no se cambia su versión histórica.

## Faltante que puede convertirse en cuenta por cobrar

El cierre ya conserva la diferencia y crea una fuente contable `WorkSessionCashDifference`. Al cerrar se mueve a la cuenta transitoria `CashClosureDifferencesPending` (139995); al conciliar, el faltante residual pasa a `CashShortageExpense` (539596). La opción nueva se ofrece **después de conciliar** un faltante confirmado, sin alterar el arqueo ni crear una venta.

1. Una persona con permiso administrativo específico elige **Registrar cuenta por cobrar por faltante**, el deudor identificado, el importe (máximo el faltante residual no reclamado), vencimiento, motivo y soporte de aceptación o título exigible. La identidad del cajero no basta para imputarle automáticamente la deuda. No hay descuento de nómina automático.
2. `ReceivablesService` recibe un tipo de apertura `WorkSessionShortageClaim`, ligado de forma única al cierre y a la línea de diferencia. Para un trabajador, la obligación se identifica por `PartyId` y rol `Employee`; se generaliza la titularidad del ledger de CxC sin crear otro motor ni disfrazar al trabajador como cliente. Se conservan los contratos de clientes existentes mediante migración y compatibilidad explícitas.
3. El motor contable existente contabiliza la reclasificación: **débito** a una categoría nueva y configurable `EmployeeShortagesReceivable` (propuesta de subcuenta del grupo 1365, distinta de `EmployeeLoansReceivable`), **crédito** a `CashShortageExpense`. No se vuelve a mover caja; el abono posterior acredita esa CxC y debita el medio de pago real. Si el reclamo solo cubre parte del faltante, el resto permanece como gasto. Si se desiste del reclamo, se registra una reversión trazable, sin borrar el cierre.
4. Unicidad por cierre/línea/deudor y clave idempotente, bloqueo de concurrencia por saldo reclamable, autorización, auditoría y asiento balanceado son obligatorios. Una configuración PUC faltante detiene el asiento y señala la categoría faltante; no se asigna una cuenta por defecto silenciosa.

La [prohibición de descuentos salariales sin autorización individual o mandamiento judicial](https://apps.procuraduria.gov.co/gd_734/docs/c_sustra.html) impide tratar un faltante como deducción automática. La creación de una CxC exige evidencia de un derecho real de cobro; no se infiere del descuadre.

## Informes de cuentas por cobrar y por pagar

Cada módulo tendrá un botón **Reportes** que abre el selector de tarjetas usado en despachos. Los tres informes por módulo son:

| Módulo | Por tercero | Por factura | Por movimiento |
| --- | --- | --- | --- |
| CxC | Cliente: original, recaudado, saldo y vencido | Factura: origen, fechas, original, abonos y saldo | Recaudos: fecha, comprobante, cliente, aplicaciones por factura y desglose por medio de pago |
| CxP | Proveedor: original, pagado, saldo y vencido | Factura o gasto: origen, concepto, fechas, original, pagos y saldo | Pagos: fecha, comprobante, proveedor, aplicaciones por obligación y desglose por medio de pago |

El reporte toma una instantánea visible de los filtros de la pestaña correspondiente al abrirse. El visor muestra esos filtros, totales del conjunto filtrado, detalle paginado desde servidor, impresión y exportación. Sin filtros, consulta el conjunto completo por páginas; nunca descarga todas las obligaciones al navegador. Para movimientos, la fecha filtra la fecha del recaudo/pago; para facturas, la fecha de emisión o creación que usa la lista. El visor no reinterpreta un filtro de estado de factura como estado del comprobante: solo ofrece los filtros pertinentes a cada tipo y los muestra antes de generar.

`ReceivablesService` y `PayablesService` siguen siendo propietarios de importes, aplicaciones, permisos y lectura paginada. Reporting compone la presentación con un contrato de proyección compartido y consultas SQL acotadas por tenant, negocio, filtros y página. Exportaciones completas se preparan fuera de la solicitud interactiva con los mismos filtros; la vista previa no hace N+1 ni un GET por fila. Objetivo de respuesta de página y totales: menos de un segundo en el volumen operativo medido, con índices y plan de ejecución verificados.

CxC opera en COP. CxP admite varias monedas: sus informes deben separar importes originales, pagos y saldos por `CurrencyCode`, o mostrar una conversión funcional a COP con tasa y fecha explícitas. No se presenta una suma de USD y COP como si fuera un único total monetario.

## Cruce de CxP con CxC

Se agrega una pestaña **Cruce de cuentas** debajo de cuentas por cobrar. Se selecciona un proveedor y el servidor resuelve su `PartyId`; solo se muestran sus roles de proveedor y cliente realmente vinculados a ese mismo tercero. No se hace cruce por coincidencia de nombre o NIT. La primera versión exige mismo tenant, mismo negocio, COP, saldos abiertos, obligaciones ciertas y exigibles o acuerdo documentado, y permiso administrativo `portfolio.offsets.create` junto con lectura de ambas carteras.

El usuario elige facturas de ambos lados y montos; el máximo a cruzar es el menor de los dos saldos seleccionados. Antes de confirmar ve el comprobante de compensación y el saldo residual de cada factura. Una única transacción SQL del propietario de cartera bloquea y aplica ambos ledgers, registra el documento fuente `PortfolioOffset`, sus asignaciones e idempotencia y deja el trabajo para el `SqlAccountingPostingProcessor`. La partida económica es **débito a CxP 220505 y crédito a CxC 130505** por el mismo importe. No mueve efectivo, banco, ingreso, gasto ni IVA de las facturas originales. No se presenta como un pago en efectivo ni como una factura nueva a la DIAN. Un fallo antes del commit deja ambos saldos intactos; un reintento de la misma clave devuelve el resultado ya aceptado.

La [compensación civil requiere obligaciones recíprocas, líquidas y exigibles](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=3644); para presentar activos y pasivos netos se requiere [derecho exigible e intención de liquidar por neto o simultáneamente](https://www.ifrs.org/issued-standards/list-of-standards/ias-32-financial-instruments-presentation/). El cruce liquida aplicaciones identificadas; no oculta saldos de otras facturas mediante una resta visual. Las [retenciones se revisan al pago o abono en cuenta, el que ocurra primero](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_900613_2022.htm); el cruce verifica las retenciones ya causadas y no las duplica.

## Aceptación

- Historial de cierres: filtro inicial pendiente, cuatro estados y todos, fecha de cierre arriba, paginación correcta, moneda `es-CO`.
- Faltante: sin deudor acreditado solo gasto; reclamo parcial y total, abonos, reversión, sin descuento de nómina; suma de gasto y CxC igual al faltante, asiento balanceado y sin segunda salida de caja.
- Informes: seis variantes, filtros y permisos idénticos a las vistas, totales correctos entre páginas, medios y aplicaciones visibles, cero consultas por fila, impresión y exportación fieles.
- Cruce: tercero con ambos roles, múltiples facturas, saldo parcial, concurrencia, reintento idempotente, fallo atómico, asiento balanceado, sin caja ni documento fiscal nuevo.
