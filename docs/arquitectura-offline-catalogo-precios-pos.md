# Arquitectura offline de catálogo y precios para Auraly POS

**Estado:** decisión de arquitectura obligatoria para el MVP  
**Fecha:** 23 de julio de 2026  
**Prioridad:** complemento del alcance definitivo de POS y devoluciones

---

## 1. Decisión principal

La caja no consultará la API de Auraly por cada producto.

Siempre operará local-first:

```text
Lector/Búsqueda
      ↓
Auraly POS PWA
      ↓ localhost
Auraly POS Edge
      ↓
SQLite local
```

La nube se usa para:

- aprovisionar la caja;
- descargar catálogo inicial;
- enviar cambios incrementales;
- subir ventas y movimientos;
- actualizar estados fiscales;
- monitorear el dispositivo.

Esto significa que el flujo de escaneo es el mismo con o sin internet. La pérdida de red no cambia de pantalla ni activa un motor diferente.

---

## 2. Componentes locales

### 2.1 PWA

El navegador conserva:

- interfaz;
- recursos estáticos;
- rutas del POS;
- estado visual temporal;
- Service Worker;
- canal autenticado con POS Edge.

La PWA no será la fuente durable de productos, ventas ni certificados.

### 2.2 Auraly POS Edge

Servicio local .NET 8 para Windows:

- SQLite cifrado;
- API en `localhost`;
- búsqueda de productos;
- motor local de precios;
- impuestos;
- códigos de balanza;
- ventas guardadas;
- outbox;
- sincronización;
- impresión;
- cajón;
- balanza;
- certificado y numeración offline;
- recuperación.

### 2.3 SQLite

Contiene solamente los datos necesarios para la caja y su negocio. No replica toda la base central.

---

## 3. Catálogo local

### 3.1 Tablas

```text
LocalProducts
LocalProductBarcodes
LocalProductUnits
LocalProductAliases
LocalProductCategories
LocalProductWarehouseSettings
LocalPriceLists
LocalProductPrices
LocalPromotions
LocalTaxProfiles
LocalInventoryBalances
LocalCustomers
LocalCustomerPriceAssignments
LocalCashRegisterSettings
LocalSyncState
LocalOutbox
```

### 3.2 `LocalProducts`

Campos de consulta:

- `ProductId`;
- código interno/SKU;
- referencia;
- nombre;
- descripción corta;
- descripción larga resumida;
- marca;
- categoría;
- unidad base;
- inventariable;
- producto por peso;
- código de balanza;
- lote/serial;
- activo;
- versión.

Las imágenes son opcionales y se descargan en tamaño reducido. Nunca bloquean una venta.

### 3.3 Códigos

`LocalProductBarcodes` almacena:

- código;
- producto;
- unidad;
- factor;
- tipo;
- código de balanza;
- principal;
- activo.

Se crea un índice único para resolución exacta.

---

## 4. Búsqueda local

### 4.1 Escaneo

La consulta exacta se realiza contra un índice:

```sql
Barcode -> ProductId + UnitId + ConversionFactor
```

Objetivo de respuesta: menos de 50 ms.

Si es código de balanza:

1. interpretar patrón;
2. extraer código de producto;
3. extraer peso o precio;
4. validar checksum;
5. obtener producto;
6. calcular cantidad;
7. agregar línea.

### 4.2 Búsqueda textual

SQLite FTS5 indexa:

- SKU;
- referencia;
- nombre;
- descripciones;
- alias;
- marca;
- categoría;
- código de proveedor.

Características:

- sin distinguir mayúsculas;
- normalización de acentos;
- coincidencia por prefijo;
- varias palabras;
- priorización de exactos;
- resultados limitados y paginados.

Objetivo:

- primeros resultados en menos de 150 ms;
- navegación con flechas;
- Enter agrega;
- Escape regresa al escáner.

### 4.3 Orden de resolución

```text
1. código exacto
2. SKU exacto
3. referencia exacta
4. alias exacto
5. coincidencia textual priorizada
```

No se elige arbitrariamente si un valor tiene varias coincidencias.

### 4.4 Producto no descargado

Offline:

- no se puede consultar la nube;
- se informa que no existe en el catálogo local;
- se registra el código no encontrado;
- una línea manual requiere permiso.

Online:

- la búsqueda puede consultar la API como respaldo;
- si encuentra un producto válido para esa caja, lo descarga y lo incorpora localmente;
- la siguiente lectura ya se resuelve localmente.

---

## 5. Precios locales

### 5.1 Contexto

El precio efectivo depende de:

- tenant;
- negocio;
- caja;
- bodega;
- lista predeterminada;
- cliente;
- unidad/empaque;
- cantidad;
- fecha y hora;
- promoción;
- canal POS;
- permisos.

### 5.2 Paquete de precios

El servidor no envía solamente un precio final. Envía un paquete versionado:

- listas aplicables;
- precios por producto y unidad;
- vigencia;
- promociones;
- descuentos por cantidad;
- reglas de acumulación;
- impuestos;
- redondeo;
- moneda;
- zona horaria;
- límites de descuento;
- versión del motor.

```text
PricingPackageVersion
TaxPackageVersion
CashRegisterConfigurationVersion
```

Cada venta y cada línea guardan esas versiones.

### 5.3 Precedencia

La regla se define una vez y se prueba con vectores comunes. Propuesta:

```text
1. precio contractual del cliente
2. promoción prioritaria válida
3. lista asignada al cliente
4. lista predeterminada de la caja
5. precio base autorizado
```

Después se aplican:

```text
descuento por cantidad
    -> descuento manual autorizado
    -> impuestos
    -> redondeo
```

Si una promoción no puede acumularse con descuentos manuales, el motor lo impide.

Esta precedencia debe validarse contra los negocios piloto antes de congelarla.

### 5.4 Fotografía

Al agregar una línea se guarda:

- precio de lista;
- precio aplicado;
- origen del precio;
- descuentos;
- impuestos;
- regla;
- versión;
- fecha.

El servidor no cambia silenciosamente el precio de una venta offline cuando se sincroniza.

Si detecta una versión antigua:

- acepta la transacción comercial;
- marca `StalePricing`;
- genera alerta según tolerancia;
- conserva el precio realmente cobrado.

### 5.5 Venta guardada

Recuperar una venta guardada muestra:

- precios originales;
- versión;
- cambios disponibles.

Opciones:

- conservar precios;
- actualizar todos;
- revisar diferencias.

Actualizar requiere una acción explícita. Nunca ocurre sin que el cajero lo sepa.

---

## 6. Impuestos

El paquete local contiene:

- perfil tributario del producto;
- tasas;
- impuesto al consumo;
- bases;
- inclusión en precio;
- reglas de redondeo;
- vigencia.

El cliente calcula de inmediato y el servidor revalida al sincronizar.

La factura conserva la fotografía tributaria. Un cambio posterior en el producto no altera ventas anteriores.

---

## 7. Inventario local

La existencia local se calcula:

```text
último saldo sincronizado
+ entradas locales
- ventas locales
+ devoluciones locales
- averías locales
± ajustes locales autorizados
```

Se muestra:

- existencia local estimada;
- fecha de última sincronización;
- pendientes no subidos.

La política de venta negativa pertenece a la caja:

- habilitada: el saldo es informativo;
- deshabilitada: se valida el saldo local al confirmar.

El saldo local no pretende representar otras cajas desconectadas.

---

## 8. Descarga inicial

### 8.1 Aprovisionamiento

1. registrar dispositivo;
2. asociarlo a caja;
3. autenticar;
4. descargar manifiesto;
5. validar compatibilidad;
6. descargar paquetes;
7. verificar hash y firma;
8. construir base temporal;
9. crear índices;
10. cambiar atómicamente a la nueva base;
11. habilitar caja.

### 8.2 Manifiesto

Incluye:

- tenant;
- negocio;
- caja;
- bodega;
- versión de esquema;
- cantidad de productos;
- versiones de catálogo, precios, impuestos y configuración;
- tamaño;
- hashes;
- fecha del servidor;
- vigencia;
- bloques.

### 8.3 Descarga por bloques

El catálogo se descarga comprimido y paginado:

```text
products-0001
products-0002
barcodes
prices
taxes
settings
customers
```

Una interrupción reanuda desde el último bloque válido.

La caja no usa una base a medio descargar.

---

## 9. Sincronización incremental

### 9.1 Revisiones

Cada cambio central recibe una revisión monotónica:

```text
CatalogRevision
PricingRevision
TaxRevision
ConfigurationRevision
```

La caja guarda el último checkpoint aplicado.

### 9.2 Deltas

Se transmiten:

- productos creados/modificados/desactivados;
- códigos;
- unidades;
- precios;
- promociones;
- impuestos;
- clientes necesarios;
- parámetros;
- existencias.

Los cambios se aplican en una transacción local.

Si un delta falla:

- no se avanza el checkpoint;
- se conserva el paquete anterior;
- se reintenta;
- se muestra diagnóstico.

### 9.3 Frecuencia

Cuando hay red:

- cambios urgentes por notificación;
- consulta incremental periódica;
- sincronización manual;
- sincronización al abrir caja.

No se hace polling cada 500 ms como el motor anterior.

### 9.4 Orden al reconectar

1. comprobar identidad y reloj;
2. subir ventas y operaciones pendientes;
3. recibir confirmaciones;
4. descargar estados fiscales;
5. descargar configuración crítica;
6. descargar catálogo y precios;
7. descargar existencias;
8. actualizar el checkpoint.

Las ventas se suben antes de reemplazar sus reglas locales para conservar la versión con la que se hicieron.

