# Decisión: reportes, consultas, filtros por columna y paginación

**Estado:** decisión transversal y obligatoria para el MVP de Auraly Commerce  
**Fecha:** 27 de julio de 2026  
**Referencias analizadas:** reportes y consultas de Xion WinForms, Xion Web y documentos funcionales de Auraly Commerce  
**Principio:** no migrar reportes ni pantallas literalmente; conservar las preguntas de negocio, dimensiones, fórmulas, filtros y casos operativos para construir una experiencia mejor.

---

## 1. Decisión ejecutiva

Toda la biblioteca de reportes de Xion y Xion Web se utilizará como una base de conocimiento para Auraly.

Por cada reporte heredado se determinará:

- qué pregunta responde;
- quién lo usa;
- qué decisiones permite tomar;
- cuál es la fuente de datos;
- qué documentos y estados incluye;
- qué fórmula utiliza;
- qué dimensiones y filtros ofrece;
- qué permisos necesita;
- si aplica al alcance actual;
- si debe conservarse, combinarse, simplificarse, mejorarse o descartarse.

No se migrarán:

- Crystal Reports;
- formularios WinForms;
- vistas MVC antiguas;
- controladores repetidos;
- consultas que cargan todos los datos y luego filtran en memoria;
- parámetros con nombres ambiguos;
- cálculos duplicados por reporte;
- diseños impresos como fuente de verdad.

Auraly tendrá:

1. un catálogo semántico de métricas y dimensiones;
2. reportes operativos dentro de cada módulo;
3. análisis transversales en `Reporting`;
4. tablas web con una experiencia común;
5. filtros por encabezado combinables;
6. ordenamiento estable;
7. paginación siempre desde servidor para listados y resultados;
8. totales calculados sobre el conjunto filtrado;
9. exportaciones asíncronas con los mismos filtros y permisos;
10. pruebas que garanticen fórmulas, aislamiento, paginación y consistencia.

La regla transversal queda cerrada:

> Toda vista que muestre una colección consultable debe soportar paginación, filtros por encabezado aplicables de forma combinada y ordenamiento desde servidor.

---

## 2. Hallazgos útiles de Xion

### 2.1 Xion WinForms

La revisión encontró reportes y consultas sobre:

- facturas y detalle;
- devoluciones de venta;
- entradas de mercancía;
- devoluciones de compra;
- traslados;
- inventarios y productos no contados;
- kardex;
- averías;
- arqueos;
- ventas por tipo de pago;
- ventas a crédito;
- entradas y salidas de caja;
- diferencias por usuario;
- descuentos;
- cuentas por cobrar;
- cuentas por pagar;
- productos y precios por canal;
- baja rotación;
- proveedores;
- vendedores;
- pedidos;
- auditoría de ventas;
- informe fiscal;
- impuestos;
- cierres e Informe Z.

También existen reportes de módulos que quedaron fuera del MVP, por ejemplo:

- apartados;
- cotizaciones;
- producción;
- conversiones;
- puntos y bonos;
- concesiones;
- lotes y seriales;
- metas, rutas y comisiones avanzadas;
- cargue de mercancía;
- contabilidad general y exógena.

Estos últimos sirven únicamente como referencia histórica. No amplían el alcance confirmado.

### 2.2 Xion Web

Xion Web aporta una base especialmente valiosa para:

- temporada de productos;
- comparativas por proveedor;
- impacto de ventas;
- ventas por producto y cliente;
- pedidos por vendedor, producto o documento;
- devoluciones;
- inventario y productos agotados;
- canales de precios;
- arqueos;
- movimientos de vendedores;
- comparaciones mensuales;
- exportación PDF y Excel;
- paginación;
- filtros principales y secundarios;
- ordenamiento;
- consultas de totales.

La existencia de métodos como `Get...Paginado`, filtros compuestos y reportes separados confirma que la necesidad ya estaba identificada. Auraly debe conservarla, pero unificar contratos y evitar que cada pantalla implemente una variante distinta.

---

## 3. Qué significa “usar Xion como base”

Cada reporte heredado se registra en una matriz:

