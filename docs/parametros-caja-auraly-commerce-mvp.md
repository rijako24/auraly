# Parámetros de caja para Auraly Commerce

**Estado:** complemento normativo del diseño de Auraly Commerce  
**Fecha:** 23 de julio de 2026  
**Documento base:** `docs/diseno-auraly-commerce-mvp.md`

Este documento revisa el módulo **Parametrizar cajas** de Xion y define qué se conserva, qué se rediseña y qué queda fuera del MVP. En caso de diferencia con el documento base, las decisiones de este complemento tienen prioridad.

---

## 1. Hallazgo sobre ventas con inventario negativo

En Xion, `ParametroCaja` contiene la bodega que usa cada equipo, pero no contiene directamente la propiedad `VenderNegativos`.

El método `ZFacturaService.VenderNegativos(equipoId)`:

1. obtiene la bodega configurada para el equipo;
2. consulta la configuración de esa bodega;
3. permite negativos cuando `Bodega.VenderNegativo` es verdadero y `Bodega.ValidarProducto` es falso.

Cuando Xion encuentra productos sin existencia, muestra una ventana que permite:

- eliminar los productos sin existencia; o
- continuar mediante permiso o autorización para vender negativos.

Cuando la caja no tiene conexión, la validación de existencia se omite.

Esto significa que Xion presenta el comportamiento desde el contexto de la caja, pero la regla realmente pertenece a la bodega. Para Auraly se hará explícita y más simple.

## 2. Decisión para Auraly

La propiedad que decide si una venta POS puede dejar saldo negativo pertenecerá a la caja:

```text
CashRegisterSettings.AllowNegativeStockSales
```

Reglas:

- `true`: la caja no bloquea la venta por existencia insuficiente. Registra la salida, permite saldo negativo y crea una alerta de conciliación.
- `false`: la caja valida antes de confirmar. Si falta existencia, bloquea o solicita una autorización de supervisor, según el permiso configurado.
- el escaneo nunca se frena para consultar el servidor; la comprobación, cuando está activa, ocurre al confirmar o de forma local no bloqueante durante la captura;
- la existencia mostrada al cajero es informativa;
- `Products.TrackInventory` solo indica si el producto mueve inventario;
- la bodega conserva reglas para traslados, reservas, ajustes y otros movimientos, pero no decide el comportamiento de una caja;
- una venta confirmada no se borra posteriormente porque la sincronización encuentre un saldo negativo.

La configuración recomendada por defecto para comercio presencial es `true`: si el cliente tiene el artículo físicamente en la mano, la caja debe poder venderlo. Sin embargo, se conserva la opción por caja porque una empresa puede tener cajas de despacho, bodegas controladas u operaciones con políticas distintas.

### 2.1 Precedencia

Para ventas POS:

1. configuración de la caja;
2. permiso excepcional del supervisor cuando la caja bloquea;
3. nunca una propiedad contradictoria del producto o de la bodega.

Para pedidos remotos, reservas y comercio electrónico se usa la política del canal, no la de la caja.

Para traslados, consumos, producción futura y otros movimientos se usa la política del módulo y de la bodega.

### 2.2 Modo offline

Si `AllowNegativeStockSales = true`, la caja offline continúa normalmente.

Si `AllowNegativeStockSales = false`, solo puede comparar:

- la última existencia sincronizada;
- entradas y salidas locales posteriores;
- reservas locales conocidas.

Ese valor no garantiza la existencia central mientras otras cajas estén desconectadas. Por lo tanto, Auraly debe ofrecer una de estas políticas:

| Política offline | Comportamiento |
|---|---|
| Permitir y alertar | Continúa, registra la excepción y concilia al sincronizar |
| Exigir supervisor | Permite con autorización local auditable |
| Bloquear offline | No confirma cuando el saldo local es insuficiente |
| Inventario asignado | La caja consume una existencia previamente reservada para ella |

Para el MVP se recomiendan las dos primeras. Una garantía estricta requiere particionar o reservar inventario por caja, lo cual añade complejidad.

---

## 3. Modelo de configuración

