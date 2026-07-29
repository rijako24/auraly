# Auraly Commerce: diseño cerrado de maestros, terceros y cajas

Fecha de decisión: 2026-07-29.

## 1. Decisiones principales

Xion es referencia funcional, no un modelo que deba copiarse. Auraly conserva los
casos de uso vigentes y elimina IDs manuales, tablas duplicadas, banderas de rol
dentro de Persona, nombres Familia1-Familia4 y configuraciones acopladas a un
computador.

Quedan cerradas estas decisiones:

1. `Party` es la identidad común de personas naturales y organizaciones.
2. Cliente, proveedor, empleado, vendedor, transportador y conductor son roles
   especializados de una Party; no son maestros de identidad separados.
3. Una cuenta de usuario pertenece a Seguridad y puede enlazarse opcionalmente con
   una Party. Ser empleado o vendedor no crea acceso al sistema.
4. El admin tendrá una sola entrada **Terceros** y una sola entrada **Maestros**.
5. **Cajas** unifica creación y parametrización en una experiencia, aunque el
   modelo interno mantenga responsabilidades separadas.
6. Los maestros solo se implementan cuando tienen un consumidor conectado. No se
   crean tablas o CRUD “por si acaso”.

## 2. Navegación del admin

### Negocio

- Productos.
- Terceros.
- Pedidos.
- Promociones, cuando corresponda al módulo existente.

### Operación

- Sedes y bodegas.
- Cajas.
- Inventario y movimientos, a medida que se implementen sus rebanadas.

### Finanzas

- Facturación electrónica.
- Cuentas por cobrar.
- Cuentas por pagar.
- Arqueos y movimientos de caja.

### Administración

- Maestros.
- Usuarios y permisos.
- Auditoría.

No se agregan al menú principal entradas individuales para países, ciudades,
unidades, impuestos, marcas o medios de pago.

## 3. Centro unificado de Maestros

`/dashboard/settings/masters` será un centro con buscador, tarjetas por grupo,
permisos y rutas profundas. Entrar a un maestro abre su tabla o árbol; no crea otra
opción permanente en el menú.

Todas las tablas simples usan el patrón visual existente del admin:

- paginación de servidor;
- ordenamiento;
- filtros combinables por encabezado;
- estado activo/inactivo;
- creación y edición en drawer o diálogo cuando el formulario sea corto;
- auditoría y permisos;
- estados de carga, vacío y error;
- accesibilidad básica.

Los árboles, configuraciones fiscales y configuraciones de caja usan pantallas
especializadas; no se fuerzan dentro de un CRUD genérico.

## 4. Party y roles

La estructura canónica es:

```text
Party
 ├── Customer
 ├── Supplier
 ├── Employee
 ├── Seller
 ├── Carrier
 ├── Driver
 └── UserAccount (vínculo opcional administrado por Seguridad)
```

No se conservan banderas `EsCliente`, `EsProveedor`, `EsEmpleado` ni herencia de
tablas. La fila especializada es la fuente de verdad del rol.

Una Party puede ser, por ejemplo:

- cliente y proveedor;
- empleado, vendedor y usuario;
- transportadora y proveedor;
- conductor y empleado.

Un rol no concede otro automáticamente. Un proveedor no obtiene acceso al sistema
y un usuario técnico puede no tener Party.

### Propiedad modular

- Parties: identidad, identificaciones, contactos, direcciones, sedes y datos
  fiscales comunes.
- Sales: Customer y condiciones comerciales.
- Purchasing: Supplier, producto-proveedor y costos.
- Workforce/Organization: Employee, Seller, Carrier y Driver.
- Security: UserAccount, autenticación, roles, permisos y alcances.

La base compartida no autoriza a un módulo a consultar directamente las tablas
internas de otro.

## 5. Experiencia de Terceros

`/dashboard/parties` muestra una sola lista paginada con:

