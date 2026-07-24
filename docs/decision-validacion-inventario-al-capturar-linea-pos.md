# Decisión: validar inventario al capturar la línea del POS

**Estado:** decisión vigente  
**Fecha:** 23 de julio de 2026  
**Prioridad:** reemplaza cualquier texto anterior que indique que la validación principal de inventario ocurre al confirmar o pagar la venta.

---

## 1. Regla

Cuando una caja no permite ventas negativas, el inventario se valida online:

- al escanear el producto;
- al seleccionar un producto desde la búsqueda;
- al modificar la cantidad;
- al cambiar unidad o empaque;
- al recuperar una venta temporal;
- al cambiar la bodega asociada, si se permitiera durante la operación.

No se espera hasta confirmar la venta.

---

## 2. Comportamiento según la caja

### `AllowNegativeStockSales = true`

- resuelve producto localmente;
- agrega la línea;
- no consulta inventario;
- permite continuar;
- la existencia puede consultarse manualmente si el usuario lo desea.

### `AllowNegativeStockSales = false`

- resuelve producto localmente;
- determina cantidad base;
- consulta disponibilidad online;
- marca el resultado en la línea;
- permite, rechaza o solicita autorización;
- vuelve el foco al escáner.

---

## 3. Flujo al escanear

```text
Escanear código
    -> resolver producto en SQLite
    -> cantidad inicial = 1 o factor del empaque
    -> consultar disponibilidad online
    -> aplicar resultado
    -> dejar foco listo para el siguiente código
```

Para producto por peso:

```text
leer código/balanza
    -> calcular peso
    -> convertir a cantidad base
    -> validar esa cantidad online
```

---

## 4. Estados de validación de línea

Cada línea mantiene:

```text
NotRequired
Pending
Available
Insufficient
Authorized
Unavailable
```

### `NotRequired`

La caja permite negativos.

### `Pending`

La consulta está en curso.

### `Available`

La cantidad solicitada estaba disponible en la revisión consultada.

### `Insufficient`

La existencia no cubre la cantidad.

### `Authorized`

Un supervisor permitió continuar.

### `Unavailable`

No fue posible consultar inventario.

---

## 5. Respuesta de disponibilidad

Solicitud:

```json
{
  "cashRegisterId": "uuid",
  "warehouseId": "uuid",
  "productId": "uuid",
  "unitId": "uuid",
  "requestedQuantity": 6,
  "saleDraftId": "uuid",
  "lineId": "uuid"
}
```

Respuesta:

```json
{
  "status": "Insufficient",
  "productId": "uuid",
  "requestedQuantity": 6,
  "availableQuantity": 4,
  "checkedAt": "2026-07-23T17:00:00Z",
  "inventoryRevision": 98123
}
```

La cantidad se convierte a unidad base antes de comparar.

---

## 6. Experiencia del cajero

### Disponible

- indicador verde discreto;
- no abre ventana;
- el foco queda listo.

### Insuficiente

Se muestra inmediatamente:

```text
Solicitado: 6
Disponible: 4
```

Acciones:

- usar cantidad disponible;
- cambiar cantidad;
- eliminar línea;
- consultar otras bodegas;
- solicitar autorización para vender negativo.

### Sin conexión

Como la caja no almacena inventario:

- no inventa una disponibilidad;
- aplica `OfflineStockPolicy`;
- muestra claramente que no pudo validar.

---

## 7. Cambios rápidos de cantidad

Editar `1 -> 12 -> 10` no debe generar resultados fuera de orden.

El cliente:

- espera un debounce corto;
- cancela la consulta anterior;
- asigna secuencia a cada solicitud;
- ignora respuestas viejas;
- valida la última cantidad.

Propuesta:

```text
debounce = 150 a 300 ms
```

Enter fuerza validación inmediata.

---

## 8. Escaneos consecutivos

Si cada lectura incrementa la misma línea:

```text
1 -> 2 -> 3
```

La validación se hace con la cantidad acumulada.

Para evitar una petición descontrolada por cada carácter o evento:

- se agrupan lecturas muy cercanas;
- se valida inmediatamente después de una ráfaga corta;
- la línea muestra `Pending`;
- el escáner permanece operativo.

Si el negocio exige validación estricta antes del siguiente producto, puede configurarse, aunque ralentiza la fila.

---

## 9. Validación en lote

Al recuperar una venta temporal o un pedido:

- no se realizan veinte llamadas independientes;
- se envía una solicitud con todas las líneas;
- la respuesta devuelve estado por producto;
- la grilla resalta únicamente los faltantes.

Ejemplo:

```json
{
  "warehouseId": "uuid",
  "lines": [
    { "lineId": "1", "productId": "p1", "quantity": 2 },
    { "lineId": "2", "productId": "p2", "quantity": 5 }
  ]
}
```

---

## 10. Concurrencia

Validar al agregar la línea mejora la experiencia, pero no reserva automáticamente la existencia.

Ejemplo:

1. Caja A consulta la última unidad: disponible.
2. Caja B consulta la última unidad: disponible.
3. Ambas intentan vender.

Por eso, para una caja configurada para bloquear negativos, el servidor debe proteger la escritura final.

Esto no significa repetir un proceso visible de validación al cobrar. Es una invariante transaccional:

- la línea ya fue validada durante la captura;
- al guardar, el servidor intenta aplicar la salida sin quedar negativo;
- si otra caja consumió el saldo, devuelve un conflicto;
- la interfaz muestra solamente las líneas afectadas;
- el supervisor puede autorizar o el cajero ajustar.

Sin esta protección, “no vender negativos” no puede garantizarse con varias cajas.

---

## 11. Reserva opcional

Para reducir conflictos puede habilitarse:

```text
ReserveStockWhileSaleIsActive
```

Al validar:

- crea reserva corta;
- se renueva mientras la venta está activa;
- se consume al confirmar;
- se libera al eliminar línea, guardar temporal o cancelar;
- expira automáticamente.

No se recomienda activarla por defecto en comercio presencial porque puede retener inventario por ventas abandonadas. Se evalúa con el piloto.

---

## 12. Rendimiento y carga

Solo las cajas con negativos deshabilitados consultan inventario.

Medidas:

- endpoint específico y liviano;
- índices por tenant, bodega y producto;
- consultas agrupadas;
- cancelación de solicitudes obsoletas;
- cache en memoria de milisegundos/segundos únicamente para absorber duplicados;
- sin persistencia local de existencias;
- métricas de latencia y errores.

Objetivo:

```text
p95 de validación < 300 ms
```

La resolución del producto y precio sigue siendo local, por lo que una demora de inventario no bloquea la búsqueda.

---

## 13. Venta offline

Para cajas que bloquean negativos:

```text
OfflineStockPolicy
```

Opciones:

- `AllowWithoutValidation`;
- `RequireSupervisor`;
- `BlockOfflineSale`.

El estado de la línea será `Unavailable` o `Authorized`, nunca `Available` sin consultar.

Para cajas que permiten negativos, offline funciona normalmente y la línea usa `NotRequired`.

---

## 14. Criterios de aceptación

- escanear con negativos deshabilitados consulta cantidad 1;
- cambiar cantidad vuelve a validar;
- cambiar empaque valida cantidad base;
- balanza valida el peso;
- faltante se informa durante la captura;
- venta temporal se valida en lote al recuperar;
- respuestas antiguas no pisan cantidades nuevas;
- cajas con negativos habilitados no generan consultas;
- offline no presenta inventario ficticio;
- el foco vuelve al escáner;
- la protección final evita carrera entre cajas;
- un conflicto final no obliga a rehacer toda la venta;
- las métricas distinguen disponible, insuficiente, autorizado y sin conexión.
