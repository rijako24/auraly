# Actualización del orden de prevalencia del diseño Auraly Commerce

**Fecha:** 27 de julio de 2026  
**Motivo:** cerrar las decisiones posteriores sobre nombres, IDs, numeración local,
Producto, UX POS y aplicación de escritorio.

Este documento complementa `README-auraly-commerce-mvp.md`. Mientras ese índice no
sea consolidado mecánicamente, las decisiones de esta actualización prevalecen.

## Orden aplicable

1. `decision-identificadores-auraly.md`: UUIDv7, `ProductCode`, IDs legados,
   `SalesInvoiceId` local y asignación exclusiva de números por caja.
2. `decision-aplicacion-escritorio-auraly.md`: Auraly Desktop, WebView2,
   enrolamiento, login y modos Backoffice/POS/Hybrid.
3. `decision-producto-minimo-y-capacidades.md`: núcleo mínimo de Producto y datos
   separados por capacidad.
4. `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md`: política
   de negativos en Bodega y ausencia de saldo local.
5. `diseno-ux-facturacion-pos-web.md`: composición, teclado, lector, pago,
   borradores, pedidos, offline y pruebas de la pantalla.
6. `decision-nomenclatura-canonica-auraly-commerce.md`: nombres nuevos.
7. `decision-despachos-verificacion-entradas-salidas-inventario.md`: Entradas,
   Movimientos, Despachos y Verificación.
8. Los demás ADR especializados, cuando no contradigan estas decisiones.

## Expresiones históricas no vigentes

No implementar:

```text
CashRegisterSettings.AllowNegativeStockSales
la política de negativos pertenece a la caja
el servidor reemplaza el SalesInvoiceId creado localmente
el número de factura se calcula con MAX + 1
PWA en navegador como única superficie de caja
Products contiene precio, costo y saldo
ProductId conserva el entero 10000 de Xion
Aduana como nombre funcional
EnSa como nombre funcional
SalidaDeMercancia para representar la factura
```

Se interpretan así:

```text
negativos            Warehouse.AllowNegativeStockSales
ID técnico           UUIDv7
10000                ProductCode o LegacyId
número de factura    serie/bloque exclusivo consumido por caja
caja instalada       Auraly Desktop
producto             núcleo mínimo + capacidades relacionadas
Aduana                Verificación de despacho
EnSa                  Movimientos de inventario
SalidaDeMercancia     SalesInvoice / Factura de venta
```

## Condición para empezar implementación

Los prompts y tareas de implementación deben enlazar este documento y los ADR que
prevalecen. Ningún desarrollador o agente debe tomar un documento histórico
aislado como especificación completa.
