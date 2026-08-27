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
- reembolso de efectivo limitado al valor originalmente pagado en efectivo y asociado a una sesión de trabajo;
- aplicación primero a la cuenta por cobrar originada por la factura y creación de saldo a favor solamente por el excedente;
- inventario, contabilidad y nota crédito mediante los motores canónicos existentes y sus señales de outbox;
- permisos, aislamiento por negocio, idempotencia y concurrencia con SQL Server real.

## Reglas económicas

`CustomerCredit` no significa crear siempre un saldo a favor. El motor contable
aplica el valor en este orden:

1. reduce el saldo abierto de la cuenta por cobrar de la factura original;
2. registra un movimiento compensatorio inmutable en el libro CxC;
3. si queda un excedente, crea el saldo a favor del cliente.

Un reembolso en efectivo requiere una sesión de trabajo y no puede superar el efectivo cobrado originalmente menos reembolsos anteriores. No se afirma un reverso de tarjeta o transferencia mientras no exista una integración real con el procesador correspondiente.

## Destino físico

- `Sellable`: retorna a inventario vendible en la bodega receptora.
- `NotReturned`: no crea entrada física.
- `Inspection` y `Damaged`: permanecen bloqueados en la interfaz hasta existir una bodega o estado de inventario canónico para cuarentena y averías. No se aceptan silenciosamente sin movimiento.

Esto evita la pérdida contable que produciría marcar un artículo como recibido sin representar dónde quedó.

## Límites explícitos

La primera conexión POS de esta rebanada es online. La devolución offline en POS Edge requiere persistencia local del documento, historial sincronizado de devoluciones, resolución de conflictos y outbox propia; se construirá como rebanada separada y no se simula llamando al servidor.

Los reversos reales a tarjeta, intereses, cambios de mercancía y devoluciones sin factura original quedan fuera de este corte.
