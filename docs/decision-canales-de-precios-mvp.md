# Decisión: canales de precios incluidos en el MVP

## Prevalencia

Este documento corrige cualquier especificación anterior que excluya los canales de precios.

La decisión definitiva es:

> Auraly Commerce no migrará las listas de precios heredadas de Xion, pero sí tendrá canales de precios desde el MVP.

Los canales pertenecen al módulo Pricing. No son promociones y no son reglas fiscales.

---

## 1. Hallazgo verificado en Xion

Xion permite:

- asignar canales a sucursales;
- asociarlos con clientes;
- habilitarlos en cajas;
- excluir casa comercial, departamento, sección, familia, subfamilia o producto;
- calcular precios mediante diferentes estrategias;
- conservar el canal aplicado en la línea vendida.

Las estrategias encontradas incluyen:

- porcentaje sobre precio con impuesto incluido;
- porcentaje sobre precio sin impuesto;
- venta al costo;
- porcentaje de pie de factura;
- aplicación por margen;
- último costo;
- costo promedio;
- precio especial.

También aparecen puntos y venta sin IVA. Esos comportamientos no se migran dentro del canal:

- puntos están fuera del MVP;
- un canal no puede suprimir impuestos;
- una exención o exclusión de IVA pertenece a la configuración fiscal del producto, cliente y operación.

---

## 2. Librerías

```text
Auraly.Domain.Pricing
Auraly.Application.Pricing
Auraly.Infrastructure.Pricing
Auraly.Contracts.Pricing
```

Pricing será propietario de:

- precio base;
- canales;
- asignaciones;
- exclusiones;
- cálculo de precio efectivo;
- historial;
- simulación;
- versiones;
- proyección para cajas.

Promotions consume el precio resuelto por Pricing y aplica beneficios después, según la precedencia definida.

---

## 3. Modelo

### `PriceChannels`

```text
PriceChannelId
TenantId
BusinessId
Name
Code
Strategy
Value
Priority
IsActive
ValidFromUtc
ValidToUtc
Version
CreatedAtUtc
UpdatedAtUtc
RowVersion
```

### Estrategias iniciales

```text
PercentageOverBasePrice
FixedSpecialPrice
PercentageOverAverageCost
FixedMarginOverAverageCost
SellAtAverageCost
```

No se debe trasladar una estrategia solo porque existe en Xion. Cada estrategia necesita fórmula, precisión, permisos y pruebas.

### Asignaciones

```text
PriceChannelAssignments
  PriceChannelAssignmentId
  PriceChannelId
  AssignmentType
  AssignmentId
  Priority
  IsActive
```

Tipos admitidos:

```text
Business
Branch
Customer
CashRegister
```

### Exclusiones

```text
PriceChannelExclusions
  PriceChannelExclusionId
  PriceChannelId
  ScopeType
  ScopeId
```

Alcances iniciales:

```text
Category
Brand
Product
```

No es necesario reproducir los cinco niveles de familias de Xion. Se utiliza la taxonomía real de Auraly.

---

## 4. Resolución del canal

Al iniciar o cambiar cliente, Facturación resuelve el canal aplicable.

Precedencia:

```text
Asignación explícita al cliente
        |
        v
Asignación a la caja
        |
        v
Asignación a la sucursal
        |
        v
Canal predeterminado del negocio
        |
        v
Sin canal: precio base
```

Si existen dos asignaciones con la misma prioridad, la configuración es inválida. No se escoge una al azar.

La caja puede permitir selección manual del canal únicamente con permiso. El usuario no puede usar un canal al que la caja o cliente no tengan acceso.

---

## 5. Pipeline de precio

El orden determinista será:

```text
1. Obtener precio base vigente
2. Resolver canal
3. Verificar exclusiones
4. Aplicar fórmula del canal
5. Aplicar promociones compatibles
6. Aplicar descuento manual autorizado
7. Calcular impuestos según perfil fiscal
8. Guardar snapshots y explicación
```

Los impuestos no se eliminan ni cambian por seleccionar un canal.

Cada línea conserva:

