# Sesiones y re-enrolamiento del equipo

## Inicio de sesión

Cada autenticación nueva del mismo usuario revoca transaccionalmente las sesiones web y las autorizaciones offline activas anteriores. La revocación conserva el registro histórico y usa la razón `ReplacedByNewLogin`; no elimina sesiones ni modifica facturas. El token anterior deja de ser aceptado inmediatamente y el nuevo token queda como la única sesión activa.

## Re-enrolamiento

La aplicación instalada puede volver a enrolar un equipo desde la configuración online. El cliente envía el `DeviceId` protegido que ya tiene guardado. El servidor valida que el equipo pertenezca al mismo tenant y negocio; si se intenta moverlo a otro negocio, rechaza la operación para proteger ventas offline pendientes.

En un re-enrolamiento válido se conserva:

- `DeviceId`.
- Serie operativa y su cursor de consecutivos.
- Series y documentos ya emitidos.
- Facturas, SQLite y outbox local.

Se actualiza de forma transaccional:

- Secreto del dispositivo.
- Nombre de instalación.
- Permisos efectivos del dispositivo.
- Sede/bodega seleccionadas en el paquete protegido.
- Usuario inicial y permisos que se sincronizarán al reiniciar.

No se crean otra fila de dispositivo ni otra serie para el mismo equipo. La aplicación reinicia después de recibir el paquete y la sincronización de identidad/catálogo se ejecuta con la nueva configuración.

## Aplicación instalada

`Auraly.Desktop` y `Auraly.Pos.Edge.Host` se publican como aplicaciones Windows GUI (`WinExe`). El launcher inicia Node y POS Edge con `CreateNoWindow=true`, captura sus logs en `%LOCALAPPDATA%\\Auraly\\PosEdge\\logs` y abre la interfaz como ventana de aplicación. Una consola visible al ejecutar manualmente `dotnet run` pertenece al modo de desarrollo y no al instalador.

## Evidencia local

- API Release: 0 errores, 0 advertencias.
- POS Edge Host Release: 0 errores, 0 advertencias.
- Pruebas POS Edge: 16 correctas.
- TypeScript y build de `admin`: correctos.
- Login real local: segundo login `200`; token anterior `401`; token nuevo `200`.
- La prueba SQL Server de re-enrolamiento queda incorporada en `PosEnrollmentApiTests`; en este entorno el fixture de despliegue se quedó esperando y no se marca como aprobada hasta ejecutarla con el SQL Server libre.## Reset local para probar desde cero

Para validar re-enrolado desde cero sin ruido de estado anterior, usa el script:

- `scripts/Reset-AuralyPosLocal.ps1` desde PowerShell admin o consola del desarrollador.
- Opcional: `-KeepServerProfile` conserva `auraly-pos.db` (no borra historial local).
- Opcional: `-KeepCatalogCache` conserva llaves de cifrado/cach�.

Paso r�pido de limpieza en navegador (solo para sesi�n web):

```js
localStorage.removeItem('auth-state');
localStorage.removeItem('selected_business_id');
sessionStorage.clear();
```

Despu�s de eso, reinicia Auraly POS y vuelve a abrir `/pos`. Si el equipo estaba enrolado, te debe pedir configuraci�n online y/o re-enrolado limpio.