---

## 10. Catálogo y precios desactualizados

### 10.1 Configuración

```text
AllowOfflineSales
MaxCatalogAge
MaxPricingAge
AllowSalesWithStaleCatalog
AllowSalesWithStalePricing
StalePricingRequiresSupervisor
BlockAfterExpiration
```

### 10.2 Estados

- `Current`;
- `Aging`;
- `Stale`;
- `Expired`;
- `Invalid`.

### 10.3 Recomendación

- catálogo: permitir varios días si no hay cambios críticos;
- precios: tolerancia menor y configurable;
- impuestos/configuración fiscal: bloquear cuando expire una regla crítica;
- mostrar advertencia antes del bloqueo;
- permitir excepción solo si la empresa acepta el riesgo.

No se debe detener una caja porque perdió internet durante una hora. Tampoco se debe permitir indefinidamente vender con precios de meses atrás.

---

## 11. Hora y vigencias

Promociones y precios dependen del reloj.

POS Edge guarda:

- última hora confiable del servidor;
- diferencia con reloj local;
- tiempo monotónico;
- zona horaria.

Si el reloj cambia de forma sospechosa:

- se alerta;
- no se activan promociones dudosas;
- puede exigirse supervisor o conexión.

Esto evita obtener precios antiguos cambiando manualmente la fecha del equipo.

---

## 12. Seguridad

- SQLite cifrado;
- dispositivo registrado;
- claves rotables;
- paquetes firmados;
- hash por bloque;
- API local autenticada;
- PWA autorizada por origen;
- precios y permisos con versión;
- auditoría de cambios manuales;
- datos de clientes limitados;
- borrado seguro al revocar caja;
- certificado fiscal fuera de la base de catálogo.

Manipular SQLite no debe permitir enviar ventas confiables al servidor. Cada operación incluye identidad, firma/hash e información de versión, y el servidor revalida.

---

## 13. Experiencia del cajero

Indicador discreto:

```text
En línea
Catálogo actualizado hace 3 min
Precios v1842
0 ventas pendientes
```

Offline:

```text
Sin conexión
Catálogo actualizado hace 2 h
Precios vigentes
7 ventas pendientes
```

Estado riesgoso:

```text
Sin conexión
Precios desactualizados
Se requiere supervisor
```

La búsqueda sigue disponible en todos esos estados.

---

## 14. Administración

El portal muestra por caja:

- versión instalada;
- última sincronización;
- productos locales;
- precios;
- tamaño;
- último error;
- ventas pendientes;
- tiempo offline;
- catálogo vigente;
- configuración;
- acción de forzar sincronización;
- reconstrucción de catálogo;
- revocación.

El soporte puede descargar un diagnóstico sin certificado ni información sensible.

---

## 15. Recuperación

### Corrupción de catálogo

- detectar al abrir;
- conservar última copia válida;
- reconstruir índices;
- descargar snapshot si hay red;
- no tocar outbox de ventas.

### Catálogo perdido

- las ventas pendientes están en almacenamiento separado o respaldado;
- no se inicializa sobre ellas;
- se recupera outbox antes de reprovisionar.

### Cambio de caja o bodega

- requiere cerrar turno;
- subir pendientes;
- descargar nuevo paquete;
- limpiar datos no aplicables;
- confirmar política de negativos, precios y numeración.

---

## 16. Criterios de aceptación

### Productos

- desconectar internet no cambia la búsqueda;
- código exacto responde en menos de 50 ms;
- texto responde en menos de 150 ms;
- busca por código, referencia, nombre y alias;
- código de balanza funciona offline;
- producto desactivado por delta deja de venderse después de aplicar el paquete.

### Precios

- calcula lista, empaque, cantidad, promoción y descuento offline;
- guarda origen y versión;
- sincronización no cambia una venta ya cobrada;
- venta guardada no se repricia silenciosamente;
- impuestos y total coinciden con pruebas del servidor.

### Sincronización

- descarga inicial es reanudable;
- no expone catálogo parcial;
- delta fallido no avanza checkpoint;
- reconexión sube ventas sin duplicar;
- luego actualiza catálogo;
- revocar dispositivo impide nuevas sincronizaciones.

### Resiliencia

- reiniciar conserva catálogo y ventas;
- caída durante actualización conserva versión anterior;
- corrupción no borra outbox;
- interfaz muestra edad y versión;
- política de expiración se cumple.

---

## 17. Resumen

La caja tendrá su propia copia operativa de:

- productos;
- códigos;
- unidades;
- precios;
- promociones;
- impuestos;
- parámetros;
- existencias estimadas.

Todo escaneo y búsqueda ocurre localmente. Auraly sincroniza cambios en segundo plano mediante snapshots y deltas versionados.

Ese diseño no solo permite trabajar offline: también hace que la caja sea rápida y evita que una latencia de Azure detenga la fila.
