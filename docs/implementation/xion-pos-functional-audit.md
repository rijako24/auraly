# Auditoría funcional del POS histórico

Fecha: 2026-07-28

## Alcance y criterio

La auditoría siguió las acciones de los formularios de facturación y pago hasta
los servicios locales, repositorios, entidades y salidas de impresión. El
sistema histórico se usa únicamente para descubrir reglas y casos de uso. No se
reutilizan su arquitectura, nombres canónicos, código, modelo de persistencia,
Windows Forms ni Crystal Reports.

La decisión de Auraly prevalece cuando existe contradicción. En particular:

- la política de negativos pertenece a la bodega;
- POS Edge no almacena inventario;
- lista y canal son asignaciones excluyentes del cliente;
- la falta de precio especial usa siempre el precio base del negocio;
- dinero y cantidades usan `decimal`;
- un borrador no contiene una fila vacía artificial;
- persistencia fiscal y CUFE anteceden a la impresión.
- la captura vigente de Auraly siempre crea una línea nueva y mantiene el foco
  del lector; la agrupación observada en Xion es solamente evidencia histórica.

## Matriz de `FrmFacturacion`

| Funcionalidad histórica | Archivo y método | Regla observada | Dependencias | Estado previo en Auraly | Decisión | Prueba objetivo |
|---|---|---|---|---|---|---|
| Restaurar venta de trabajo | `FrmFacturacion.ConfiguracionInicialForm`, `IniciarFacturacion`; `ZFacturaService.IniciarFactura` | Al abrir se reutiliza el documento local en curso y se carga su detalle | BD local, parámetros de equipo | Solo venta emitida durable | Mejorar con `DraftId` y autosave SQLite | Reinicio recupera borrador |
| Estado de red y pendientes | `EstadoRed`, `UpdateProceso` | Muestra conectividad y cantidad pendiente de proceso | Motor local/servidor | Uploader durable sin UI | Conservar, midiendo API y Edge | Estado real online/offline |
| Atajos globales | `FrmFacturacion_KeyDown` | Toda acción principal puede ejecutarse por teclado | Formularios y permisos | Sin estación POS | Conservar con mapa moderno y accesible | E2E solo teclado |
| Apertura de cajón | `AbrirCajon` | Apertura manual exige permiso y configuración | Impresora, `RawPrinterHelper` | No implementado | Mejorar con `ICashDrawer` en Edge | Permiso y adaptador |
| Selección de vendedor | `BuscarVendedor`, propiedad `Vendedor`, `UpdateVendedorDetalle` | Vendedor opcional/requerido y propagable a líneas | Personas, parámetros | No implementado | Conservar | Cambio y recuperación |
| Arqueo | `CierreDeSeccion`; `FrmCierreSeccion` | Consolida ventas, pagos, entradas, salidas, conteo y diferencia | Servicios de arqueo, impresora | No implementado | Renombrar a arqueo y reconstruir | Apertura, conteo y cierre |
| Informe Z | `InformeZ`; `FrmInformeZ` | Consulta un resumen fiscal/caja sin ser el cierre operativo | Reportes | No implementado | Diferir reporte, no confundir con arqueo | Documentación de exclusión |
| Domicilios | `BuscarDomicilios` | Recupera/cobra operaciones de domicilio | Servidor y pagos | Fuera de MVP | Excluir | Arquitectura impide botón |
| Sumar valor a factura | `SumarValorFactura` | Distribuye un valor sobre líneas y audita | Permiso, auditoría | No implementado | Excluir del MVP; usar cambio de precio explícito | Ausencia de acción |
| Devolución | `Devolucion` | Abre flujo separado ligado a venta | Servidor, documento original | Siguiente rebanada | Diferir sin botón desconectado | Punto de integración documentado |
| Apartados | `GenerarApartado`, `BuscarApartado` | Guarda/recupera apartados y bloquea mezcla | Servidor/local | Fuera de MVP | Excluir | Ausencia de contrato |
| Cotización | `GenerarCotizacion` | Recupera o crea cotización con cliente | Servidor/local | Fuera de MVP | Excluir | Ausencia de contrato |
| Pedido | `RecuperaPedido`; `FrmFacturacionPedido` | Recupera un pedido solo con borrador vacío; consulta online; conserva cliente/vendedor/origen; la vista también factura seleccionados | `FacturaService`, estados de pedido | Orders existente, sin contrato POS | Conservar y separar recuperar/facturar | Un pedido por borrador |
| Remisión | `Remision` | Recupera documentos y marca ocupación | Servidor | Fuera de MVP | Excluir | Ausencia de contrato |
| Código no encontrado | `ProductosNoEncontrados`, `AgregarProducto` | Registra el código, alerta y devuelve el flujo de captura | Repositorio local | Búsqueda local devuelve `null` | Mejorar como telemetría durable | Código inválido devuelve foco |
| Puntos | `RedimirPuntos` | Usa puntos como beneficio/pago | Cliente y pagos | Fuera de MVP | Excluir | Ausencia de medio |
| Observación general | `ObservacionGeneral`; `FrmFacturacionObservacion` | Persiste observación del encabezado | Factura local | No implementado | Conservar | Autosave y recuperación |
| Observación de línea | `ObservacionDetalle`; `FrmFacturacionObservacionDetalle` | Persiste observación por producto | Detalle local | No implementado | Conservar | Autosave y recuperación |
| Cambiar descripción | `CambiarDescripcionProducto`; `FrmFacturacionCambiarDescripcion` | Acción autorizada por línea y auditada | Permisos | No implementado | Conservar solo con permiso específico | Denegación y auditoría |
| Existencia por bodegas | `ExistenciaBodegas` | Consulta servidor bajo demanda | API servidor | Endpoint de disponibilidad | Conservar online, sin persistir saldo | SQLite sin inventario |
| Rentabilidad | `RentabilidadDeVenta` | Expone costo/margen bajo permiso | Costos locales | Prohibido para POS MVP | Excluir | Contrato sin costos |
| Balanza | `Balanza`, `VenderConBalanza`, `ActivarBalanza`, `AgregarProducto` | Puede leer dispositivo o código pesable; cantidad debe ser mayor que cero | Hardware y producto | Parser de código implementado | Mejorar con adaptador Edge consumido | Peso, timeout y recuperación |
| Eliminar por búsqueda | `BuscarEliminarProductos` | Permite localizar una línea antes de eliminar | Detalle local | No implementado | Conservar dentro de búsqueda de líneas | Permiso y foco |
| Pago | `Pagar` | Requiere líneas, resolución, vendedor cuando aplica, validaciones, caja y abre flujo de pagos | Fiscal, inventario, caja, pagos | Confirmación offline básica | Mejorar y conectar | E2E confirmación |
| Máximo de efectivo | `ValidarMaximoCajon` | Umbral, intentos y autorización para continuar | Parámetros y permisos | No implementado | Conservar rediseñado | Umbral y supervisor |
| Copia/reimpresión | `CopiaFactura`; `FrmFacturacionBuscar` | Busca factura, selecciona formato e imprime copia | Servidor/local, impresora | No implementado | Conservar auditado | No duplica documento |
| Venta temporal | `GenerarVentaTemporal` | Exige líneas, permiso, máximo diario y opcionalmente cliente | BD local, parámetros | No implementado | Mejorar con nombre, vigencia y estado | Guardar/reiniciar/recuperar |
| Recuperar temporal | `RecuperarVentaTemporal`; `FrmFacturacionTemporal` | Solo con factura activa vacía; no debe recuperarse dos veces | BD local | No implementado | Conservar idempotente | Recuperación única |
| Cantidad por embalaje | `CantidadXEmbalaje` | Cambia cantidad al factor configurado si está permitido | Producto, parámetro | Unidad base mínima | Diferir empaques avanzados; conservar extensión | Capacidad no desconectada |
| Buscar producto | `BuscarProducto`; `FrmFacturacionBuscarProducto` | Busca por referencia, descripción, código y agrega por el mismo flujo | Repositorio local | Búsqueda SQLite | Conservar con un caso de captura único | Búsqueda y escáner equivalentes |
| Descuento | `Descuento`; `FrmFacturacionDescuento` | Requiere permiso, límites y auditoría | Permisos, detalle | Permiso de descuento básico | Mejorar determinísticamente | Permitido/denegado |
| Eliminar línea | `EliminarDetalle`; `FrmFacturacionEliminarProducto` | Permiso, confirmación/motivo, borra detalle relacionado | Factura local | No implementado | Conservar | Sin huérfanos |
| Eliminar factura | `EliminarFactura`; `FrmFacturacionEliminarFactura` | Permiso separado, posible motivo, limpia encabezado y líneas sin facturar | Factura local | No implementado | Conservar sobre borrador | No consume serie |
| Captura exacta | `AgregarProducto(string)` | Código resuelve producto, precio, balanza, disponibilidad, agrega y devuelve foco | `ZFacturaService`, repositorio producto | Captura local mínima | Conservar y mejorar | 100 lecturas rápidas |
| Precio de venta | `SProductoRepository.AgregarProductoFactura` | Intenta lista, canal y precio base; guarda fuente en línea | Cliente, lista, canal, producto | Solo precio de canal de caja | Reemplazar por resolutor canónico | Matriz de fallback |
| Promoción | `GetPrecioEvento` | Aplica por producto, sede, fecha, hora, día y cantidad | Eventos | Promociones existentes no conectadas | Auditar y usar precedencia explícita | No acumulación accidental |
| Edición de cantidad | `dtg_CellValidated` | Permiso, cantidad válida, inventario y recálculo inmediato | Permisos e inventario | Endpoint de disponibilidad | Conservar con debounce/cancelación | Última respuesta gana |
| Persistencia por cambio | `BindingOnListChanged`, `InsertarDetalle`, `UpdateDetalle` | Cada cambio de la grilla persiste localmente | BD local | No hay borrador | Mejorar con transacción/journal | Terminación abrupta |
| Fila vacía | `FactoryDetalleVacio` | Usa una línea producto cero para sostener la UI | DataGridView/BD | No existe | Excluir | No hay línea artificial |
| Agrupar producto | `CodigoAgregado`, parámetro `AgruparFactura` | Puede consolidar repeticiones | Parámetro y detalle | No implementado | No conservar: cada captura crea línea nueva, hace visible la última y mantiene el foco del lector | Repetido crea otra línea |
| Validación de inventario | `ValidarExistencia` | Si hay red consulta servidor; combina reglas legacy y pedidos | Réplica local + servidor | Endpoint online | Conservar solo consulta online y política de bodega | Permite/bloquea negativos |
| Navegación de grilla | `PreviewKeyDown`, `FocoNuevaFila`, `FocoCodigoActual`, `Inicio/Anterior/Siguiente/Fin` | Enter/flechas mantienen captura continua | DataGridView | No UI | Conservar accesiblemente | Solo teclado |

