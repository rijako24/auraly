# Decisión definitiva: negativos por bodega y sin inventario local

**Estado:** vigente  
**Fecha:** 24 de julio de 2026  
**Prevalencia:** este documento reemplaza cualquier texto anterior que ubique la política en la caja, permita políticas distintas entre cajas de una misma bodega, valide solo al confirmar o use existencias locales.

## Fuente de verdad

```text
CashRegister.DefaultWarehouseId -> Warehouse.Id
Warehouse.AllowNegativeStockSales
```

La política pertenece a la **bodega**. Todas las cajas asociadas la heredan y ninguna caja puede sobrescribirla.

## Si la bodega permite negativos

- el POS resuelve el producto y el precio desde su catálogo local;
- no consulta inventario para capturar la línea;
- puede vender online u offline;
- el motor registra la salida contra la bodega de la caja;
- si el saldo queda negativo, genera trazabilidad y alerta de conciliación.

## Si la bodega bloquea negativos

- al escanear/agregar el producto se consulta en línea la disponibilidad;
- al cambiar la cantidad se vuelve a consultar;
- se descartan respuestas antiguas si el cajero cambia rápidamente la cantidad;
- si la cantidad no está disponible, se informa inmediatamente;
- el motor revalida dentro de la transacción final para cubrir concurrencia;
- si no hay red, la venta se captura sin validación de existencia y queda pendiente
  de sincronización; el POS no inventa ni persiste un saldo local.

La caja nunca descarga ni reconstruye inventario. Su almacenamiento local contiene catálogo, códigos, precios efectivos, canales, promociones aplicables y configuración, pero no existencias.

`AllowNegativeStockSales` sí se conserva como configuración local de la bodega
enrolada. Cambiarla guarda un outbox transaccional, envía una invalidación push y el
Edge recupera la instantánea de configuración. Si permite negativos, capturar no hace
una consulta remota; si los bloquea y hay conexión, se consulta disponibilidad al
capturar y al cambiar cantidad.

## Configuración

La pantalla de bodega permite cambiar la política con permiso y auditoría. La pantalla de caja:

- selecciona la bodega;
- muestra la política efectiva como valor heredado;
- no permite editarla;
- obliga a resincronizar configuración al cambiar de bodega.

## Documentos anteriores

Ante contradicción, prevalece este documento. En particular quedan reemplazadas las secciones que mencionen:

- `CashRegisterSettings.AllowNegativeStockSales`;
- “política de negativos por caja”;
- dos cajas de la misma bodega con políticas distintas;
- validación principal únicamente al confirmar;
- “existencia local conocida” o “último saldo sincronizado”;
- autorización local basada en un inventario descargado.
