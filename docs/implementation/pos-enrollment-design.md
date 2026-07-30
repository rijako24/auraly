# Configuración única de Facturación y enrolamiento POS Edge

**Fecha:** 30 de julio de 2026  
**Estado:** decisión cerrada; implementación del asistente Edge pendiente.

## Decisión

Auraly tiene una sola aplicación, un solo login, un solo menú y una sola
experiencia de facturación. “POS Edge” no es otra caja ni otro diseño: es una
capacidad enrolada en un equipo concreto.

- Un equipo no enrolado factura en línea.
- Un equipo enrolado usa la misma pantalla y, además, dispone de SQLite,
  periféricos, numeración provisionada, catálogo local y continuidad sin red.
- Estar enrolado no obliga a trabajar desconectado. Mientras exista conexión,
  Edge sincroniza y usa el servidor normalmente.

## Configuración inicial

La primera configuración siempre requiere servidor.

1. El usuario inicia sesión con Auraly.
2. El cliente solicita un bootstrap autenticado al servidor.
3. El servidor devuelve los negocios, sedes y cajas que el usuario puede usar.
4. El usuario elige negocio, sede y caja.
5. La bodega, política de negativos, series y resolución se derivan de la caja;
   no se capturan manualmente ni se aceptan sin validación.
6. El usuario elige una capacidad:
   - **Usar en línea:** guarda solo el contexto recordado del navegador.
   - **Disponible sin conexión en este equipo:** inicia el enrolamiento Edge.

La opción Edge solamente aparece con permiso `pos.devices.enroll`. Si la caja ya
tiene otro equipo enrolado, se exige una reasignación explícita que revoque el
anterior; nunca se reemplaza silenciosamente.

## Enrolamiento Edge

El servidor:

- registra la identidad del dispositivo;
- emite una credencial de dispositivo de una sola visualización;
- entrega negocio, sede, bodega y caja validados;
- provisiona series operativas y fiscales exclusivas para operación offline;
- entrega configuración de impresora/balanza y políticas aplicables;
- emite un snapshot firmado y con caducidad de usuarios y permisos offline;
- inicia la sincronización del catálogo mínimo requerido para facturación.

POS Edge:

- protege la credencial y la clave técnica con DPAPI/almacén seguro;
- persiste configuración, usuarios/permisos, catálogo, cursores y progreso;
- muestra progreso por etapas;
- no habilita venta offline hasta completar e integrar el bootstrap;
- conserva facturas, outbox y series ante reinicios;
- reanuda una descarga interrumpida.

La clave privada del certificado DIAN nunca se entrega al equipo.

## Aperturas posteriores

Un Edge ya enrolado:

1. abre la misma aplicación;
2. permite login local con el snapshot vigente;
3. habilita facturación inmediatamente si tiene un catálogo válido;
4. comprueba servidor y cambios en segundo plano;
5. aplica deltas sin bloquear la venta;
6. muestra únicamente el estado “Conectado con Auraly” o “Sin conexión”.

El usuario final no necesita conocer procesos, puertos, SQLite ni el nombre
“POS Edge”.

Un equipo online recordará su última caja, pero permite cambiarla. Cambiar un
Edge de caja, sede o negocio requiere conexión, autorización y un nuevo
bootstrap porque cambian catálogo, precios, políticas y numeración.

## Navegación

Facturación incluye una acción **Menú** para volver a la aplicación general en
ambos casos. El borrador activo no se pierde:

- online permanece en SQL Server;
- Edge permanece en SQLite.

Con conexión, todos los módulos autorizados funcionan normalmente. Sin conexión,
el menú puede mostrarse, pero debe marcar como no disponibles los módulos que
dependen del servidor; solo se habilitan capacidades locales expresamente
implementadas y probadas.

## Diferencia con la implementación actual

Ya existe:

- selección de negocio/sede/caja para facturación online;
- bootstrap online de identidad y cajas en una llamada a Auraly Commerce;
- contexto Edge configurable;
- SQLite, catálogo durable, outbox, series e impresión Edge;
- acción para volver al menú.

Falta conectar:

- asistente visual para elegir “Disponible sin conexión”;
- endpoint seguro de enrolamiento/reasignación;
- entrega y protección automática de credenciales;
- sincronización inicial de usuarios y permisos offline;
- provisionamiento automático de configuración y series desde el servidor;
- menú offline local con módulos no disponibles claramente marcados.

Ninguno de esos puntos se considera terminado por existir como configuración de
arranque.

## Pruebas obligatorias

- usuario sin permiso no puede enrolar;
- negocio/sede/caja se derivan del tenant y permisos autenticados;
- una caja no se enrola silenciosamente en dos equipos;
- reasignar revoca la credencial anterior;
- secretos no aparecen en logs ni respuestas posteriores;
- reinicio conserva el enrolamiento;
- interrupción del bootstrap reanuda sin perder ventas/outbox;
- online no descarga catálogo operativo;
- Edge sí descarga únicamente datos necesarios;
- login offline respeta vigencia y permisos;
- cambio de caja Edge exige conexión y nuevo bootstrap;
- salir de facturación conserva el borrador online y local;
- la misma UI POS funciona online y Edge.
