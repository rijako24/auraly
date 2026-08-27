# Decisión: entrada única Web/POS Edge e instalador genérico

**Estado:** decisión vigente del Commerce MVP
**Plataforma local inicial:** Windows
**Principio:** una sola experiencia funcional, dos capacidades de ejecución.

## 1. Decisión

La ruta `/pos` es la entrada única. El usuario no elige entre dos productos ni recibe un instalador preparado para una empresa.

- En navegador, Auraly trabaja online contra la API y el motor documental central.
- En una instalación Auraly POS, la misma interfaz usa POS Edge, SQLite, periféricos y sincronización durable para operar sin conexión.
- Ambos modos comparten contratos, permisos, reglas fiscales, numeración y motor contable; no existen dos implementaciones del negocio.

No es técnicamente seguro ni posible que una página web instale silenciosamente ejecutables, servicios, controladores o una base local. Por eso Auraly detecta capacidades: deja vender online y ofrece instalar el modo resistente cuando el usuario autorizado configura su puesto.

## 2. Flujo canónico

### Acceso genérico

1. El usuario abre Auraly o `/pos`.
2. Si no existe tenant en la URL, el login solicita empresa, usuario y contraseña. El ejemplo de empresa es `@auraly`.
3. Si el enlace contiene el tenant key, por ejemplo `?tenant=@auraly`, el campo empresa aparece resuelto y no se puede editar.
4. Un login correcto recuerda el tenant key en ese dispositivo. Un intento fallido nunca reemplaza el valor recordado.
5. El tenant key es inmutable después de crear la empresa. La aplicación puede copiar un enlace empresarial, pero no modificar la clave.

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

- si está disponible, continúa con la inicialización y sincronización local;
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
  ├─ navegador: API online
  └─ Auraly Desktop: API local de POS Edge
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

- el catálogo, identidad autorizada, consecutivos y documentos necesarios se guardan localmente;
- el primer usuario con permiso `sales.create` que elige trabajar sin conexión entra a ventas automáticamente cuando termina la descarga inicial, sin un segundo login;
- la identidad local incluye a todos los usuarios activos con permiso `sales.create` para ese negocio, de modo que cualquiera de ellos puede iniciar una sesión local posteriormente;
- el modo puede volver a online de forma explícita; esa transición cierra la sesión local y vuelve a la autenticación autoritativa del servidor;
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

1. Login genérico y enlaces empresariales llegan a la misma sesión.
2. El tenant key no se puede editar después de crearlo.
3. El dispositivo recuerda la última empresa solo tras autenticación correcta.
4. El instalador descargado no contiene información del tenant.
5. Navegador y escritorio muestran las mismas opciones autorizadas.
6. Comprobante entra sin configuración fiscal; factura no entra sin fiscalidad válida.
7. POS instalado reinicia y conserva SQLite, enrolamiento y outbox.
8. Impresoras y cajón usan configuración local por dispositivo.
9. Entrada/salida de efectivo afecta el arqueo y su contabilización exactamente una vez.
10. La venta sincronizada produce los mismos efectos operativos, fiscales y contables que la venta online.

## 10. Conclusión

La unificación correcta es una sola entrada, una sola interfaz y un solo motor, no pretender que todos los navegadores sean una instalación local. El navegador entrega acceso inmediato online; el instalador genérico agrega continuidad offline y periféricos después de que el login determine el tenant. Así se evita preconfigurar empresas, duplicar endpoints y mantener dos reglas de negocio.
