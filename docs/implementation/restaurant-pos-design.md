# POS para restaurantes y bares

Fecha: 2026-09-09. Estado: propuesta de producto y arquitectura para revisión; no implementada.

Este es el documento propietario de la propuesta. La entrega comprende investigación, diseño de interacción, configuración, brechas del runtime y criterios de aceptación. La maqueta usa datos ficticios y no ejecuta ventas. La implementación deberá ratificar los cambios de persistencia y contratos aquí propuestos; este documento no sustituye decisiones vigentes ni autoriza un motor nuevo.

## 1. Decisión de producto

Agregar **Restaurante y bar** como presentación del POS existente. El usuario conserva catálogo, precios, clientes, descuentos, pedidos, facturación, devoluciones, movimientos de dinero, inventario y cierre de sesión de venta. Mesas y cuentas abiertas son el contexto de atención adicional.

Dos vistas principales, **Mesas** y **Venta**, comparten el mismo estado de trabajo. **Cuentas** permite encontrar consumos abiertos de mesas, barra y ventas pausadas sin recorrer el plano. **Comandas** ofrece la preparación y entrega virtual por estación. **Operaciones** reúne los accesos existentes; **Configuración** queda sujeta a permiso administrativo. No se crea otra aplicación de facturación ni un catálogo gastronómico duplicado.

La pantalla de venta abre con categorías verticales a la izquierda, la primera categoría seleccionada y productos con fotografías en el centro; a la derecha, el plano del local. Elegir mesa abre su cuenta en ese mismo panel, con retorno inmediato a Mapa. Cambiar de vista nunca cambia silenciosamente entre servidor y Edge.

### Cajero, meseros y dinero: definición de la primera versión

**Decisión final del usuario:** el cajero inicia sesión y conserva la sesión de trabajo de Auraly. Los meseros toman pedidos, abren mesas, agregan consumos, envían rondas e imprimen la **precuenta**. El cajero recibe el dinero, registra el pago y emite la factura. No hay sesiones de caja, fondos ni cierres por mesero. Esta definición sustituye las alternativas anteriores de login financiero por mesero y cobro indistinto.

La identificación rápida del mesero es contexto de autoría de atención: no cambia el principal cajero autenticado, no cierra ni reemplaza su WorkSession y no concede sus permisos financieros. Cada ronda/línea registra quién la capturó y quién la envió. En V1 la mesa queda asociada al mesero que inicia su cuenta. Para continuar se valida la contraseña secundaria del responsable; otro mesero necesita autorización del supervisor con permiso explícito de intervención. La aprobación no reasigna permanentemente la mesa. Registrar responsable, actor efectivo y supervisor aprobador por separado. La huella queda fuera de V1.

**Recorrido:** cajero inicia su sesión habitual → mesero toca mesa → valida contraseña secundaria del responsable o aprobación acotada del supervisor → abre/recupera cuenta → agrega cantidades y envía → cocina/barra reciben → mesero imprime precuenta → cliente entrega dinero → mesero lo lleva al cajero → cajero recupera la misma cuenta, verifica el total actual, registra pago y factura. La precuenta no registra dinero recibido ni convierte la cuenta en factura. Antes de que el cajero registre el cobro, el importe sigue pendiente en el sistema; el traslado físico del efectivo no crea una caja del mesero.

La precuenta lleva referencia recuperable (código o QR), mesa, fecha/hora, versión, líneas, cantidades, total y texto **Precuenta · No es factura**. El QR identifica la cuenta para un operador autorizado; no contiene credenciales ni confiere acceso público. Si hubo nuevos consumos, el cajero ve Consumo actualizado, compara con la precuenta y cobra la versión vigente. El checkout congela versión/importe y preserva idempotencia; un resultado incierto se reconcilia antes de otro intento. Pagar no libera la mesa hasta confirmar salida y resolver entregas.

### Una o dos pantallas del mismo computador

Una pantalla reúne atención y cobro con un cambio explícito a modo Cajero. **El cajero utiliza la misma pantalla de venta y el mismo mapa: toca mesa y pulsa Facturar.** La cuenta se recupera directamente; no se copia ni se convierte en otro pedido. Dos pantallas permiten Atención atrás y Cajero delante, sobre el mismo computador y la misma sesión financiera del cajero. La tablet de cada estación de preparación es independiente de esta opción. No son dos cajas ni dos jornadas.

La superficie Atención tiene capacidades limitadas a pedidos y precuentas, aunque el equipo tenga sesión del cajero. Cobro, devoluciones financieras, movimientos de dinero y cierre exigen actuación del cajero y sus permisos canónicos. Cambiar una etiqueta, ruta o campo de usuario en la UI no habilita esas acciones. En una sola pantalla, volver a Cajero requiere revalidar su identidad; el mesero nunca puede aprovechar una sesión privilegiada que siga abierta por detrás. En doble pantalla, identificar un mesero atrás no cambia al cajero delante ni sus operaciones en curso.

**Brecha técnica:** el host actual conserva un usuario local activo por dispositivo. La propuesta V1 preserva ese principal cajero y no requiere sesiones financieras por monitor. Sí exige una capacidad de atención limitada y evidencia verificable de identidad del mesero bajo Authorization: contexto vinculado al principal, sede, dispositivo/superficie, vigencia y operaciones permitidas. Debe validarse en API/host, sin entregar el token pleno del cajero a la pantalla posterior ni confiar en un WaiterId arbitrario. No crear otro motor de autenticación. Concretar este contrato y actualizar la documentación propietaria del acceso es parte del slice de implementación; no afirmar que existe hoy.

DeviceId sigue identificando el computador y WorkSessionId la jornada del cajero. Un identificador técnico de superficie, si el shell lo necesita para foco/lectores, no es caja, usuario financiero ni serie. Persistencia, numeración, catálogo y outbox mantienen sus propietarios. La cuenta gastronómica compartida conserva autoría de atención y recibe la sesión canónica del cajero que finalmente factura; no se mueve dinero al mesero.

Configurar Una/Dos pantallas, monitor de atención/cobro y entrada táctil. Comprobar uso simultáneo, foco, teclado en pantalla, suspensión e impresión sobre el computador real. No incorporar lectores biométricos en V1. Dos monitores conectados no certifican dos operadores simultáneos.

Aceptación: responsable continúa con su contraseña secundaria; otro mesero solo interviene con aprobación válida y autoría separada; la WorkSession del cajero permanece igual; precuenta sin pago/factura; mesero sin acceso a cobro aun manipulando UI; cajero recupera la misma cuenta y cobra una sola vez; consumo agregado invalida el total anterior; relevo de mesero no cambia al cajero de la pantalla frontal; dinero, documentos y cierre aparecen en la jornada canónica del cajero.

## 2. Investigación de interfaces

Consulta de fuentes oficiales el 9 de septiembre de 2026. Se revisaron guías y capturas públicas, no instalaciones comerciales autenticadas ni pruebas cronometradas de esos productos. Las observaciones describen la documentación consultada; no aseguran paridad entre países, planes o versiones.

