# Motor documental ordenado de Auraly

Fecha: 2026-07-31  
Estado: obligatorio para todo documento operativo

## Regla central

Ningún módulo actualiza de forma independiente los efectos definitivos de un documento confirmado. En la misma transacción que congela el documento se crea un movimiento durable por procesar. El motor es el único responsable de aplicar los efectos derivados.

Aplica a:

- ventas y devoluciones;
- entradas y salidas de mercancía;
- conteos, inventarios y ajustes;
- traslados entre bodegas;
- averías;
- conversiones;
- compras y devoluciones de compra;
- notas crédito y débito;
- movimientos de cartera;
- todo nuevo documento que modifique estado operativo.

## Registro durable

La tabla de movimientos conserva identidad, negocio, secuencia, documento, estado, intentos, lease, fechas, error y versión de concurrencia.

El documento y el movimiento se guardan en una sola transacción SQL. El movimiento referencia el snapshot inmutable del módulo propietario; no duplica tablas completas ni contiene código ejecutable.

## Transporte

SQL es la fuente de verdad del orden y del estado. El broker es el activador durable.

- SaaS: Azure Service Bus con sesiones.
- `SessionId = BusinessId`.
- `MessageId = MovementId`.
- Un consumidor por sesión.
- Negocios distintos pueden procesarse en paralelo.
- No existe Timer Function sondeando SQL.
- No existe fallback de polling.
- On-premise utilizará RabbitMQ durable mediante una implementación productiva, no un simulador.

En RabbitMQ, el carril documental usa un solo consumidor con `prefetch = 1`.
Una entrega no se confirma ni se reemplaza por una cola temporizada mientras se
está procesando: el consumidor conserva el turno y reintenta el mismo
`MovementId` hasta cinco veces. Si agota el límite, ejecuta `nack` sin requeue y
RabbitMQ lo mueve a la dead-letter durable; entonces el consumidor continúa con
la siguiente entrega. Las colas TTL se reservan para trabajos fiscales que sí
tienen una fecha explícita de reintento.

La API confirma recepción únicamente después de persistir el movimiento y obtener confirmación durable del broker. Los reintentos usan los mismos identificadores.

## Orden y errores

El cursor SQL solo permite procesar `LastCompletedSequence + 1`.

Si falla un documento:

1. se revierte toda su transacción de efectos;
2. queda `RetryScheduled` o `NeedsIntervention`;
3. no avanza el cursor del negocio;
4. ningún movimiento posterior del mismo negocio se ejecuta;
5. otros negocios continúan;
6. el mensaje no se confirma como exitoso.

## Efectos intrínsecos

Cada manejador ejecuta atómicamente lo que corresponda:

- kardex y saldo de inventario;
- valoración y costo;
- cuentas por cobrar o pagar;
- pagos y efectivo;
- asientos de contabilidad y centro de costo;
- impuestos totalizados;
- estadísticas operativas;
- datos consolidados necesarios para reportes;
- eventos de outbox;
- estado final del documento y del mismo movimiento idempotente en
  `DocumentProcessingJobs`; no existe una tabla paralela de recibos.

Contabilidad, estadísticas y reportes no son procesos opcionales desconectados. Son efectos del mismo movimiento o proyecciones idempotentes creadas desde su evento confirmado. Un fallo crítico impide completar el movimiento.

## Lista de control para cada nuevo documento

Antes de declarar listo un tipo documental debe existir:

1. snapshot canónico e inmutable;
2. creación transaccional del movimiento;
3. publicador real al broker;
4. manejador registrado y consumido;
5. reglas de inventario y costo;
6. reglas de cartera/pagos;
7. reglas contables y centro de costo;
8. impuestos y totales aplicables;
9. estadísticas y datos de reportes;
10. evento de outbox;
11. idempotencia;
12. orden por negocio;
13. reintento y estado de intervención;
14. pruebas de duplicado, concurrencia, rollback y recuperación;
15. prueba SQL Server real y prueba del broker real.

No se aceptan interfaces vacías, TODO, mocks permanentes ni módulos que solo persistan el encabezado.