## Matriz de `FrmFacturacionPago`

| Funcionalidad histórica | Archivo y método | Regla observada | Dependencias | Estado previo en Auraly | Decisión | Prueba objetivo |
|---|---|---|---|---|---|---|
| Atajos de pago | `FrmFacturacionPago_KeyDown` | F1-F12 agregan medios; espacio asigna faltante; guardar/imprimir/eliminar por teclado | Catálogos y formularios de medio | Un pago en contrato servidor | Conservar solo medios MVP | E2E solo teclado |
| Efectivo | `EfectivoDetalle`, `AgregarTipoDePago` | Medio inicial; permite excedente y genera cambio | Grilla de pagos | Soportado en venta básica | Conservar | Cambio exacto |
| Crédito | `dtg_CellValidated`, `Validar` | Exige cliente/cartera/cupo o autorización; calcula porción fiscal | Cliente y permisos | No crea cartera | Mejorar con política online/offline | CxC única |
| Débito/crédito tarjeta | `DetalleTipoDePago` | Captura entidad/autorización; pago confirmado puede bloquear edición | Datafono | No implementado | Conservar referencia, diferir integración física | Referencia y edición |
| Transferencia | `DetalleTipoDePago` | Captura entidad o referencia y no produce cambio | Entidades financieras | No implementado | Conservar | No genera cambio |
| Pagos múltiples | `AgregarTipoDePago`, `PonerFaltan`, `GetCancelo` | Distribuye saldo, calcula cancelado/faltante y permite eliminar antes de confirmar | Grilla local | Contrato acepta colección | Conservar con `decimal` | Pago combinado |
| Eliminar pago | `EliminarTipoDePago` | No elimina operaciones irrevocables y restaura efectivo inicial | Grilla | No implementado | Conservar | Saldo recalculado |
| Validaciones por medio | `Validar` | Reglas de cliente, permisos, vencimientos y duplicidad | Parámetros/permisos | Parcial | Reconstruir solo reglas MVP | Tabla de casos |
| Confirmar sin imprimir | `Guardar(false)` | Guarda documento y pagos; impresión es decisión posterior | `ZFacturaService` | Confirmación durable | Conservar, pero siempre crear trabajo de impresión | Persistencia antecede impresión |
| Confirmar e imprimir | `Guardar(true)` | Guarda, envía fiscal según proveedor y luego imprime | Fiscal y Crystal | CUFE local + outbox | Reordenar: snapshot/CUFE/outbox, job, impresión | Fallo no duplica |
| Factura electrónica | `Guardar`, caso `FacturaElectronica` | Integra proveedores y DIAN dentro del formulario | Secretos/UI/transacción | Verificación fiscal interna | No copiar; fiscal servidor separado | Sin secretos en frontend |
| Ticket de pedido | `GuardarTicketPedido` | Mismo documento con otra representación | Impresora | Fuera del flujo mínimo | Diferir | Sin botón desconectado |
| Informe de venta | `InformeFacturaVenta` | Arma empresa, factura, líneas, pagos, descuentos y copia; selecciona impresora/cantidad | Crystal Reports | No renderer | Conservar datos, reemplazar renderer | 58/80 mm |
| Informe electrónico | `InformeFacturaElectronica` | Añade CUFE, QR y folio | Crystal Reports | CUFE/QR existentes | Conservar contenido | CUFE visible, QR decodificable |
| Comprobante cartera | `InformeComprobanteCartera` | Imprime porción financiada | Crystal Reports | No implementado | Integrar en tirilla principal y recibo de crédito | Valor financiado |
| Apertura tras pago | `InformeTicketPedido`, `InformeFacturaVenta`, `InformeFacturaElectronica` | Abre cajón cuando corresponde | Impresora | No implementado | Adaptador Edge y solo efectivo | Auditoría de apertura |
| Copias y corte | selección de `NoCopiasTirilla`, `CortaPapel...` | Usa impresora configurada, número de copias y corte | Parámetros | No implementado | Conservar | Copias y corte simulable |
| Medios excluidos | bonos, puntos, cheques, retenciones, domicilios, consignaciones especializadas | Flujos específicos no requeridos | Muchos módulos legacy | Fuera de alcance | Excluir | Contratos no los exponen |