No se copiará la tabla ancha de Xion con decenas de booleanos. Se propone un agregado tipado:

- `CashRegisters`: identidad y relaciones.
- `CashRegisterSettings`: reglas operativas.
- `CashRegisterDevices`: dispositivo y capacidades.
- `CashRegisterPrintProfiles`: impresión.
- `CashRegisterPaymentSettings`: medios e integraciones.
- `CashRegisterFiscalSettings`: perfil fiscal y numeración.
- `CashRegisterOfflineSettings`: sincronización y operación local.

Las opciones se exponen por secciones en la interfaz y tienen valores heredables:

```text
valor efectivo = caja ?? sede ?? negocio ?? valor predeterminado de Auraly
```

Las reglas críticas deben quedar materializadas en el paquete de configuración offline de la caja. Cada venta guarda la versión de configuración con la que fue realizada.

---

## 4. Configuraciones de Xion que sí sirven para el MVP

### 4.1 Identidad y asignación

| Configuración Xion | Decisión Auraly | Motivo |
|---|---|---|
| `EquipoId` | Adoptar como `CashRegisterId` + `DeviceId` | Caja y dispositivo no deben ser la misma entidad |
| `SucursalId` | Adoptar como `BranchId` | Define establecimiento |
| `BodegaId` | Adoptar como `DefaultWarehouseId` | Toda venta necesita bodega de salida |
| `CentroCostoId` | Opcional en MVP | Útil para reportes; no implica contabilidad general |
| estado del equipo | Adoptar | Permite revocar una caja |
| alias | Adoptar | Nombre visible de la caja |
| responsable | Adoptar opcional | Custodio o responsable operativo |

Una caja puede cambiar de dispositivo sin perder su identidad, historial o numeración. Un dispositivo se registra, revoca y reemplaza independientemente.

### 4.2 Inventario

| Nueva configuración | MVP | Comportamiento |
|---|---:|---|
| `AllowNegativeStockSales` | Sí | Regla principal por caja |
| `NegativeStockOverridePermission` | Sí | Autorización de supervisor cuando está desactivada |
| `ShowStockOnLine` | Sí | Existencia informativa en grilla |
| `CreateNegativeStockAlert` | Sí | Genera excepción para conciliación |
| `DefaultWarehouseId` | Sí | Bodega afectada |
| `OfflineInsufficientStockPolicy` | Sí | Permitir, autorizar o bloquear |

La alerta debe indicar caja, bodega, producto, saldo previo conocido, cantidad vendida, saldo resultante, usuario, dispositivo y estado de conciliación.

### 4.3 Venta y captura

| Configuración Xion | Decisión Auraly | Alcance |
|---|---|---|
| `VenderxEmbalaje` | Adoptar | Un código puede representar empaque y factor |
| `VenderConLista` | Adoptar | Lista predeterminada y permiso para cambiar |
| `VenderConCanal` | Simplificar | Canal de venta predeterminado |
| `AgruparFactura` | Adoptar con nombre claro | Escaneo repetido incrementa línea o crea línea nueva |
| `ExigeVendedor` | Adoptar | Requerir vendedor antes de confirmar |
| `TieneVendedor` | Reemplazar | Se deriva de que la caja use vendedor |
| `VendedorDetalle` | Diferir | Solo si se paga comisión por línea |
| `ValorMinimoFactura` | Adoptar opcional | Mínimo de venta con permiso de excepción |
| `ExigeObservacionDevolucion` | Adoptar | Motivo y observación de devolución |
| `ExigeLoteAlFinal` | Rediseñar | Captura obligatoria según producto, no global ciega |
| `ExigeSerialAlFinal` | Rediseñar | Captura por unidad serializada |
| `ProductoNoCodificadosAlFinal` | Adoptar como flujo | Líneas manuales se revisan antes de confirmar |
| `CodigoInteligente` | No copiar aún | Primero se debe definir la regla que representaba |

`AgruparFactura` se convierte en:

```text
RepeatedScanBehavior = IncrementQuantity | AddNewLine
```

El valor normal para supermercado o comercio minorista es `IncrementQuantity`. Para productos con serial, lote específico, precio manual o modificadores puede forzarse una línea nueva.

