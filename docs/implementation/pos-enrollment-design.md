# Configuración única de facturación y enrolamiento POS Edge

**Fecha:** 30 de julio de 2026  
**Estado:** primera rebanada de enrolamiento implementada y probada.

## Decisión

Auraly mantiene una sola aplicación, un solo login y una sola experiencia de
facturación. POS Edge no es otra caja: es la capacidad local de un equipo
enrolado.

- Un equipo sin host local factura en línea contra Auraly Server.
- Un host local nuevo arranca en estado `EnrollmentRequired`.
- En ese estado ya permite configurar impresora, cajón y balanza; el
  enrolamiento se exige únicamente para el respaldo de venta sin conexión.
- Un usuario con `pos.devices.enroll` elige sede y caja y puede
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
5. El servidor valida tenant, sede, bodega, caja y enrolamiento existente.
6. El servidor crea una autorización aleatoria de un solo uso, válida por diez
   minutos; en SQL solo conserva su hash.
7. El navegador entrega la autorización al host local, nunca el paquete fiscal.
8. El host canjea directamente la autorización contra Auraly Server.
9. El servidor crea la identidad del dispositivo, devuelve las series
   operativa y fiscal exclusivas, configuración derivada y credencial.
10. POS Edge protege el paquete completo mediante el almacén de protección de
    datos del sistema y lo escribe de forma atómica.
11. La misma respuesta de canje incluye el snapshot inicial completo de
    usuarios POS autorizados, sus verificadores locales y permisos; no existe
    una segunda descarga obligatoria de usuarios para terminar el enrolamiento.
12. Al reiniciar el servicio, SQLite se crea o actualiza automáticamente. Edge
    instala atómicamente ese snapshot y consume una sola vez el acceso inicial
    protegido para abrir la identidad local.
13. El sincronizador de identidades queda como propietario de las puestas al
    día posteriores al enrolamiento, mientras el bootstrap inicial del catálogo
    continúa por su cursor durable.
14. La venta permanece inhabilitada hasta que el catálogo local queda `Ready`.

Si una etapa de la preparación falla, Edge conserva el checkpoint y detiene esa
ejecución. La interfaz muestra una causa segura y accionable; solo el usuario
dispara **Reintentar preparación**. No existe un temporizador de reintento de la
preparación inicial.

La URL predeterminada del host es `http://127.0.0.1:47831`. El host exige el
token de sesión generado por el lanzador, valida el origen y solo permite HTTP
para un servidor Auraly de loopback; un servidor remoto debe usar HTTPS.

## Datos y seguridad

El paquete local contiene únicamente lo necesario para operar la caja:

- dispositivo y secreto;
- snapshot inicial de todos los usuarios POS autorizados y usuario que autorizó
  el enrolamiento;
- tenant, sede, bodega y caja;
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

Al arrancar, Edge ejecuta una puesta al día en segundo plano sobre el cursor
durable. Si falla, conserva el cursor y espera un reintento manual. Después de
completarla deja de consultar el catálogo. El disparo push para cambios ocurridos durante una
sesión abierta sigue pendiente de conectar al transporte real; no se simula
mediante polling continuo.

## Límites que siguen pendientes

Esta rebanada no declara terminado:

- menú general offline;
- revocación y reasignación administrativa explícita de un Edge;
- selección administrativa de impresora y balanza desde `Periféricos`, con o
  sin enrolamiento;
- instalador Windows y validación del reinicio automático como servicio.

La impresora queda con el proveedor de vista previa y tirilla de 80 mm ya
existente hasta que se implemente su maestro.
