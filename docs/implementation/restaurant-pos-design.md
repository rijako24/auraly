# POS para restaurantes y bares

Fecha de consolidación: 2026-09-10. Estado: **diseño cerrado de V1 para implementar por etapas; no implementado**.

Este documento sustituye las versiones anteriores de la propuesta gastronómica. Cada regla tiene una sección propietaria; planes, formularios y maqueta la referencian, sin conservar alternativas contradictorias. Se mantienen los propietarios canónicos de Auraly. Las pruebas sobre hardware son condiciones de habilitación, no decisiones funcionales pendientes.

## 1. Alcance y fronteras

Restaurante y bar es una nueva presentación táctil de ventas, con su propia captura online por mesas. Reutiliza catálogo, precios, impuestos, pagos, factura, inventario, contabilidad y consultas existentes.

**Frontera acordada:** borrador gastronómico completamente nuevo e independiente del borrador normal. La caja local, su preparación, enrolamiento, SQLite, sesión y formato actual de envío permanecen iguales. No se modifica SalesDrafts, su unicidad por WorkSession, su ciclo ni OnlineSalesCheckoutReceipts para alojar mesas. No se agrega ServiceOpen ni una entidad de visita/atención que duplique el borrador.

El endpoint de facturación admite únicamente estos metadatos adicionales opcionales: **TableId en encabezado; AddedByUserId y AddedAt por detalle**, todos nullable y omitibles. AddedByUserId identifica al usuario que incorporó el producto, sin atarlo al rol Mesero. AddedAt es la hora servidor de esa incorporación, no la emisión ni la persistencia posterior de la factura. SalesDocumentLines y PosSaleLineContract revisados no tienen actualmente ese timestamp: se agrega nullable. La ausencia de los campos conserva el contrato normal. Los IDs internos de origen, ronda, aprobación, receta y auditoría se resuelven en servidor desde la captura gastronómica; no se añaden al payload público de facturación. §6 define el enlace y la idempotencia.

V1 incluye editor del restaurante, mesas/cuentas, rol Mesero y permisos, tablets y puestos compartidos, una/dos pantallas, ingredientes/recetas, inventario excluyente, comandas virtuales/físicas, push/sonido, precuenta, factura, cancelación con pérdida, auditoría y menú digital público por QR.

Fuera de V1: huella, restaurante offline, varias cuentas simultáneas por mesa, fusionar cuentas, dividir una mesa en varias facturas, reservas de fecha/hora, lista de espera, autopedido QR, nuevos domicilios, preautorización de tarjeta, propina y reparto, subrecetas y producción anticipada por lotes. No mostrar controles operativos de esas ampliaciones. Se conservan los medios de pago combinados y pedidos comerciales soportados por Auraly.

### Vocabulario único

| Concepto | Significado |
| --- | --- |
| Salón | Área física: interior, terraza, piso. |
| Mesa | Lugar identificado con una sola cuenta abierta. Un puesto de barra numerado puede modelarse como mesa. |
| Cuenta / borrador de restaurante | El mismo pedido temporal que acumula consumos hasta facturar o cancelar. |
| Origen | Identidad permanente de ese ciclo para auditoría y vínculos; no contiene otro carrito. |
| Ronda | Envío confirmado de cantidades nuevas o correcciones referenciadas. |
| Estación | Destino de preparación o retiro, diferente de salón, bodega y computador. |
| Configuración de comanda | Recepción/salida de una estación: virtual, física o ambas. |
| Comanda | Instancia por ronda y estación; tarjeta y tirilla representan el mismo trabajo. |
| Precuenta | Presentación revisable del consumo. No es factura ni pago. |
| Cajero | Usuario que factura y recibe dinero en su WorkSession habitual. |
| Mesero | Rol de Terceros. El usuario de acceso se vincula por separado. |

## 2. Propietarios y base existente

Aplican [estándares](../estandares-de-ingenieria.md), [invariantes](../invariantes-arquitectonicas-auraly.md), [mapa](../mapa-motores-flujos-y-extensiones.md) y [cuatro motores](../decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md). La última decisión prevalece sobre textos anteriores: el motor operacional procesa efectos físicos; finanzas, fiscal y reporting tienen sus propietarios.

| Capacidad | Propietario y extensión |
| --- | --- |
| Presentación POS | Comandos/adaptadores/diálogos compartidos de PosClient; nueva vista, sin copiar reglas de la página normal. |
| Plano, mesas, captura y preparación | Sales, con persistencia gastronómica propia y casos de uso autorizados. |
| Menú, ingrediente y receta | Catalog, sobre Products, ProductImages y merchandising existentes. |
| Persona/Mesero | Parties; relación especializada sobre Party por negocio. |
| Usuarios y permisos | Identity/Authorization, credencial secundaria y aprobaciones actuales ampliadas por propósito. |
| Negocio, bodega, jornada | Organization/WorkSessions actuales. |
| Emisión | Endpoint vigente y ReceivePosSaleService; no otro emisor, numerador o calculador. |
| Inventario y costo | DocumentProcessingEngine/handlers → SqlInventoryLedgerWriter → InventoryValuationCalculator. Operaciones dedicadas reutilizan SqlInventoryOperationProcessor. |
| Dinero, cartera y asientos | AccountingProcessingCoordinator y SqlAccountingPostingProcessor. |
| Facturas/notas electrónicas | FiscalProcessingCoordinator y sus snapshots/adaptadores. |
| Auditoría | Infraestructura AuditLogs existente, con eventos de negocio transaccionales. |
| Push | Streams, gateway y dispatcher/outbox POS existentes. |
| Impresión | Perfiles, PosPrintTemplateCatalog y adaptadores actuales. |
| Reportes | Reporting y consultas operativas paginadas; no otro consolidado de ventas. |

Hallazgos revisados: SalesDrafts exige usuario/sesión y exclusividad Active por WorkSession; LockDraftAsync filtra por usuario. OnlineSalesCheckoutReceipts tiene FKs al draft normal y al siguiente. SaveProductRequest incluye ManageInventory; falta Es ingrediente. Parties aún no enumera Mesero. Las aprobaciones actuales no equivalen a login de meseros. El stream de preparación, receta, autoría y recepción automática de comandas necesitan extensión. Ninguna de estas brechas se declara implementada por existir una maqueta.

La nueva lógica gastronómica se integra del lado servidor. La ampliación de inventario/contabilidad sí necesita desarrollo en esos propietarios: que el payload cambie poco no significa que el backend solo necesite añadir columnas. No se requiere cambiar el cliente local ni sus contratos de preparación/enrolamiento.

### Frontera de compatibilidad comprobable

La mayoría de datos gastronómicos será nueva, pero no se afirma que solo cambien las tablas de factura en toda la solución: también se extienden clasificación/configuración de productos, auditoría, permisos y contratos propietarios de receta/pérdida/impresión. La captura normal y la caja local conservan su comportamiento. Las extensiones compartidas tienen defaults compatibles y solo aplican efectos gastronómicos con origen servidor validado; TableId por sí solo no activa receta, autorización ni tratamiento contable.

Hallazgo de código: PosSaleContractSerializer.Hash calcula SHA-256 del JSON serializado y ReceivePosSaleService lo compara con el recibo previo. Agregar propiedades nullable sin omitirlas puede alterar la huella de solicitudes antiguas. En los tres campos nuevos usar omisión individual cuando son nulos (JsonIgnoreCondition.WhenWritingNull), sin cambiar opciones globales, orden/nombres de campos existentes ni algoritmo de hash. Se exige conservar exactamente serialización y hash de fixtures históricos, verificar omitido frente a null explícito y recuperar reintentos previos al despliegue; no regenerar las huellas esperadas para ocultar una regresión. Los valores gastronómicos presentes sí forman parte de la huella.