| Campo | Descripción |
|---|---|
| `LegacyReport` | Nombre y ubicación en Xion/Xion Web |
| `BusinessQuestion` | Pregunta que responde |
| `Audience` | Rol que lo utiliza |
| `Measures` | Métricas |
| `Dimensions` | Dimensiones |
| `Filters` | Filtros |
| `SourceDocuments` | Documentos y estados |
| `Formula` | Regla de cálculo |
| `KnownProblems` | Ambigüedades o errores |
| `UsageEvidence` | Evidencia de uso |
| `AuralyDestination` | Reporte, dashboard o consulta destino |
| `Decision` | Adoptar, combinar, mejorar o descartar |
| `GoldenTest` | Dataset y resultado esperado |

Un archivo `.rpt`, una clase de informe o una pantalla no es la especificación final. La especificación final es la pregunta, la fórmula y el resultado probado.

---

## 4. Arquitectura de Reporting

### 4.1 Módulos

```text
Auraly.Application.Reporting
Auraly.Infrastructure.Reporting
Auraly.Contracts.Reporting
```

`Auraly.Domain.Reporting` solo será necesario cuando existan conceptos con comportamiento propio, por ejemplo:

- definición guardada;
- programación;
- suscripción;
- distribución;
- snapshot certificado.

Para el MVP, las consultas operativas permanecen cerca de su módulo:

```text
Auraly.Application.Sales.Reports
Auraly.Application.Inventory.Reports
Auraly.Application.Purchasing.Reports
Auraly.Application.Cash.Reports
Auraly.Application.Receivables.Reports
Auraly.Application.Payables.Reports
```

`Auraly.Application.Reporting` coordina análisis transversales como utilidad, comparativos y consolidaciones.

### 4.2 Misma base inicialmente

El MVP usa la misma base SQL y esquema `dbo`.

No se agrega:

- data warehouse independiente;
- otra base;
- cubo OLAP;
- lakehouse;
- Power BI como dependencia obligatoria;
- esquema separado por módulo.

Sí se permiten:

- vistas SQL controladas por el proyecto de base;
- proyecciones de lectura;
- tablas agregadas mantenidas por el motor;
- índices específicos;
- jobs de reconstrucción;
- caché de catálogos;
- una réplica de lectura futura sin cambiar contratos.

### 4.3 Responsabilidad de los módulos

Cada módulo publica hechos confirmados:

```text
SaleConfirmed
SaleReturned
GoodsReceiptConfirmed
PurchaseReturned
InventoryMoved
InventoryAdjusted
DamageConfirmed
CashSessionClosed
ReceivableCreated
ReceivablePaymentApplied
PayableCreated
PayablePaymentApplied
SupplierCostActivated
ElectronicInvoiceStatusChanged
```

Reporting consume proyecciones o consulta fuentes autorizadas. No reimplementa los efectos del documento.

### 4.4 Consistencia

Los reportes operativos muestran documentos:

- definitivos;
- procesados por el motor;
- dentro de los estados seleccionados explícitamente.

Los borradores y documentos fallidos no se mezclan con cifras definitivas.

Cada respuesta debe indicar:

```text
asOf
timeZone
currency
dataFreshness
appliedFilters
```

Cuando una proyección sea eventual, la UI muestra la última actualización. No debe aparentar tiempo real si no lo es.

---

## 5. Catálogo semántico

### 5.1 Métricas versionadas

Las métricas no se codifican de manera distinta en cada reporte.

Ejemplos:

```text
sales.grossAmount.v1
sales.discountAmount.v1
sales.returnAmount.v1
sales.netAmount.v1
sales.taxAmount.v1
sales.costAmount.v1
sales.grossProfit.v1
sales.grossMargin.v1
purchases.netAmount.v1
inventory.valuation.v1
receivables.balance.v1
payables.balance.v1
cash.expectedAmount.v1
cash.differenceAmount.v1
```

Cada definición documenta:

- descripción;
- fórmula;
- granularidad;
- documentos incluidos;
- estados incluidos;
- tratamiento de devoluciones;
- impuestos incluidos o excluidos;
- moneda;
- zona horaria;
- costo utilizado;
- versión;
- pruebas doradas.

### 5.2 Definiciones mínimas

```text
Venta bruta
= suma de líneas antes de descuentos, sin documentos anulados

Venta neta
= venta bruta - descuentos - devoluciones

Costo de venta
= snapshot de costo de cada línea confirmada
   - costo revertido por devoluciones

Utilidad bruta
= venta neta sin impuestos trasladados - costo de venta

Margen bruto %
= utilidad bruta / venta neta sin impuestos trasladados * 100
```

