# Decisión: ventas negativas heredadas de la bodega

**Estado:** decisión corregida y vigente  
**Fecha:** 23 de julio de 2026  
**Prioridad:** esta decisión reemplaza cualquier sección anterior que ubique `AllowNegativeStockSales` directamente en la caja.

## Contexto

En Xion cada caja o equipo se asocia a una bodega mediante `ParametroCaja.BodegaId`.

El método de facturación recibe el equipo, obtiene su bodega y consulta:

```text
Bodega.VenderNegativo
Bodega.ValidarProducto
```

Esto es intencional: todas las cajas que descargan inventario de la misma bodega deben comportarse bajo la misma política. No es una limitación del modelo.

## Decisión para Auraly

La fuente de verdad será:

```text
Warehouse.AllowNegativeStockSales
```

La relación será:

```text
CashRegister.DefaultWarehouseId -> Warehouse.Id
```

Por lo tanto:

```text
política efectiva de la caja =
    Warehouse.AllowNegativeStockSales
```

No se duplicará la propiedad en `CashRegisterSettings`.

## Comportamiento

### Cuando está habilitada

- todas las cajas asociadas a la bodega pueden vender aunque el saldo resulte negativo;
- la captura y confirmación no se bloquean por existencia;
- la salida se registra normalmente;
- el saldo negativo genera una alerta para conciliación;
- estar offline no cambia la regla.

### Cuando está deshabilitada

- todas las cajas asociadas a la bodega validan existencia;
- el faltante se muestra antes de confirmar;
- el negocio puede permitir continuar mediante autorización de supervisor;
- la excepción queda auditada;
- offline se valida contra el saldo local conocido, entendiendo que puede estar desactualizado.

## Pantallas de configuración

### Parametrizar bodega

Contiene:

- permitir ventas con saldo negativo;
- validar productos de la bodega;
- política de autorización;
- generar alertas de negativos;
- responsable de conciliación.

### Parametrizar caja

Contiene:

- bodega asociada;
- muestra la política de negativos efectiva;
- indica que el valor es heredado;
- ofrece un enlace a la configuración de la bodega si el usuario tiene permiso;
- advierte que cambiar de bodega cambia inventario, política y operación offline.

La caja no puede sobrescribir la política en el MVP.

## Por qué se conserva la herencia

- evita que dos cajas que venden desde la misma existencia tengan reglas contradictorias;
- centraliza el control operativo;
- simplifica sincronización offline;
- facilita auditoría;
- permite cambiar una vez la política para todas las cajas de una bodega;
- coincide con la forma en que Xion fue diseñado y utilizado.

## Implicaciones offline

El paquete de configuración local incluye:

- `WarehouseId`;
- `AllowNegativeStockSales`;
- versión de configuración;
- fecha de sincronización.

Una venta offline guarda la versión utilizada.

Si la bodega permite negativos, la venta continúa.

Si no los permite, la caja usa:

- último saldo sincronizado;
- movimientos locales posteriores;
- autorizaciones locales válidas.

El servidor nunca elimina una venta ya cobrada por encontrar después una diferencia. La marca para conciliación.

## Criterios de aceptación

- dos cajas de la misma bodega heredan la misma política;
- cambiar la política de la bodega actualiza todas sus cajas en la siguiente sincronización;
- la pantalla de caja muestra de dónde viene el valor;
- no existe una copia divergente en la tabla de caja;
- cambiar la bodega de una caja exige confirmación y nueva sincronización;
- una venta registra la bodega y versión de configuración usadas;
- las excepciones por supervisor quedan auditadas;
- reportes de negativos agrupan por bodega y muestran la caja que originó cada salida.