## Configuración útil encontrada

Se conservan, con nombres canónicos y dueño explícito:

- caja, sede, bodega y asociación heredada de negativos;
- vendedor opcional/requerido y eventual vendedor por línea;
- crédito habilitado y política offline;
- cantidad, descuento y cambio manual de precio según permisos;
- captura repetida como líneas independientes, sin trasladar `AgruparFactura`;
- balanza;
- impresora, ancho, copias, corte y cajón;
- máximo de efectivo e intentos de autorización;
- venta temporal, máximo/vigencia;
- mensajes de tirilla;
- operación offline y reimpresión.

No se trasladan las banderas que activan lista/canal, costos/rentabilidad en POS,
lotes, seriales, domicilios, apartados, cotizaciones, remisiones, puntos, bonos,
cheques, retenciones, producción ni parámetros móviles heredados.

## Hallazgos adicionales

- `ProductoPrecio` mezcla costo, cuatro escalas, márgenes y precios públicos en
  una fila por sucursal. Auraly separa precio base, listas, canales resueltos y
  costos administrativos.
- La lista histórica guarda precio final y cantidad mínima. El canal puede
  derivarse de costo, margen, precio público, exclusiones y redondeo.
- Los cálculos históricos usan `double`; no son vectores numéricos confiables.
- La vigencia de lista en una consulta histórica compara inicio/fin en sentido
  incorrecto. Auraly define `inicio <= instante < fin` y lo prueba.
- El POS histórico consulta y conserva costo/rentabilidad; Auraly no los expone.
- Pedidos consultan servidor y distinguen recuperar uno en POS de facturar una
  selección desde la vista de pedidos. Esa separación se mantiene.