- identificación;
- nombre o razón social;
- roles mediante badges;
- sede principal;
- teléfono y correo principales;
- negocio relacionado;
- estado;
- estado de completitud para importaciones.

Filtros:

- identificación;
- nombre;
- rol;
- estado;
- país, departamento y ciudad;
- negocio;
- datos incompletos.

La ficha de Party tiene cabecera común y pestañas:

1. Identidad.
2. Contactos.
3. Sedes y direcciones.
4. Datos fiscales.
5. Cliente, si posee el rol o el usuario puede agregarlo.
6. Proveedor.
7. Empleado.
8. Vendedor.
9. Transportador.
10. Conductor.
11. Cuenta de usuario, visible solo con permisos de Seguridad.

Crear un proveedor con una identificación existente agrega `Supplier` a la misma
Party. Agregar una sede nunca crea otra Party.

La relación `Customer-Business` es informativa y permite configuración comercial;
nunca autoriza a bloquear una venta. Si una Party ya es cliente de otro negocio
del mismo tenant y compra aquí, el servidor crea o reutiliza el rol Customer para
el negocio que vende. La venta conserva `LocationId`, que es la fuente para saber
en qué sucursal compró; no se duplica una asignación por sede solo para reportes.
Un identificador interno de cliente inaccesible no cruza tenants: la venta
continúa con su snapshot fiscal, pero sin enlazar esa referencia ajena.

## 6. Sedes y geografía de una Party

Una Party puede tener varias sedes bajo la misma identificación legal. Cada sede
conserva:

- código y nombre;
- país, división administrativa y ciudad;
- dirección;
- barrio escrito libremente;
- código postal;
- contactos propios;
- indicadores de sede principal, facturación y entrega;
- estado.

Si cambia la identificación legal, es otra Party.

Los únicos maestros geográficos del MVP son:

- `Countries`;
- `AdministrativeDivisions`, para departamento, estado o provincia;
- `Cities`.

La creación de cliente usa dropdowns dependientes:

1. país;
2. departamento/estado filtrado por país;
3. ciudad filtrada por división;
4. barrio como texto libre.

No se crea tabla de barrios. El documento transaccional congela códigos y nombres
geográficos, por lo que un cambio posterior del maestro no altera el histórico.

## 7. Clientes externos e importaciones

`ExternalCommerceCustomers` no es otro maestro de clientes. Es la procedencia y
reconciliación de un cliente extraído de un sistema externo.

Al importar:

- se crea o reutiliza `Party`;
- se crea o reutiliza `Customer` para el Business;
- los datos ausentes permanecen nulos;
- no se inventa identificación, dirección ni correo;
- se conserva sistema externo, clave externa y estado de reconciliación;
- una sincronización futura enriquece la misma Party;
- los conflictos no se fusionan silenciosamente.

Una Party importada puede quedar `Incomplete`. La creación manual desde POS o web
aplica sus validaciones mínimas, pero no impide conservar registros externos
parciales.

## 8. Clasificación de productos

Las tablas Familia de Xion se sustituyen por un solo árbol
`ProductCategories` con `ParentProductCategoryId`.

Los nombres de presentación son:

1. Área.
2. Línea.
3. Grupo.
4. Subgrupo.

Ejemplo:

```text
Aseo hogar > Jabones > Baño > Líquidos
```

Un producto referencia su nodo más específico. No se agregan cuatro llaves
foráneas a Products. La ruta se deriva del árbol y se guarda como snapshot donde
un documento histórico lo necesite.

Se evoluciona la tabla `ProductCategories` existente; no se crea un árbol paralelo.
En el MVP se usa selección explícita del padre y orden. No se requiere drag-and-drop.

## 9. Matriz definitiva de maestros de Xion

### 9.1 Fundación y MVP inmediato