Migración aditiva, sin backfill de autores/mesas inventados, defaults obligatorios ni borrado en cascada hacia ventas. Consultas e impresos admiten metadatos ausentes, sin INNER JOIN que elimine facturas normales. Validar despliegue y bloqueos sobre un volumen representativo, contratos anteriores, operación normal con restaurante deshabilitado y con restaurante habilitado, y convivencia de clientes antiguos/nuevos. La seguridad de diseño permite implementar con riesgo acotado; ausencia de regresiones requiere evidencia sobre la implementación, no se certifica con esta maqueta.

## 3. Pantalla de venta y operaciones

### Composición

**Distribución final acordada: dos pasos.** Primero, el plano ocupa el **100% del área de trabajo**, con salones, estados y selección de mesa; todavía no se muestran productos ni cuentas. Tocar mesa identifica/autoriza según §5 antes de devolver su borrador. Al aprobar, el plano deja lugar a la toma de pedido: **cuenta a la izquierda, aproximadamente 30%; categorías y productos con imágenes a la derecha, aproximadamente 70%**. Cabecera compacta; primera categoría disponible abierta, cuadrícula táctil y cuenta visibles simultáneamente. Cuenta muestra mesa, mesero responsable, actor actual, cajero/contexto financiero y total; tablet de mesero sin cajero asignado indica Pendiente de caja, sin atribuir uno ficticio.

Volver a mesas recupera el plano completo y conserva lo confirmado de la cuenta. En puesto compartido termina la intervención y exige identificar al mesero al abrir cualquier mesa, incluso la misma; no cambia la sesión financiera del cajero. Cancelar o fallar autenticación conserva el mapa, sin abrir/asignar mesa ni mostrar su detalle. La proporción 70/30 es objetivo de escritorio, no un ancho rígido: la cuenta conserva espacio para nombres, importes y controles táctiles; si no caben, se usa navegación móvil.

Cabecera: negocio/bodega, usuario/contexto efectivo, conexión y accesos Venta, Mesas, Cuentas, Comandas, Operaciones y Configuración según permisos. Salón filtra mesas, categoría/línea filtra productos, estación enruta comanda y bodega determina stock. Cambiar una dimensión no cambia implícitamente otra.

Mosaicos con nombre, fotografía propia, precio y marcas de modificadores/agotado; imagen neutra si falta foto. Posiciones estables, favoritos administrados y búsqueda global por nombre/código con ruta de categoría. No reordenar productos automáticamente durante la jornada. Elegibilidad de ingredientes definida en §7.

En tablet vertical/teléfono: Mesas → Productos → Cuenta, con resumen y acción al alcance del pulgar. Apilar sin encoger controles. Plano con zoom/desplazamiento y lista equivalente. Objetivos: botones primarios 56 px, secundarios 48 px, separación 8 px, texto operativo 16–18 px, contraste/foco y etiquetas además del color.

**Apariencia de Auraly, con modo luz y modo oscuro completos.** Reutilizar ThemeToggle/next-themes y los tokens semánticos de la aplicación; no tema exclusivo del restaurante ni configuración por mesa/negocio. En luz predominan blanco en cabecera, plano, cuenta, tarjetas y diálogos, con fondo gris muy suave, bordes discretos y acento verde azulado en acciones/selección. Evitar cabecera permanentemente oscura, grandes rellenos saturados y cuadrícula decorativa dominante en el plano de venta. En oscuro usar superficies diferenciadas y texto legible, sin invertir fotografías. Estados de mesa mantienen texto/iconos además de color. Cambiar tema conserva mesa, actor, cantidades, categoría y contexto; no exige recarga. Aplicar a POS, editor, comandas y diálogos. El menú público respeta la preferencia del visitante, sin publicar la preferencia privada del cajero.

### Interacción

- Producto simple agrega una unidad; sin mesa/cuenta activa solicita selección, sin agregar a una cuenta implícita.
- Tocar otra vez el mosaico del catálogo suma una unidad; los botones + y − de la cuenta aparecen al agregar. + conserva autor/hora del incremento; − reduce pendientes. Afectar enviados abre Cancelar/corregir (§9). **Tocar nombre, importe, cantidad como texto o cuerpo de la línea no hace nada en V1:** no abre editor, descuento ni diálogo. Modificaciones sensibles permanecen en acciones explícitas autorizadas, no en el toque de la fila.
- Modificadores por grupos administrados, mínimo/máximo, obligatoriedad y orden; extras cobrables usan precio/producto canónicos. Preparaciones distintas conservan líneas distintas.
- Agrupar visualmente no fusiona incorporaciones de diferentes autores, horas, recetas o preparaciones.
- Enviar comanda manda solo cantidades nuevas; Repetir ronda crea nuevas identidades y revalida disponibilidad/configuración/precio.
- Notas y alergias se muestran en preparación. No interpretar texto libre como descuento de ingredientes o garantía alimentaria. Cambios de receta/ruta se declaran en opciones tipadas.
- Pausar conserva la cuenta online y vuelve al mapa; no factura, reserva ni envía cocina.
- Toda captura diferencia Guardando, Confirmado, Fallido e Incierto. Sin acciones esenciales por hover/doble clic/pulsación larga.
- Diálogos reutilizan lector, teclado, foco y convenciones POS; el mapa siempre permite volver.

Estados de UI: carga, vacío con acción pertinente, sin resultados, agotado, configuración inválida, conflicto, permiso insuficiente, emisión pendiente y desconexión. La falta de conexión no parece una cuenta vacía.

### Paridad con Auraly

| Operación | Acceso y comportamiento |
| --- | --- |
| Cliente, precios, descuentos, factura | Comandos/diálogos de Sales con permisos actuales. |
| Pausas normales/pedidos comerciales | Cuentas → flujos existentes. Los pedidos conservan sus reservas; no son comandas. |
| Facturas, reimpresión y devoluciones | Operaciones → documentos originales. |
| Dinero, arqueo y cierre | Operaciones → WorkSession del cajero. |
| Entrada de mercancía | Operaciones → Purchasing. |
| Salida, avería, conteo y traslado | Operaciones → Inventory. |
| Periféricos | Configuración canónica de dispositivos/impresión. |

Abrir un flujo existente identifica claramente su contexto y conserva el de restaurante. No copiar una cuenta de mesa a otro carrito ni convertirla a pedido comercial para enviarla. El mesero no hereda permisos financieros o de inventario. La venta normal directa continúa en su pantalla actual; el restaurante atiende también puestos numerados de barra con el mismo ciclo de mesa.

## 4. Estudio de salón: editor personalizable

### Inicio y composición

Ruta: Configuración → Restaurante → Salones y mesas. Nombre: **Estudio de salón**. Iniciar con Plano vacío, Plantilla editable o Mi plano como fondo. El asistente solicita salón y dimensiones; para imagen permite calibrar una distancia conocida o trabajar en proporción, identificado como tal. No inferir medidas del local desde la referencia visual del usuario.

Escritorio: biblioteca lateral, lienzo dominante e inspector de selección. Tablet: biblioteca/inspector plegables. Herramientas: seleccionar, mover lienzo, zoom/encajar, guías, deshacer/rehacer, vista previa y Publicar. Arrastrar desde biblioteca o tocar objeto y tocar lienzo son alternativas equivalentes.

### Herramientas V1