La fórmula exacta se cerrará con datos reales de Xion y reglas fiscales de Auraly. Lo importante es que exista una única definición versionada.

### 5.3 Dimensiones

- fecha y hora;
- día, semana, mes y año;
- empresa/negocio;
- sucursal;
- bodega;
- caja;
- sesión de caja;
- canal de precio;
- vendedor;
- cliente;
- proveedor;
- producto;
- categoría;
- marca;
- medio de pago;
- tipo de documento;
- estado;
- origen del pedido;
- usuario;
- ciudad, departamento y barrio cuando aplique.

Los reportes solo ofrecen dimensiones compatibles con la métrica y el permiso.

---

## 6. Catálogo mínimo de reportes

### 6.1 Ventas

- ventas por rango;
- ventas por negocio y sucursal;
- ventas por caja y sesión;
- ventas por vendedor;
- ventas por cliente;
- ventas por producto, categoría y marca;
- ventas por canal de precio;
- ventas por medio de pago;
- descuentos;
- impuestos;
- ventas a crédito;
- ventas offline sincronizadas;
- ventas anuladas;
- devoluciones;
- comparativo por día o mes;
- utilidad y margen.

### 6.2 Pedidos

- pedidos creados;
- pendientes;
- confirmados;
- facturados;
- cancelados;
- origen/canal;
- pedidos por cliente;
- pedidos por producto;
- pedidos por vendedor;
- tiempo hasta facturación;
- conversión de pedido a factura;
- pedidos con error de facturación.

### 6.3 Compras y proveedores

- entradas por período;
- entradas por proveedor;
- compras por producto;
- compras por sucursal y bodega;
- costo negociado frente a costo recibido;
- variación de costos;
- última compra;
- devoluciones a proveedor;
- productos por proveedor;
- proveedores por producto;
- proveedor principal y alternos;
- cambios pendientes;
- cuentas por pagar originadas.

### 6.4 Inventario

- existencia actual en línea;
- existencia por bodega;
- kardex;
- movimientos por tipo;
- traslados enviados, recibidos y pendientes;
- inventarios físicos;
- diferencias de conteo;
- ajustes;
- averías;
- negativos;
- productos sin movimiento;
- baja rotación como análisis posterior o piloto;
- valoración;
- productos sin costo;
- productos sin proveedor.

### 6.5 Caja y arqueo

- sesiones;
- apertura;
- ventas por medio de pago;
- entradas y salidas;
- efectivo esperado;
- conteo;
- diferencias;
- autorizaciones;
- devoluciones;
- ventas a crédito;
- arqueo por caja y usuario;
- consolidado por negocio/sucursal.

### 6.6 Cuentas por cobrar

- saldos;
- movimientos;
- facturas pendientes;
- abonos;
- vencimientos;
- antigüedad;
- cartera por cliente;
- cartera por negocio;
- documentos vencidos;
- conciliación con ventas.

### 6.7 Cuentas por pagar

- saldos;
- movimientos;
- entradas pendientes de pago;
- abonos;
- vencimientos;
- antigüedad;
- cartera por proveedor;
- documentos vencidos;
- conciliación con entradas.

### 6.8 Facturación electrónica

- documentos generados;
- enviados;
- aceptados;
- rechazados;
- pendientes;
- contingencia;
- notas crédito;
- intentos y errores;
- tiempos de respuesta;
- resoluciones y rangos;
- documentos que requieren intervención.

### 6.9 Seguridad y operación

- actividad de usuarios;
- cambios de permisos;
- autorizaciones excepcionales;
- cambios de precios y costos;
- documentos reprocesados;
- errores del motor;
- sincronización de cajas;
- cajas desactualizadas;
- exportaciones de información sensible.

---

## 7. Reportes que no amplían el MVP

Los reportes heredados de estas capacidades no obligan a construirlas:

- apartados;
- cotizaciones;
- remisiones;
- producción;
- conversiones;
- lotes;
- seriales;
- puntos;
- bonos especializados;
- concesiones;
- fletes;
- retenciones;
- rutas/GPS;
- metas;
- comisiones avanzadas;
- contabilidad general;
- exógena;
- cargue de mercancía.

Se conservan en el inventario legado para una fase futura.

---

## 8. Contrato tabular transversal

### 8.1 Componente común

El portal usará un componente compartido:

```text
AuralyDataGrid
```