| Conocimiento de Xion | Diseño Auraly | Superficie |
|---|---|---|
| País | Countries | Maestros > Geografía |
| Departamento | AdministrativeDivisions | Maestros > Geografía |
| Ciudad | Cities | Maestros > Geografía |
| Barrio | Texto libre en PartySite/Address | Sin maestro |
| Tipo de documento de identidad | IdentificationTypes, semilla controlada | Maestros > Referencias |
| Familia 1-4/5 | Árbol ProductCategories | Maestros > Catálogo |
| Marca | ProductBrands | Maestros > Catálogo |
| Unidad de medida | UnitsOfMeasure | Maestros > Catálogo |
| IVA | TaxProfiles y códigos tributarios | Maestros > Catálogo/Fiscal |
| Lista de precios | PriceLists | Maestros > Comercial |
| Canal de precio | PriceChannels | Maestros > Comercial |
| Tipo/medio de pago | PaymentMethods | Maestros > Comercial |
| Balanza y comunicación serial | Perfil de periférico + configuración del producto pesable | Cajas/Producto |
| Perfil de impresión | PrintProfiles tipados | Cajas > Impresión |

Marca, unidad e impuesto se entregan en la misma rebanada en que el formulario de
Producto los consume. No se crean antes como servicios desconectados.

### 9.2 Roles Party, no maestros independientes

| Estructura Xion | Rol Auraly | Dueño |
|---|---|---|
| Cliente | Customer + Party | Sales/Parties |
| Proveedor/Casa comercial | Supplier + Party | Purchasing/Parties |
| Empleado | Employee + Party | Workforce/Parties |
| Vendedor | Seller + Party | Workforce/Sales |
| Transportador | Carrier + Party | Workforce/Logistics |
| Conductor | Driver + Party | Workforce/Logistics |
| Usuario | UserAccount con PartyId opcional | Security |

### 9.3 Configuraciones con módulo propio

No se presentan como maestros genéricos:

- sedes y bodegas;
- cajas, dispositivos y periféricos;
- autorizaciones, series y configuración del emisor fiscal;
- usuarios, roles y permisos;
- promociones;
- cuentas bancarias y condiciones de pago del proveedor;
- rutas y vehículos cuando se active logística;
- motivos de devolución, avería y movimiento cuando su rebanada los consuma;
- conversiones y factores de unidades dentro del módulo de Conversión.

### 9.4 Referencias administradas por la plataforma

Se cargan como semilla o catálogo normativo. El usuario normal las consulta, pero
no las modifica libremente:

- códigos DIAN;
- responsabilidades fiscales;
- tipos de identificación;
- monedas;
- unidades estándar;
- códigos de impuestos;
- países y divisiones oficiales cuando se distribuyan como semilla;
- estados internos de documentos y procesos.

El administrador on-premise puede aplicar paquetes de actualización, no editar
códigos normativos rompiendo documentos.

### 9.5 Se implementan junto con su módulo, no ahora

- motivos de avería;
- motivos de entrada, salida y ajuste;
- conceptos de arqueo;
- condiciones de pago;
- tipos operativos de proveedor;
- rutas;
- tipos de vehículo;
- centros de costo, si reportes o contabilidad los consumen;
- clasificaciones de cliente, solo cuando exista una regla real de precios,
  promociones o reportes que las utilice.

### 9.6 Excluidos del MVP

- Talla y Color, hasta implementar variantes;
- EPS, ARL/ARP, pensión, cesantías, caja de compensación, profesión, estado civil
  y estrato, hasta existir RR. HH. o nómina;
- puntos, premios y bonos especializados;
- cheques posfechados;
- retenciones y fletes;
- lotes y seriales;
- producción/manufactura;
- tablas genéricas `TablasTipo` y `ValoresPermitidos`;
- parámetros e-commerce legacy;
- paridad, bits de datos, bits de parada y puertos como maestros globales;
- apartados, cotizaciones, remisiones y domicilios;
- enums o catálogos legacy sin consumidor comprobado.

La comunicación serial vive dentro de un perfil validado de periférico; sus
valores técnicos no justifican varias pantallas de maestros.

