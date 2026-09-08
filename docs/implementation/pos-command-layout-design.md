# Superficie de comandos del POS

Fecha: 2026-07-29.

## Estado real

- Cobro, búsqueda de producto, guardar temporal, recuperar temporal, eliminar
  línea y reiniciar venta tienen comportamiento local consumido por la UI.
- El panel de Pedidos es actualmente solo una entrada visual; no existe todavía
  un contrato canónico de Orders conectado al POS.
- La búsqueda y selección de cliente no está expuesta todavía por POS Edge,
  aunque SQLite ya conserva la proyección mínima usada por precios.
- Entregar caja y cerrar caja tienen API, persistencia y permisos en el servidor,
  pero todavía no pueden exponerse con seguridad en el POS porque falta la sesión
  de usuario local/online unificada.

No se presentará ninguna de estas tres capacidades pendientes como terminada
mediante botones sin consumidor.

## Organización visual

La pantalla conserva tres zonas estables:

1. **Contexto compacto**: caja, cajero, conexión, cliente seleccionado y acceso
   al lanzador. No repite el logo.
2. **Zona de trabajo**: captura, tabla de líneas y foco continuo. Ocupa el máximo
   espacio disponible.
3. **Panel operativo**: total, cobro y bandeja con Temporales y Pedidos.

Las acciones frecuentes viven en una barra de comandos fija al pie. Las
acciones de custodia del dinero viven en un menú **Caja**, visualmente separado
de las acciones de la venta para evitar cierres accidentales.

## Mapa de teclado

| Tecla | Acción | Regla |
|---|---|---|
| F1 | Buscar producto | Abre para agregar; dentro del diálogo alterna verificador |
| F2 | Editar líneas | Línea seleccionada y permisos vigentes |
| F3 | Eliminar línea | Confirma; foco inicial en Aceptar |
| F4 | Reiniciar venta | Confirma; foco inicial en Aceptar |
| F5 | Facturas | Consulta de documentos emitidos |
| F6 | Devolución | Flujo vinculado a documento original |
| F7 | Buscar cliente | Selecciona o crea según permisos |
| F8 | Cobrar | Solo con líneas |
| F9 | Guardar temporal | Solo con líneas |
| F10 | Guardar pedido | Según conexión y permisos |
| F12 | Cerrar caja | Permiso, arqueo e impresión |

`Enter` ejecuta la acción primaria del diálogo y `Escape` lo cierra. `Tab`
recorre controles y filas; las flechas navegan dentro de listas. Al cerrar un
diálogo, el foco regresa al escáner salvo cuando se abrió otro flujo explícito.

El mapa refleja `POS_ACTION_SHORTCUTS` en `pos-function-shortcut.ts`. Cada
apertura del buscador, por botón o F1, inicia en **Buscar producto** con foco en
la búsqueda. El verificador se activa explícitamente con F1 dentro de la ventana;
Enter consulta en ese modo y no agrega productos. Cerrar descarta ese modo.
Las filas conservan columnas alineadas para nombre, promoción opcional y precio.
No se presenta el código genérico EA ni una etiqueta para ausencia de promoción;
los productos por peso conservan su unidad junto al precio. Existencias crece
con sus filas sin scroll interno; con muchas bodegas o una pantalla baja se
desplaza el diálogo completo para conservar acceso a todas ellas.

Los atajos se muestran en la interfaz, pero la autorización siempre se repite
en backend. Si el sistema operativo captura una tecla de función, la PWA ofrece
la misma acción mediante botón y permite configurar una combinación alternativa.

## Líneas de venta

La grilla operativa evita columnas separadas para descuento e IVA. Conserva
solamente Producto, Cantidad, Precio unitario y Total; el botón de eliminación
es una acción, no un dato comercial.

Producto usa una jerarquía compacta:

1. nombre destacado;
2. código, unidad y origen del precio;
3. descuento, únicamente cuando es mayor que cero;
4. tarifa y valor del IVA.

