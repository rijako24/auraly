# Decisión: módulo Conversión de productos

Fecha: 2026-08-21
Estado: implementado en el módulo de inventario
Referencia funcional: Xion, sin trasladar sus defectos ni su complejidad accidental

---

## 1. Decisión ejecutiva

Auraly soportará conversiones de inventario entre productos de una misma familia ya definida mediante `ProductLinks`.

No se crearán perfiles, recetas, versiones ni un maestro paralelo de conversiones para el MVP. La configuración permanecerá en la sección **Familia de productos** del producto principal.

Cada vínculo podrá marcarse con **Permitir conversión** y declarar su equivalencia física respecto al producto principal. Todos los integrantes habilitados de la familia podrán convertirse entre sí, sin imponer una dirección padre-hijo:

- principal a vinculado;
- vinculado a principal;
- vinculado a otro vinculado;
- un producto a varios productos;
- varios productos a uno.

No se permite muchos-a-muchos en una sola operación durante el MVP.

La conversión descontará inventario de los productos consumidos y aumentará inventario de los productos obtenidos en una sola operación atómica. Nunca podrá crear cantidad física equivalente. Podrá registrar merma dentro de una tolerancia configurable para la familia.

Todas las páginas y búsquedas del módulo seguirán el estándar transversal de [reportes, consultas, filtros y paginación](decision-reportes-consultas-filtros-y-paginacion.md). La bandeja y los selectores consultan y paginan desde servidor; las líneas de un documento activo conservan la operación completa y usan desplazamiento o virtualización, no paginación.

---

## 2. Qué se conserva de Xion

Xion demuestra las necesidades funcionales correctas:

- agrupar productos que pueden transformarse entre sí;
- habilitar o deshabilitar la conversión;
- consumir uno o varios productos;
- obtener uno o varios productos;
- validar existencias;
- generar entradas, salidas y kárdex;
- conservar el costo total de la transformación;
- consultar el historial.

Auraly no copiará:

- validaciones de pérdida defectuosas;
- formularios que cargan catálogos completos;
- dependencia de una dirección fija padre-hijo;
- configuración duplicada;
- perfiles o aprobaciones sin una necesidad vigente;
- reglas de inventario confiadas únicamente al frontend.

---

## 3. Familia convertible

La familia canónica es:

```text
producto principal + productos vinculados activos
```

El producto principal organiza la familia, pero no es necesariamente el producto de entrada. La relación no define la dirección de la conversión.

Un producto vinculado participa en conversiones únicamente cuando su vínculo activo tiene `AllowsConversion = 1`.

Desde cualquier miembro habilitado se podrá llegar al principal o a cualquier otro miembro habilitado de la misma familia.

### 3.1 Incompatibilidad con inventario compartido

`AllowsConversion` y `SharesInventory` son incompatibles en el mismo vínculo.

Cuando dos productos comparten inventario, ambos resuelven al mismo saldo canónico. Convertir entre ellos produciría movimientos ficticios sobre una existencia que ya es común.

La UI deshabilita una opción al activar la otra y el servidor aplica la misma invariante.

### 3.2 Requisitos de los productos

Para participar en una conversión, cada producto debe:

- pertenecer al mismo `BusinessId`;
- estar activo;
- administrar inventario;
- pertenecer a la misma familia convertible activa;
- no compartir inventario mediante ese vínculo;
- tener una equivalencia válida;
- admitir la precisión capturada por su unidad.

---

## 4. Equivalencia física

El modelo actual de unidades describe código, nombre, símbolo y precisión, pero no contiene conversiones dimensionales confiables. Por eso `BaseUnitCode` no basta para demostrar que dos cantidades representan el mismo contenido físico.

Cada vínculo convertible declarará una equivalencia respecto al producto principal:

```text
1 unidad del producto vinculado = X unidades equivalentes del producto principal
```

El producto principal tiene factor implícito `1`.

Ejemplo:

| Producto | Factor equivalente |
| --- | ---: |
| Bulto de 10 kg, principal | 1,00 |
| Bolsa de 1 kg | 0,10 |
| Bolsa de 500 g | 0,05 |

Esto permite:

- 1 bulto a 10 bolsas de 1 kg;
- 10 bolsas de 1 kg a 1 bulto;
- 5 bolsas de 1 kg a 10 bolsas de 500 g;
- 1 bulto a una combinación equivalente de bolsas habilitadas.

El factor debe ser mayor que cero. No se infiere a partir del nombre, código o símbolo del producto.

---

## 5. Merma de conversión