| Elemento | Personalización |
| --- | --- |
| Mesas | Circular, ovalada, cuadrada, rectangular, esquinas redondeadas y polígono simple editable; número/nombre, capacidad, tamaño, orientación, color base y sillas. |
| Barra | Recta, en L o contorno poligonal; puestos independientes numerados cuando reciben cuentas. Dibujar Barra no crea una estación automáticamente. |
| Construcción | Paredes por segmentos con espesor, puertas/apertura visual, ventanas, columnas y divisiones; guías para unir extremos. |
| Referencias | Cocina, baños, entrada, escalera, plantas, etiquetas, zonas y objetos genéricos con forma/nombre. Sin efectos comerciales por dibujarlos. |
| Manipulación | Arrastrar/soltar, tiradores para tamaño/giro, campos numéricos, mover por teclado, duplicar, selección múltiple, alinear/distribuir, agrupar y bloquear. |
| Creación rápida | Lotes de mesas por filas/columnas con numeración y preview; duplicar salón exige códigos nuevos. |
| Capas | Fondo, estructura, mobiliario y mesas; mostrar/ocultar/bloquear. |
| Apariencia | Colores/materiales planos, etiquetas y sillas opcionales. Estados operativos mantienen texto/contraste sobre el estilo elegido. |

Geometría personalizada como datos vectoriales validados, nunca scripts/HTML del usuario. Fondos mediante almacenamiento de archivos existente con scope, validación de formato/tamaño y versión. Plantillas y biblioteca administrables se obtienen de catálogos; fixtures de la maqueta no se trasladan como listas quemadas a producción.

### Representaciones y publicación

Tres representaciones: **Plano detallado**, **Plano simplificado**, **Lista de mesas**. Los planos comparten coordenadas; simplificar oculta decoración, no reacomoda. Lista con número, salón, capacidad y estado, búsqueda y filtros. No se incluye 3D.

Autoguardado online del borrador de plano con estado visible y recuperación. Deshacer/rehacer dentro de sesión. Una concesión de edición por salón, validada en servidor y con versión: otro editor ve lectura o toma control autorizado explícitamente. Desconexión suspende cambios y exige reconciliar, sin publicar una copia local obsoleta.

Publicación con preview y validación: códigos únicos por negocio, dimensiones positivas, límites del salón, polígonos simples, referencias válidas; mesas superpuestas o atravesadas por paredes bloquean. Superposición decorativa puede advertirse y confirmarse. El dibujo no certifica aforo o normas de construcción.

Mesa ocupada mantiene identidad, número, salón y geometría publicados hasta liberarse. Se admiten cambios en mesas libres/decoración que no invadan ocupadas. Desactivar/eliminar lógicamente conserva historia. Revertir plano pasa las mismas validaciones y nunca restaura estado financiero. La publicación emite una versión completa por push; todas las pantallas muestran el mismo plano.

## 5. Meseros, autorización y equipos

### Rol y cuenta de usuario

Agregar Mesero al formulario de Terceros, filtros y role-options paginado canónico. Relación especializada Party + Business, única, con estado, código/nombre corto opcionales, versión y auditoría. Misma persona, sin duplicar identificación/contactos, sin IsWaiter en Parties y sin reinterpretar Seller.

**Marcar Mesero no crea/vincula Usuario ni permite login.** Guardar su configuración sin usuario es válido; se muestra Sin usuario vinculado. Un administrador lo vincula manualmente después por el flujo existente AppUsers.PartyId y asigna permisos. Empleado, Mesero y Usuario son independientes; no activa nómina ni WorkSession. La credencial secundaria pertenece a Authorization.

Desactivar Mesero exige resolver responsables activos mediante relevo o cierre, revoca nuevas intervenciones y conserva historial y otros roles. La [decisión del maestro de personas](../decision-maestro-parties-roles-sedes-y-cuentas-usuario.md) sigue siendo autoridad de identidad.

### Acceso a mesa

Negocio → Restaurante → Atención: **Restringir pedidos al mesero responsable de la mesa**. Política versionada del negocio; modalidad Personal/Compartido autorizada por servidor.

| Contexto | Restricción desactivada | Restricción activada |
| --- | --- | --- |
| Tablet personal | Mesero ya autenticado abre cualquier mesa permitida sin contraseña adicional. | Responsable entra directamente; otro mesero requiere aprobación supervisora antes de recibir el borrador. |
| Puesto compartido | Antes de abrir cualquier mesa, identificar al mesero con su contraseña secundaria. Cualquier mesero autorizado puede intervenir, sin aprobación por pertenencia de mesa. | Antes de abrir cualquier mesa, contraseña secundaria del mesero; si es otro responsable, además aprobación del supervisor. |
| Cajero para facturar | Accede con su usuario y permisos financieros. | Igual: no se exige ser mesero responsable para cobrar. |

Mesa libre se asigna al mesero de la apertura. Sin restricción, ese responsable es referencia histórica, no exclusividad. **Identificación y exclusividad son reglas distintas:** en compartido siempre se verifica la contraseña secundaria; la opción de negocio solo decide si otro mesero necesita supervisor. Seleccionar un nombre no basta para obtener acceso. En tablet personal, la sesión vigente ya verifica al mesero: no se repite la clave por mesa, pero se aplica la restricción y se reautentica si vence la sesión. No atribuir al responsable histórico lo que agregó otra persona. Registrar por separado usuario del puesto, mesero verificado y supervisor; la credencial no constituye prueba biométrica.

Supervisor aprueba ejecutor, cuenta, acción, versión y vigencia mediante mecanismo canónico consumible; ejecutor y aprobador son distintos. Aprobar no reasigna responsable ni concede descuentos/cobro. Relevo es acción separada auditada. Compartido vuelve al mapa y termina intervención al enviar, pausar, terminar o vencer; tablet conserva su sesión personal. No se cierra WorkSession del cajero.

Activar restricción con cuentas abiertas exige resolver responsables faltantes, sin asignarlos al administrador por defecto. API protege lectura del borrador y cada mutación. Mapa expone solo resumen autorizado. Política, permisos y credenciales revocados se revalidan online.

### Caja y pantallas

Mesero puede iniciar sesión personal sin abrir caja financiera. Cajero mantiene su usuario y jornada habitual, recibe dinero y factura muchas mesas. La cuenta pertenece al negocio; no queda bloqueada por cerrar la jornada del cajero que estaba al abrirla. Cierre informa mesas abiertas, sin facturarlas/cancelarlas automáticamente; otro cajero autorizado puede cobrarlas en su propia WorkSession.

Una pantalla alterna Atención/Cajero con revalidación al entrar a cobro. Dos pantallas del mismo computador: Atención detrás y Cajero delante, como superficies online separadas; sin alterar el enrolamiento o la preparación local. Atención usa credencial/capacidad limitada, nunca el token financiero pleno. Cambio de mesero no cambia al cajero. Tablets de meseros y cocina son independientes del número de monitores.

Permisos nuevos se registran en Authorization: administrar restaurante/plano/comandas, atender, ver/iniciar/listo/entregar, trasladar/relevar, aprobar intervención y cancelación preparada. Reutilizar permisos existentes de descuentos, factura, devolución, impresión e inventario. Administrador los recibe por sincronización determinista. Aprobar una pérdida vinculada no concede al mesero permiso general de averías.

## 6. Borrador independiente, factura y auditoría

### Ciclo

Cuenta: Abierta → En emisión → Facturada, o Abierta → Cancelada. Pausa/Precuenta son indicadores de Abierta. Cancelación pendiente impide emitir lo afectado hasta resolverla. Al iniciar emisión se congela versión/total y no se aceptan nuevas capturas.

Mesa: Libre, Ocupada, Por limpiar, Fuera de servicio. Cuenta abierta/en emisión ocupa mesa. Facturada queda Ocupada/Pagada hasta resolver entregas y salida de clientes; finalizar conduce a limpieza o Libre si ese paso está desactivado. Fuera de servicio solo sin ciclo ocupado. No nuevas cuentas simultáneas ni cambios de estado arbitrarios.

Abrir explícitamente fija OpenedAt del servidor aunque todavía no haya productos; no deducir apertura del primer producto. Restricción transaccional única de origen activo por mesa. Traslado solo a mesa libre del mismo negocio/bodega: conserva origen, responsable, rondas y autoría; notifica nueva ubicación. Origen/destino se bloquean en orden estable. No fusionar cuentas.

