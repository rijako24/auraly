# Evidencia: devoluciones de compra

Fecha de ejecución: 2026-08-02.
Rama: `feature/auraly-commerce-purchase-returns`.
Base: `03156ba`.

## Resultado funcional

- Entrada procesada consultable por API paginada y por detalle.
- Devolución parcial con número `DCP00-00000001`.
- Cantidades monetarias prorrateadas y remanente final exacto.
- Salida de inventario al costo inmutable de la entrada.
- Reducción de cuenta por pagar mediante transacción `Credit`.
- Saldo a favor de proveedor cuando la CxP no tiene saldo suficiente.
- Trabajo contable conectado y asiento DCP balanceado.
- Outbox servidor e idempotencia sin efectos duplicados.
- Exceso de cantidad rechazado antes de crear documento o job.
- Permisos y Business validados en backend.
- Vista web paginada y conectada en `/dashboard/purchasing/purchase-returns`.

## Evidencia ejecutada

| Comprobación | Resultado |
|---|---:|
| `dotnet build Auraly.Commerce.sln --configuration Release` | Correcto, 0 errores, 0 advertencias |
| Fundación | 149/149 |
| Integración SQL Server real + DACPAC | 90/90 |
| POS Edge Host | 15/15 |
| POS web | 25/25 |
| `npx tsc --noEmit` | Correcto |
| `npm run build` | Correcto, 51 páginas; ruta de devoluciones generada |
| DACPAC | Compilado y desplegado por la suite SQL |

La prueba contable comprueba un DCP con débito a proveedores y créditos a inventario, IVA descontable y compra no inventariable, con débitos y créditos iguales. Las pruebas de devolución usan productos exclusivos para no depender del saldo mutable de otros escenarios. Esto permitió además comprobar que un fallo documental realmente bloquea los documentos posteriores del mismo Business, como exige el diseño del motor; la causa de aislamiento fue corregida y la suite completa terminó limpia.

## Escenarios nuevos

- asignación parcial y final sin residuos de redondeo;
- devolución procesada exactamente una vez;
- replay con el mismo `ReturnId` e idempotency key;
- una sola salida de inventario, transacción de CxP y outbox;
- devolución de una entrada ya pagada crea `SupplierCredits`;
- devolución superior al remanente responde conflicto y no encola;
- permisos insuficientes;
- Business diferente;
- consulta paginada y detalle con cantidad disponible;
- contabilización real y balanceada.

## Pendiente explícito

- consumo o reembolso de saldos a favor con proveedores;
- documento compensatorio para revertir una devolución procesada;
- operación offline de devoluciones;
- artefactos fiscales recibidos del proveedor.