Esto reduce el desplazamiento horizontal sin ocultar información. La pantalla
operativa no sustituye la representación gráfica. El snapshot y el XML
conservan la discriminación fiscal por línea y los totales por tarifa; la
tirilla debe representar los impuestos exigidos por la Resolución DIAN 000165
de 2023 y el anexo técnico aplicable.

## Cliente

El contexto de cliente aparece sobre el total como una tarjeta compacta:

- Consumidor final cuando no se ha seleccionado otro;
- nombre e identificación cuando existe;
- distintivo de precio: base del negocio, lista o canal;
- acción F7 para buscar/cambiar;
- acción de creación rápida con permiso.

Seleccionar un cliente debe resolver nuevamente precios de lista/canal. Las
líneas ya capturadas no cambian silenciosamente: la UI muestra el resumen de
cambios y el usuario confirma la repricing. Una venta nunca se bloquea por no
tener precio especial; usa el precio base del `Business`.

## Temporales y pedidos

Temporales y Pedidos comparten una bandeja con pestañas y contadores:

- **Temporales** funciona localmente, incluso sin conexión.
- **Pedidos** se consulta en línea, pagina de 50 en 50 y conserva filtros.
- Recuperar incorpora un solo pedido a la venta actual.
- Facturar uno o varios pedidos es una acción propia de la vista de Pedidos y no
  se confunde con recuperar.
- Si no hay conexión, la pestaña Pedidos permanece visible y deshabilitada con
  una explicación; no muestra datos antiguos como si fueran actuales.

Cuando se muestra el cambio a entregar, esta bandeja se sustituye temporalmente
por el aviso de cambio. El aviso desaparece al capturar el primer producto de la
siguiente venta o al cerrarlo explícitamente.

## Entrega y cierre de caja

El menú **Caja** muestra:

- Entregar caja · F11;
- Cerrar caja · F12;
- último arqueo y reimpresión, sujetos a permiso.

Ambas acciones piden confirmación. Entregar caja exige la autorización puntual
del supervisor. Cerrar caja muestra el arqueo por medios de pago, congela la
tirilla e imprime. Ninguna acción cierra la sesión de Auraly.

En una caja online compartida, varios cajeros pueden vender simultáneamente.
Cada venta conserva su usuario y turno. Entregar termina solo el turno del
solicitante; cerrar la caja termina la `CashSession` y todos sus turnos activos.

Cuando el diálogo de cobro está abierto, su manejador es el propietario de
`F1` a `F5`: una tecla agrega el medio elegido con el saldo pendiente o cambia
el medio enfocado si el total ya está completo. El capturador global del POS no
consume esas teclas mientras el diálogo esté activo.

La búsqueda ampliada de productos conserva dos cargas independientes. El
catálogo local aparece de inmediato; la disponibilidad por sede y bodega se
consulta al servidor solo con `pos.inventory.availability.read`, un permiso del
propio punto de venta que no concede acceso al módulo de inventario.
`businesses.read` amplía el
resultado a otras sedes del mismo tenant. Las bodegas internas nunca se
exponen. Para no mezclar artículos distintos, la equivalencia entre sedes usa
identidades estables compartidas —código de barras, identificador tipado o
identidad del mismo proveedor de integración— y nunca el `ProductCode`, porque
ese código pertenece a cada negocio y puede coincidir o cambiar. Sin conexión
la búsqueda local continúa y el panel remoto explica por qué no está disponible.

Una captura que no resuelve producto limpia el lector, emite un sonido negativo
corto y termina en una señal roja persistente con el código rechazado. La señal
solo desaparece al iniciar otra captura o al resolver correctamente un producto;
así el cajero no puede interpretar una animación ya terminada como una venta
aceptada.

## Orden de implementación

1. Sesión unificada, enrolamiento y login local/online.
2. Proyección y búsqueda local de clientes, selección y repricing confirmado.
3. Contratos canónicos de pedidos y recuperación/facturación.
4. Barra de comandos, menú Caja y diálogos conectados.
5. Pruebas E2E completas de teclado, permisos, concurrencia, desconexión,
   arqueo e impresión.

