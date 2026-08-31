# Decisión: entrada única Web/POS Edge e instalador genérico

**Estado:** decisión vigente del Commerce MVP
**Plataforma local inicial:** Windows
**Principio:** una sola experiencia funcional, dos capacidades de ejecución.

## 1. Decisión

La ruta `/login` es la entrada única tanto para web como para el ejecutable. El usuario no elige entre dos productos ni recibe un instalador preparado para una empresa. `/pos` es el módulo de facturación, no un login alternativo.

- En navegador, Auraly trabaja online contra la API y el motor documental central.
- En una instalación Auraly POS, la misma interfaz usa POS Edge, SQLite, periféricos y sincronización durable para operar sin conexión.
- Ambos modos comparten contratos, permisos, reglas fiscales, numeración y motor contable; no existen dos implementaciones del negocio.

No es técnicamente seguro ni posible que una página web instale silenciosamente ejecutables, servicios, controladores o una base local. Por eso Auraly detecta capacidades: deja vender online y ofrece instalar el modo resistente cuando el usuario autorizado configura su puesto.

## 2. Flujo canónico

### Acceso genérico

1. El usuario abre Auraly y siempre ve el login general. El ejecutable no presupone que el usuario sea cajero ni lo redirige a Facturación por el solo hecho de estar instalado.
2. Si no existe tenant en la URL, el login solicita empresa, usuario y contraseña. El ejemplo de empresa es `@auraly`.
3. Si el enlace contiene el tenant key, por ejemplo `?tenant=@auraly`, el campo empresa aparece resuelto y no se puede editar.
4. Sin enrolamiento, el login siempre valida contra Auraly Server y un fallo de red no se sustituye por una credencial almacenada en el navegador.
5. Con enrolamiento, la misma pantalla visual delega la autenticación al runtime local protegido y entra a Facturación; no existe una segunda pantalla de “cajero”.
6. Un login online correcto recuerda el tenant key en ese dispositivo. Un intento fallido nunca reemplaza el valor recordado.
7. El tenant key es inmutable después de crear la empresa. La aplicación puede copiar un enlace empresarial, pero no modificar la clave.

La identidad sigue siendo única dentro de un tenant, no globalmente. Correo, nombre de usuario e identificación pueden repetirse en empresas diferentes.

### Preparación del punto de venta

1. La API resuelve las sedes, bodegas y cajas permitidas para el usuario autenticado.
2. El usuario selecciona el contexto operativo.
3. Se elige solamente `Factura electrónica` o `Comprobante de venta`.
4. `Comprobante de venta` entra directamente a vender.
5. `Factura electrónica` abre la configuración fiscal mínima requerida y no habilita la emisión hasta validarla.
6. Dentro de la caja, el selector de documento existente permite cambiar. Si se cambia de comprobante a factura, el mismo diálogo solicita la configuración fiscal faltante.

No se agrega un asistente fiscal paralelo ni estados duplicados.

### Descarga e instalación

Después del login y de conocer el contexto operativo, Auraly comprueba si POS Edge está disponible:

- si está disponible pero no está enrolado, ofrece preparar el equipo mediante una opción explícita y un único botón para continuar;
- si el usuario continúa sin preparar, el escritorio usa el mismo POS online del navegador y POS Edge se limita a periféricos y enrolamiento: no abre Web PubSub, no descarga proyecciones, no inicia sincronizadores y no mantiene outbox operativo;
- si el usuario prepara el equipo, el enrolamiento se convierte en la única fuente de verdad para activar la operación local sincronizada;
- si no está disponible, muestra una tarjeta de instalación y permite seguir vendiendo online;
- la descarga obtiene de una única API autenticada la versión, URL HTTPS y SHA-256 del instalador publicado;
- el instalador es genérico: no contiene tenant, usuario, contraseña, token, sede ni caja;
- al abrir Auraly, el login determina el tenant y el enrolamiento autorizado asocia el dispositivo con sede, bodega y caja;
- una actualización preserva la base SQLite y la identidad protegida del dispositivo.