No será una tabla con reglas de negocio. Proporcionará:

- encabezados;
- filtros tipados;
- ordenamiento;
- paginación;
- selección;
- estados de carga y error;
- densidad;
- columnas fijadas;
- visibilidad de columnas;
- accesibilidad;
- atajos de teclado;
- exportación;
- persistencia de vista.

Cada módulo aporta:

- definición de columnas;
- tipos de filtro permitidos;
- endpoint;
- acciones;
- permisos;
- formateadores;
- totales;
- claves estables.

### 8.2 Metadatos de columna

```text
GridColumnDefinition
--------------------
Key
Label
DataType
Filterable
AllowedOperators
Sortable
SensitivePermission?
Width?
Pinned?
Format?
LookupSource?
```

El cliente solo puede solicitar campos y operadores incluidos en esta lista.

---

## 9. Filtros por encabezado

### 9.1 Regla

Cada columna que represente un dato consultable tendrá un control de filtro en su encabezado.

Filtros activos de distintas columnas se combinan con `AND`.

Ejemplo:

```text
Sucursal = Centro
AND Estado IN (Confirmada, Enviada)
AND Total >= 100000
AND Fecha BETWEEN 2026-07-01 AND 2026-07-31
AND Cliente CONTAINS "María"
```

Dentro de una selección múltiple se usa `OR`:

```text
Estado = Confirmada OR Enviada
```

### 9.2 Operadores por tipo

#### Texto

- contiene;
- no contiene;
- es igual;
- no es igual;
- empieza por;
- termina en;
- vacío;
- no vacío.

La búsqueda define normalización de mayúsculas y acentos de forma consistente. Los identificadores exactos no se normalizan indebidamente.

#### Número y moneda

- igual;
- diferente;
- mayor;
- mayor o igual;
- menor;
- menor o igual;
- entre;
- vacío;
- no vacío.

#### Fecha/hora

- fecha exacta;
- antes;
- después;
- entre;
- hoy;
- ayer;
- semana actual;
- mes actual;
- rango personalizado.

Los límites se traducen usando la zona horaria del negocio y se consultan en UTC.

#### Enum/estado

- selección única;
- selección múltiple;
- excluir selección.

#### Booleano

- todos;
- sí;
- no.

#### Relaciones

- selector con búsqueda paginada;
- selección múltiple;
- por ID estable;
- muestra nombre y código.

Ejemplos: cliente, proveedor, producto, bodega, caja, vendedor.

### 9.3 Experiencia

- icono en cada encabezado filtrable;
- indicador cuando el filtro está activo;
- resumen en tooltip;
- chips visibles con todos los filtros activos;
- quitar un chip;
- limpiar filtro de columna;
- limpiar todos;
- aplicar con Enter;
- debounce corto para texto;
- no consultar por cada tecla sin control;
- conservar foco;
- mostrar errores de formato;
- mostrar cantidad filtrada;
- persistir filtros en URL cuando sea razonable;
- restaurar al volver a la vista;
- guardar vistas personales;
- copiar enlace si el usuario tiene permiso sobre los mismos datos.

### 9.4 Filtro global

El filtro global es adicional, no reemplaza los encabezados.

Solo busca en campos declarados:

```text
SearchableFields
```

No se construye un `LIKE` sobre todas las columnas.

---

## 10. Contrato de consulta

Para filtros simples se puede usar `GET`. Las grillas ricas utilizarán:

```text
POST /api/{module}/{resource}/search
```

Solicitud:

```json
{
  "page": {
    "number": 1,
    "size": 50
  },
  "search": "texto global opcional",
  "filters": [
    {
      "field": "branchId",
      "operator": "in",
      "values": ["uuid-1", "uuid-2"]
    },
    {
      "field": "documentDate",
      "operator": "between",
      "from": "2026-07-01",
      "to": "2026-07-31"
    },
    {
      "field": "total",
      "operator": "greaterThanOrEqual",
      "value": 100000
    }
  ],
  "sort": [
    {
      "field": "documentDate",
      "direction": "desc"
    },
    {
      "field": "id",
      "direction": "desc"
    }
  ],
  "includeTotals": true
}
```

Respuesta:

```json
{
  "items": [],
  "page": {
    "number": 1,
    "size": 50,
    "totalItems": 1280,
    "totalPages": 26,
    "hasNext": true,
    "hasPrevious": false
  },
  "totals": {
    "netAmount": 482000000,
    "taxAmount": 73100000
  },
  "asOf": "2026-07-27T16:10:00Z",
  "timeZone": "America/Bogota",
  "appliedFilters": []
}
```

El servidor:

- rechaza campos desconocidos;
- rechaza operadores incompatibles;
- limita tamaños;
- aplica tenant y alcances;
- parametriza SQL;
- usa orden estable;
- permite cancelar solicitudes;
- devuelve errores por campo.

---

## 11. Paginación

### 11.1 Regla general

No se carga una tabla completa para paginar en el navegador.

Valores recomendados:

```text
DefaultPageSize = 50
AllowedPageSizes = 25, 50, 100
MaximumPageSize = 200
```

Un endpoint no puede aceptar `pageSize = 0` como “traer todo”.

### 11.2 Paginación por página

Se usa cuando el usuario necesita:

- conocer total de páginas;
- saltar a una página;
- navegar listados administrativos;
- comparar resultados.

Aplica a productos, proveedores, documentos, usuarios y reportes acotados.

### 11.3 Cursor/keyset

Se usa en conjuntos muy grandes o de cambio frecuente:

- kardex;
- auditorías;
- movimientos;
- eventos del motor;
- logs;
- sincronizaciones.

Ejemplo:

```json
{
  "page": {
    "size": 100,
    "after": "opaque-cursor"
  }
}
```

El cursor es opaco. La UI no deriva ni modifica su contenido.

### 11.4 Orden estable

Toda consulta paginada termina con un desempate único:

```text
ORDER BY DocumentDate DESC, Id DESC
```

Esto evita filas duplicadas o ausentes entre páginas cuando varias comparten el mismo valor.

### 11.5 Cambio de filtros

Al cambiar:

- filtro;
- búsqueda;
- orden;
- alcance;
- tamaño de página;

la vista vuelve a la primera página o invalida el cursor.

---

## 12. Excepción: grillas de captura de documentos

La tabla de líneas de una factura, entrada, traslado, inventario o devolución no es un listado consultable independiente: representa un único documento que debe recalcularse como agregado.

Por ello:

- no usa paginación de servidor por línea;
- conserva todas las líneas del documento en el estado de trabajo;
- usa virtualización visual si hay muchas;
- permite desplazamiento y navegación por teclado;
- mantiene lector enfocado;
- recalcula totales sobre el documento completo;
- puede ofrecer agrupación o búsqueda interna.

Dividir una factura activa en páginas del servidor rompería:

- captura continua;
- totales inmediatos;
- eliminación/edición de líneas;
- navegación por teclado;
- consistencia del borrador.

La obligación de paginación aplica a las vistas de consulta y a las tablas que representan colecciones de registros. Las grillas transaccionales usan virtualización, que resuelve el rendimiento sin romper el documento.

---

## 13. Ordenamiento

- cada columna declara si es ordenable;
- orden simple y múltiple;
- el usuario ve prioridad y dirección;
- se conserva en URL/vista guardada;
- se ejecuta en servidor;
- siempre tiene desempate único;
- los nulos siguen una regla definida;
- texto usa colación consistente;
- montos ordenan por valor, no por texto formateado;
- fechas ordenan por instante UTC.

No se aceptan nombres de columna arbitrarios en SQL.

---

## 14. Totales

La UI diferencia:

```text
Subtotal de página
Total del conjunto filtrado
Total general autorizado
```

Por defecto, un reporte muestra:

- filas de la página;
- total de registros filtrados;
- agregados sobre todo el conjunto filtrado.

Los totales no se calculan sumando la página visible.

La consulta de filas y la de totales deben usar:

- mismos filtros;
- mismos permisos;
- mismos estados;
- misma zona horaria;
- misma versión de métrica.

Si el total es costoso:

- se calcula solo al solicitarlo;
- se sirve desde proyección;
- o se ejecuta de forma asíncrona.

---

## 15. Exportación

Exportar no significa descargar únicamente la página visible, salvo que el usuario lo seleccione explícitamente.

Opciones:

```text
ExportCurrentPage
ExportFilteredResult
```

`ExportFilteredResult`:

