# Decisión: autenticación exclusiva y sesión de trabajo independiente

Fecha: 2026-07-31  
Estado: obligatoria; actualizada el 2026-08-31
Prevalencia: complementa `decision-sesiones-operativas-cierre-usuario.md`.

## Regla

Solo puede existir una `AuthenticationSession` activa por `TenantId + UserId`.
Varias pestañas del mismo navegador reutilizan el `ClientId` durable y la misma
sesión. Iniciar sesión en otro navegador o computador marca inmediatamente como
inactiva la autenticación anterior, sin desenrolar ni cerrar el dispositivo. El
cliente anterior lo detecta en su siguiente acción conectada, informa al usuario y
regresa al login. No se usa una orden push para cerrar la aplicación anterior.
Volver al primer cliente invalida a su vez el login del segundo.

La sesión de autenticación y la `WorkSession` son conceptos separados:

- `AuthenticationSession` controla identidad, acceso único, renovación y revocación.
- `WorkSession` registra la operación de facturación, sede, canal/dispositivo y cierre
  financiero; la bodega pertenece a cada documento.

Login y logout no cierran una `WorkSession`: autenticarse y abrir/cerrar operación
son responsabilidades distintas. La sesión web se recupera desde cualquier navegador
autorizado. Cada Edge recupera su propia sesión local por usuario/dispositivo. Todas
terminan únicamente mediante el cierre operativo explícito.

## Online

El servidor adquiere atómicamente una concesión exclusiva por usuario. La sesión usa un
identificador, hash del token de renovación, cliente durable, fechas de emisión,
expiración, último contacto, revocación y versión de concurrencia.

Una sesión abandonada expira después de 24 horas continuas sin renovación. El
token de acceso dura 15 minutos y la primera petición posterior lo renueva; cada
renovación desplaza atómicamente otras 24 horas la ventana de inactividad. La
validación del middleware no renueva, revoca ni cambia la sesión. Un supervisor autorizado puede revocarla. La
revocación queda auditada y los tokens dejan de renovarse.
La rotación del token de renovación es atómica. Una solicitud paralela que llegue
con el secreto inmediatamente anterior recibe conflicto y no revoca la sesión que
otra solicitud acaba de renovar; las pestañas del cliente ganador siguen operando.

El login elimina primero del navegador la identidad, tenant y negocio persistidos
por el usuario anterior. La respuesta exitosa reemplaza en una sola respuesta las
cookies `HttpOnly` de acceso y renovación conservando el `ClientId` durable del
navegador; no existe un intervalo intermedio con cookies parcialmente instaladas.

Los errores conservan semántica estricta: un permiso insuficiente es `403`, una
autenticación ausente o expirada es `401`, y solo una renovación cuya sesión fue
revocada con `ReplacedByNewLogin` devuelve `409 AuthenticationSessionReplaced`.
El cliente muestra el aviso «sesión iniciada en otro lugar» exclusivamente para
ese último código. Un `401` de un recurso, incluso después de una renovación
exitosa, nunca demuestra por sí solo que ocurrió otro login y no puede invalidar
la sesión vigente. Una expiración real usa el título neutral «Sesión finalizada»;
el encabezado del cuadro tampoco puede atribuirla a otro login.

## Edge offline

La exclusividad global no puede comprobarse entre dos computadores totalmente
desconectados. Edge conserva la autorización firmada que obtuvo al enrolarse o
sincronizar identidades, asociada a `UserId + DeviceId`, para validar que esas
credenciales sí fueron provisionadas por el servidor.

Edge valida firma, dispositivo y continuidad temporal. La fecha histórica del
paquete firmado no bloquea el acceso de un equipo ya enrolado: la autorización
offline no vence por tiempo ni obliga a reconectar periódicamente. Edge no crea
autorizaciones por sí mismo.

Un logout offline termina la autenticación local, pero no cierra la `WorkSession`.
El cierre operativo explícito conserva su flujo durable y sincronizable. Un
supervisor puede revocar el acceso, pero un Edge desconectado solo conoce esa
revocación al reconectar. Esa limitación física se acepta expresamente para
garantizar operación offline indefinida después del enrolamiento.

## Numeración

La sesión única no protege consecutivos. La numeración continúa perteneciendo a
la serie central del servidor o a la serie técnica del dispositivo Edge.

## Pruebas obligatorias

1. Un login en otro cliente revoca inmediatamente la autenticación anterior.
2. Dos pestañas del mismo navegador reutilizan el mismo `ClientId`.
3. Alternar el login entre dos clientes deja exactamente uno autorizado.
4. Login y logout no cierran ni reemplazan la `WorkSession` abierta.
5. Expiración y revocación permiten recuperación auditada.
6. Dos renovaciones paralelas dejan vigente el token rotado por la ganadora.
7. Una concesión offline solo funciona en su dispositivo.
8. Un login online autenticado revoca la concesión offline anterior.
9. Reiniciar Edge recupera la misma sesión y `WorkSession`.
10. Logout offline sobrevive al reinicio sin cerrar la `WorkSession`.
11. La fecha histórica de la autorización no bloquea un Edge ya enrolado.
12. Manipular firma, fecha de inicio, usuario o dispositivo invalida la autorización.
13. Dos envíos simultáneos del formulario de login crean una sola sesión ganadora.
14. Un `401` de un recurso tras renovar no muestra conflicto de login ni cierra la sesión.
15. Una sesión reemplazada devuelve el código explícito `AuthenticationSessionReplaced`.
16. Login y cada renovación dejan exactamente 24 horas de ventana de inactividad.