| Producto y fuente | Organización observada | Decisión para Auraly |
| --- | --- | --- |
| [Toast: opciones de pantalla](https://doc.toasttab.com/doc/platformguide/adminUiOptionsReference.html), [modificadores](https://central.toasttab.com/articles/Knowledge/Advanced-Modifier-Configuration) y [guía visual, pp. 7–9 y 16–19](https://s3.amazonaws.com/toasttab/static-content/training/Toast_Quickstart_Guide.pdf) | Menús y grupos de productos, cuenta, envío a cocina, atención por mesa y separación de cuentas. La guía visual es histórica, sin fecha comprobada: sirve como referencia de composición, no como prueba de la interfaz vigente. | Productos a un toque, modificadores legibles bajo cada plato y separación clara de enviar/cobrar. |
| [Lightspeed Restaurant K-Series: pantalla de venta](https://k-series-support.lightspeedhq.com/hc/en-us/articles/360050328394-Understanding-the-Register-screen), [servicio de mesas](https://k-series-support.lightspeedhq.com/hc/en-us/articles/360050308894-Placing-basic-orders) | Resumen de pedido, teclado y menú; categorías centrales, productos a la derecha. Comensales y tiempos organizan las líneas. La captura oficial fue inspeccionada visualmente. | Mantener zonas estables; teclado numérico contextual para dar más espacio al catálogo. Cuenta por comensal o ronda cuando se necesite. |
| [Square: grupos y diseño del menú](https://squareup.com/help/us/en/article/7804-organize-your-menu-with-square-for-restaurants), [plano](https://squareup.com/help/us/en/article/6427-building-your-floor-plan) | Editor de mosaicos con tamaños, colores, imágenes y posición. Los grupos visuales se separan de las categorías que sirven a reportes y enrutamiento. La captura del editor fue inspeccionada visualmente. | Favoritos y orden visual administrables sin reclasificar productos ni alterar precios. Evitar mosaicos de tamaños arbitrarios en la operación diaria. |
| [Odoo 19: restaurantes](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/restaurant.html) | Navegación entre mesas, venta y pedidos; salones, nombres de cuentas de barra, traslado/unión, rondas y división de cuentas. | Un recorrido continuo mesa → captura → preparación → cobro. Diferenciar ubicación física de cuenta. |
| [Fudo: funcionalidades](https://fu.do/es/funcionalidades/), [mostrador](https://soporte.fu.do/es/articles/11730844-7-mostrador-meson), [cobro parcial](https://soporte.fu.do/es/articles/11730861-como-realizar-un-cierre-cobro-parcial-dividir-el-total-de-una-mesa) | Mapa de salas y mesas, transferencia de consumos, venta directa y cobro de algunos productos manteniendo abierta la atención. La documentación distingue capacidades de computadora y app. | Lenguaje cercano a la operación local: salón, mesa, mesero, comanda y precuenta. Misma cuenta recuperable entre vistas y cierre parcial explícito. |

La síntesis es propia: Auraly debe priorizar posición predecible, lectura rápida y pocos toques. La fotografía es opcional; nombre, precio y disponibilidad nunca dependen de ella. No se adoptan automáticamente reglas fiscales, modelos de caja ni pagos específicos de los proveedores.

La maqueta que acompaña esta propuesta permite explorar Venta, Mesas, Cuentas y Configuración con un conjunto ficticio de productos y mesas. Simula cantidades, modificadores, rondas, traslado, pago, limpieza y edición básica de mesas. La maqueta ilustra contraseña secundaria con claves ficticias, autorización de supervisor, una cuenta por mesa, cajero financiero único, plano por coordenadas y comandas por estación. Credenciales, push, impresoras y operaciones reales no están conectados. Sus cambios viven solo durante la demostración y no persisten al recargar.

## 3. Base existente y brechas comprobadas

La revisión es de código y esquema del checkout actual, incluyendo cambios ajenos que se preservaron. No equivale a certificar producción ni a ejecutar sus pruebas.

| Capacidad | Evidencia local | Tratamiento |
| --- | --- | --- |
| Contrato común de POS | `admin/src/services/pos/pos-edge-client.ts`, interfaz `PosClient`; `online-pos-client.ts` | Compartir comandos y adaptadores entre ambas presentaciones. |
| Captura, cobro, temporales, pedidos y cierre | `admin/src/app/(pos)/pos/page.tsx`: `captureSelectedProduct`, `saveTemporary`, `recoverTemporary`, `saveOrder`, `recoverOrder`, `invoiceOrders`, `completeSale`, `closeWorkSession` | Extraer únicamente el estado/comandos necesarios para montar dos layouts; no copiar la página ni sus reglas. |
| Borradores durables | `OnlineSalesDraftService`, `SqlOnlineSalesDraftStore`, `SalesDrafts`, `SalesDraftLines`, `SalesDraftMutationReceipts` | Extender el mismo propietario para cuentas de atención. |
| Restricción de cuentas actuales | `UX_SalesDrafts_ActiveWorkSession`; `LockDraftAsync` filtra `d.UserId=@UserId`; mutaciones exigen `Active` | Varias cuentas de mesa compartidas **no** se obtienen simplemente guardando el número en `Name`. Hace falta ámbito compartido autorizado, ciclo de cuenta y revisión de índices. |
| Catálogo para mosaicos | `OnlineSalesProduct` tiene identificación, precio y unidad; `SearchOnlineSalesRequest` busca texto y pagina. No expone allí categoría, foto ni orden de menú. `ProductCategories`, `ProductImages`, `ProductMerchandising` ya existen. | Extender la proyección y búsqueda canónica con filtros y metadatos; no descargar todo el catálogo administrativo. |
| Variantes, modificadores y rondas | `SalesDraftLines` tiene `Note` pero el DTO `OnlineSalesDraftLine` no la expone; no se encontró modelado gastronómico de grupos, selección obligatoria o envíos por ronda en la búsqueda realizada. | Diseñar el slice completo: configuración, captura, snapshot, precio, preparación, impresión y pruebas. |
| Checkout y efectos | `OnlineSalesDraftApi` → `OnlineSalesCheckoutService` → `ReceivePosSaleService`; DI en `Program.cs` registra el mismo store para drafts/checkout/historial/importación | La cuenta se emite por el pipeline actual. Inventario, fiscal, contabilidad y reporting conservan sus propietarios. |
| Pedidos comerciales | `PosOrdersApi`, `OrderService`; el POS informa que guardar pedido reserva inventario | Una comanda no puede convertirse automáticamente en pedido comercial: cambiaría el efecto físico. Conservar la acción Guardar pedido con su semántica actual. |
| Impresión | `PosPrinterConfigurationStore`, `ConfigurableOrderDocumentPrinter`, `ConfigurablePosReceiptPrinter`; diseño `pos-printer-configuration-design.md` | Reutilizar configuración, render y transportes. Incorporar propósitos y plantillas versionadas de comanda/precuenta. No asumir confirmación física de papel. |
| Mesas | No se localizaron maestros/atenciones de restaurante en módulos, tablas o POS examinados. `BusinessResources` representa un recurso con cantidad, no mesa identificada con atención y cuenta. | Nuevos datos de atención bajo Sales; no reinterpretar recursos de agenda como mesas activas. |
| Productos vinculados | `ProductLinks` y `decision-modulo-conversion-de-productos.md` | Reutilizar equivalencias válidas, por ejemplo botella/copa. Una receta de múltiples ingredientes no es esa conversión. |

**Prevalencia documental:** varios documentos POS del 29–30 de julio aún mencionan `RegisterId`, cajas y pendientes ya conectados en la UI. Para contexto/numeración prevalece `decision-eliminar-caja-contexto-usuario-dispositivo.md` del 31 de julio; el esquema actual de `SalesDrafts` confirma sede, bodega, usuario y sesión. Para sincronización prevalece `decision-pos-sync-push-sin-polling.md`. Esta propuesta no adopta los textos históricos contradictorios; su limpieza global queda fuera del alcance documental de restaurantes. No hay bloqueo para este diseño, pero la implementación no debe usar esas secciones como especificación vigente.

## 4. Pantalla táctil de venta

### Composición

En terminal horizontal de 1280×800 o superior: cabecera compacta, categorías verticales a la izquierda, mosaicos fotográficos al centro y panel Mapa/Cuenta a la derecha (aproximadamente 32–35%). La primera categoría se abre automáticamente. En 1024 px se reducen columnas de producto antes que tamaño de controles. En teléfono de 360–430 px: Mesas, Productos y Cuenta son vistas consecutivas; resumen y acción de cuenta al alcance del pulgar. El modo compacto no encoge la pantalla de escritorio.

Cabecera: sede/bodega, usuario, conectividad, navegación Mesas/Venta/Cuentas y Operaciones. El número de mesa y nombre de cuenta permanecen visibles durante cualquier modificación o cobro.

Catálogo: búsqueda por nombre/código, categorías verticales y líneas opcionales sobre la cuadrícula; mosaicos estables con fotografía del producto, nombre, precio final y distintivo de modificadores/agotado. Las fotos reutilizan ProductImages; una imagen faltante muestra una ausencia explícita, nunca otra foto engañosa. La maqueta utiliza fotografías ilustrativas de Unsplash. Un máximo de dos niveles visibles. La búsqueda global muestra la ruta de categoría y no queda atrapada en el filtro anterior. Los productos populares se fijan manualmente: no se reordenan en plena jornada.

Cuenta: mesa o nombre de barra, mesero responsable, comensales, líneas y cantidades, separación de **Por enviar** y rondas ya enviadas. Pie con total del consumo, propina aceptada cuando exista, saldo y acciones **Enviar a preparación**, **Precuenta**, **Dividir**, **Cobrar**.

Área de salón (Terraza) filtra mesas. Grupo de menú (Bebidas) filtra productos. Estación de preparación (Barra) enruta comandas. Bodega gobierna inventario. Son dimensiones diferentes aunque el negocio use nombres parecidos; cambiar una no cambia implícitamente las otras.

### Interacciones

- Producto simple: un toque agrega una unidad. La línea se destaca y muestra estado de guardado; se distingue captura pendiente de confirmada. Cada toque intencional suma; un reintento de red no suma otra vez.
- Cantidad: botones +/− grandes en línea y teclado numérico al tocar la cantidad. Retirar una línea enviada exige el flujo de corrección con motivo.
- Producto configurable: panel con grupos, mínimo/máximo, extras y precio resultante. No se puede agregar sin completar un grupo obligatorio. Separar dos unidades con preparaciones distintas.
- Ronda: enviar únicamente cambios pendientes. Volver a una mesa nunca reenvía lo anterior. Repetir ronda crea cantidades nuevas, revalida precio/disponibilidad y muestra el resumen antes de enviar.
- Notas: presets administrados y texto libre; alertas de alergia visibles en línea y comanda. No afirmar que un plato es seguro a partir de una nota.
- Cambio de mesa/cuenta: guarda el estado, conserva selección por cuenta y avisa de cambios pendientes fallidos. Un error de red no convierte el borrador en una cuenta guardada.
- Acciones frecuentes a un toque; adicionales a dos. Sin acciones esenciales ocultas en hover, doble clic, swipe o pulsación larga. Arrastrar solo en el editor de plano y siempre con alternativa por botones/campos.
- Controles principales de 56 px; secundarios al menos 48 px; separación de 8 px. Tipografía de operación 16–18 px y nombres legibles. Contraste, foco y texto acompañan siempre al color.
- Mantener teclado y lector existentes. Un diálogo conserva sus atajos y devuelve foco al invocador táctil o a captura según el dispositivo.

Estados obligatorios: carga, salón vacío con acceso a configuración autorizado, búsqueda sin resultados, producto agotado, guardado pendiente/fallido, conflicto de edición, cuenta en emisión, permiso insuficiente y conexión perdida. Un saldo obsoleto no habilita cobrar.

## 5. Mesas, atenciones y cuentas

**Mesa** es el lugar físico. **Atención** es la visita del grupo. **Cuenta** es el consumo que se cobrará mediante un borrador de venta. **Factura/documento de venta** es el resultado confirmado e inmutable. **Una mesa solo puede tener una cuenta activa y una atención activa a la vez.** Se abre, acumula rondas, se factura, termina la atención y luego puede abrirse una cuenta nueva. Cada ciclo tiene identidad e historial propios. No permitir cuentas hermanas ni apertura paralela en la misma mesa.

No guardar un único `TableId` mutable en una factura y usarlo para representar todo el historial. Conservar la relación con la atención y el contexto congelado al emitir.

### Estados visibles

| Estado de mesa | Significado y transición |
| --- | --- |
| Libre | Sin atención abierta, limpia y habilitada. Tocar abre contexto; confirmar Abrir mesa ocupa atómicamente. |
| Ocupada | Atención activa, incluso si está pagada y los clientes siguen sentados. Mostrar tiempo, mesero, personas, saldo de su única cuenta. |
| Por limpiar | Atención terminada; todavía no admite una nueva. Acción Listo para usar → Libre. |
| Fuera de servicio | Bloqueo administrativo con motivo. Solo se establece sin atención abierta; no borra historial. |

**Por cobrar**, **pagada**, **productos por enviar**, **demora** y **reserva próxima** son indicadores independientes, no estados mutuamente excluyentes de la mesa. **Cerrada** identifica la cuenta/atención terminada en el historial. Evita confundir mesa clausurada, venta pagada y mesa lista.

Las transiciones se ejecutan mediante acciones autorizadas; no hay un dropdown que permita pintar cualquier estado. Los labels y metadatos visibles se sirven desde catálogos persistidos; las invariantes de transición pertenecen al dominio.

### Plano y lista

Pestañas de salones con ocupadas/total; alternancia Plano/Lista; filtros Todas, Mis mesas y Necesitan atención. La lista favorece búsqueda por número, mesero o cuenta y teléfonos pequeños. El plano reproduce posiciones con mesas redondas/rectangulares, capacidad y referencias visuales como entrada/barra. La maqueta muestra una distribución ilustrativa, no medidas del local.

**Estudio de salón:** módulo visual con lienzo por salón, dimensiones/proporciones, mesas numeradas, forma, capacidad, tamaño, posición y orientación. Permite arrastrar, ajustar con campos, alinear a guías y añadir paredes, columnas, puertas, barra y referencias de circulación. Un fondo/plano aportado por el negocio puede servir de guía; no inventar medidas reales a partir de la imagen de referencia. Publicar conserva coordenadas y proporciones en el mapa de ventas: nunca reorganiza mesas automáticamente para llenar una cuadrícula. En pantallas pequeñas usar ampliación o lista accesible, manteniendo el plano como representación fiel.

Cambios de distribución se preparan como borrador, se previsualizan y se publican con versión. No renumerar, eliminar ni mover una mesa ocupada a otro salón sin el flujo autorizado. Detectar códigos repetidos, figuras fuera del plano y solapamientos antes de publicar; preservar versión anterior para reversión. La maqueta permite posiciones/tamaños por campos, arrastre, formas, numeración y referencias, con datos ilustrativos; el versionado, dimensiones físicas, rotación y validación completa de publicación son requisitos de implementación.

Traslado: seleccionar destino libre, revisar y confirmar. En V1 un destino ocupado se rechaza; no fusionar cuentas ni mesas ocupadas. Conservar la misma cuenta, responsable, rondas y ubicación actualizada en comandas. Los documentos emitidos conservan su contexto histórico.

La mesa queda ocupada después de facturar hasta resolver entregas y confirmar salida. La cuenta emitida no se edita: correcciones usan devoluciones/notas canónicas. Para otro consumo se debe finalizar el ciclo anterior y abrir una atención nueva. No crear una segunda cuenta mientras la atención previa siga activa.

## 6. Cobro y operación de bar

**Precuenta:** presentación revisable del consumo actual, identificada como precuenta; no consume consecutivo fiscal, no registra pagos ni cierra la mesa. Incluye número de revisión y hora para distinguirla de una cuenta modificada después.

**Dividir pago:** una cuenta/documento con varios medios o aportantes. Los importes deben sumar exactamente el saldo; el redondeo residual se presenta explícitamente. No implica dividir platos ni emitir varias facturas.

**Una cuenta por mesa:** separar consumos en cuentas simultáneas y cobrar productos abriendo otros borradores queda fuera de V1. Dividir el pago usa los medios/aportantes soportados por el cobro canónico, conservando una sola cuenta y su documento. No introducir cobros parciales nuevos sin contrato existente y aceptación contable.

**Propina:** concepto separado, visible y voluntario; aceptar, editar o rechazar. No convertirla en ingreso por producto ni en descuento negativo. El tratamiento financiero, la distribución y el snapshot deben integrarse con Accounting y los contratos de pago antes de habilitarla. La voluntariedad se apoya en la [Ley 1935 publicada por la SIC](https://sedeelectronica.sic.gov.co/transparencia/normativa/ley-1935) y su [orientación sobre precios y propinas](https://sedeelectronica.sic.gov.co/index.php/temas/proteccion-al-consumidor/derechos-y-deberes/inconvenientes-precio). El diseño no fija impuestos ni porcentajes universales.

Bar: cuentas con nombre corto, acceso a últimas cuentas, repetir ronda, favoritos de bebidas, copa/botella como presentaciones del catálogo y precios horarios mediante promociones existentes. Preautorización de tarjeta requiere una capacidad verificada del proveedor; no se simula guardando datos de tarjeta. Cover, descorche o cargos adicionales explícitos usan conceptos vendibles y tratamiento fiscal configurados, separados de propina.

### Paridad con la venta actual

| Acción solicitada | Ubicación y propietario |
| --- | --- |
| Buscar, agregar, editar, descuentos, cliente, factura/ticket | Venta; comandos actuales y diálogos compartidos. |
| Pausar/recuperar venta | Cuentas → Pausadas; la cuenta de mesa se conserva además vinculada a su atención. Pausar no libera mesa ni envía cocina. |
| Guardar, recuperar y facturar pedidos | Cuentas → Pedidos; `OrderService` y políticas actuales. La reserva de mercancía conserva su significado. |
| Consultar/reimprimir facturas y devolver | Operaciones → Documentos/Devoluciones; snapshot y casos de uso existentes. |
| Entrada/salida de dinero, arqueo y cierre | Operaciones → Sesión de venta; `WorkSessions`. No es movimiento de mercancía. |
| Entrada de mercancía | Operaciones → Recepción de mercancía; workspace de compras existente, con vuelta al contexto de atención. |
| Salida/ajuste/avería/traslado de mercancía | Operaciones → Inventario; workspace y motor documental existentes. No modificar stock desde el mosaico. |
| Impresoras y periféricos | Operaciones → Periféricos; perfiles existentes ampliados. |

El mesero no hereda permisos de compras, inventario o cierre por usar esta vista. Las acciones se ofrecen conforme a permisos/capacidades reales y la API vuelve a autorizarlas.

## 7. Configuración gastronómica

### Contraseña secundaria y autorización de intervención

V1 reutiliza la **contraseña secundaria existente**. No incorpora huella, biometría ni un PIN paralelo. El cajero conserva login y WorkSession; la validación del mesero solo concede atención sobre la cuenta indicada. Al abrir una mesa libre se selecciona e identifica al mesero; la apertura/primera captura aceptada asigna responsable de forma atómica. Seleccionar el nombre en una lista no prueba su identidad.

Mesa ocupada: mostrar responsable y pedir su contraseña secundaria. Si otro mesero interviene, validar su identidad como ejecutor y solicitar la contraseña secundaria del supervisor con permiso para intervenir cuentas de otros meseros. La aprobación se limita a cuenta, intervención y versión/operación autorizada; se consume con el mecanismo canónico y expira. No convierte al operador en supervisor ni cambia permanentemente el responsable. El prototipo simplifica esta doble validación con selección de ejecutor y clave ficticia del supervisor; la implementación exige evidencia del ejecutor y del aprobador por separado.

Al enviar, pausar, cerrar la precuenta o vencer la intervención, terminar el acceso del mesero y volver al mapa. Su cuenta y la sesión del cajero permanecen. Respuestas tardías conservan actor y correlación originales. Cambiar a Cajero requiere la validación canónica del cajero; ocultar botones no es un control de seguridad. La superficie Atención no recibe su token financiero pleno.

**Capacidad existente comprobada:** Authorization posee PosApprovalService, IPosApprovalStore/SqlPosApprovalStore, SupervisorCredentials y PosApprovalRequests. AuthorizeLocallyAsync verifica la credencial secundaria con PBKDF2 y comparación en tiempo constante, filtra supervisores por permiso/sede y rechaza autoaprobación. Existe configuración/revocación, vencimiento y credencial de un solo uso. Se reutiliza este propietario y almacenamiento; no crear RestaurantPasswords ni otro servicio de credenciales.

**Extensión necesaria:** actualmente AuthorizeLocallyAsync busca supervisores habilitados para una acción sensible; no es un login genérico de meseros. ValidateSensitivePermission todavía no admite una intervención gastronómica. Añadir el caso de uso tipado de identificación del mesero y el permiso de supervisor al catálogo/flujo canónicos, conservando autenticación financiera del cajero. No otorgar a todos los meseros permisos de supervisor para reutilizar la API tal cual. La validación del responsable no es una autoaprobación: es otro propósito explícito dentro de Authorization; la excepción supervisada conserva la prohibición de autoaprobación y sus recibos. Revisar contratos que hoy ligan RequestedByUserId/WorkSessionId al cajero para preservar ejecutor mesero y aprobador reales sin datos ficticios.

Credenciales fuera de logs, snapshots, localStorage y comandas. Conservar políticas canónicas de vigencia, revocación e intentos y ampliar evidencia donde falte. La API valida tenant, sede, usuario habilitado, responsable de la cuenta, alcance y versión. Un WaiterId enviado por la UI no habilita mutaciones.

Aceptación: contraseña correcta/incorrecta; responsable permitido; mesero distinto rechazado; supervisor sin permiso rechazado; aprobación válida limitada a esta cuenta; reuso, vencimiento y cambio de versión controlados; autor y aprobador auditados; ningún cambio en la WorkSession del cajero; mesa única aun con aperturas concurrentes.

Ruta propuesta: Configuración → Ventas → Restaurante y bar, por sede. Editor con vista previa y guardado versionado. Valores iniciales provienen de perfiles/seeds idempotentes sin sobrescribir personalizaciones.

| Sección | Datos y comportamiento |
| --- | --- |
| Experiencia de venta | Habilitación por sede, presentación predeterminada, pantalla inicial por perfil, densidad táctil y fotos opcionales. |
| Salones y mesas | Nombre/código, orden, vigencia, plano; mesa con código único por sede, salón, capacidad, forma y coordenadas. Crear en lote, duplicar disposición, mover por campos o arrastre y previsualizar. Desactivar conserva historial; prohibido retirar una mesa ocupada. |
| Menú táctil | Seleccionar productos/categorías existentes, grupos visuales, favoritos, posición y disponibilidad por horario/canal. No duplica nombre fiscal, precio, impuestos ni existencia. Un agotado permanece visible con razón. |
| Modificadores | Grupos por producto con mínimo/máximo, obligatoriedad y orden; opciones administradas, extras vinculados a producto vendible cuando tengan precio/stock. Alergias y notas distinguibles de extras cobrables. |
| Preparación | Estaciones Cocina/Barra/etc. administradas; regla de enrutamiento por producto o categoría con precedencia explícita y validación de destino. Rondas, tiempos y avisos de demora configurables. |
| Impresión | Destinos por estación y dispositivo, formato/copia y plantilla versionada; comanda de prueba, falla visible y reimpresión identificada. No activar cajón desde comanda/precuenta. |
| Atención | Comensales opcionales u obligatorios, mesero responsable, flujo de limpieza, reglas de traslado/unión y relevo. No hacer a las mesas propiedad permanente de un usuario. |
| Cobro | Separación de cuentas, propina y motivos de corrección; capacidades habilitadas solo cuando el contrato financiero correspondiente esté listo. Medios y precios vienen de catálogos existentes. |
| Permisos | Atender, ver otras mesas, reasignar mesero, trasladar, corregir enviados, cobrar, finalizar atención, administrar plano y configuración. Se agregan al catálogo de permisos actual. |

Las áreas y motivos son maestros reales; usar `BusinessReasons` con `ReasonType` para los motivos, no un nuevo catálogo aislado. Los meseros usan identidades/roles operativos existentes y búsqueda paginada; no crear una segunda tabla de personas.

Reservas de hora/fecha, lista de espera, QR/autopedido, domicilios nuevos y recetas/costeo por ingredientes quedan como ampliaciones posteriores. Las comandas virtuales y el tablero de preparación sí forman parte del alcance solicitado. No se exponen configuraciones operativas vacías.

### Comandas virtuales: pedir, preparar y entregar

El modo de atención se configura **por producto vendible**, con destino explícito y posibles reglas predeterminadas por categoría que se resuelven en Catalog. La UI consume el resultado; no deduce la necesidad de cocinar por el nombre, categoría o precio del artículo.

| Modo | Ejemplo ilustrativo | Recorrido |
| --- | --- | --- |
| Entrega directa | Cerveza embotellada, agua | Capturado → Por entregar → Entregado. No aparece como trabajo de cocina. Puede mostrarse en la bandeja de retiro de Barra si el negocio usa esa estación. |
| Requiere preparación | Hamburguesa, pizza | Capturado → Pendiente en Cocina → En preparación → Listo → Entregado. |
| Requiere preparación en barra | Cóctel, bebida elaborada | El mismo recorrido de preparación, con estación Barra. |

Los ejemplos no son reglas globales: una cerveza servida desde barril puede requerir una tarea de barra; un producto envasado puede entregarlo directamente el mesero. Configurar modo, estación de preparación o retiro, nombre corto de comanda, orden y objetivo de tiempo. Las variantes o extras que cambien el destino deben declarar su efecto tipado, no esconderlo en una nota.

**Enviar pedido** confirma una ronda inmutable con todos sus ítems y distribuye tareas: una cerveza entra en Por entregar y una hamburguesa en Pendiente de Cocina. Los productos siguen en la misma cuenta comercial. Marcar Entregado en un producto directo puede confirmar su solicitud y entrega en un comando explícito, manteniendo trazabilidad. El stock conserva el efecto de la venta/pedido actual; avanzar una tarjeta no lo descuenta.

**Tablero virtual por estación:** columnas Pendientes, En preparación y Listos; bandeja Por entregar para productos directos cuando corresponda. Cada tarjeta muestra mesa/cuenta, ronda, antigüedad, mesero, cantidades y modificaciones. Un envío puede crear grupos por Cocina y Barra vinculados a una única ronda. El personal usa Empezar, Marcar listo y Entregado, según permisos. El mesero recibe un aviso visible de los ítems listos; una alerta sonora opcional no es la única señal.

El estado se conserva por ítem/cantidad y estación, no solo por cuenta. Si de tres hamburguesas están listas dos, mostrar 2/3. Si el café sale antes del plato principal, puede entregarse sin cerrar el resto de la ronda. Los estados agregados se derivan de esas cantidades. El cierre financiero y la finalización del servicio son independientes; pagar por anticipado no marca preparado ni entregado.

**Correcciones:** retirar una línea no enviada modifica el borrador. Si fue enviada, la cocina recibe una cancelación o cambio identificado con motivo y revisión; no desaparece silenciosamente. Una preparación iniciada puede requerir aprobación y registrar merma mediante el flujo de inventario correspondiente. Rehacer un producto es una tarea explícita vinculada al original, sin cobrarlo otra vez automáticamente. Reimprimir o refrescar el tablero no vuelve a preparar.

**Confiabilidad:** aceptación durable de ronda antes del aviso; acciones versionadas por ítem/cantidad; idempotencia del envío y de Empezar/Listo/Entregado; distribución y acuses asociados a la misma identidad de ronda. Ante conflicto, se recarga el estado actual. Una estación desconectada se marca sin conexión y conserva el último dato como histórico; al volver recupera lo pendiente mediante outbox/notificación y consulta canónicas, sin sondeo por temporizador ni otra cola de cocina. La UI distingue Pedido guardado, Pendiente de recibir en estación y Recibido; nunca deduce preparación a partir de que salió papel.

**Pantallas:** Comandas es una vista operativa reutilizable en la pantalla existente o en un dispositivo de cocina/barra autorizado. No convierte la configuración de una/dos pantallas de ventas en obligación de comprar una tercera. La elección de impresora complementaria es opcional; el tablero virtual debe funcionar sin papel.

**Tablet dedicada en cocina, solicitada por el usuario:** instalar/abrir una vista de preparación a pantalla completa, orientada horizontalmente y con sesión autorizada de esa estación. La tablet de cocina es adicional a la opción de una/dos pantallas del computador de ventas: son configuraciones independientes. Puede haber otra tablet de Barra si el negocio la necesita. Kitchen no recibe credenciales administrativas ni facultades de cobro por mostrar comandas.

**Nombre y distribución:** la configuración se llama **Estaciones de preparación** y la pantalla operativa **Comandas en vivo**. Cocina y Barra son ejemplos editables, no dos destinos fijos: el negocio puede crear Parrilla, Pizzería o Postres. Cada estación define nombre, sede, orden, activación, dispositivos autorizados, impresora opcional, objetivos de tiempo y preferencias de aviso. Cada producto declara entrega directa o preparación y su destino válido. Una tablet queda vinculada a su estación y abre su propio listado; la vista Todas es para coordinación con permiso, no un filtro que permita acceder a estaciones no autorizadas. El filtro visual de la maqueta solo ilustra esas vistas.

Ejemplo: mesa 04 envía dos hamburguesas, un mojito y dos cervezas en la misma ronda. Cocina recibe una tarjeta con las hamburguesas; Barra, otra con el mojito; Entrega, las cervezas configuradas para retiro directo. Las tres conservan la misma referencia de mesa/ronda y una sola cuenta comercial. No duplicar todos los productos en todas las tablets. El ruteo por estación tiene precedente en [Toast: routing overview](https://doc.toasttab.com/doc/platformguide/platformKitchenRoutingOverview.html); aquí se adopta una regla explícita de producto y extensiones tipadas para modificadores.

Al desactivar una estación con trabajo pendiente, exigir resolverlo o reasignarlo explícitamente. Una reasignación deja origen/destino, motivo y versión, avisa a ambas estaciones y conserva la identidad de la preparación. Trasladar la mesa cambia su ubicación visible, sin crear otra comanda. Dejar sin estación un producto que requiere preparación produce un error de configuración visible, nunca envío silencioso a Cocina.

**Recepción automática push, requisito obligatorio:** la tablet abierta recibe nuevas comandas, modificaciones, cancelaciones y estados sin refrescar ni pulsar Recibir. Tras el commit de Sales, el stream durable y la outbox canónicos publican la invalidación; la tablet recupera el delta autorizado, lo aplica y actualiza tarjetas. La señal despierta la lectura, no reemplaza el registro durable de la comanda. Confirmar recepción del delta no significa que el cocinero haya empezado: Empezar sigue siendo una acción operativa.

El runtime tiene `PosSynchronizationStreams`, `IPosSynchronizationPushGateway`, `PosSynchronizationInvalidation` e `IPosSynchronizationOutboxDispatcher` en `PosSynchronization.cs`, además de `SqlPosSynchronizationOutboxDispatcher`. Todavía no declara un stream de preparación: extender este contrato y sus proyecciones, autorizaciones, suscripción y recuperación, bajo la [decisión de sincronización push](../decision-pos-sync-push-sin-polling.md). No crear otro gateway, dispatcher o job table de notificaciones. La partición por estación debe respetar tenant/sede y permisos tanto al suscribir como al leer o mutar; los grupos actuales por negocio/dispositivo/usuario no certifican por sí solos el aislamiento de estación.

Push en una tablet activa y una notificación del sistema con la aplicación cerrada son capacidades distintas. La primera es obligatoria para operar. Web Push de segundo plano puede añadirse como complemento, sujeto a suscripción, permisos y soporte del sistema ([MDN: Push API](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)); no garantiza ejecutar un tono personalizado con el dispositivo suspendido. La operación de cocina debe mantener la aplicación visible y validar la política de suspensión del equipo. Durante desconexión mostrar Sin conexión y la hora del último estado confirmado; al reconectar recuperar por cursor sin recarga manual, duplicados ni repetición masiva de pitidos históricos.

**Diseño de las tarjetas:** una tarjeta por ronda y estación, con la mesa y antigüedad destacadas arriba, mesero y referencia como apoyo, y líneas de producto con cantidad grande. Modificadores bajo el plato, alertas relevantes diferenciadas y progreso 2/3 cuando haya entrega parcial. Los ítems de una misma ronda comparten tarjeta; no llenar la tablet de tarjetas por cada unidad. Botones Empezar, Marcar listo y Entregado de al menos 56 px en la operación real; acciones parciales junto al ítem. La prioridad de lectura es mesa → producto/cantidad → modificaciones → tiempo → acción.

Tarjetas sobre fondo de bajo brillo, bordes suaves, contraste alto, espacios generosos y estados con color + texto. Llegada con aparición/desplazamiento suave de 200–350 ms y marca Nueva; mover de estado conserva la referencia visual y evita saltos de orden durante un toque. No hay parpadeos ni animaciones perpetuas. Respetar reducción de movimiento ([W3C, técnica C39](https://www.w3.org/WAI/WCAG21/Techniques/css/C39)). Las demoras usan umbrales por estación/producto, texto de tiempo y énfasis progresivo; no una animación roja constante ni urgencias inventadas.

**Pitido de nueva comanda:** sonido corto, amable y reconocible de dos notas, habilitado por tablet con volumen y prueba. Mostrar **Activar sonido** al iniciar si el navegador requiere interacción; no dar por hecho que una web puede reproducir audio en segundo plano. La política de reproducción automática de Chrome incluye Web Audio y recomienda reanudar `AudioContext` tras un gesto del usuario: [documentación oficial de Chrome](https://developer.chrome.com/blog/autoplay/). Sonido activo/silenciado/bloqueado es visible; acompaña una señal visual de llegada y nunca es el único aviso.

Señal audible **una vez por comanda/estación/notificación nueva**, no por cada línea ni por cada re-render. Agrupar ráfagas para evitar saturar la cocina. Guardar el cursor/recibo de aviso por dispositivo en el mecanismo de sincronización existente; una reconexión muestra las comandas recuperadas sin reproducir una cascada histórica. Un cambio o cancelación de una comanda ya recibida utiliza un aviso distinguible y no se presenta como otra comanda nueva. La selección del tono es configuración de presentación local, sin otro motor de notificaciones.

La tablet muestra conexión, última actualización y pedidos pendientes de recibir/confirmar. No dejar silenciosamente una pantalla vacía cuando perdió red. Probar suspensión/reactivación, bloqueo de pantalla, permisos de audio y volumen sobre la tablet real; conservar mensajes de error accionables. El prototipo puede simular una llegada y reproducir un tono tras interacción explícita, pero no certifica recepción push ni audio sobre hardware de cocina.

**Permisos:** administrar enrutamiento, enviar pedido, consultar estación, iniciar preparación, marcar listo, entregar, corregir/anular enviados y rehacer. Se materializan en los catálogos/roles de Authorization; un preset de pantalla no los concede. En V1 los meseros solo atienden y emiten precuentas; el cajero es quien registra el pago y factura, con revalidación explícita al retornar a su modo.

Datos adicionales: estaciones activas por sede, reglas de atención de producto, snapshots de ronda y estado/cantidades atendidas por ítem y estación. La cuenta conserva líneas comerciales; las tareas de preparación se vinculan por identidad y no mantienen otros precios o totales. Sales y el handler de comanda del motor documental son los propietarios propuestos; cambios de estado y señales continúan por los casos de uso del mismo módulo y la outbox existente.

Aceptación: cerveza directa no crea tarea de cocina; hamburguesa llega a Cocina; cóctel llega a Barra; ronda mixta se distribuye sin duplicarse; preparación/entrega parcial por cantidades; listo notifica al mesero; doble toque o reconexión no duplica tareas; cancelar enviado deja historial visible; pagar no implica entregar; cambiar de mesero no cambia actor del envío ya aceptado; funcionamiento de tablero sin impresora; ninguna transición de preparación crea un movimiento de inventario adicional.

Aceptación de tablet: tarjeta agrupa líneas por ronda/estación; llegada anima una vez; sonido suena una vez con audio habilitado; silencio y permiso bloqueado tienen indicación visual; recuperación tras suspensión no repite todos los tonos; alertas de cambio/cancelación conservan referencia; ráfaga de pedidos no bloquea los controles; reducción de movimiento no oculta información; cocinero distingue mesa, cantidades y modificaciones a distancia de trabajo y puede completar el recorrido sin entrenamiento técnico.

## 8. Extensión técnica propuesta

### Impresión automática de comandas, opcional por estación

**Requisito incluido:** cada estación admite **Pantalla**, **Impresora** o **Pantalla + impresora**. La comanda virtual no elimina la tirilla. Cocina y Barra eligen salidas independientes, y una estación puede operar solo en papel. La precuenta del mesero y la factura del cajero mantienen propósitos y destinos propios; no se confunden con la comanda de preparación.

**Montaje propuesto:** impresora térmica USB o instalada por red en Windows, con el componente local de Auraly en el computador que puede imprimir en ella. Puede ser el computador del cajero u otro equipo autorizado como receptor de impresión. La tablet muestra comandas; no necesita conectarse por USB a la impresora ni mantener una pestaña haciendo de puente. El host local recibe la señal push y recupera el trabajo autorizado. El equipo, Auraly local y la impresora deben estar encendidos y disponibles.

No se requiere un mesero/cajero adicional autenticado en la pantalla de cocina para escuchar impresiones. Se requiere identidad técnica del equipo, autorización de los destinos asignados y el proceso local en ejecución. **El runtime actual ya separa impresión de sesión comercial:** RequiresLocalUserSession excluye /edge/v1/print y configuración de impresoras, manteniendo la seguridad local del host. Esto no prueba que exista recepción autónoma de comandas: hay que añadir su suscripción/consumo durable al host y su autorización de dispositivo, sin abrir endpoints públicos ni distribuir el token del cajero.

**Límite de Windows:** SystemWindowsRenderedPrintJob invoca Auraly.Desktop.exe --print-html --printer y espera su resultado. Por eso V1 debe operar con el componente instalado en un contexto de Windows que soporte ese adaptador y su driver. Configurar inicio automático en ese contexto; no prometer funcionamiento tras cerrar la sesión de Windows, con PC suspendido o como servicio de sesión 0 sin validarlo. Puede cerrarse/bloquearse la interfaz comercial solo si el host continúa vivo; el empaquetado debe comprobarlo. No crear un servicio de Windows nuevo por suposición.

**Flujo durable y propietario:**

1. Sales confirma la ronda y congela mesa/visita, estación, actor, líneas, notas y versión de plantilla; registra su entrega por destino mediante los contratos/outbox canónicos.
2. La sincronización existente avisa al equipo designado. El host descarga solo lo autorizado, incluso después de reconexión, y reclama la entrega con versión/lease si hay varios receptores posibles.
3. El receptor persiste el intento/recibo idempotente en el almacenamiento/outbox existentes antes del efecto físico. Identidad: ronda + estación + destino + revisión + copia. La notificación duplicada no inicia otro intento ya resuelto.
4. Usa PosPrinterConfigurationStore y el adaptador/render canónicos de ConfigurableOrderDocumentPrinter/ConfigurablePosReceiptPrinter según el nuevo propósito tipado. No crear otra biblioteca de tirillas, dispatcher propietario, cola de jobs o writer paralelo. El registro operativo de entrega es trazabilidad de ese documento, no otra cola general.
5. Reporta Pendiente, Recibido por equipo, Enviado a impresora, Falló o Resultado por verificar. Enviar al spooler o terminar el proceso no demuestra que salió papel. No marcar Preparado ni Entregado a cliente por una impresión.

Los destinos tienen un equipo primario explícito. Ver el tablero en dos tablets o recibir push en dos computadores no imprime dos veces. Si se permite respaldo, la reasignación es controlada y conserva la identidad; no activar dos receptores como dueños simultáneos. Un timeout después de entregar al spooler deja resultado incierto: revisar y reimprimir con referencia y marca **Copia/Reimpresión**, no reintentar físicamente en silencio. No prometer exactamente una hoja bajo toda falla posible. Impresión fallida no revierte pedido ni repite venta, inventario o pago; en modo dual la tarjeta sigue visible y muestra el fallo de papel.

**Estilo de tirilla Auraly:** ampliar PosPrintTemplateCatalog con propósitos/versiones inmutables de comanda y precuenta, siguiendo [configuración de impresión POS](pos-printer-configuration-design.md). Reutilizar anchos 58/80 mm, marca, tipografía, capitalización natural, márgenes, jerarquía y transportes. Encabezado Comanda · Cocina/Barra; mesa y ronda destacadas; hora/mesero; cantidad, producto y modificadores muy legibles; referencia de seguimiento y copia cuando corresponda. Comanda sin precios, pagos, CUFE ficticio ni apariencia de factura. Precuenta con precios/total y texto No es factura. La factura real conserva la plantilla canónica y sus datos fiscales.

Configuración por estación: salida, equipo receptor, impresora instalada, ancho, copias, corte compatible, plantilla activa y prueba. Cajón deshabilitado en comanda/precuenta. BrowserPreview abre diálogo y sirve para impresión manual; **no** es el transporte de impresión automática. Para impresión silenciosa usar el adaptador Windows instalado y la impresora comprobada. Mantener opciones disponibles desde API/configuración, no nombres de impresoras del desarrollador.

Aceptación: Cocina virtual, Barra papel y salida dual; recepción sin refrescar ni sesión comercial en cocina; recuperación tras reinicio del receptor; notificación duplicada sin reimpresión; estación y receptor aislados por tenant/sede; dos receptores sin doble toma; papel agotado/impresora apagada y recuperación visible; timeout incierto/reimpresión identificada; cancelación y cambio como corrección referenciada; consistencia entre preview y tirilla 58/80 mm; nombres largos/acentos/notas; ningún cajón ni movimiento financiero al imprimir. Validar con impresora física antes de habilitar automático. La maqueta solo ofrece vista previa y preferencias ilustrativas.

### Propietarios y persistencia

Sales posee atención, asociación de mesas y cuentas; sus casos de uso existentes se amplían con comandos de servicio. La configuración visual del menú pertenece a Catalog; el POS solo la presenta. Organization/Authorization siguen validando sede, bodega, actor y sesión.

Datos nuevos mínimos propuestos, a concretar en schema-first:

- `RestaurantAreas` y `RestaurantTables`: maestros por sede, orden/activación, capacidad/geometría y versión. Configuración visual del plano no tiene efectos contables.
- `RestaurantVisits` y `RestaurantVisitTables`: visita, responsable, comensales, apertura/salida y ocupación histórica. Índice único filtrado por mesa para vínculo activo; FKs y validación de sede evitan cruces.
- `SalesDrafts`: referencia opcional a visita, nombre de cuenta y discriminación tipada del ámbito de borrador. Una cuenta de servicio abierta pertenece a la sede/visita, con autor original y responsables auditados; no al borrador activo exclusivo de una sesión. La venta estándar conserva su índice y comportamiento. No usar un JSON libre ni perder el control de concurrencia.

La unicidad V1 se impone en persistencia: una visita activa por mesa y una cuenta de servicio por visita, con FKs y restricciones únicas compatibles con cierre/histórico. Apertura, asignación del mesero y creación de cuenta se aceptan en la misma transacción. Un doble toque o dos dispositivos concurrentes recuperan la misma cuenta o reciben conflicto; nunca crean dos. No basta con ocultar Nueva cuenta en UI.
- `SalesDraftLines`: identidad de línea estable y metadatos gastronómicos tipados; selecciones de modificadores y extras relacionados. No mezclar automáticamente líneas con preparaciones, comensales o rondas distintas.
- Configuración de grupos/opciones de modificadores y su relación con productos en Catalog, sin precios paralelos a productos/extras vendibles.
- Rondas de preparación y líneas snapshot vinculadas a las líneas de venta. Son el registro inmutable de lo solicitado a cocina, no una segunda cuenta comercial ni una tabla de jobs.

No introducir todas las tablas por anticipado: cada slice incorpora solo las necesarias junto con contratos, API, permisos, seeds, DI, admin y pruebas.

Para concretar la compatibilidad se propone un estado interno `ServiceOpen` para las cuentas gastronómicas. `Active` conserva su unicidad por sesión para la venta estándar. Los comandos autorizados de atención pueden editar `ServiceOpen`; no basta con relajar globalmente `DemandActiveVersion`. La sesión de creación queda como auditoría y el checkout recibe/valida la sesión actual del cobrador. La transición a `Issuing` congela esa atribución. El cambio de estado y sus restricciones deben aprobarse junto con la migración del contrato, sin asignar usuarios o sesiones ficticias a las mesas.

### Preparación y motores

Propuesta: la comanda confirmada es un tipo operacional del motor documental existente, con su `IConfirmedDocumentHandler`, estado y eventos de preparación bajo Sales. Confirmar una ronda crea el trabajo en `DocumentProcessingJobs` y congela sus líneas; el handler no factura, no cobra y no mueve inventario. No crear `RestaurantEngine`, `KitchenWorker` propietario ni una segunda cola general. Las actualizaciones de preparación operan sobre ese documento autorizado y versionado.

La impresión consume el snapshot de la ronda mediante los transportes actuales. La durabilidad de envío/acuse a varias estaciones debe comprobarse en el slice: los renderers actuales no demuestran por sí solos entrega de una comanda. Cualquier entrega durable adicional se integra a la outbox y activación existentes; no se inventa un spooler propietario. Un timeout de impresora significa resultado desconocido: comprobar y reimprimir con la misma referencia y marca de copia, sin prometer exactamente una impresión física.

El inventario mantiene el momento definido por ventas/pedidos canónicos. Una comanda no reserva por sí sola. Si el negocio exige consumir ingredientes al preparar, deberá aprobarse un diseño de documento/receta que entre al motor documental y evite descontarlos otra vez al vender. La conversión por familia actual no autoriza ese comportamiento.

### Flujo de extremo a extremo

1. Abrir mesa: autorizar sede/actor, validar habilitación y ocupar de forma atómica; devolver visita, cuenta y versión. Repetir la clave devuelve el mismo resultado.
2. Capturar: `PosClient` → comandos de Sales → pricing/disponibilidad canónicos → transacción de draft/líneas/recibo de mutación. Notificar cambios mediante outbox existente.
3. Enviar: versión esperada → diferencias aún no enviadas → ronda snapshot y trabajo documental. Mostrar guardado, pendiente de preparación o fallo; no confundirlos con papel impreso.
4. Cobrar: bloquear mutaciones de la cuenta elegida → preview autoritativo → `OnlineSalesCheckoutService`/ruta Edge vigente → pipeline de venta, recibo idempotente y snapshot. Un fallo fiscal/contable posterior no repite consumo, cobro ni comanda.
5. Actualizar la relación cuenta/documento como parte de la aceptación durable; si una proyección de atención requiere reconciliación, usar el mismo documento/clave. La pantalla no declara pagado hasta recuperar el resultado autoritativo.
6. Finalizar atención: exigir cuentas y entregas resueltas y salida confirmada; cerrar ocupación y marcar Por limpiar en una transacción. Listo para usar habilita otra visita.

La selección de mesa compartida exige revisar tanto `LockDraftAsync` como checkout, temporales, índices y permisos; agregar únicamente una excepción de usuario a una query sería insuficiente. Los borradores personales conservan aislamiento. El usuario que atendió y quien cobró quedan separados; el cobro pertenece a la sesión de trabajo del cobrador, no modifica ventas históricas del mesero.

### Concurrencia y desconexión

Cada comando mutante tiene versión esperada e idempotencia por sede, entidad, actor/operación y hash de contenido. SQL conserva recibos durables. Las transferencias bloquean origen/destino en orden estable. Dos aperturas de mesa o dos cobros concurrentes producen un único ganador; el segundo recibe resultado idempotente o conflicto visible, nunca último guardado silencioso.

Mesas compartidas requieren una autoridad accesible. Primera entrega: atención de mesas centralizada online; el POS Edge conserva la venta directa offline existente. Al perder conexión se muestra el último plano con hora, bloqueando ocupación, traslado y cobro de cuentas compartidas. No copiar la misma mesa a varios SQLite independientes. Operación compartida por LAN o concesión exclusiva offline requeriría decisión de autoridad y recuperación, no un interruptor de UI.

Push invalida vistas; las lecturas recuperan cambios al abrir, reconectar, volver de suspensión o ante notificación. No sondeo periódico. Los tiempos visibles pueden avanzar localmente como presentación, usando instantes del servidor y zona del negocio para reglas.

Auditar aperturas, traslados, intervenciones autorizadas, correcciones, envíos, cobros y cierre con usuario, sesión, documento/visita y correlación. Métricas: latencia de captura/envío, conflictos, comandos pendientes y reimpresiones. No registrar datos de tarjeta ni notas sensibles completas en logs.

## 9. Entrega por slices

1. **Vista táctil con paridad:** catálogo proyectado para mosaicos, filtros y dos layouts usando los mismos comandos; operaciones existentes accesibles. Aceptar únicamente con los journeys actuales funcionando en ambas vistas.
2. **Mesas y cuentas compartidas:** configuración, plano/lista, apertura, cuentas, traslado, limpieza, permisos, concurrencia y asociación a checkout. Conectividad central obligatoria.
3. **Servicio de restaurante:** modificadores, notas, rondas, entrega directa, comandas virtuales y tablero de preparación por estación, comanda/precuenta versionadas, una cuenta por mesa y precuenta. Para la primera operación real de restaurante, completar 1–3, no declarar suficiente el plano.
4. **Acceso y presentación por equipo:** identificación de mesero sin cambiar la WorkSession del cajero; una pantalla o dos superficies del mismo equipo, con capacidad limitada de atención y retorno autorizado a Cajero. El modo doble se habilita después de validar sus contratos y hardware real. Huella fuera de V1.
5. **Bar y gestión avanzada:** propina con integración financiera completa, repetir ronda, tiempos y reportes de atención. Recetas, reservas y preautorizaciones requieren su diseño propietario específico.

Activación por sede con configuración validada. Migración aditiva que preserve borradores/documentos estándar. Desactivación permite terminar las atenciones ya abiertas y evita abrir nuevas; no elimina historial. Rollback de la UI a estándar es válido para ventas estándar; cuentas de servicio abiertas deben drenarse mediante una versión compatible antes de retirar soporte backend. No revertir esquema destruyendo cuentas.

## 10. Criterios de aceptación

Objetivos de usabilidad propuestos, todavía no medidos: agregar producto simple en un toque, cambiar categoría en uno y salón en uno; abrir mesa con confirmación en dos. Prueba con personal de salón/bar: tomar una ronda de seis productos, corregir uno, enviarla, imprimir precuenta y facturar con el cajero sin asistencia. Medir errores, toques y tiempo, además de recoger comprensión de los estados.

| Escenario | Evidencia requerida al implementar |
| --- | --- |
| Venta estándar y táctil | Mismos importes, impuestos, cliente, promociones, pagos y documento; paridad de pausas/pedidos/cierre. |
| Dos usuarios abren la misma mesa | Una sola visita activa; rechazo o recuperación explícita de la segunda solicitud. |
| Dos usuarios editan/cobran una cuenta | Sin pérdida de líneas, doble cobro ni dos documentos por el mismo intento. |
| Reintento después de timeout | Misma mutación/ronda/documento; mismos números y ningún efecto físico o financiero duplicado. |
| Reabrir navegador y cambio de mesero | Recuperación durable, roles correctos e historia de atención intacta. |
| Cuenta única por mesa | Dos aperturas concurrentes no crean cuentas paralelas. Después de cerrar se abre otra con identidad nueva. |
| Producto con extras obligatorios | Validación de mínimo/máximo, precio autoritativo, snapshot e impresión consistentes. |
| Modificar después de enviar | Corrección explícita con motivo y nueva referencia; no borrar silenciosamente lo que vio cocina. |
| Recalcular cuenta con promociones | Preview del propietario de precios; conservación de cantidades, sin mover líneas pagadas ni inventar descuentos. |
| Falla de impresión/fiscal/contabilidad | Venta/ronda recuperable; reintento no vuelve a capturar o cobrar. |
| Aislamiento | IDs de otra sede/tenant y acciones sin permiso rechazados por API, incluso con UI manipulada. |
| Plano y configuración | Opciones activas desde API, orden/scope correcto, históricos inactivos legibles, sin listas de negocio quemadas. |
| Sin red | Plano identificado como desactualizado, mutaciones compartidas bloqueadas y venta directa Edge con contrato vigente. |
| Táctil/accesibilidad | Journeys en 1280×800, 1024×768, tableta vertical y 390 px; teclado, foco, targets, estados vacíos/errores y contraste. |
| Inventario | Enviar/duplicar/reimprimir comanda no genera kardex; venta/pedido conserva exactamente el efecto canónico. |

Regresiones existentes a extender: `OnlineSalesDraftCommandTests`, `OnlineSalesCheckoutTests`, `OrderRecoveryTests`, `OrderBatchInvoiceTests`, `PosArchitectureTests`, `PosDraftStoreTests`, `admin/e2e/pos-browser-regression.spec.ts` y `cash-closure-and-return-resolution.spec.ts`. Ejecutar los checks de backend, SQL y admin proporcionales a cada slice según las normas del repositorio.

Auditoría de esta entrega de diseño: se contrastó la propuesta con AGENTS, estándares, invariantes y decisiones POS citadas. Se preservan propietarios de ventas, inventario, pagos, fiscal, contabilidad, reporting y catálogos; no se implementaron motores, tablas, endpoints ni reglas nuevas. Los contratos compartidos, preparación durable, propina y operación offline de mesas son brechas identificadas, no capacidades que la maqueta pueda certificar. Builds, SQL y pruebas de negocio corresponden a la implementación posterior y no se ejecutan para esta entrega documental.

La revisión final incorpora las decisiones vigentes del usuario: contraseña secundaria sin huella; responsable por mesa con excepción supervisada; una sola cuenta por atención/mesa; sesión y dinero del cajero; facturación desde el mismo mapa; estaciones con push y salidas virtual, papel o ambas. La extensión de identidad del mesero, las restricciones únicas y el receptor automático son trabajo pendiente sobre propietarios existentes. No se altera el checkout ni se crea una ruta alternativa de inventario/contabilidad.

Evidencia de la maqueta: comprobación de sintaxis y recorridos locales con Playwright/Edge; acceso válido/inválido, responsable y excepción supervisada, autoría visible, continuidad del cajero, precuenta, cobro simulado y nueva atención sin consumo anterior, modificadores/rondas, enrutamiento Cocina/Barra/Entrega, tarjetas agrupadas, activación/silencio de audio, editor por coordenadas y arrastre, referencias físicas, selección de salida y preview de tirilla. Revisión de 20 combinaciones de vista/ancho (320, 390, 768 y 1024 px), apariencia oscura y preferencia de movimiento reducido. Sin errores de navegador en los recorridos comprobados. Esta evidencia no valida credenciales reales, push entre dispositivos, impresión física, concurrencia SQL ni funcionamiento simultáneo de dos monitores.