1. conserva filtros, orden, columnas y zona horaria;
2. vuelve a validar permisos;
3. crea un job;
4. genera CSV/XLSX y PDF solo cuando el formato aporte valor;
5. registra progreso;
6. almacena temporalmente;
7. entrega un enlace con expiración;
8. audita usuario y alcance;
9. elimina el archivo según retención.

Para datos sensibles:

- permiso específico;
- marca de agua opcional;
- auditoría;
- límite;
- cifrado en tránsito y reposo.

No se debe depender de Crystal Reports para exportar.

---

## 16. Vistas guardadas

El usuario puede guardar:

- nombre;
- recurso;
- filtros;
- orden;
- columnas visibles;
- ancho y fijación;
- tamaño de página;
- versión de definición.

Alcances:

```text
Private
SharedWithProfile
SharedWithBusiness
```

Compartir requiere permiso. Una vista guardada no concede acceso a datos ni campos sensibles.

Los favoritos iniciales pueden sembrarse por perfil:

- Ventas de hoy;
- Facturas pendientes DIAN;
- Productos negativos;
- CxC vencida;
- CxP próxima a vencer;
- Diferencias de arqueo;
- Cambios de costo pendientes.

---

## 17. Seguridad

Cada consulta:

1. autentica;
2. valida permiso de reporte/vista;
3. aplica negocio;
4. aplica sucursales, bodegas y cajas;
5. limita campos sensibles;
6. valida filtros;
7. ejecuta;
8. filtra acciones;
9. audita exportaciones.

Ejemplos:

```text
reports.sales.view
reports.sales.view-cost
reports.sales.view-profit
reports.purchases.view
reports.inventory.view
reports.inventory.view-valuation
reports.cash.view
reports.receivables.view
reports.payables.view
reports.electronic-invoicing.view
reports.export
reports.saved-views.share
```

Un usuario sin permiso de utilidad:

- no ve columnas de costo/utilidad;
- no puede filtrarlas;
- no puede ordenarlas;
- no recibe sus totales;
- no puede exportarlas;
- no puede recuperarlas mediante una vista guardada.

---

## 18. Rendimiento

### 18.1 Principios

- filtrar antes de proyectar;
- proyectar solo columnas necesarias;
- no usar `Include` de grafos completos;
- consultas `AsNoTracking`;
- SQL parametrizado;
- índices alineados con tenant, fecha, estado y claves frecuentes;
- evitar funciones sobre columnas indexadas cuando bloqueen el índice;
- búsquedas relacionales paginadas;
- cancelación;
- timeout;
- límites de rango para consultas costosas;
- jobs para exportaciones.

### 18.2 Índices

Cada vista documenta sus patrones principales.

Ejemplo:

```text
(BusinessId, DocumentDate DESC, Id DESC)
(BusinessId, BranchId, DocumentDate DESC, Id DESC)
(BusinessId, Status, DocumentDate DESC, Id DESC)
(BusinessId, ProductId, WarehouseId, OccurredAt DESC, Id DESC)
```

Los índices se agregan por el proyecto SQL y se validan con planes reales.

### 18.3 Objetivos

Con volúmenes representativos:

- primera página operativa p95 menor a 2 segundos;
- cambio de filtro común p95 menor a 1,5 segundos;
- selector/autocomplete p95 menor a 500 ms;
- navegación de página sin cargar el conjunto completo;
- exportación grande asíncrona.

Son objetivos de aceptación que deben medirse en Cloud y On-Premise, no promesas sin benchmark.

---

## 19. Concurrencia y estabilidad de resultados

Mientras el usuario pagina pueden entrar documentos nuevos.

Para reportes financieros o conciliaciones se ofrece:

```text
snapshotAsOf
```

Todas las páginas consultan el mismo corte lógico.

Para listados operativos en vivo:

- orden estable;
- cursor cuando conviene;
- aviso de nuevos registros;
- botón actualizar;
- no insertar filas silenciosamente y desplazar la selección.

---

## 20. On-Premise

Los mismos contratos funcionan en ambos modos.

Cloud:

- API;
- Azure SQL;
- Worker;
- almacenamiento de exportaciones;
- telemetría administrada.

On-Premise:

- IIS/API;
- SQL Server;
- `Auraly.Worker`;
- almacenamiento local protegido;
- SignalR local;
- OpenTelemetry/logs.

Los reportes fundamentales no dependen de un servicio cloud.

Una exportación on-premise:

- se genera en el Worker local;
- usa almacenamiento temporal configurado;
- respeta cuota;
- expira;
- queda auditada.

