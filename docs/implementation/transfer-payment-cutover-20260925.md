# Corte de medios de pago: `Transfer`

El único código comercial y de cierre para transferencia es `Transfer`. No se
aceptan nuevos comandos con `BankTransfer` ni se proyecta ese código en lecturas.
El script `database/Auraly.Database/Scripts/Migrations/20260925_NormalizeCommerceTransferCode.sql`
es una operación de datos separada del postdespliegue: necesita una pausa de
escrituras del servidor anterior y, si aplica, un asiento correctivo ya
contabilizado. La fuente original de contabilidad no se reescribe.
Los mapeos contables de perfiles distintos al predeterminado conservan su
categoría al cambiar de código. El script se detiene si encuentra el código
anterior en otra fuente comercial que no puede convertir sin revisión.

## Evidencia de Megafruver antes del corte

El cierre `01a0d9fa-6924-76fa-ae1b-82bd6b0849a8` de Lizy tiene
`BankTransfer` esperado $697.114,60, sin contado, y `Transfer` esperado $0,
contado $697.925. El asiento de diferencias ya registrado debitó la cuenta
130520 y acreditó 139995 por $697.925. El esperado unificado es $697.114,60,
y la diferencia contada queda en $810,40, pendiente de verificar.
La auditoría de solo lectura del 25/09 encontró cero códigos anteriores en
pagos de venta, devoluciones, hechos de pago del informe, líneas y cruces de
conciliación, y opciones de medios comerciales. Encontró una opción del
catálogo de nómina, que cambia el seed, y dos mapeos contables comerciales,
que convierte o elimina la migración según exista ya `Transfer` en el perfil.

Antes de convertir el cierre, el módulo contable debe confirmar y **contabilizar**
un comprobante manual con concepto `MANUAL_VOUCHER`, descripción exacta
`Corrección transferencia cierre 01a0d9fa-6924-76fa-ae1b-82bd6b0849a8`,
y estas dos partidas:

| Cuenta | Débito | Crédito |
| --- | ---: | ---: |
| 139995 Diferencias de cierre pendientes de conciliación | $697.114,60 | $0 |
| 130520 Transferencias por conciliar | $0 | $697.114,60 |

El comprobante revierte únicamente la parte ficticia del asiento inicial.
No clasifica los $810,40 como sobrante: la conciliación deberá verificar los
comprobantes y el valor recibido y resolver esa diferencia.

## Secuencia de publicación

1. Compilar y probar el commit integrado en `origin/main`.
2. Generar el comprobante correctivo por el flujo contable normal y comprobar
   su asiento `Posted`. Verificar cuentas, sentido e importe contra el asiento
   original; el script exige estas condiciones.
3. Pausar la API anterior y las cajas preparadas para que no se acepten pagos
   con el código anterior durante el corte. Confirmar que no queden pagos ni
   trabajos contables pendientes con ese código.
4. Respaldar la base y publicar el esquema del mismo commit. Ejecutar el script
   de normalización una vez. Es una sola
   transacción: ante cualquier inconsistencia aborta sin cambiar datos.
5. Comprobar que no haya `BankTransfer` en
   `CustomerPaymentTenders`, `SupplierPaymentTenders`,
   `WorkSessionMovements`, `WorkSessionClosurePaymentTotals` ni en
   `CashClosurePaymentMethodMappings`. Leer el cierre por API y comprobar que
   muestra una sola línea `Transfer`: esperado $697.114,60, contado $697.925,
   diferencia $810,40. Comprobar la nueva huella y el registro en `AuditLogs`.
6. Publicar API, administración y Edge del mismo commit, reanudar escrituras y
   hacer un abono por transferencia de prueba. Cerrar una sesión de prueba y
   verificar que la transferencia aparece una sola vez y se contabiliza en la
   cuenta bancaria configurada.

El script no pertenece a `PostDeployment.sql`: un despliegue automático de
esquema no puede presumir que el comprobante correctivo exista ni que el
servidor anterior haya dejado de escribir.

Si la publicación de la aplicación falla después de convertir los datos,
mantener las escrituras pausadas. Se puede reintentar la publicación del mismo
commit; para volver al servidor anterior se restaura el respaldo previo al
corte y, si ese respaldo ya contiene el comprobante correctivo, se registra un
comprobante inverso mediante el motor contable (débito 130520, crédito 139995
por $697.114,60) antes de aceptar movimientos. Se verifica el saldo de ambas
cuentas y la trazabilidad de los dos comprobantes. El
servidor anterior no debe reabrirse sobre datos ya convertidos porque volvería
a escribir `BankTransfer`.
