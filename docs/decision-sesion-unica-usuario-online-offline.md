# Decisión: sesiones online por cliente y concesión offline exclusiva

Fecha: 2026-07-31  
Estado: obligatoria; reemplaza la regla online de sesión única desde 2026-08-26
Prevalencia: complementa `decision-sesiones-operativas-cierre-usuario.md`.

## Regla

Puede existir una `AuthenticationSession` activa por `TenantId + UserId + ClientId`.
Varias pestañas del mismo navegador reutilizan el `ClientId` durable y la misma
sesión. Otro navegador o computador obtiene una sesión independiente: iniciar
sesión allí no revoca tokens ni cierra trabajo del primero. Un nuevo login con el
mismo `ClientId` reemplaza únicamente la sesión anterior de ese cliente.

La sesión de autenticación y la `WorkSession` son conceptos separados:

- `AuthenticationSession` controla identidad, acceso único, renovación y revocación.
- `WorkSession` registra la operación de facturación, sede, bodega y cierre financiero.

El login no cierra una `WorkSession`: autenticarse y abrir/cerrar operación son
responsabilidades distintas. El logout explícito sí cierra la operación abierta
del usuario según el contrato operativo vigente. Cerrar solo una vista no termina
ninguna sesión.

## Online

El servidor adquiere atómicamente una concesión por usuario y cliente. La sesión usa un
identificador, hash del token de renovación, cliente, fechas de emisión,
expiración, último contacto, revocación y versión de concurrencia.

Una sesión abandonada expira. Un supervisor autorizado puede revocarla. La
revocación queda auditada y los tokens dejan de renovarse.
La rotación del token de renovación es atómica. Una solicitud paralela que llegue
con el secreto inmediatamente anterior recibe conflicto y no revoca la sesión que
otra solicitud acaba de renovar; una pestaña atrasada no puede cerrar las demás.

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

1. Dos clientes online del mismo usuario permanecen autorizados simultáneamente.
2. Dos pestañas reutilizan una sesión sin crear otra.
3. Un nuevo login del mismo cliente revoca solo la sesión anterior de ese cliente.
4. Logout revoca únicamente la sesión autenticada y permite un nuevo login.
5. Expiración y revocación permiten recuperación auditada.
6. Dos renovaciones paralelas dejan vigente el token rotado por la ganadora.
7. Una concesión offline solo funciona en su dispositivo.
8. Un login online autenticado revoca la concesión offline anterior.
9. Reiniciar Edge recupera la misma sesión y `WorkSession`.
10. Logout offline sobrevive al reinicio y se sincroniza una sola vez.
11. Edge deja de operar al vencer la concesión.
12. Manipular firma, fechas, usuario o dispositivo invalida la concesión.