---

## 21. Pruebas obligatorias

### 21.1 Filtros

Por cada columna:

- cada operador permitido;
- valores nulos;
- acentos;
- mayúsculas;
- caracteres especiales;
- límites numéricos;
- fechas y zona horaria;
- enums múltiples;
- relaciones;
- operador inválido rechazado.

Combinaciones:

- dos filtros;
- tres o más filtros;
- global + encabezados;
- `AND` entre columnas;
- `OR` dentro de selección;
- filtro + orden + página;
- filtro + totales;
- filtro + exportación.

### 21.2 Paginación

- tamaño predeterminado;
- máximo;
- página inexistente;
- primera y última;
- total;
- orden estable;
- sin duplicados;
- sin omisiones;
- cambio de filtros reinicia;
- cursor inválido;
- concurrencia;
- aislamiento por tenant.

### 21.3 Métricas

- datasets dorados;
- venta;
- descuento;
- devolución;
- impuestos;
- costo;
- utilidad;
- pagos mixtos;
- crédito;
- documentos anulados;
- zonas horarias;
- cierre de mes.

### 21.4 Seguridad

- menú;
- endpoint;
- fila fuera de alcance;
- columna sensible;
- filtro sensible;
- orden sensible;
- total sensible;
- exportación;
- vista guardada;
- revocación de permiso.

### 21.5 Rendimiento

- millones de movimientos;
- catálogo grande;
- múltiples filtros;
- orden no predeterminado;
- totales;
- primera página;
- página profunda;
- cursor;
- exportación;
- Cloud;
- On-Premise.

### 21.6 E2E

- abrir vista;
- filtrar en encabezados;
- combinar;
- quitar uno;
- ordenar;
- paginar;
- cambiar tamaño;
- conservar URL;
- guardar vista;
- exportar;
- volver;
- permiso deshabilitado;
- error recuperable;
- teclado y lector de pantalla.

---

## 22. Migración de conocimiento

Por cada reporte Xion seleccionado:

1. tomar un conjunto de datos representativo;
2. ejecutar el reporte heredado;
3. conservar parámetros y resultado;
4. documentar ambigüedades;
5. implementar la métrica Auraly;
6. comparar;
7. explicar diferencias intencionales;
8. aprobar con negocio;
9. dejar prueba dorada.

No se persigue igualdad de píxel ni de archivo. Se persigue igualdad o mejora de significado.

Ejemplos de mejora:

- combinar tres reportes en una vista con drill-down;
- reemplazar una impresión por tabla, resumen y exportación;
- separar venta bruta, neta y utilidad;
- mostrar filtros activos;
- permitir pasar de total a documentos;
- explicar la fórmula;
- enlazar documento origen;
- guardar vista;
- ejecutar exportación sin bloquear.

---

## 23. Definición de terminado para cualquier vista

Una vista tabular no está terminada hasta que:

- pagina desde servidor;
- filtra por encabezados compatibles;
- combina filtros;
- ordena establemente;
- aplica permisos y alcances;
- muestra carga, vacío y error;
- conserva filtros;
- limita tamaño;
- funciona con volumen real;
- tiene índices;
- pasa pruebas;
- es usable con teclado;
- exporta de forma segura si aplica;
- distingue total de página y total filtrado.

Un reporte no está terminado hasta que además:

- define métrica y versión;
- declara documentos y estados;
- trata devoluciones;
- declara impuestos y costo;
- respeta zona horaria;
- tiene dataset dorado;
- concilia con Xion cuando corresponda.

---

## 24. Decisión final

La base de reportes de Xion y Xion Web es un activo estratégico. No debe desecharse ni trasladarse literalmente.

Auraly absorberá:

- preguntas;
- métricas;
- dimensiones;
- filtros;
- fórmulas;
- jerarquías;
- necesidades de exportación;
- experiencia operativa.

Y mejorará:

- consistencia semántica;
- seguridad;
- rendimiento;
- navegación;
- filtros;
- paginación;
- trazabilidad;
- exportación;
- pruebas;
- despliegue Cloud/On-Premise.

La norma transversal queda establecida: todas las listas y consultas de datos usan filtros por encabezado combinables, ordenamiento y paginación de servidor. Las grillas de captura de un documento completo usan virtualización en vez de paginación para no romper la operación del cajero o bodeguero.