El instalador inicial es un bundle WiX convencional, muestra una interfaz gráfica
de progreso y no inicia PowerShell oculto. El bundle, el MSI y los ejecutables se
firman con el certificado de publicación; el launcher vuelve a validar el SHA-256
y, cuando hay una huella configurada, la firma Authenticode antes de aceptar una
actualización.

La aplicación instalada consulta la misma metadata autenticada al iniciar y, si
existe una versión superior, presenta un aviso compacto sin descargarla. El usuario
inicia la descarga de forma explícita y ve su progreso. Al terminar puede reiniciar
en ese momento o continuar trabajando; si continúa, la actualización se aplica al
abrir Auraly la próxima vez. La comprobación nunca interrumpe una venta ni acepta
una URL de descarga distinta al endpoint canónico.

El resumen de aprovisionamiento de una empresa usa exactamente la misma fuente del instalador. No existe un segundo endpoint ni una descarga binaria a través del proxy JSON del frontend.

## 3. Responsabilidades

```text
Interfaz React compartida
  ├─ navegador o escritorio no enrolado: API online
  └─ Auraly Desktop enrolado: API local de POS Edge
                         ├─ SQLite y outbox durable
                         ├─ impresión y cajón
                         ├─ identidad offline protegida
                         └─ sincronización push con el servidor

API Auraly
  └─ motor documental
       ├─ operación e inventario
       ├─ fiscal
       └─ contabilidad
```

El navegador no intenta emular capacidades locales. POS Edge no implementa otro motor contable ni accede directamente a SQL Server central.

## 4. Operación online y offline

### Navegador online

- el servidor es autoritativo;
- los borradores y documentos se procesan por la API;
- si se pierde conexión antes de confirmar, la interfaz conserva únicamente lo que el flujo online soporte de manera explícita;
- no se promete continuidad offline sin POS Edge.

### POS instalado

- sin enrolamiento conserva el flujo online: borrador y venta permanecen en la API, no se descargan datos operativos y no existe una conexión push local;
- el usuario puede marcar `Preparar este equipo para trabajar sin conexión` antes de continuar; sin marcarla no se crea enrolamiento ni estado sincronizado;
- el bootstrap consulta el cupo autoritativo y muestra uso/límite; sin permiso o sin cupo la opción queda deshabilitada y el canje vuelve a comprobar la capacidad bajo bloqueo transaccional para cubrir carreras entre cajas;
- el catálogo, identidad autorizada, consecutivos y documentos necesarios se guardan localmente;
- una vez completada la preparación, el paso del tiempo no invalida la identidad local ni obliga a contactar al servidor para iniciar sesión; el usuario puede entrar cuantas veces necesite con la credencial protegida descargada;
- una conexión disponible actualiza usuarios, permisos, bloqueos y revocaciones mediante la sincronización de seguridad existente, pero una falla de red no convierte en inválida una preparación durable ya completada;
- al abrir, POS Edge inicia la actualización de identidades en segundo plano sin bloquear a usuarios ya descargados; si el usuario escrito no existe localmente, el submit visible conserva su estado de carga, espera como máximo una única actualización serializada y reintenta localmente, cubriendo la carrera con usuarios creados mientras la aplicación estuvo cerrada;
- una contraseña incorrecta de un usuario ya presente nunca dispara sincronización ni consulta de autenticación al servidor;
- el primer usuario con permiso `sales.create` que elige trabajar sin conexión entra a ventas automáticamente cuando termina la descarga inicial, sin un segundo login;
- la identidad local incluye a todos los usuarios activos con permiso `sales.create` para ese negocio, de modo que cualquiera de ellos puede iniciar una sesión local posteriormente;
- una sesión local vigente se recupera al reiniciar la aplicación; el lanzador no la revoca ni obliga a adquirir otra concesión por abrir de nuevo;
- una vez enrolado, el equipo usa siempre el runtime local-first; no existe un archivo ni selector de modo que pueda contradecir el enrolamiento;
- confirmar una venta escribe documento, numeración y outbox en una sola transacción SQLite;
- la sincronización es idempotente y llega al mismo motor del servidor;
- Web PubSub notifica cambios; no reemplaza la outbox ni la reconciliación por cursor;
- reiniciar la aplicación no pierde ventas confirmadas ni trabajo durable.

