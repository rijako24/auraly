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
13. La preparación completa, sin depender de un cliente seleccionado, descarga
    por páginas todos los productos y todos los clientes del negocio, además de
    promociones, canales, parámetros y catálogos operativos. El snapshot del
    canje ya aporta todos los usuarios POS autorizados.
14. Cada familia conserva su propio cursor durable: catálogo, clientes,
    configuración comercial y seguridad. Después del bootstrap, cada cambio
    actualiza únicamente la entidad afectada; nunca vuelve a descargar una
    colección completa por haber cambiado un producto, cliente o usuario.
15. La venta permanece inhabilitada hasta que la proyección local completa queda
    `Ready`.

Si una etapa de la preparación falla por DNS, timeout o una respuesta temporal
del servidor, Edge conserva el checkpoint y agenda hasta tres reintentos con
espera creciente (5, 10 y 20 segundos). Mientras espera, la interfaz informa el
número de intento sin bloquear el cierre de la aplicación. Si los tres fallan,
la preparación queda pausada y ofrece **Reintentar preparación**, **Repetir
enrolamiento** y **Salir de Auraly**. Un reintento manual inicia una serie nueva
de hasta tres intentos automáticos. Los fallos permanentes de identidad,
compatibilidad o validación no se disfrazan como problemas transitorios.

Los detalles técnicos completos permanecen en los logs locales. La salud y el
historial consumidos por la interfaz nunca exponen `ServerUrl`, hostnames,
puertos ni el texto crudo de excepciones de transporte.

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

La pantalla de fallo puede reiniciar el enrolamiento mediante `POST
/edge/v1/enrollment/restart`. El endpoint exige loopback y el token opaco del
lanzador, elimina únicamente el paquete protegido y reinicia el host en
`EnrollmentRequired`; conserva SQLite, outbox, comprobantes, `DeviceId`, series
y cursores locales. La interfaz confirma esta acción antes de ejecutarla.

El enrolamiento no descarga inventario. La preparación local mantiene todos los
productos, códigos, precios de venta, impuestos, promociones, canales, clientes,
usuarios y parámetros operativos del negocio ya definidos por la rebanada de
sincronización. Esa preparación es una proyección del negocio y no se construye
para un cliente concreto.

## En línea y offline

La experiencia visual es la misma:

- **En línea:** búsquedas, borradores y confirmación usan el servidor.
- **Edge conectado:** la caja usa sus capacidades locales y sincroniza con el
  servidor.
- **Edge sin red:** usa catálogo, series, factura, impresión y outbox locales.

Los modales personalizados del POS comparten una pila de foco: el control
inicial gana el foco al abrir, `Escape` cierra solo la ventana superior y el
foco vuelve a la anterior. En **Finalizar venta**, `↑` y `↓` recorren los campos
**Valor recibido**, `F6` abre el selector de documento, `F1` marca factura y
`F2` marca comprobante; `Enter` confirma y devuelve el foco al primer valor.

La pantalla inicial obtiene del bootstrap de ventas únicamente los indicadores
de preparación fiscal y cuota que necesita para cada sede. Un cajero con acceso
al POS no consulta el endpoint administrativo `fiscal.configuration.read`, no
recibe errores técnicos de permisos al elegir comprobante y tampoco recibe en
esta proyección números de resolución, certificados ni secretos fiscales.

Al arrancar, Edge ejecuta una puesta al día en segundo plano sobre el cursor
durable. Si una conexión transitoria falla, conserva el cursor y ejecuta la
política acotada de tres reintentos; un fallo permanente o el agotamiento de esa
política espera intervención manual. Después de completar la puesta al día deja
de consultar el catálogo. El disparo push para cambios ocurridos durante una
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
