# Rebanada conectada de devoluciones de venta

Fecha: 2026-08-03

## Diagnóstico

Auraly ya confirma una devolución como documento durable. El motor operacional
procesa exactamente una vez sus líneas e inventario vendible y publica señales
durables; los motores contable y fiscal canónicos procesan respectivamente el
resumen económico/asiento y la nota crédito. No se crea un segundo motor ni una
venta negativa.

La rebanada pendiente debe conectar esa base con consultas y experiencia operativa, y cerrar dos efectos económicos que hoy están incompletos: la aplicación a una cuenta por cobrar y el registro de un reembolso de efectivo dentro de la sesión de trabajo.

## Alcance de esta rebanada

- búsqueda paginada de facturas retornables por número Auraly, número fiscal, CUFE, cliente y producto;
- detalle basado en la factura original, incluyendo cantidades ya devueltas y saldo por línea;
- devolución parcial o total, con cantidades decimales;
- motivo, observación y disposición física por línea;
- consulta paginada e historial de devoluciones;
- confirmación desde la vista web;
- acceso desde facturación online reutilizando el mismo editor;
- reembolso en efectivo por el valor confirmado de la devolución, aunque la venta se haya pagado con otro medio; si el POS aporta una sesión de trabajo abierta, el movimiento queda asociado a esa caja;
- aplicación primero a la cuenta por cobrar originada por la factura y creación de saldo a favor solamente por el excedente;
- inventario, contabilidad y nota crédito mediante los motores canónicos existentes y sus señales de outbox;
- `sales.returns.create` como único permiso operativo para buscar la factura y confirmar la devolución, sin depender del usuario o sesión que emitieron la venta;
- aislamiento por negocio, idempotencia y concurrencia con SQL Server real.

## Reglas económicas

`CustomerCredit` no significa crear siempre un saldo a favor. El motor contable
aplica el valor en este orden:

1. reduce el saldo abierto de la cuenta por cobrar de la factura original;
2. registra un movimiento compensatorio inmutable en el libro CxC;
3. si queda un excedente, crea el saldo a favor del cliente.

El reembolso no puede superar el valor retornable de la venta; en efectivo no
exige que el pago original haya sido en efectivo. Desde POS incluye la sesión abierta del usuario para
afectar su cierre; desde administración puede omitirse y se registra como
liquidación de tesorería/contabilidad, sin inventar un movimiento de caja.

El importe de una devolución incluye la parte del ajuste al peso cobrado en la
factura original. El documento operacional asigna ese ajuste proporcionalmente
al valor acumulado de líneas y cargos devueltos, con cuatro decimales; la última
devolución recibe el remanente exacto. El detalle consultado muestra el ajuste
pendiente para anticipar el importe. Los motores contable y fiscal consumen el
importe ya confirmado; ninguno vuelve a decidirlo.

## Destino físico

- `Sellable`: retorna a inventario vendible en la bodega receptora.
- `NotReturned`: no crea entrada física.
- `Inspection` y `Damaged`: permanecen bloqueados en la interfaz hasta existir una bodega o estado de inventario canónico para cuarentena y averías. No se aceptan silenciosamente sin movimiento.

Esto evita la pérdida contable que produciría marcar un artículo como recibido sin representar dónde quedó.

## Límites explícitos

La primera conexión POS de esta rebanada es online. El navegador web llega mediante
su sesión HttpOnly; una caja enrolada llega a la misma API y a los mismos servicios
canónicos a través del transporte autenticado de Edge. Edge no persiste ni decide
la devolución: sin conexión al servidor, la operación se rechaza explícitamente.

La devolución offline en POS Edge requiere persistencia local del documento,
historial sincronizado de devoluciones, resolución de conflictos y outbox propia;
se construirá como rebanada separada y no se simula llamando al servidor.

Los reversos reales a tarjeta, intereses, cambios de mercancía y devoluciones sin factura original quedan fuera de este corte.