### 4.4 Clientes, crédito y devoluciones

| Configuración Xion | Decisión Auraly | Alcance |
|---|---|---|
| `PermiteCartera` | Adoptar | Habilita venta a crédito desde esa caja |
| `ExigeTelefono` | Adoptar como regla de captura | Por tipo de documento/cliente |
| `ExigeEmail` | Adoptar | Especialmente para entrega electrónica |
| `ExigeClienteFacturaTemporal` | Reemplazar | Reglas para borrador/venta suspendida |
| `ExigeObservacionF9` | Renombrar | Observación obligatoria en la acción concreta |
| `ModoDevolucion` | Adoptar | Contra venta original o devolución excepcional |
| `RecibeChequesPostfechados` | Diferir | No es necesario para el primer MVP |
| `RealizaPrestamos` | Excluir | Módulo financiero especializado |

Permitir cartera en la caja no reemplaza las validaciones del cliente: cupo, mora, bloqueo y autorización siguen aplicando.

### 4.5 Impuestos y visibilidad

| Configuración Xion | Decisión Auraly | Alcance |
|---|---|---|
| `AplicaIvaUnico` | No copiar como regla general | El impuesto nace del producto y la operación |
| `IvaId` | No usar como reemplazo tributario global | Solo podría existir como excepción fiscal explícita |
| `MostrarCostoFactura` | Adoptar por rol, no solo por caja | Protege información sensible |
| `MostrarIvaFactura` | Adoptar | Columna configurable |
| `MostrarRentabilidadFactura` | Adoptar por rol | Oculta margen al cajero si corresponde |
| `MostrarVendedorDetalleFactura` | Diferir | Unido a vendedor por línea |
| opciones equivalentes de remisión | Reutilizar perfil de columnas | No duplicar booleanos por documento |

La grilla se configura mediante perfiles de columnas, pero seguridad de costo y margen siempre se valida también en el servidor.

### 4.6 Periféricos

| Configuración Xion | Decisión Auraly | Alcance |
|---|---|---|
| `TipoDeImpresora` | Adoptar | Térmica, sistema operativo o PDF |
| `PerfilDeImpresionId` | Adoptar | Plantilla y ancho |
| `TipoDeCajon` | Adoptar | Integración mediante agente Edge |
| `ModoDatafono` | Adoptar como integración opcional | Manual o integrado |
| `EntidadDatafono` | Rediseñar como proveedor/terminal | No enum rígido |
| `VenderConBascula` | Adoptar opcional | Solo cuando el piloto la requiera |
| `BalanzaId` | Reemplazar por `PeripheralId` | Dispositivo registrado |
| `EquipoVerificadorId` | Diferir | Verificador de precios futuro |

El lector de código de barras HID no necesita una opción especial para funcionar. Sí se configura sufijo, comportamiento de escaneo y, si se requiere, identificación del dispositivo.

### 4.7 Impresión

Xion guarda número de copias separado por documento y tamaño, mensajes, pies de página, corte de papel y posición. El conocimiento sirve, pero el modelo se simplifica:

- perfil por tipo de documento;
- impresora;
- tamaño/ancho;
- copias;
- cortar papel;
- abrir cajón;
- encabezado;
- pie;
- logo;
- imprimir automáticamente;
- representación fiscal o comprobante operativo;
- plantilla versionada.

No se crearán campos como `NoFacturasTirilla`, `NoFacturasMedia` y `NoFacturasCarta`. Se usarán filas en `CashRegisterPrintProfiles`.

### 4.8 Caja y arqueo

| Configuración Xion | Decisión Auraly | Alcance |
|---|---|---|
| `MaximoCajon` | Adoptar | Alerta o retiro sugerido |
| `NoIntentos` | Redefinir | Intentos de arqueo antes de escalar |
| `ImprimirFamiliaZ` | Diferir | Reporte Z configurable |
| copias de tirilla | Adoptar en perfil | Cierre y comprobantes |

También se requieren parámetros que no estaban claramente modelados en la entidad:

- fondo inicial obligatorio;
- apertura automática o manual;
- permitir múltiples turnos;
- permitir retiro de efectivo;
- límite para retiro;
- exigir conteo ciego;
- tolerancia de diferencia;
- aprobación de diferencias;
- permitir venta sin turno abierto.

Para el MVP, vender sin turno abierto debe estar desactivado por defecto.

---

## 5. Configuraciones útiles después del MVP

### 5.1 Domicilios y pedidos

- exigir domiciliario;
- exigir cliente en domicilio;
- exigir orden del cliente;
- equipo de pedidos vinculado;
- valor mínimo de pedido;
- canal y ruta;
- impresión de ticket de domicilio.

Auraly ya origina pedidos desde canales conversacionales. Estas reglas son valiosas, pero deben vivir en configuración de canales/pedidos y no mezclarse por completo con el POS.

### 5.2 Cotizaciones, apartados y remisiones

- copias y plantillas;
- mensajes;
- corte de papel;
- reglas de conversión;
- vencimientos.

El MVP puede tener venta suspendida y devolución. Cotización, apartado y remisión completos se agregan cuando haya demanda comercial confirmada.

### 5.3 Puntos y fidelización

- mostrar puntos;
- acumular;
- relación unidades/valor;
- mensajes de ganancia;
- saldos anterior, actual y acumulado.

Toda esta sección se aplaza. Cuando se construya, debe ser un módulo de fidelización con libro de puntos, no columnas sueltas en caja.

### 5.4 Facturas temporales

- máximo por día;
- vencimiento;
- cliente obligatorio.

Se reemplazan inicialmente por ventas suspendidas y borradores recuperables. Si “factura temporal” representa un documento fiscal o comercial específico, debe definirse antes de revivir el concepto.

---

## 6. Configuraciones que no se deben migrar como están

| Configuración Xion | Razón |
|---|---|
| `NoMostrarAuditoriaF6` | La auditoría sensible no puede desactivarse |
| `VersionTouch` | La web debe ser responsiva por capacidad, no tener dos versiones |
| `ServidorVinculado` | El endpoint y tenancy se administran de forma segura |
| `IMEI`, teléfono y modelo como caja | Se sustituyen por registro de dispositivo |
| `TiempoGps` dentro de caja | Pertenece a logística/dispositivo móvil |
| `MovilPedidos`, `MovilFacturacion` | Se expresan como capacidades/roles de aplicación |
| enums rígidos de datafono | Se usan adaptadores configurables |
| booleanos duplicados por factura/remisión | Se usan perfiles de grilla y documento |
| campos individuales por tamaño de papel | Se usan perfiles de impresión |
| mensajes de fidelización en caja | Pertenecen a plantillas del módulo |
| IVA único sin contexto fiscal | Riesgo de cálculo incorrecto |

---

## 7. Parámetros nuevos necesarios para Auraly

Xion no cubre explícitamente varias necesidades del nuevo diseño:

### 7.1 Fiscal

- `FiscalProfileId`;
- resolución/prefijo predeterminado;
- bloque de numeración offline;
- política de contingencia;
- emitir automáticamente;
- esperar validación antes de imprimir;
- formato de entrega;
- consumidor final predeterminado;
- alerta de rango;
- alerta de certificado.

### 7.2 Offline y sincronización

- permite operar offline;
- máximo tiempo sin sincronizar;
- versión mínima del catálogo;
- tamaño de lote;
- política de precio vencido;
- política de cliente no sincronizado;
- política de inventario insuficiente;
- último checkpoint;
- límite de pendientes;
- modo degradado;
- retención de datos locales;
- copia local cifrada.

### 7.3 Escáner y grilla

- comportamiento de escaneo repetido;
- columna inicial;
- salto después de editar cantidad;
- reproducción de sonido;
- confirmación visual;
- sufijo esperado;
- tiempo máximo entre caracteres;
- permitir código manual;
- abrir búsqueda si no existe;
- cantidades fraccionarias;
- comportamiento de códigos de balanza.

### 7.4 Pagos