“Siempre local” aplica solamente al puesto instalado. Forzarlo en cualquier navegador crearía una promesa imposible de cumplir y una superficie de seguridad mayor.

## 5. Periféricos

POS Edge descubre y persiste configuración por dispositivo:

- impresora de tirilla, con ancho de 58 u 80 mm;
- impresora de carta;
- apertura de cajón cuando el hardware lo permita;
- futuras balanza y periféricos mediante adaptadores explícitos.

El navegador puede previsualizar o usar el diálogo normal del sistema, pero no imprime silenciosamente. La impresión automática y la apertura de cajón requieren POS Edge.

## 6. Caja y arqueo

Las entradas y salidas de efectivo son movimientos explícitos del turno, con motivo, importe, usuario y hora. Se reflejan en el arqueo y llegan al motor como eventos operativos auditables. La pantalla de caja ofrece botones visibles y atajos `Ctrl+F8` para entrada y `Ctrl+F9` para salida; no se reservan teclas de función solas que puedan interferir con el navegador o el sistema.

## 7. Seguridad

- secretos locales protegidos con mecanismos del sistema operativo;
- POS Edge escucha únicamente en loopback y valida origen/token de sesión local;
- enrolamiento revocable y sujeto al cupo de dispositivos del tenant;
- el tenant y el dispositivo se validan en servidor en cada operación sensible;
- el acceso local depende del enrolamiento durable y de la proyección mínima de identidades protegida por el sistema operativo, no de un lease temporal por login;
- el instalador no confiere permisos ni autoridad;
- el SHA-256 publicado permite comprobar integridad y el artefacto debe firmarse antes de producción.

## 8. Fallos esperados

| Falla | Comportamiento |
|---|---|
| API no disponible en navegador | informa desconexión y no simula una confirmación |
| API no disponible con POS Edge | continúa dentro de la capacidad offline provisionada y deja outbox pendiente |
| POS Edge no está instalado | ofrece descarga y mantiene modo online |
| instalador no publicado | muestra indisponibilidad, sin enlace roto |
| impresora no disponible | conserva documento y permite reintentar/reconfigurar |
| conflicto de enrolamiento o cupo | bloquea el enrolamiento, no la identidad del usuario |
| versión incompatible | exige actualización antes de usar capacidades locales incompatibles |

## 9. Criterios de aceptación

1. Web y ejecutable presentan exactamente el mismo login general; no existe un login separado de cajero.
2. Una instalación no enrolada solo autentica contra el servidor; una instalación enrolada usa el runtime local para Facturación.
3. El tenant key no se puede editar después de crearlo.
4. El dispositivo recuerda la última empresa solo tras autenticación correcta.
5. El instalador descargado no contiene información del tenant.
6. Navegador y escritorio muestran las mismas opciones autorizadas.
7. Comprobante entra sin configuración fiscal; factura no entra sin fiscalidad válida.
8. POS instalado reinicia y conserva SQLite, enrolamiento y outbox.
9. Impresoras y cajón usan configuración local por dispositivo.
10. Entrada/salida de efectivo afecta el arqueo y su contabilización exactamente una vez.
11. La venta sincronizada produce los mismos efectos operativos, fiscales y contables que la venta online.

## 10. Conclusión

La unificación correcta es una sola entrada, una sola interfaz y un solo motor, no pretender que todos los navegadores sean una instalación local. El navegador entrega acceso inmediato online; el instalador genérico agrega continuidad offline y periféricos después de que el login determine el tenant. Así se evita preconfigurar empresas, duplicar endpoints y mantener dos reglas de negocio.