El término visible será **Merma de conversión**.

La tolerancia se configura una sola vez para la familia, en la sección de productos vinculados del producto principal:

```text
Tolerancia máxima de merma (%)
```

No se configura inicialmente por sede o bodega. La tolerancia describe el comportamiento físico de la familia y debe ser consistente sin importar dónde se ejecute. Además, el modelo vigente no tiene una sede operativa que sea propietaria de esta política.

Una futura política de bodega podría actuar como límite más estricto, pero no forma parte del MVP.

### 5.1 Cálculo

```text
equivalente de entrada = suma(cantidad consumida × factor)
equivalente de salida  = suma(cantidad obtenida × factor)

merma equivalente = equivalente de entrada - equivalente de salida
merma % = merma equivalente / equivalente de entrada × 100
```

Reglas:

- la entrada equivalente debe ser mayor que cero;
- la salida equivalente debe ser mayor que cero;
- la salida no puede superar la entrada más el epsilon técnico de redondeo;
- una salida superior a la entrada se rechaza como creación de inventario;
- la merma no puede superar la tolerancia configurada;
- el redondeo se ejecuta una sola vez con la precisión definida por el dominio.

Ejemplo:

```text
Entrada equivalente: 10,000 kg
Salida equivalente:    9,500 kg
Merma:                  0,500 kg
Merma:                  5,00 %
Tolerancia máxima:      5,00 %
Resultado:              permitido
```

Con tolerancia de 3 %, la misma operación se rechaza.

---

## 6. Persistencia mínima

Se extiende el modelo existente; no se crea un subsistema paralelo.

### 6.1 `ProductLinks`

Nuevas columnas:

```text
AllowsConversion bit NOT NULL DEFAULT 0
ConversionFactor decimal(19,6) NULL
```

Restricciones:

- si `AllowsConversion = 1`, `ConversionFactor > 0`;
- si `AllowsConversion = 1`, `SharesInventory = 0`;
- si `AllowsConversion = 0`, `ConversionFactor` queda nulo;
- se respeta la pertenencia existente al negocio y a una sola familia.

### 6.2 `Products`

Nueva columna utilizada en la raíz de la familia:

```text
ConversionMaximumLossPercent decimal(9,6) NULL
```

Debe estar entre 0 y 100. Es obligatoria cuando existe al menos un vínculo convertible activo.

### 6.3 Documento de inventario

Se reutilizan `InventoryOperations`, `InventoryOperationLines`, el motor documental, el escritor canónico de inventario y el kárdex.

El documento confirmado debe conservar:

- familia raíz utilizada;
- dirección de cada línea;
- cantidad capturada;
- factor de equivalencia congelado;
- cantidad equivalente congelada;
- entrada equivalente total;
- salida equivalente total;
- merma y porcentaje calculados;
- tolerancia aplicada;
- costos procesados.

Los snapshots impiden que el historial cambie si después se modifica un factor o una tolerancia.

---

## 7. Operación permitida

La UI presenta dos grupos, sin lenguaje de padre o hijo:

```text
Se descuenta
Se obtiene
```

El MVP permite:

- una entrada y una salida;
- una entrada y varias salidas;
- varias entradas y una salida.

No permite varias entradas y varias salidas simultáneamente.

La primera selección determina la familia. Desde ese momento todos los selectores quedan restringidos a miembros convertibles de esa familia.

### 7.1 Confirmación

Antes de aceptar el documento, el servidor valida:

1. negocio y permisos;
2. bodega válida;
3. familia activa;
4. vínculos habilitados;
5. factores y tolerancia;
6. forma uno-a-uno, uno-a-muchos o muchos-a-uno;
7. cantidades y precisión;
8. existencia disponible de cada entrada;
9. conservación física y merma;
10. idempotencia.

El motor repite las invariantes críticas dentro del procesamiento transaccional. Una validación de la UI nunca es autoridad.

Si la operación no tiene existencia o supera la merma, se rechaza antes de enviarla al procesamiento asíncrono. No debe presentarse como aceptada para terminar después en dead letter por una regla conocida de negocio.

---

## 8. Costo

El costo total de las entradas se conserva y se distribuye entre las salidas usando el mecanismo actual del motor.

Para el MVP, la merma física queda absorbida por los productos obtenidos:

```text
valor total de entradas = valor total distribuido a salidas
```

Si 10 kg con costo total de 100 producen 9,5 kg, los 9,5 kg conservan el costo total de 100. El costo unitario resultante aumenta.

