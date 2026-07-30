# Configuración única de facturación y enrolamiento POS Edge

**Fecha:** 30 de julio de 2026  
**Estado:** primera rebanada de enrolamiento implementada y probada.

## Decisión

Auraly mantiene una sola aplicación, un solo login y una sola experiencia de
facturación. POS Edge no es otra caja: es la capacidad local de un equipo
enrolado.

- Un equipo sin host local factura en línea contra Auraly Server.
- Un host local nuevo arranca en estado `EnrollmentRequired`.
- Un usuario con `pos.devices.enroll` elige negocio, sede y caja y puede
  preparar ese equipo para trabajar sin conexión.
- La misma caja puede seguir usándose en línea desde uno o varios equipos.
- La asociación exclusiva aplica al dispositivo Edge que posee series y
  credenciales offline; nunca se reemplaza silenciosamente.

## Flujo implementado

1. La aplicación detecta el host local mediante una sesión de loopback.
2. Si el host no existe, muestra la configuración de caja en línea.
3. Si existe pero no está enrolado, carga desde el servidor las cajas permitidas.
4. La opción **Preparar modo offline** solo se muestra con
   `pos.devices.enroll`.
5. El servidor valida tenant, negocio, sede, caja y enrolamiento existente.
6. El servidor crea una autorización aleatoria de un solo uso, válida por diez
   minutos; en SQL solo conserva su hash.
7. El navegador entrega la autorización al host local, nunca el paquete fiscal.
8. El host canjea directamente la autorización contra Auraly Server.
9. El servidor crea la identidad del dispositivo, devuelve las series
   operativa y fiscal exclusivas, configuración derivada y credencial.
10. POS Edge protege el paquete completo mediante el almacén de protección de
    datos del sistema y lo escribe de forma atómica.
11. Al reiniciar el servicio, SQLite se crea o actualiza automáticamente y el
    sincronizador real inicia el bootstrap del catálogo.
12. La venta permanece inhabilitada hasta que el catálogo local queda `Ready`.

La URL predeterminada del host es `http://127.0.0.1:47831`. El host exige el
token de sesión generado por el lanzador, valida el origen y solo permite HTTP
para un servidor Auraly de loopback; un servidor remoto debe usar HTTPS.

## Datos y seguridad

El paquete local contiene únicamente lo necesario para operar la caja:

- dispositivo y secreto;
- usuario que autorizó el enrolamiento;
- negocio, sede, bodega y caja;
- política de negativos derivada de la bodega;
- serie operativa offline;
- serie fiscal offline, resolución y clave técnica;
- permisos técnicos del dispositivo.

El secreto del dispositivo y la clave técnica no se almacenan en texto plano.
La clave privada del certificado DIAN nunca llega al navegador ni al POS.

El enrolamiento no descarga inventario. El catálogo local mantiene productos,
códigos, precios de venta, impuestos y datos mínimos ya definidos por la
rebanada de sincronización.

## En línea y offline

La experiencia visual es la misma:

- **En línea:** búsquedas, borradores y confirmación usan el servidor.
- **Edge conectado:** la caja usa sus capacidades locales y sincroniza con el
  servidor.
- **Edge sin red:** usa catálogo, series, factura, impresión y outbox locales.

La puesta al día posterior se ejecuta en segundo plano sobre los cursores
durables. Las notificaciones push siguen siendo la señal para adelantar el
delta; el cursor es la fuente de verdad y evita depender de polling continuo.

## Límites que siguen pendientes

Esta rebanada no declara terminado:

- login offline de todos los usuarios;
- snapshot durable de usuarios, credenciales locales y permisos;
- menú general offline;
- revocación y reasignación administrativa explícita de un Edge;
- selección administrativa de impresora y balanza durante el enrolamiento;
- instalador Windows y validación del reinicio automático como servicio.

Actualmente el paquete identifica al usuario que autorizó el equipo, pero no
sincroniza todavía a todos los cajeros. La impresora queda con el proveedor de
vista previa y tirilla de 80 mm ya existente hasta que se implemente su maestro.

## Siguiente rebanada recomendada

La siguiente rebanada debe ser **Identidad y sesión local de caja**:

1. sincronización inicial e incremental de usuarios autorizados;
2. hash local seguro de credenciales o PIN/código de supervisor;
3. login offline y vigencia;
4. permisos por acción y autorización de supervisor;
5. sesión de cajero, entrega y arqueo;
6. cierre de sesión sin cerrar caja;
7. revocación de usuario y puesta al día al recuperar conexión.

Debe reutilizar el enrolamiento implementado; no crear otra aplicación ni otro
protocolo de configuración.