### Datos y enlace de emisión

Sales incorpora persistencia de salones/planos/versiones/objetos, mesas, estaciones/comandas, **borradores gastronómicos**, incorporaciones, rondas/revisiones, preparación y vínculos de efectos. Catalog incorpora receta/componentes/versiones/modificadores; Parties incorpora Mesero. No se crean job tables nuevas ni tabla de visita adicional.

Cada borrador recibe origen permanente. Incorporación tiene producto, cantidad, autor, evidencia de identidad, hora/orden, receta/modificadores y versión. El resumen puede agrupar, pero no destruir proveniencia. Reducir cantidad conserva quién incorporó y quién corrigió. Una única línea agregada con un solo mesero no puede representar contribuciones de varias personas: al emitir se separan los detalles necesarios.

**El endpoint normal conserva su función:** valida y persiste los metadatos nullable de §1 y ejecuta su emisor actual. Se conservan SoldByUserId/WorkSession del cajero. IDs de mesero/mesa se validan en tenant/negocio cuando están presentes; jamás confieren permisos.

AddedByUserId referencia AppUsers.UserId; no usar un campo ambiguo que admita indistintamente UserId o PartyId. AppUsers ya vincula PartyId. Para esta V1 toda intervención se autentica y, por tanto, tiene usuario: no se necesita otro PartyId público en el detalle para identificarla. La auditoría congela también la persona/rol y nombres históricos al capturar; no reconstruye autoría histórica desde un vínculo de usuario editable. Desactivar usuario o cambiar su vínculo no borra ni reasigna la autoría anterior. En restaurante, el servidor obtiene autor/hora del borrador autorizado y rechaza discrepancias en la emisión; no confía en un ID de mesero suministrado libremente por el cliente. En compartido, el autor es el mesero autenticado con clave secundaria, no el cajero de la sesión financiera ni el supervisor que aprobó. Si dos usuarios agregan el mismo producto se conservan incorporaciones/detalles distintos.

La coordinación del borrador nuevo vive en Sales, fuera de OnlineSalesDraftService/OnlineSalesCheckoutReceipts. Al confirmar la cuenta gastronómica registra en su propio estado un intento durable: origen, versión, cajero/WorkSession, DocumentId e idempotencia compatibles con la emisión existente y hash del snapshot. Usa el mismo endpoint/servicio de emisión, sin otro cálculo o factura intermediaria. El vínculo servidor DocumentId → origen permite resolver auditoría/receta sin nuevos campos públicos. Debe quedar preparado antes de enviar a emitir y ser único por origen/documento. Un estado de intento/recibo no es otro borrador ni una cola de facturación.

La implementación valida la procedencia contra ese intento, no infiere una receta histórica solamente porque TableId sea no nulo. La factura conserva mesa y autor/hora nullable; etiquetas históricas de mesa/mesero se congelan internamente para que renombrar no cambie impresos. Atribuciones y contexto se guardan junto al snapshot aceptado. Fallos entre emisión y limpieza se reconcilian mediante DocumentId/clave originales, nunca reenviando como otra venta.

Tras documento aceptado, recibo/vínculo durables y referencias de auditoría/comandas seguras, puede purgarse el contenido del borrador. La mesa conserva referencia al ciclo/documento ocupado, sin copiar líneas. No cascadas desde borrador a eventos, comandas, pérdidas o recibos. Un cancelado sin factura conserva historia y soportes; no requiere factura cero. La limpieza se recupera por el ciclo propietario, sin crear worker de purga exclusivo.

### Precuenta y cobro

Mesero imprime precuenta con referencia/QR, mesa, revisión, fecha/hora, líneas y total: **Precuenta · No es factura**. QR solo localiza cuenta para operador autorizado. No registra pago ni consume numeración fiscal. Cliente entrega dinero y mesero lo lleva al cajero; no se crea caja del mesero.

Cajero toca mesa → Facturar → preview vigente → medios → confirma. Cambios posteriores a precuenta muestran Consumo actualizado. Bloquear productos por enviar, cancelaciones pendientes o resultado incierto; se permiten entregas pendientes indicadas, sin marcarlas listas por cobrar. Varios medios soportados deben sumar total; una cuenta/un documento.

UI distingue emisión, confirmación financiera y estado fiscal. Una falla DIAN/contable no repite cobro/stock. El borrador no declara éxito sin recibo autoritativo. Después de facturar, correcciones usan documentos de §9.

### Auditoría completa

Cada mutación y evento de negocio se guarda transaccionalmente: apertura, agregar/quitar/cantidades, precios/descuentos, notas/modificadores, actor/autorización, envío/corrección/cancelación, preparación, traslado, precuenta/impresión/copia, intentos/resultados de cobro/emisión, cierre y configuración. Efectos externos distinguen solicitud/acuse/resultado. Si falla auditoría no se confirma la mutación.

Evento: ID, origen, tenant/business, mesa contextual, incorporación/ronda/revisión, usuario efectivo, mesero verificado y evidencia de autenticación, supervisor, dispositivo/superficie, hora servidor, secuencia/versión, operación/correlación, motivo y antes/después relevantes. No contraseñas/tokens/datos de tarjeta ni copia de estos hechos a logs inseguros.

Consulta por origen antes de factura; factura usa su vínculo interno al mismo origen. No copiar/reasociar evento por evento al emitir ni editar pasado: corrección crea nuevo hecho. Retención sobrevive al borrador y respeta política documental/privacidad canónica. Identidad histórica mínima no depende de usuario activo.

CentralAuditPolicy actual usa whitelist EF y no garantiza auditoría comercial completa. Extender AuditLogs/contratos e índices de origen/secuencia con escritura enlistada en la transacción Sales; no truncar hechos con límites de diagnóstico ni crear otra auditoría gastronómica. Consulta paginada en el servicio actual.

## 7. Productos, recetas e inventario

### Ingredientes y receta

En Productos, checkbox **Es ingrediente**: “Se utiliza en recetas y no está disponible para venta directa”. Sigue visible en compras, administración e inventario. El catálogo de venta online excluye ingredientes de mosaicos, búsqueda, favoritos y códigos; Sales valida adición directa por ID. No desactivar el producto globalmente para ocultarlo.

La clasificación se proyecta del lado servidor en los catálogos de venta compatibles; no cambia DTO, preparación o sincronizador del cliente local. No se promete que un terminal antiguo desconectado conozca una nueva clasificación. Su envío normal sin metadatos conserva el contrato y conciliación existentes. La exclusión gastronómica se garantiza online; no se añade soporte de recetas gastronómicas al cliente local en esta V1. El alta/clasificación identifica ese alcance para no ofrecer una garantía offline inexistente.

Sección Receta del producto preparado: solo ingredientes activos autorizados, con cantidad positiva por unidad vendible y unidad compatible. Un mismo ingrediente en varias recetas; no familias/ProductLinks, subrecetas o ciclos. Conversión y precisión proceden del catálogo de unidades.

Receta versionada/publicada; la ronda congela versión, cantidades y modificadores de cada incorporación. Una ronda posterior puede usar nueva receta, sin recalcular lo enviado. Notas libres no alteran stock; omisiones/extras que cambian receta tienen efecto tipado. Extras destinados a otra estación se agregan como producto separado en V1.

No desmarcar ingrediente en recetas activas sin resolver dependencias. Cambios que invaliden cuentas abiertas requieren resolución/revalidación explícita. Migración deja productos existentes sin marcar, sin inferencia por nombre.

### Modalidades excluyentes

Producto con receta elige **Qué inventario se descuenta al vender**:

| Modalidad | Configuración | Efecto |
| --- | --- | --- |
| Producto terminado | ManageInventory activo en terminado; receta informativa para esta venta. | Descontar terminado, no componentes. |
| Ingredientes de la receta | Terminado sin saldo propio; receta válida con ingredientes inventariables. | Descontar componentes, no terminado. |

Una sola selección validada en Catalog; no dos interruptores independientes. Los ingredientes mantienen control de stock para sus otros usos. Productos sin receta mantienen su política actual. Cambiar modalidad exige resolver existencias/reservas/operaciones afectadas con movimientos explícitos, sin convertir inventario editando un campo.

Vender terminados preelaborados requiere existencias de entradas/operaciones legítimas. Receta no crea automáticamente producción. Producción anticipada por lotes queda fuera de V1; no se inventa una entrada gratuita ni se presenta conversión por familia como receta.

### Momento elegido para V1

**Descontar al procesar la factura aceptada, tanto preparados como directos.** Capturar/enviar/Empezar/Listo/Entregado/Imprimir no reserva ni mueve inventario. Única excepción: recurso ya preparado que se cancela antes de facturar, cuya pérdida se registra por §9 y se excluye del consumo facturado.

Descontar al iniciar cocina se descarta para esta versión: requeriría consumo parcial previo, stock en proceso, liberación/reclasificación y distinguir en factura lo ya aplicado. No se añade un flag “ya consumido” al endpoint ni un motor de comandas para descontar inventario.

Disponibilidad de captura consulta InventoryBalances según terminado o ingredientes. Es orientativa: mesas abiertas no reservan y pueden competir por stock. Antes de aceptar factura se aplica validación/resolución de existencias canónica. Procesar un hecho ya aceptado conserva orden y política de negativos vigentes; no introducir un rechazo tardío contrario al motor.

Costo reconocido por SqlInventoryLedgerWriter en su secuencia, con InventoryValuationCalculator. Costo del plato por receta es suma de sus componentes valorados; no precio de menú ni otra fórmula UI. Guardar asignación de cantidades/costo por detalle aunque el writer consolide movimientos de ingredientes. Precisión/redondeos canónicos con residuo determinista. V1 no capitaliza mano de obra/indirectos mediante recetas; siguen sus gastos actuales.

La adaptación de receta se resuelve desde snapshot servidor de origen gastronómico. Documentos normales sin dicho origen conservan efecto vigente. Cambiar la receta después no modifica costo histórico, devoluciones o pérdidas.

## 8. Estaciones, comandas, push e impresión

### Configuración única

**Producto → Estación → Configuración de comanda → Virtual / Física / Ambas**.

Estación: código/nombre, negocio, orden, activación, preparación/retiro y objetivo de tiempo. Producto vendible: modo directo/preparado, estación explícita y nombre corto opcional. Categoría facilita asignación inicial, pero se persiste destino del producto; cambiar categoría no redirige pendientes.

Comanda de estación: nombre, estación, modalidad, accesos/dispositivos, aviso y vínculo a equipo/perfil de impresora. Una activa por estación/negocio. Perfil de Printing es única fuente de nombre de impresora, ancho 58/80 mm, copias, corte/plantilla; el formulario Comanda lo administra mediante ese propietario, sin duplicar en Estación o Producto. Cambiar impresora no exige editar productos.

Directos también tienen destino de retiro/Entrega para conservar trazabilidad sin poner cerveza embotellada a cocinar. Estación inactiva/sin comanda bloquea envío con error visible; nunca fallback a Cocina. Nuevo destino/perfil aplica a nuevos envíos; pendientes se reasignan explícitamente con auditoría.

### Ronda y estados

Una tarjeta por mesa/ronda/estación, con número grande, tiempo, mesero, cantidades y notas/modificadores. Pantalla y tirilla comparten ID/contenido; acuses de salida no son estado de preparación.

Preparación: Pendiente → En preparación → Listo → Entregado. Directo: Por entregar → Entregado. Cantidades parciales 2/3, sin avanzar más de lo pendiente. Cancelación usa §9; no borrado silencioso. Vista Todas exige coordinación; cada estación lee/muta solo lo autorizado.

Tarjetas de bajo brillo, contraste, jerarquía mesa/productos/notas/tiempo/acción y botones 56 px. Nueva llegada con transición 200–350 ms; no cambiar posición durante toque, destellos o animación perpetua. Movimiento reducido elimina desplazamiento y conserva señal textual.

**Comandas sin efecto físico no crean DocumentProcessingJobs.** Sales acepta ronda/estados, recibo, auditoría y outbox en transacción; sincronización distribuye. No KitchenWorker, RestaurantEngine ni cola propietaria. Solo factura/pérdida u otros efectos físicos usan motor operacional; reclasificaciones financieras usan Accounting.

### Push y audio

Extender streams/gateway/dispatcher/outbox existentes de acuerdo con la [decisión push](../decision-pos-sync-push-sin-polling.md). Partición tenant/business/estación, autorización al suscribir/leer/actuar. Aviso invalida y cursor durable recupera al abrir, reconectar, reanudar o recibir notificación; sin HTTP periódico. Backoff de reconexión del socket no es sondeo de pedidos.

Notificación duplicada no crea tarea, impresión o sonido. Pitido de dos notas, volumen/silencio/prueba por equipo, Activar sonido por gesto cuando el navegador lo requiere. Recuperación histórica sin ráfaga de tonos; entradas nuevas agrupadas si llegan juntas. Tablet suspendida no garantiza sonido: recupera al reanudar. Mostrar conexión/última actualización/pendientes; no vacío engañoso.

### Papel y receptor

Computador autorizado con impresora Windows USB/red y componente de impresión Auraly ejecutándose. Tablet no hace de puente. No exige sesión financiera de cocina; sí identidad técnica y autorización de destino. Esta recepción de comandas es extensión del adaptador de impresión, aislada del enrolamiento/preparación y envío de facturas de la caja local. No cambia sus rutas ni su proceso comercial.

Un receptor primario reclama por lease con identidad origen/ronda/estación/revisión/destino/copia en el almacenamiento/outbox actuales; dos tablets no imprimen dos veces. Respaldo mediante reasignación exclusiva auditada. Impresión no se dispara por renderizar tarjeta. Dispositivo debe estar online para reclamar; lo ya entregado a Windows puede terminar desconectado.

Estados de salida: Pendiente, Aceptado por receptor, Entregado al sistema de impresión, Fallido, Resultado incierto. Spooler no prueba papel físico. Incertidumbre requiere revisión/reimpresión explícita marcada Copia; no retry físico ciego. Fallo de papel no borra tarjeta ni repite factura/stock.

Reutilizar [impresión POS](pos-printer-configuration-design.md), PosPrinterConfigurationStore y PosPrintTemplateCatalog. Plantilla Comanda versionada: estación, mesa, ronda/hora, actor, productos/cantidades/modificadores y corrección referenciada; sin precios fiscales ni cajón. Precuenta/factura mantienen propósitos propios. Reimpresión usa versión congelada.

Windows directo para automático; BrowserPreview manual y File diagnóstico. Adaptador actual invoca Auraly.Desktop en contexto Windows compatible: proceso disponible e impresora probada. No prometer sesión 0, equipo dormido o sesión Windows cerrada. No agregar servicio/worker propietario de impresión.

## 9. Cancelación, pérdida y contabilidad

### Investigación y decisión

Fuentes oficiales consultadas el 2026-09-10, con contexto colombiano. [IAS 2, IFRS Foundation](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/) establece reconocimiento como gasto de pérdidas de inventario cuando ocurren. Aplicación de diseño: plato preparado cancelado y descartado genera pérdida al costo registrado, no por precio de menú. Cuenta específica según perfil contable del negocio; no código universal impuesto por esta norma.

