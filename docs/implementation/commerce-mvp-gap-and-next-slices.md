# Brechas vigentes y orden de implementación de Auraly Commerce

Fecha de corte: 2026-08-03  
Rama base verificada: `feature/auraly-commerce-external-customer-events`  
Commit base: `a90d8766fde29aec27c8b573d6cd6a5d49abe07c`

## Resultado ejecutivo

Auraly ya tiene una columna vertebral transaccional real: catálogo y precios,
POS online y Edge, operación offline, pedidos, ventas, motor documental ordenado,
inventario, entradas de mercancía, cuentas por pagar, devoluciones de compra,
devoluciones de venta con nota crédito fiscal, Parties, sesiones de trabajo y
contabilidad operativa.

Todavía no se considera completo el MVP comercial ni se ofrece como sistema
contable o facturador electrónico legalmente habilitado. Las brechas que siguen
son capacidades verticales, experiencia administrativa y validación productiva;
no requieren reemplazar la arquitectura existente.

## P0: bloqueantes para un piloto comercial integral

1. Cuentas por cobrar end-to-end.
2. Devoluciones de venta completas en POS y web.
3. Reportes Commerce conciliables.
4. Experiencia web de conteos, ajustes, traslados, conversiones y averías.
5. Habilitación DIAN real, configuración segura de certificados y resoluciones.
6. Maestros Party restantes y clasificación de producto conectada.

> Actualizacion posterior a la implementacion: la rebanada CxC descrita abajo ya
> fue construida. Cubre venta a credito online, obligacion, libro, perfil y cupo,
> abonos parciales, sesion de trabajo, contabilidad, idempotencia, concurrencia,
> API paginada y vista administrativa. La mora se deriva de vencimiento y saldo.
> Quedan pendientes el credito offline de POS Edge, la configuracion visual del
> perfil dentro del editor de cliente y las compensaciones o castigos autorizados.
> El texto siguiente se conserva como alcance original y queda reemplazado por
> `receivables-slice-design.md` y `receivables-slice-evidence.md`.
### Cuentas por cobrar

No existe todavía un módulo canónico `Receivables`. Debe cubrir:

- obligación originada por una venta a crédito;
- cliente, documento origen, valor, fecha, vencimiento, saldo y estado;
- abonos parciales y aplicación de pagos;
- reversión mediante movimiento compensatorio, sin editar el libro;
- límite de crédito, crédito disponible, mora y antigüedad;
- estado de cuenta e historial;
- recepción del pago desde una sesión de trabajo;
- contabilización e idempotencia;
- API paginada, filtros y vista administrativa;
- permisos, concurrencia, RabbitMQ y SQL Server real.

Estados iniciales: `Open`, `PartiallyPaid`, `Paid`, `Overdue`, `Cancelled` y
`WrittenOff`. Cuotas, intereses, cheques posfechados, retenciones y cruces
complejos permanecen fuera del MVP.

### Devoluciones de venta

El procesamiento, inventario, reembolso/crédito y nota crédito fiscal ya tienen
base. Falta la experiencia conectada para buscar la factura, seleccionar líneas,
capturar cantidades, motivo, condición y destino, resolver el valor económico,
consultar historial y operar desde POS y web.

### Reportes Commerce

Faltan ventas, compras, utilidad, inventario, kardex, CxC, CxP, sesiones de
trabajo, estado fiscal, pendientes offline y errores del motor. Todos deben usar
consultas del servidor, filtros combinables, paginación, permisos y totales
conciliables con los libros transaccionales.

### Inventario administrativo

El motor procesa conteos, ajustes, traslados y conversiones, pero faltan sus
vistas de consulta y captura. Averías todavía requiere documento propio,
procesamiento, destino físico, consulta y reporte.

### DIAN

La generación UBL, firma, CUFE/CUDE, artefactos, estados y transporte durable
están implementados hasta el límite del transporte. Faltan habilitación real,
credenciales y certificado válidos, `TestSetId`, prueba SOAP/WS-Security,
Azure Key Vault para SaaS, administración completa de resoluciones y la matriz
de contingencia validada en el ambiente oficial.

## P1: operación estable

- cargue de mercancía y aduana;
- rutas, despachos y verificación por lector;
- transportadores, conductores, vendedores y empleados como roles Party;
- múltiples sedes y contactos editables desde Party;
- reasignación administrativa de dispositivos;
- configuración física de impresora y balanza;
- adaptador push autohospedado para on-premise;
- monitoreo, dead-letter, recuperación y retención de outbox/recibos;
- migración y conciliación de datos reales de Xion;
- pruebas de volumen, respaldo, recuperación y piloto.

## Contabilidad y cumplimiento posteriores

La fundación contable ya incluye plan, periodos, centros de costo, asientos,
ventas, entradas, pagos, devoluciones y balance de prueba. Antes de ofrecerla
como contabilidad completa faltan libros, auxiliares, estados financieros,
conciliaciones, bancos, impuestos y retenciones versionadas, nota débito,
reversiones completas, exógena y pruebas de cierre con datos representativos.

## Orden aprobado de las siguientes rebanadas

1. Cuentas por cobrar.
2. Devoluciones de venta completas.
3. Inventario administrativo y averías.
4. Reportes Commerce.
5. Habilitación DIAN real.
6. Despachos, rutas, cargue y aduana.
7. Contabilidad y cumplimiento ampliados.
8. On-premise, periféricos y endurecimiento de piloto.

## Primera rebanada en ejecución: Cuentas por cobrar

El corte debe ser vertical y no se declarará terminado solo con tablas o
contratos. El flujo de aceptación es:

    venta a crédito confirmada
      -> DocumentProcessingJob
      -> obligación CxC y movimiento inicial
      -> asiento contable
      -> consulta paginada
      -> abono parcial
      -> movimiento de sesión de trabajo
      -> aplicación CxC
      -> asiento del recaudo
      -> saldo y estado actualizados exactamente una vez

La implementación reutilizará el motor documental y los patrones probados de
Payables, pero el modelo de CxC será canónico e independiente. No se copiarán
tablas ni nombres de Xion y no se agregará polling, EF migrations, una segunda
base o componentes sin consumidor.