No se crea un producto de merma ni un asiento contable separado en esta fase.

Los costos se muestran solamente a usuarios con el permiso vigente de lectura de costos.

---

## 9. Diseño web y usabilidad

El módulo debe sentirse parte de Auraly. Reutiliza `Card`, `Button`, `Input`, `Select`, `Dialog`, `Skeleton`, badges de estado, tokens de color y el componente compartido `DataTable`. No crea una librería visual ni una tabla propia.

Ubicación implementada:

```text
/dashboard/inventory → pestaña Conversiones
/dashboard/inventory → Nueva operación → Conversión
/dashboard/inventory → Conversiones → detalle inmutable
```

Se mantiene una sola página operativa de inventario para evitar navegación y componentes duplicados. La pestaña **Conversiones** tiene consulta paginada propia y el formulario reutiliza el espacio de captura documental existente.

### 9.1 Bandeja de conversiones

La página contiene:

1. encabezado con título, descripción breve y acción primaria **Nueva conversión**;
2. filtros y búsqueda en una barra compacta;
3. tabla o tarjetas adaptables;
4. paginador visible;
5. detalle al abrir una fila.

Columnas mínimas:

- fecha;
- número;
- bodega;
- resumen de productos consumidos;
- resumen de productos obtenidos;
- merma;
- estado;
- responsable;
- acciones permitidas.

La consulta:

- pagina desde servidor;
- ordena por fecha descendente y desempata por ID descendente;
- permite búsqueda por número, producto y responsable;
- filtra por bodega, estado y rango de fechas;
- reinicia en la primera página al cambiar filtros, búsqueda, orden o tamaño;
- conserva los parámetros relevantes en la URL;
- nunca descarga todo el historial para filtrarlo en el navegador.

El paginador muestra rango, total, página actual, navegación anterior/siguiente y tamaño de página permitido por el estándar transversal.

### 9.2 Nueva conversión

La captura se organiza en bloques claros:

- datos generales: bodega, fecha y observación;
- **Se descuenta**;
- **Se obtiene**;
- resumen fijo de equivalencia, merma y validaciones;
- acciones **Cancelar**, **Guardar borrador** si se conserva esa capacidad y **Confirmar conversión**.

Los selectores de producto:

- consultan al servidor con búsqueda paginada;
- buscan por código, código de barras, referencia y nombre;
- no cargan el catálogo completo;
- muestran unidad, equivalencia y existencia disponible cuando el permiso lo permite;
- quedan restringidos a la familia después de la primera selección;
- conservan el foco y soportan teclado y lector.

Las líneas capturadas no se paginan. Constituyen un solo documento y sus totales deben recalcularse completos. Si el número de líneas crece, se usa un contenedor con altura controlada y virtualización.

El resumen se actualiza inmediatamente:

```text
Entrada equivalente
Salida equivalente
Merma equivalente
Merma porcentual
Tolerancia máxima
Estado de validación
```

La acción de confirmar permanece deshabilitada mientras exista un error y muestra la causa junto al campo o línea responsable.

### 9.3 Detalle

La página de detalle muestra:

- estado y trazabilidad;
- bodega, fecha, usuario y observación;
- entradas y salidas en grupos separados;
- factores y equivalencias congeladas;
- merma real y tolerancia aplicada;
- costos solo con permiso;
- vínculo al kárdex relacionado.

El detalle no es editable después de confirmar.

### 9.4 Configuración en producto

En **Familia de productos**, el producto principal muestra:

- **Tolerancia máxima de merma (%)**;
- para cada vínculo, **Permitir conversión**;
- para cada vínculo habilitado, el campo **1 unidad de este producto equivale a X unidades del producto principal**;
- un ejemplo calculado en ambos sentidos;
- explicación de incompatibilidad con **Compartir inventario**.

La búsqueda para agregar vinculados es paginada desde servidor. No se carga el catálogo completo.

### 9.5 Estados obligatorios

Cada página implementa explícitamente:

- carga con `Skeleton`, sin saltos bruscos de estructura;
- vacío inicial con explicación y acción principal cuando corresponda;
- sin resultados por filtros con opción de limpiarlos;
- error recuperable con mensaje humano y acción **Reintentar**;
- confirmación en curso con acción protegida contra doble envío;
- éxito mediante actualización visible y notificación breve;
- permisos insuficientes sin exponer datos restringidos.

### 9.6 Responsive y accesibilidad

