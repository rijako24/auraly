# Catálogo público de tiendas — propuesta para revisión

Estado: **diseño, sin implementación ni publicación de enlaces**. El usuario pidió revisar la propuesta antes de implementar este catálogo. Las mejoras actuales de Productos y POS son independientes de esta propuesta.

## Experiencia propuesta

Productos conserva su vista predeterminada por productos. Dos controles resuelven preguntas diferentes:

- **Explorar por:** Productos, Áreas, Líneas, Grupos y Subgrupos. Define qué elementos se exploran; no altera el maestro.
- **Presentación:** Lista, Tarjetas y Galería. Define cómo se ven los resultados y conserva la preferencia del usuario en su tenant.

«Categorías» es el nombre de la navegación de la jerarquía existente Área → Línea → Grupo → Subgrupo, no un sexto maestro ni una clasificación duplicada. Los niveles sin datos no se inventan. La familia agrupa variantes de un modelo y sigue siendo una relación distinta de la categoría.

En la cabecera de Productos, un acceso «Catálogo público» permite abrir la vista publicada y encontrar el enlace general. A su derecha aparece un **icono de copiar**, discreto, sin fondo de color y con el mismo tratamiento que los iconos de acciones existentes. No se imprime una URL larga dentro de cada fila.

Cada resultado tiene el mismo icono a la derecha: junto a las acciones en lista, en el pie de una tarjeta y junto al nombre en galería. En categorías, una fila muestra nombre, ruta de padres, acceso para explorar e icono de copiar. Pulsar el nombre abre el contenido; pulsar copiar no navega ni abre el editor. El icono permanece disponible sin hover, tiene nombre accesible «Copiar enlace de Camisetas» y tooltip equivalente; al terminar confirma «Enlace copiado» y cambia brevemente a check. Si el navegador deniega el portapapeles, muestra el error y ofrece el enlace en un campo de solo lectura, sin afirmar que se copió.

Un producto no publicado muestra el motivo de indisponibilidad del enlace. Copiar nunca publica automáticamente. El estado de publicación debe venir con la página o por lote; no se consulta por cada tarjeta.

## Ejemplo de navegación administrativa

```text
Productos                                      Catálogo público  ↗  [copiar]

Explorar por  [Productos ▾]                     Lista | Tarjetas | Galería
Buscar…                                       Filtros

Aurali › Ropa › Deportiva › Camisetas

Camiseta deportiva azul · M                      $ 49.900  [copiar]
Camiseta deportiva negra · L                     $ 49.900  [copiar]
```

Al cambiar «Explorar por» a Grupos, los resultados serían «Camisetas», «Pantalonetas», etc., con sus padres visibles. Una categoría con productos en sus descendientes abre ese conjunto filtrado en el catálogo público. Se puede copiar directamente sin entrar en cada producto. Cambiar de nivel mantiene solo los filtros compatibles y conserva visible la ruta actual.

## Catálogo público

- Cabecera con nombre y marca del tenant, reutilizando su configuración actual. La sede origen de precios se define al publicar; no se mezclan listas de precios de diferentes sedes.
- Portada sobria con título y productos como protagonistas. Fotos reales con transparencia, `object-fit: contain`, fondo neutro y espacios consistentes. Sin estirar ni cortar prendas.
- Buscador, navegación por categorías disponibles y filtros de talla y color únicamente cuando existan valores publicados. En móvil: dos columnas y filtros en un panel accesible; ficha de producto de una columna.
- Tarjetas con foto, nombre y precio público. La ficha muestra galería, descripción y variantes de talla/color que pertenezcan a la familia y estén publicadas. El enlace del producto identifica la variante seleccionada; el nombre escrito por la empresa permanece intacto.
- Breadcrumbs clicables: **Aurali › Ropa › Deportiva › Camisetas › Camiseta azul**. Se muestran únicamente los niveles reales. En móvil se compactan los padres intermedios con acceso a la ruta completa.
- Icono de copiar enlace también disponible en la cabecera de categoría y ficha. Compartir por WhatsApp prepara un mensaje que el usuario decide enviar; no requiere una integración de mensajería ni envía mensajes automáticamente.
- La vista previa al compartir debe incluir nombre del tenant, nombre de la categoría o producto e imagen correspondiente. No debe depender del token de una sesión administrativa.

## Enlaces estables propuestos

