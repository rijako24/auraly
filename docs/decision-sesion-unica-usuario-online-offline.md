# Decisión: una sola sesión activa por usuario

Fecha: 2026-07-31  
Estado: obligatoria  
Prevalencia: complementa `decision-sesiones-operativas-cierre-usuario.md`.

## Regla

Solo puede existir una `AuthenticationSession` activa por `TenantId + UserId`.
Varias pestañas del mismo cliente reutilizan la misma sesión; iniciar sesión en
otro computador se rechaza mientras la anterior siga activa.

La sesión de autenticación y la `WorkSession` son conceptos separados:

- `AuthenticationSession` controla identidad, acceso único, renovación y revocación.
- `WorkSession` registra la operación de facturación, sede, bodega y cierre financiero.

Cerrar sesión termina ambas cuando exista una `WorkSession` abierta y genera el
cierre operativo correspondiente. Cerrar solo una vista no termina la sesión.

## Online

El servidor adquiere atómicamente una concesión por usuario. La sesión usa un
identificador, hash del token de renovación, cliente, fechas de emisión,
expiración, último contacto, revocación y versión de concurrencia.

Una sesión abandonada expira. Un supervisor autorizado puede revocarla. La
revocación queda auditada y los tokens dejan de renovarse.

## Edge offline

La exclusividad global no puede comprobarse entre dos computadores totalmente
desconectados sin una concesión previa. Por tanto, Edge solo permite login offline
con una concesión exclusiva firmada por el servidor y asociada a `UserId +
DeviceId`.

Mientras la concesión está vigente, el servidor bloquea cualquier otro login del
usuario. Edge valida firma, dispositivo, vigencia y continuidad temporal. No
acepta retrocesos de reloj ni crea concesiones por sí mismo.

Un logout offline cierra localmente la `WorkSession` y escribe en outbox la
liberación. El bloqueo del servidor termina cuando recibe esa liberación o cuando
vence la concesión. Un supervisor puede revocarla, pero un Edge desconectado solo
conoce la revocación al reconectar; por eso nunca puede operar después del
vencimiento firmado.

La duración máxima de la concesión es una política de seguridad configurable con
un límite del sistema. No se prometen sesiones offline indefinidas y exclusividad
global simultáneamente.

## Numeración

La sesión única no protege consecutivos. La numeración continúa perteneciendo a
la serie central del servidor o a la serie técnica del dispositivo Edge.

## Pruebas obligatorias

1. Un segundo login online del mismo usuario se rechaza.
2. Dos pestañas reutilizan una sesión sin crear otra.
3. Logout libera la sesión y permite un nuevo login.
4. Expiración y revocación permiten recuperación auditada.
5. Una concesión offline solo funciona en su dispositivo.
6. El servidor bloquea login mientras la concesión offline está vigente.
7. Reiniciar Edge recupera la misma sesión y `WorkSession`.
8. Logout offline sobrevive al reinicio y se sincroniza una sola vez.
9. Edge deja de operar al vencer la concesión.
10. Manipular firma, fechas, usuario o dispositivo invalida la concesión.