- escritorio usa tabla y paneles con jerarquía visual Auraly;
- móvil usa tarjetas o lista cuando la tabla pierda legibilidad;
- no se reduce información crítica únicamente a un icono o color;
- cantidades usan números tabulares y alineación consistente;
- acciones tienen texto accesible;
- filas accionables responden a Enter y espacio;
- el foco vuelve a un lugar predecible al cerrar diálogos;
- errores se asocian al control correspondiente;
- la navegación principal es posible con teclado.

---

## 10. Contrato de paginación

La bandeja y los buscadores utilizan el contrato paginado común:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 50,
  "totalCount": 0,
  "totalPages": 0
}
```

Reglas:

- `page` comienza en 1;
- el servidor limita `pageSize`;
- `pageSize = 0` nunca significa traer todo;
- búsqueda, filtros y ordenamiento se ejecutan en servidor;
- el orden siempre termina con un identificador único;
- cada consulta aplica `BusinessId`, permisos y bodega antes de paginar;
- los selectores pueden usar cursor cuando el catálogo lo requiera, sin cambiar la experiencia visible.

---

## 11. Permisos

El módulo reutiliza la autorización vigente; no crea permisos redundantes:

```text
inventory.read
inventory.conversions.confirm
catalog.update
inventory.costs.read
```

`catalog.update` protege la configuración de la familia en producto. `inventory.read` protege bandeja, detalle y selector; `inventory.conversions.confirm` protege la confirmación. La lectura de costos continúa gobernada por `inventory.costs.read`. El backend vuelve a autorizar cada endpoint y recurso.

---

## 12. Pruebas obligatorias

### Dominio y aplicación

- principal a vinculado;
- vinculado a principal;
- vinculado a vinculado;
- uno-a-uno;
- uno-a-muchos;
- muchos-a-uno;
- rechazo de muchos-a-muchos;
- familia diferente;
- vínculo deshabilitado;
- inventario compartido;
- factor cero, negativo o ausente;
- salida superior a entrada;
- merma exacta al límite;
- merma superior al límite;
- precisión y redondeo;
- existencia insuficiente;
- idempotencia.

### Persistencia y motor

- descuento y entrada atómicos;
- kárdex completo;
- snapshots inmutables;
- costo total conservado;
- reintento sin duplicados;
- concurrencia sobre el mismo saldo;
- aislamiento por negocio y bodega.

### UI/E2E

- bandeja paginada con total correcto;
- siguiente, anterior y cambio de tamaño;
- cambio de filtro vuelve a página 1;
- filtros y orden ejecutados en servidor;
- búsqueda paginada de productos;
- restricción automática a la familia;
- captura con teclado y lector;
- cálculo inmediato de equivalencia y merma;
- estados carga, vacío, sin resultados y error;
- diseño móvil;
- confirmación sin doble envío;
- costos y acciones ocultos según permisos.

---

## 13. Criterios de aceptación

La primera versión está terminada cuando:

- reutiliza `ProductLinks` como única definición de la familia;
- convierte en cualquier dirección entre miembros habilitados;
- impide combinar conversión e inventario compartido;
- conserva equivalencia física y rechaza creación de inventario;
- calcula y controla la merma con una tolerancia de familia;
- valida existencia antes de aceptar y durante el procesamiento;
- mueve inventario y costo atómicamente mediante el motor canónico;
- conserva snapshots suficientes para auditar el documento;
- la bandeja y todos los buscadores paginan desde servidor;
- las líneas activas conservan el documento completo;
- todas las páginas cumplen los estados, responsive y accesibilidad definidos;
- no introduce perfiles, recetas ni aprobaciones innecesarias;
- pasa las pruebas de dominio, SQL, motor y UI.

---

## 14. Fuera del MVP

- producción y listas de materiales;
- etapas, máquinas y mano de obra;
- muchos-a-muchos;
- conversiones entre bodegas;
- tolerancia diferente por dirección;
- límite adicional por bodega;
- lotes, seriales y vencimientos;
- producto separado para merma;
- contabilización independiente de la merma;
- aprobación por exceso de tolerancia;
- conversión offline;
- reversión parcial.

---

## 15. Decisión final

Conversión será una operación simple de inventario basada en familias vinculadas. La configuración vive junto al producto, la dirección es libre dentro de la familia habilitada, la equivalencia física impide crear inventario y la merma queda controlada por una tolerancia única de familia.

La experiencia web forma parte del requisito funcional: bandejas y búsquedas paginadas desde servidor, captura continua sin dividir el documento, componentes compartidos, feedback inmediato, estados completos, responsive y accesibilidad coherentes con Auraly.