## 10. Resoluciones y configuración fiscal

Auraly ya tiene `FiscalAuthorizations`, `FiscalSeries` y
`FiscalIssuerConfigurations`. Son los propietarios canónicos. No se crea una tabla
`ResolucionDian` paralela.

**Facturación electrónica** tiene pantalla propia y permite:

- datos del emisor y ambiente;
- número y fechas de autorización;
- prefijo y rango DIAN;
- tipo documental;
- asignación de series a cajas;
- estado y vigencia;
- referencia segura a clave técnica, PIN y certificado;
- historial y auditoría;
- validación de rangos y prefijos solapados.

Los secretos no se muestran de vuelta ni se almacenan en texto plano.

## 11. Cajas: creación y parametrización unificadas

### Hallazgo en Xion

`FrmParametrizarCajas` crea y modifica simultáneamente `Equipo` y
`ParametroCaja`. El primero mezcla identidad física/lógica y el segundo concentra
en una tabla ancha reglas de ventas, cartera, impresión, periféricos, móviles,
puntos y arqueo.

La funcionalidad útil se conserva; el acoplamiento no.

### Decisión de UX

Existe una sola opción **Cajas**:

- la vista inicial lista cajas con sede, bodega, código, estado, dispositivo,
  sincronización, turno y alertas;
- **Nueva caja** abre un asistente corto;
- seleccionar una caja abre su ficha y todas sus pestañas de configuración;
- no existe otra opción de menú llamada “Parametrizar cajas”;
- clonar configuración es una acción explícita y auditada, no duplica identidad,
  credenciales ni numeración.

### Asistente de creación

1. Identidad: nombre, código de caja único por Business y estado.
2. Ubicación: sede y bodega.
3. Operación: perfil base de venta, pagos y turno.
4. Fiscal: serie operativa y asignación fiscal válida.
5. Dispositivo: enrolar ahora o dejar pendiente.
6. Periféricos: impresora y balanza opcionales.
7. Resumen y activación.

Una caja no queda operativa hasta tener las dependencias obligatorias válidas.

### Ficha de caja

Pestañas:

1. General.
2. Venta y captura.
3. Clientes, vendedores y crédito.
4. Pagos y turno.
5. Impresión.
6. Periféricos.
7. Fiscal y numeración.
8. Offline y sincronización.
9. Dispositivos.
10. Permisos efectivos y auditoría.

### Separación interna

- `CashRegisters`: identidad lógica, Business, sede, bodega, código y estado.
- `CashRegisterSettings`: reglas operativas tipadas y versionadas.
- `PosDevices`: instalación física, credencial, revocación y última conexión.
- `CashRegisterDeviceAssignments`: historial de asignación si se requiere
  reasignación segura.
- `PrintProfiles`: plantilla, ancho, copias, corte, apertura de cajón y versión.
- `PeripheralProfiles`: impresora, balanza, cajón y terminal de pago.
- `DocumentSeries`: numeración operativa Auraly.
- `FiscalSeries`: numeración DIAN separada.
- `Warehouse.AllowNegativeStockSales`: política de negativos heredada por todas
  las cajas asociadas a la bodega.

No se repite BusinessId, LocationId o WarehouseId en `PosDevices` si pueden
derivarse de la asignación activa a `CashRegisters`; antes de retirar columnas
existentes se hará una migración segura y pruebas de integridad.

### Herencia de configuración

Solo se permite herencia donde reduzca duplicación sin ocultar reglas:

```text
valor efectivo = caja ?? sede ?? negocio ?? valor predeterminado de Auraly
```

La política de negativos es una excepción: pertenece a la bodega, no puede
sobrescribirse por caja. La interfaz muestra el valor heredado y enlaza a la
configuración de la bodega.

Los datos físicos del dispositivo, puertos e impresoras no se heredan porque
pertenecen a la instalación local.

