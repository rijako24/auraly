# Evidencia de concurrencia de pagos

Fecha: 2 de agosto de 2026

La prueba `PayablesVerticalSliceTests` envía simultáneamente dos pagos que, en
conjunto, exceden el saldo disponible de una misma obligación.

SQL Server puede seleccionar una aceptación serializable como víctima de
deadlock. `SqlPayablesStore` reintenta exclusivamente el error 1205, hasta tres
veces y con espera acotada. No reintenta conflictos funcionales ni otros errores.
La combinación de `PaymentId`, clave idempotente y hash del comando permite
repetir la transacción completa sin consumir otra numeración ni duplicar pagos.

Resultado verificado con SQL Server real:

- exactamente una solicitud fue aceptada;
- exactamente una recibió conflicto por saldo insuficiente;
- no se devolvió error 500;
- el saldo final correspondió a un solo pago adicional;
- el payload persistido conservó el hash SHA-256 de sus bytes UTF-8 exactos;
- cuerpos incompletos devolvieron 400;
- la lista validó tanto Business como Tenant autenticados.

Después de este escenario se ejecutaron las 85 pruebas de integración del
servidor en una misma base aislada desplegada por DACPAC. Todas aprobaron.