El [Estatuto Tributario, art. 64](https://normograma.dian.gov.co/dian/compilacion/docs/estatuto_tributario.htm#64) condiciona disminuciones fiscales y soportes; una cancelación no es automáticamente deducible ni toda pérdida es obsolescencia. El [art. 486](https://normograma.dian.gov.co/dian/compilacion/docs/estatuto_tributario.htm#486) prevé ajustes de IVA descontable por pérdidas con condiciones/excepciones. Registrar evidencia para revisión tributaria, sin aplicar deducibilidad, porcentajes o IVA automáticamente por cancelar.

La [doctrina unificada DIAN, §3.1.7](https://normograma.dian.gov.co/dian/compilacion/docs/concepto_tributario_dian_0000106_2022.htm) distingue anulación de operación y falta de pago; las correcciones de factura usan nota/caso de uso según tipo y estado. No borrar factura ni reutilizar número.

### Regla y flujo

**Un plato preparado cancelado y sus ingredientes no regresan al inventario disponible.** Separar retiro del cobro y disposición física. No cargo automático a mesero/cocinero/nómina.

| Situación | Resolución | Efecto |
| --- | --- | --- |
| Error antes de enviar y sin preparar | Editar/quitar con auditoría. | Sin stock ni pérdida. |
| Enviado, estación confirma No preparado | Solicitud, confirmación de estación y autorización de corrección enviada. | Sin salida/reposición; retirar cobro y avisar cancelación. |
| Preparado/consumo irreversible, sin factura | Cantidad perdida, motivo, confirmación y aprobación supervisora. | Retirar cobro; baja única de ingredientes o terminado y gasto al costo. |
| Preparación parcial | Estación informa componentes realmente usados, supervisor valida. | Baja solo lo consumido; el resto nunca salió. No inferir un porcentaje uniforme de receta. |
| Preparado, factura aceptada | Cajero tramita devolución/nota sin reposición. | Corrección financiera/fiscal y reclasificación del costo original; cero movimientos adicionales de stock. |
| Rehacer | Pérdida/reproceso y nuevo intento vinculados. | Una unidad cobrable; registrar recursos extra una sola vez, según momento descrito abajo. |

Solicitud sobre enviado coloca Cancelación solicitada; bloquea transiciones incompatibles de esa cantidad hasta resolución. No suponer No preparado porque pantalla muestre Pendiente. Virtual: estación confirma realidad; papel: supervisor registra confirmación obtenida de cocina sin fingir acuse digital. Concurrencia con Empezar/Listo exige reconciliación por versión y realidad física.

Retirar preparados antes de factura exige aceptar duraderamente su efecto de pérdida; la cuenta muestra incidencia pendiente hasta resultado. Si hay faltantes contables/físicos, conservar hecho y resolución canónica, sin inventar entrada compensatoria ni perder incidente. Cancelación total cierra sin factura una vez resueltos efectos y salida. Historial sobrevive al borrador.

Plato consumido/entregado y cliente no paga no es desperdicio por definición: continuar cobro/cartera autorizado; no anular venta ficticiamente. Cortesías, donación/autoconsumo no se disfrazan de pérdida. Producto directo intacto solo puede reponerse por devolución estándar si se verifica vendible; jamás por inferencia para preparados.

### Integración antes y después de factura

Antes de factura: reutilizar **Damage** del módulo Inventory. Su modalidad actual lleva avería a AVE a valor cero; agregar disposición tipada **Destrucción/consumo irreversible**, salida definitiva sin entrada a AVE ni otra bodega. Avería normal mantiene su default y comportamiento. No almacenar carne cocinada como carne cruda en AVE.

Sales acepta cancelación/snapshot/aprobación/auditoría y fuente Damage en transacción mediante contratos enlistados de los propietarios; ninguna tabla tiene dos writers. Damage usa DocumentProcessingJobs → handler/processor → writer actuales. Modalidad receta baja componentes congelados; modalidad terminado baja unidades del terminado. La factura excluye cantidades canceladas. Accounting recibe la señal canónica, nunca se escribe asiento desde cocina.

Después de factura: extender devolución/nota con disposición **Sin reposición por consumo irreversible** por detalle, cantidad física devuelta cero y costo original vinculado. No generar entrada de stock ni otro Damage que repita la baja. El handler y SqlAccountingPostingProcessor deben distinguir este caso del retorno normal que debita Inventario. La fuente financiera conserva corrección y reclasificación una sola vez, esperando costo original confirmado cuando todavía esté pendiente.

Rehacer antes de factura: Damage consume intento fallido; factura consume reemplazo. Después de factura: venta original conserva ingreso/costo, y consumo adicional del reemplazo se reconoce con motivo de reproceso por el propietario de pérdidas cuando se realiza. No registrar dos pérdidas por el mismo exceso de consumo ni otra factura al cliente. Autorizar rehacer no equivale a consumir: confirmar realización/consumo irreversible con evidencia.

### Asientos ilustrativos y soporte

Ejemplo: precio $30.000, costo registrado $10.000; impuestos omitidos solo para explicar costo.

- Preparado cancelado antes de factura: débito Pérdida por alimentos preparados $10.000 / crédito Inventario $10.000. No ingreso, caja ni pérdida por $30.000.
- Después de factura y baja ya reconocida: débito Pérdida por alimentos preparados $10.000 / crédito Costo de ventas $10.000, más corrección financiera/fiscal de venta por importe procedente. No debitar ni volver a acreditar Inventario.
- Reembolso, si procede, pertenece al cajero y al flujo financiero actual; no a estación.

Política elegida para descarte. Accounting resuelve DamagedInventoryExpense, cuentas y centro de costo desde configuración; etiquetas por motivo pueden identificar pérdida gastronómica. No códigos PUC/tasas quemados. Con/sin libro mayor mantiene el modo canónico congelado; error contable no reaplica stock.

Soporte: origen, mesa, ronda/incorporación, cantidades solicitadas/canceladas/preparadas, receta/componentes, bodega, costo y referencia de valoración, motivo BusinessReasons, disposición, actor/confirmación de estación/aprobador, fechas y documento de pérdida/corrección. Adjuntos y revisión contable/fiscal según caso. Aprobación operativa no sustituye acta/firmas tributarias: permitir anexarlas en el historial. Reporte distingue valor retirado del cobro y costo perdido; no etiqueta pérdida como deducción aprobada.

## 10. Menú digital público por QR

### Experiencia del cliente

Cada tenant tiene un enlace público estable para imprimir como QR. Abre el menú en el navegador del celular, sin instalar app, login o datos personales. Un solo negocio publicado abre directamente su carta; varios negocios permiten elegir sede desde el enlace general y cada sede tiene enlace directo. No mezclar precios/cartas de negocios distintos. URL con dominio público configurado y alias estable; esta entrega no inventa dominio ni publica un tenant real.

**Única jerarquía visible: Categoría → Productos.** Pescados, Carnes, Pastas, Bebidas y Postres son ejemplos. Sin líneas, grupos, subgrupos ni familias. Reutilizar categoría del producto, corregida en Productos; no otra taxonomía o catálogo público de productos.

Diseño móvil: marca/logo/sede y portada opcional compactos, categorías accesibles inmediatamente, primera categoría abierta y búsqueda por nombre/descripción. Categorías con desplazamiento horizontal y alternativa Ver categorías. Tarjetas con foto, nombre, descripción breve, precio final y moneda; tocar abre ficha con foto amplia, descripción completa y presentaciones/precios existentes. Volver conserva categoría y posición. Escritorio amplía columnas. Dos estilos configurables: **Carta fotográfica** y **Carta compacta con miniaturas**, mismo contenido/orden. Acento/portada de marca con contraste validado. Sin imagen, tarjeta limpia; no foto falsa.

Agotado se muestra cuando existe indisponibilidad comercial declarada, sin publicar stock exacto. Ocultar categorías vacías, ingredientes, inactivos y productos sin precio publicable; los últimos generan incidencia administrativa, no precio cero inventado. Favoritos del POS no crean categorías públicas adicionales.

Animaciones: cambio de categoría 180–250 ms, ficha/imagen 200–300 ms, respuesta al toque y entrada breve de tarjetas visibles. Sin carrusel automático, vídeo automático o movimiento perpetuo. Movimiento reducido conserva toda la interacción sin desplazamientos. Imágenes responsivas, primeras prioritarias y resto lazy, espacio reservado sin saltos. Controles de 48 px, foco, lector de pantalla, zoom de texto y lectura fluida desde 320 px.

V1 es **solo consulta**: sin carrito, pedido, pago o acceso a mesa/factura. El QR contiene únicamente URL pública, no token/usuario/cuenta. Es distinto del QR de precuenta para operadores.

### Configuración, publicación y seguridad

Ruta Configuración → Restaurante → Menú digital: habilitar, negocio, alias/enlace, marca/estilo, orden de categorías/productos, preview móvil y descargar QR. Productos mantiene categoría, nombre, imagen y precio; merchandising incorpora **Mostrar en menú digital** y descripción comercial pública cuando la interna no sea apta para publicación. No capturar el precio otra vez. Publicar por primera vez exige revisión explícita de selección/textos/precios; productos nuevos no se hacen públicos por crearlos.

Precios desde canal/lista pública elegida en el motor actual, con importe final/moneda/impuestos canónicos; sin precios privados por cliente ni propina/cargos ocultos. Cambios publicados actualizan el menú versionado manteniendo QR; borradores no se publican. Precio horario usa reglas vigentes del motor y caché hasta siguiente cambio efectivo. No calcular precios/impuestos en frontend. Despublicar muestra Menú no disponible. Cambio de alias mantiene redirección controlada, sin reasignar la dirección vieja a otro tenant.

Catalog/merchandising posee selección/proyección; Organization resuelve tenant/negocio/alias, Branding y archivos conservan propietarios. Endpoint anónimo de solo lectura con DTO permitido: marca, categoría, nombre/descripción pública, imagen, presentación, precio final, moneda, disponibilidad comercial y revisión. Nunca costos, recetas, proveedores, márgenes, saldos, usuarios, mesas o cuentas. Scope resuelto desde publicación; parámetros no amplían visibilidad/tenant. Rate limit, caché por tenant/negocio/canal/revisión y ETag; contenido escapado, sin HTML ejecutable.

Invalidación canónica al cambiar publicación/precio, sin worker/cola nuevos. En páginas abiertas, gateway existente con suscripción pública limitada a revisión del menú, jamás a streams privados; recuperar al aviso/reanudar conservando posición. Cambio de carta avisa sin mover contenido durante un toque. Sin conexión, último contenido marcado desactualizado hasta revalidar; no promete precio vigente ni habilita POS offline.

QR descargable PNG/SVG y composición imprimible con nombre, Ver menú y URL legible. Generar QR real para URL configurada y probar escaneo; no QR decorativo. On-premise requiere dirección HTTPS públicamente accesible/autorizada o publicación pública existente: no exponer caja local ni prometer acceso celular a IP privada. Es condición de despliegue verificable. La maqueta es preview sin enlace público activo.

Referencias oficiales consultadas el 2026-09-10: [Sunday menú](https://sundayapp.com/digital-menu/) y [configuración](https://intercom.help/sundayapp-help/en/articles/12857479-how-to-create-a-digital-menu), [Menutech menú](https://www.menutech.com/en/features/digital-menus) y [URL/QR](https://help.menutech.com/article/1338-how-can-i-share-my-menutech-menu-via-chromecast-url-or-qr-code). Se toman marca/fotos, acceso sin app, categorías y enlace reutilizable. La propuesta Auraly conserva precio único del catálogo y no adopta pedidos, traducción automática o IA de esos proveedores.

## 11. Integridad, conexión y administración

Mutación: OperationId, hash, versión esperada y recurso; actor/scope derivados de autorización. Misma clave/contenido recupera recibo; otro contenido se rechaza. Apertura y emisión únicas por origen; cantidades controladas por incorporación/porción/intento físico. Reloj servidor y secuencia, no reloj del dispositivo.

Restaurante solo online contra servidor SaaS/on-premise accesible. Sin captura desconectada, draft local autoritativo ni pedidos acumulados para subir. Sin red: último confirmado en lectura/desactualizado, bloqueo de mutaciones/envío/cobro/reclamos de impresión. Resultado incierto se concilia por clave original. No fallback de mesa a caja local.

Configuración por propietario: Organization habilitación/acceso/limpieza/comensales; Sales plano/mesas/estaciones/comandas; Catalog ingredientes/recetas/menú; Parties Mesero; Identity/Authorization acceso; Printing perfil físico; Accounting cuentas. Formularios enlazan sin duplicar campos.

Catálogos/opciones de negocio desde tabla/API, paginados si aplica; labels y presets editables persistidos. Estados técnicos tipados protegen transiciones. Motivos desde BusinessReasons. Sin semillas que sobrescriban personalización ni datos del desarrollador como defaults.

Observabilidad: latencia de captura/envío/recepción, conflictos, estación retrasada, impresión/reimpresión, cancelaciones pendientes, costo perdido, emisión incierta y efectos pendientes. Consultas paginadas e integración con Reporting; no otro consolidado ni jobs preventivos.

## 12. Plan de implementación y habilitación

Diseño cerrado; no quedan elecciones funcionales delegadas a quien programe. Nombres físicos de contratos/tablas se ajustan a convenciones, conservando estas fronteras.

| Etapa | Entrega vertical |
| --- | --- |
| 1. Identidad/configuración | Mesero independiente, permisos/matriz de acceso, estaciones/comandas y perfil de impresión. |
| 2. Plano y mesas | Estudio de salón, publicación/versiones, mapa/lista, cuenta única y acceso autorizado. |
| 3. Cuenta/factura/auditoría | Persistencia gastronómica, incorporaciones, intento independiente y emisor vigente con campos opcionales; limpieza segura. |
| 4. Preparación | Rondas/parciales/correcciones, push/cursor, audio e impresión por estación. |
| 5. Receta/pérdida | Elegibilidad, componentes/snapshots, consumo al facturar y cancelación antes/después, Damage sin AVE, devolución sin reposición y contabilidad. |
| 6. Menú público | Proyección segura, categorías/productos, marca, enlace/QR, precios vigentes y experiencia móvil. |
| 7. Experiencia integrada | Vista táctil/diálogos y recorridos completos en dispositivos; pruebas de compatibilidad y fallos. |

Operación real gastronómica requiere las siete etapas, auditoría y cancelaciones incluidas. Habilitación por negocio tras configuración válida; no interfaz operativa con integridad incompleta. Migraciones aditivas schema-first, contratos opcionales y seeds idempotentes. No cambiar preparación/enrolamiento local, draft normal, recibos normales o formato histórico de sus facturas.

Deshabilitar impide nuevas aperturas y deja terminar cuentas con una versión compatible. Rollback conserva datos/origen/eventos/trabajos/plantillas y no vuelve a un cliente incapaz de leer cuentas abiertas. Modificar datos del restaurante no borra historia de ventas normales. Trabajar en único checkout preservando cambios ajenos.

## 13. Criterios de aceptación

| ID | Escenario | Resultado obligatorio |
| --- | --- | --- |
| A01 | Caja local/normal | Mismo enrolamiento/preparación/draft/envío y resultado sin nuevos campos; omitidos/nulos compatibles, JSON/hash históricos idénticos y reintentos anteriores reconocidos; probar con restaurante habilitado y deshabilitado. |
| A02 | Mesero sin Usuario | Rol guardable sin alta/vínculo automático ni login; vinculación posterior manual sobre misma Party. |
| A03 | Matriz de acceso | Compartido exige clave con restricción activa o inactiva; otro responsable requiere supervisor solo con restricción. Personal reutiliza sesión vigente. Clave inválida/cancelada no revela borrador ni abre mesa; volver al plano termina intervención compartida. Cobro mantiene permisos del cajero. |
| A04 | Concurrencia | Una cuenta/factura por origen; conflictos sin pérdida ni doble efecto. |
| A05 | Emisión y limpieza interrumpidas | Resultado original recuperable, atribución/auditoría intactas, nuevo ciclo con otra identidad. |
| A06 | Precuenta vieja | Mostrar consumo actual; no cobrar versión obsoleta. |
| A07 | Offline/reconexión | Cero nuevas mutaciones desconectadas; cursor/recibo sin duplicados; no polling. |
| A08 | Rondas/estaciones | Productos a destino, tarjeta por ronda/grupo, parcial 2/3 y correcciones coherentes. |
| A09 | Virtual/física/ambas | Una identidad, contenido consistente, acuses separados, múltiples tablets sin copias accidentales. |
| A10 | Impresora/fallo | Pruebas físicas 58/80 mm, recepción sin cajero de cocina, incertidumbre y copia explícitas, sin doble stock/pago. |
| A11 | Sonido/animación | Nueva llegada una vez; bloqueo/mute visibles, ráfagas/reconexión y movimiento reducido. |
| A12 | Editor completo | Plantilla/vacío/fondo, mesas personalizadas, paredes, arrastre/tiradores/giro, selección múltiple, deshacer y campos. |
| A13 | Publicación/plano | Detallado/simplificado fieles y lista equivalente; validación, versión y ocupado protegido al publicar/revertir. |
| A14 | Ingredientes/receta | Selector solo ingredientes; exclusión online incluso por ID, unidades/versiones y dependencias válidas. |
| A15 | Inventario excluyente | Factura descuenta terminado o componentes, nunca ambos; comanda no genera kardex. |
| A16 | Cancelación antes de factura | No preparado: cero stock. Preparado: baja/gasto únicos al costo y sin retorno a AVE/disponible. |
| A17 | Cancelación después | Corrección sin reposición; reclasificación sin doble costo/stock; reembolso único. |
| A18 | Parcial/rehacer | Consumo real por intento y una unidad cobrable; sin pérdida duplicada. |
| A19 | Falla de motor | Rollback íntegro, retry del mismo job, sin avance o efectos parciales. |
| A20 | Auditoría | Agregar/quitar/enviar/cancelar/autorizar/cobrar visibles desde factura y origen sin factura. |
| A21 | Scope/permisos | IDs ajenos, revocación, replay y manipulación rechazados por API y receptor. |
| A22 | Relevo cajero | Mesas permanecen; siguiente cajero cobra en su jornada; mesero sin dinero propio. |
| A23 | Hardware/accesibilidad/tema | 1280×800, 1024×768, tablet vertical y 320–430 px; teclado/foco/lector y dos monitores sin fuga de sesión. Luz/oscuro en plano, cuenta, catálogo, editor, comandas y diálogos; contraste WCAG AA, estados reconocibles y cambio de tema sin perder contexto. |
| A24 | Migración/rollback | Contrato previo legible, filas históricas conservadas y reversión compatible con cuentas abiertas. |
| A25 | QR público por tenant/sede | Escaneo real abre carta correcta sin login, sin cruces de precios/tenant ni tokens; mismo QR tras actualizar. |
| A26 | Menú por categorías | Solo categoría/productos; foto/descripción/precio; exclusión de ingredientes, búsqueda y ficha sin carrito. |
| A27 | Publicación del menú | Preview/activar/desactivar, precio canónico publicado, datos privados ausentes, caché invalidada y offline identificado. |
| A28 | Menú accesible | Carta fotográfica/compacta de 320 px a escritorio, imágenes sin saltos, categorías legibles y movimiento reducido. |
| A29 | Composición y toque de línea | Plano inicial al 100%, sin catálogo/cuenta. Tras acceso, cuenta izquierda ≈30% y productos/categorías derecha ≈70%; volver recupera plano completo. Mosaico agrega, +/− ajustan y tocar fila no abre editor; controles legibles en tamaños menores. |

Implementación: build/backend/SQL y pruebas frontend pertinentes; ampliar OnlineSalesDraftCommandTests, OnlineSalesCheckoutTests, OrderRecoveryTests, OrderBatchInvoiceTests, PosArchitectureTests y recorridos POS para probar que lo normal no cambia. Casos gastronómicos nuevos prueban contratos públicos, concurrencia, reintentos y efectos. Cada etapa termina con auditoría posterior contra AGENTS y documentos propietarios. Maqueta no certifica SQL, hardware o finanzas.

## 14. Referencias visuales y entrega de diseño

Investigación inicial de interfaces del 2026-09-09: [Toast](https://doc.toasttab.com/doc/platformguide/adminUiOptionsReference.html) y [enrutamiento](https://doc.toasttab.com/doc/platformguide/platformKitchenRoutingOverview.html), [Lightspeed Register](https://k-series-support.lightspeedhq.com/hc/en-us/articles/360050328394-Understanding-the-Register-screen), [Square plano](https://squareup.com/help/us/en/article/6427-building-your-floor-plan) y [menú](https://squareup.com/help/us/en/article/7804-organize-your-menu-with-square-for-restaurants), [Odoo restaurante](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/restaurant.html) y [Fudo](https://fu.do/es/funcionalidades/). Se revisaron guías/capturas oficiales, no instalaciones comerciales ni tiempos medidos. Inspiración: zonas estables, selección directa, plano fiel, rondas y separación envío/cobro; funciones observadas como dividir cuentas no se incorporan por defecto.

La imagen aportada orienta fotos y selección táctil; la distribución final es la de §3: plano completo y, tras acceso, cuenta/productos en proporción aproximada 30/70. La maqueta de artifacts/restaurant-pos/auraly-restaurante.html usa datos ficticios; muestra interacción, no autentica/factura/contabiliza/sincroniza/imprime realmente. Este documento define el contrato completo; las herramientas no simuladas exhaustivamente no se consideran implementadas.

Auditoría de cierre: se retiraron visita paralela, ServiceOpen, alteración de draft/checkout normal, usuario automático por Mesero, huella, offline gastronómico y exclusividad incondicional por mesa. Se fijaron endpoint nullable mínimo, receta excluyente, consumo al facturar, pérdidas sin reposición, comanda sin job de inventario, impresora configurada en Comanda y editor personalizable. Un propietario por regla; alcance, etapas y aceptación coherentes. Esta entrega modifica documentación y demostración visual, no código productivo, schema, datos ni dependencias.

Verificación de la demostración (2026-09-10): recorrido automatizado del plano completo sin catálogo, apertura 30/70, clave obligatoria en puesto compartido aun sin exclusividad, rechazo/cancelación de acceso sin mostrar detalle, regreso al plano y nueva identificación, toque de línea inerte y aprobación supervisora. Se comprobaron cambio luz/oscuro conservando cuenta, cabecera blanca en luz, vínculo manual Mesero/Usuario, receta, edición/deshacer/preview/publicación del plano, configuración de impresora de comanda, menú/ficha pública, adaptación entre 320 y 1024 px, estado desconectado y movimiento reducido, sin errores JavaScript en ese recorrido. Revisión visual de plano y cuenta en luz, cuenta y editor oscuros y carta. `git diff --check` pasó para los documentos de esta entrega. La revisión posterior conserva Sales/Authorization y el tema global como propietarios, sin cambios productivos ni nuevas fuentes de datos. Estas verificaciones no equivalen a ejecutar los 29 criterios sobre producción: autenticación real, concurrencia SQL, contabilidad, push, impresión y dos monitores se validarán al implementar sus etapas.