Cada paquete de configuración tiene versión. POS Edge descarga el valor efectivo,
lo conserva en SQLite y reporta la versión aplicada. Un cambio genera delta; no
requiere volver a descargar todo el catálogo.

### Configuraciones de Xion que se conservan rediseñadas

- bodega, sede, nombre, código y estado;
- escaneo repetido y venta por empaque;
- vendedor requerido;
- venta a crédito según permiso y condición del cliente;
- múltiples medios de pago;
- fondo, turno, retiros, arqueo y tolerancias;
- impresora, copias, corte, cajón y tirilla;
- balanza desde el MVP;
- venta offline y sincronización;
- límites de borradores/facturas suspendidas cuando el caso de uso lo requiera;
- perfiles de columnas sin exponer costo o margen al cajero;
- mensajes de impresión dentro de plantillas versionadas.

No se conservan como caja:

- fidelización;
- domicilios;
- parámetros de servidor escritos manualmente;
- IMEI/teléfono como identidad;
- permisos desactivables de auditoría;
- IVA único que reemplace los impuestos del producto;
- campos duplicados por cada documento y tamaño de papel.

## 12. Alcance por rebanadas

### Rebanada Party/Customer actual

- Countries, AdministrativeDivisions y Cities;
- Party, contactos y sedes;
- Customer y configuración de precio excluyente lista/canal;
- creación web y creación rápida desde POS;
- importación/reconciliación de ExternalCommerceCustomers;
- snapshot del adquirente;
- pruebas SQL, API, permisos, concurrencia y offline.

### Rebanada Product/Supplier

- Supplier sobre Party;
- árbol ProductCategories;
- marcas, unidades e impuestos conectados al formulario Producto;
- proveedores y costos por producto;
- UI de Terceros y centro de Maestros para estas capacidades.

### Rebanada Workforce/Security

- Employee, Seller, Carrier y Driver;
- vínculo opcional UserAccount-Party;
- asignaciones por negocio/sede;
- permisos y migración controlada.

### Rebanada Cajas

- pantalla unificada;
- configuración tipada;
- enrolamiento y revocación de dispositivo;
- perfiles de impresión y periféricos;
- balanza;
- distribución incremental de configuración;
- pruebas desde creación hasta operación real del POS.

Los motivos de inventario, arqueo y pagos se entregan junto con los módulos que
los consumen.

## 13. Pruebas de aceptación del diseño

- la misma Party puede tener varios roles sin duplicar identidad;
- agregar o desactivar un rol no modifica los demás;
- proveedor, transportador o cliente no obtiene usuario automáticamente;
- empleado/vendedor puede enlazarse a un usuario existente con permiso;
- varias sedes comparten identificación y una identificación distinta crea otra Party;
- importaciones incompletas no inventan datos y pueden enriquecerse;
- no existe maestro de barrios;
- un producto referencia un solo árbol de clasificación;
- no hay tablas Familia1-Familia4;
- los maestros normativos no pueden alterarse como datos libres;
- no existe menú por cada tabla;
- crear y parametrizar caja son un solo flujo;
- caja lógica y dispositivo físico conservan IDs y ciclos de vida separados;
- cambiar dispositivo no cambia historia, series ni número de caja;
- la política de negativos se modifica en bodega y todas sus cajas la heredan;
- la caja muestra la configuración efectiva y su origen;
- la configuración llega a POS Edge de forma versionada e incremental;
- ningún maestro se declara listo sin consumidor, interfaz, API, SQL Server y pruebas.

## 14. Resultado

Auraly conserva lo mejor de Xion: una identidad común, configuración rica de caja
y conocimiento funcional de los maestros. Lo mejora con composición, módulos
dueños, una navegación compacta, configuración tipada, caja separada del
dispositivo y eliminación de catálogos legacy sin uso.

La implementación debe seguir esta matriz; cualquier maestro adicional requiere
un caso de uso, propietario, consumidor y pruebas antes de entrar al alcance.