| Destino | Ruta ilustrativa |
| --- | --- |
| Catálogo completo | `/catalogo/{codigoPublicacion}` |
| Área, línea, grupo o subgrupo | `/catalogo/{codigoPublicacion}/c/{categoryId}/{nombreOpcional}` |
| Producto o variante | `/catalogo/{codigoPublicacion}/p/{productId}/{nombreOpcional}` |

Los nombres legibles son decorativos; el identificador conserva el enlace después de renombrar. Los enlaces se construyen con el dominio del ambiente, nunca con una URL de DEV fija. Búsqueda y filtros pueden conservarse en query string. El icono de cada elemento copia su destino canónico; una acción «Compartir esta selección» podría copiar los filtros, pero queda fuera de la primera entrega propuesta para evitar controles adicionales.

Abrir un enlace directamente debe reconstruir su breadcrumb desde el maestro, sin depender de haber navegado por la portada. Producto o categoría retirados no revelan datos internos: muestran que ya no están disponibles y permiten volver al catálogo publicado. No se promete disponibilidad de stock ni se exponen cantidades.

## Propietarios y límites de una implementación futura

El menú existente usa `RestaurantDigitalMenuService`, `IRestaurantDigitalMenuStore`, `sales.RestaurantDigitalMenus` y `sales.RestaurantDigitalMenuProducts`. Sus consultas y autorización están restringidas a sedes RestaurantBar; **no se puede usar ese endpoint sin cambios para una tienda**.

La siguiente etapa debe acordar la extensión compatible de ese propietario de publicación para menú y catálogo, manteniendo enlaces actuales de restaurantes. No se copiará el servicio, writer, tablas ni motor para formar una segunda publicación paralela. Se conserva el catálogo canónico de productos, categorías, familias, imágenes, tallas y colores; los precios se obtienen de la sede seleccionada. La migración y el punto exacto de extensión se revisan antes de programar.

La publicación debe seleccionar expresamente los productos y el precio público. Que los maestros se compartan en el tenant no autoriza publicar ingredientes, costos, márgenes, listas privadas, identificadores de clientes ni productos de prueba. Todas las consultas públicas se acotan a la publicación y su tenant. La configuración conserva la autorización administrativa existente y un único propietario de carga/refresco.

Primera entrega propuesta: consulta y compartir. No incluye carrito, pedidos, checkout, pagos ni cambios en facturación. Las imágenes siguen en el almacenamiento de su ambiente; la lectura pública requiere una proyección autorizada por publicación que entregue URLs vigentes, sin hacer público todo el contenedor ni reutilizar SAS de una sesión administrativa. El diseño exacto de esa lectura pertenece a la revisión de implementación, no queda aprobado implícitamente aquí.

## Rendimiento y evidencia de aceptación futura

- Página pública de 24 productos; límite máximo explícito de 50. Filtros, orden estable y paginación se aplican en SQL antes de imágenes o precios. Conteos separados bajo los mismos filtros.
- Una petición de página devuelve sus portadas y estado compartible. La ficha carga una galería acotada al producto. No hay HTTP/SQL/blob por tarjeta, fetch-all, consulta de cada categoría ni polling.
- Objetivo a validar en un ambiente de referencia sin carga concurrente: p95 de API de página ≤ 800 ms con 10.000 productos y p95 de navegación visible ≤ 1,5 s después de la primera carga, sin incluir la descarga de fotos. Primera página y siguientes se miden por separado; estos son criterios propuestos, no resultados ya obtenidos.
- Probar copia real del enlace general y de cada nivel; acceso anónimo directo; renombrado sin romper enlaces; breadcrumb; filtros y paginación; imagen y texto de vista previa; rechazo entre tenants; retiro de producto; portapapeles denegado; móvil; conservación de las rutas actuales del menú.

## Referencias de diseño

La separación entre agrupación del catálogo y presentación visual sigue el patrón de [colecciones de Shopify](https://help.shopify.com/en/manual/products/collections). Los filtros de talla/color sobre la colección toman como referencia [storefront filtering de Shopify](https://help.shopify.com/en/manual/online-store/themes/customizing-themes/common-customizations/storefront-filters). La navegación ascendente por la jerarquía coincide con la función descrita por [Google para breadcrumbs](https://developers.google.com/search/docs/appearance/structured-data/breadcrumb). Son referencias para la propuesta, no funcionalidades ya implementadas en Auraly.