- medios habilitados;
- efectivo predeterminado;
- pagos combinados;
- cambio máximo;
- redondeo de efectivo;
- integración de terminal;
- exigir referencia;
- permitir crédito;
- permitir pago mayor;
- manejo de propina cuando aplique.

### 7.5 Seguridad

- permisos para cambiar precio;
- aplicar descuento;
- vender negativo cuando la caja bloquea;
- anular línea;
- cancelar venta;
- reimprimir;
- abrir cajón;
- registrar ingreso/egreso;
- vender a crédito;
- exceder cupo;
- reabrir turno;
- ver costo/margen.

---

## 8. Interfaz de Parametrizar caja

La pantalla se divide en:

1. **General:** nombre, sede, bodega, perfil fiscal, estado.
2. **Venta:** negativos, escaneo, agrupación, lista, vendedor y mínimos.
3. **Cliente y crédito:** datos exigidos, cartera y autorizaciones.
4. **Inventario:** visibilidad, alertas y política offline.
5. **Pagos:** medios, terminal, efectivo y combinaciones.
6. **Caja:** turnos, fondo, arqueo, tolerancias y máximo de efectivo.
7. **Periféricos:** impresora, cajón, balanza y lector.
8. **Impresión:** perfiles y plantillas.
9. **Facturación electrónica:** perfil, numeración y contingencia.
10. **Offline:** operación local, sincronización y estado del dispositivo.
11. **Permisos efectivos:** vista de reglas heredadas y excepciones.

Cada opción muestra:

- valor actual;
- origen del valor: negocio, sede o caja;
- impacto;
- si requiere reiniciar/sincronizar;
- última modificación;
- usuario que la cambió.

Las opciones peligrosas requieren confirmación y quedan auditadas.

---

## 9. Configuración inicial recomendada

Para una caja minorista:

```text
AllowNegativeStockSales = true
ShowStockOnLine = true
CreateNegativeStockAlert = true
RepeatedScanBehavior = IncrementQuantity
AllowOfflineSales = true
OfflineInsufficientStockPolicy = AllowAndAlert
RequireOpenCashSession = true
AllowMixedPayments = true
RequireCustomerEmailForElectronicDelivery = false
RequireReturnReason = true
AutoPrintReceipt = true
```

Para una caja de despacho controlado:

```text
AllowNegativeStockSales = false
NegativeStockOverridePermission = Supervisor
OfflineInsufficientStockPolicy = RequireSupervisor
```

---

## 10. Criterios de aceptación

- dos cajas de la misma bodega pueden tener políticas de negativos diferentes;
- cambiar la bodega no cambia silenciosamente la política de la caja;
- una caja con negativos habilitados no consulta ni bloquea por existencia al confirmar;
- la salida puede llevar el saldo bajo cero y genera alerta;
- una caja con negativos deshabilitados muestra el faltante y aplica autorización;
- offline usa la política descargada y conserva su versión;
- sincronizar nunca elimina una venta ya cobrada;
- toda excepción identifica al supervisor;
- las opciones heredadas muestran su origen;
- desactivar una caja revoca nuevas operaciones y sincronización;
- costo y margen siguen protegidos aunque se manipule el cliente web;
- perfiles de impresión evitan columnas duplicadas por cada formato;
- las configuraciones se incluyen en pruebas automáticas de POS.

---

## 11. Conclusión

La parametrización de Xion contiene mucho conocimiento útil, pero mezcla identidad de equipo, reglas de venta, impresión, móviles, fidelización, periféricos y operación financiera en una sola entidad.

Auraly debe conservar las capacidades, no esa forma de almacenarlas. Para el MVP son esenciales:

- asignación de sede y bodega;
- política de negativos por caja;
- escaneo y agrupación;
- listas y empaques;
- cliente, vendedor y cartera;
- pagos;
- turnos y arqueo;
- periféricos e impresión;
- perfil fiscal;
- offline y sincronización;
- permisos y auditoría.

La decisión principal queda cerrada: **vender con inventario negativo es una configuración de la caja**. La opción debe venir habilitada por defecto para la caja presencial típica, porque la realidad física de la fila tiene prioridad, pero cada negocio puede endurecer cajas específicas y exigir autorización.