```text
BaseUnitPrice
PriceChannelId
PriceChannelVersion
PriceChannelStrategy
ChannelAdjustmentAmount
PromotionAdjustmentAmount
ManualDiscountAmount
TaxProfileId
TaxAmount
FinalUnitPrice
```

Esto permite auditar cómo se llegó al precio final.

---

## 6. Canales derivados del costo

Los canales que calculan desde costo promedio son sensibles.

Reglas:

- solo usuarios autorizados pueden configurarlos;
- el cajero no ve el costo;
- el servidor vuelve a calcular;
- la caja recibe únicamente el precio efectivo o una regla que no exponga el costo;
- un costo cero o inexistente bloquea el cálculo o utiliza una política explícita;
- se puede configurar margen mínimo;
- nunca permiten precio negativo;
- el cambio de costo genera una nueva revisión de precio para cajas afectadas.

Para operación offline, la caja debe recibir el precio efectivo ya materializado. No descarga el costo promedio.

---

## 7. Relación con promociones

Un canal define el precio comercial de referencia. Una promoción otorga un beneficio temporal.

Ejemplo:

```text
Precio base:                 $100.000
Canal mayorista -10%:         $90.000
Promoción adicional -5%:      $85.500
```

La promoción puede declarar:

```text
CanCombineWithPriceChannel
AllowedPriceChannelIds
```

Si no es combinable, el motor elige el beneficio conforme a una regla explícita, por ejemplo mayor ahorro permitido. Nunca depende del orden de consultas.

---

## 8. POS y sincronización

La primera sincronización incluye:

- precio base efectivo;
- canales accesibles para esa caja;
- asignaciones relevantes;
- exclusiones relevantes;
- revisión;
- precio materializado para estrategias basadas en costo.

Los cambios posteriores generan deltas:

- canal creado o modificado;
- asignación cambiada;
- producto excluido;
- precio base cambiado;
- costo cambiado cuando afecta un canal materializado;
- canal desactivado.

Cuando cambia el cliente:

1. se resuelve su canal;
2. se informa si cambiarán precios;
3. las líneas existentes no cambian silenciosamente;
4. el usuario ejecuta **Recotizar factura**;
5. se muestra antes/después;
6. queda auditoría.

---

## 9. Pedidos

Cada pedido conserva el canal y precio usados.

Al recuperar o facturar:

- se conserva el precio del pedido;
- el servidor valida que el canal existía y era aplicable;
- no se recotiza silenciosamente;
- si el pedido solicita recotización, se muestra el cambio;
- la factura conserva referencia al canal del pedido.

En facturación múltiple, cada pedido utiliza su propio snapshot de canal. No se aplica un canal global al lote.

---

## 10. Interfaz web

### Administración

```text
Pricing
  Canales
  Asignaciones
  Exclusiones
  Historial
  Simulador
```

El simulador recibe:

- producto;
- cliente;
- sucursal;
- caja;
- fecha;
- cantidad.

Y explica:

```text
Precio base
Canal resuelto
Motivo de asignación
Exclusiones
Fórmula
Promociones
Precio final
```

### POS

Facturación muestra el canal activo cerca del cliente. El cambio manual requiere permiso y vuelve a validar precios.

---

## 11. Proyecto de base de datos

Las tablas se agregan en:

```text
Auraly.Database.sqlproj
```

Se mantiene:

- una base;
- `dbo`;
- un DACPAC;
- sin migraciones EF.

Todos los importes y porcentajes usan `DECIMAL`.

---

## 12. Criterios de aceptación

- Canales de precios están incluidos en el MVP.
- Las listas de precios heredadas continúan excluidas.
- Pricing y Promotions son módulos distintos.
- El canal puede asignarse por cliente, caja, sucursal o negocio.
- Existen exclusiones por categoría, marca o producto.
- La resolución tiene precedencia determinista.
- Impuestos no se modifican desde el canal.
- El precio aplicado conserva snapshots y explicación.
- Canales basados en costo no exponen el costo a la caja.
- Las cajas offline reciben precios efectivos materializados.
- Un cambio genera deltas solo para cajas afectadas.
- Cambiar cliente no recotiza líneas silenciosamente.
- Pedidos conservan su canal.
- Las tablas pertenecen a `Auraly.Database.sqlproj`.
