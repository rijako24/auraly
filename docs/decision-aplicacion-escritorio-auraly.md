# Decisión: Auraly como aplicación de escritorio

**Estado:** incluido en el Commerce MVP  
**Plataforma inicial:** Windows  
**Objetivo:** que Caja y Backoffice puedan instalarse y abrirse como una
aplicación del sistema, conservando una única interfaz y lógica web.

---

## 1. Decisión

Auraly tendrá dos superficies:

```text
Auraly Desktop     aplicación instalada; principal para POS
Auraly Web         navegador; disponible para administración
```

No son dos productos funcionales. Ambos ejecutan el mismo frontend, contratos y
permisos.

La caja se opera desde `Auraly Desktop`:

- icono en Inicio/escritorio;
- ventana propia sin barra del navegador;
- instalación y actualización controladas;
- inicio de sesión;
- soporte offline y almacenamiento local;
- integración con Auraly POS Edge;
- identificación permanente del dispositivo;
- asociación segura a una caja y bodega.

Un usuario administrativo también puede instalarla en modo Backoffice, sin caja.

---

## 2. Tecnología recomendada

Para el MVP Windows:

```text
Auraly.Desktop             host .NET
WebView2                   renderiza React
frontend compartido        mismo código de Auraly Web
Auraly POS Edge            periféricos/sincronización
SQLite                     datos POS locales
```

El host es delgado: ventana, assets locales, almacenamiento protegido,
actualización, activación y comunicación con POS Edge. No contiene reglas de
precios, facturación, inventario ni fiscalidad.

### Alternativas

| Alternativa | Decisión |
|---|---|
| solo PWA instalada | respaldo útil, pero menor control sobre instalación, soporte, almacenamiento y periféricos |
| Electron | maduro, pero agrega un runtime pesado cuando Windows ya ofrece WebView2 |
| Tauri | liviano, pero introduce Rust innecesariamente |
| MAUI/Blazor Hybrid | obligaría a cambiar o duplicar el frontend React |
| UI WPF/WinUI completa | duplicaría pantallas y pruebas |

WebView2 permite experiencia instalada sin construir otra UI.

---

## 3. Proyectos

```text
src/Desktop/Auraly.Desktop
src/Desktop/Auraly.Desktop.Contracts
src/Desktop/Auraly.Desktop.Infrastructure
src/POS/Auraly.Pos.Edge
src/POS/Auraly.Pos.LocalStore
```

Frontend:

```text
admin/src/features/pos
admin/src/features/backoffice
admin/src/platform
```

Adaptadores:

```text
IDesktopBridge
ILocalStore
IPeripheralGateway
IConnectivityMonitor
ISecureSecretStore
IApplicationUpdater
IDeviceEnrollmentClient
```

---

## 4. Instalación e inicio

El instalador:

- instala Auraly Desktop;
- valida WebView2 Runtime;
- instala POS Edge solo si el perfil lo requiere;
- crea accesos;
- registra `auraly://`;
- registra desinstalador;
- no solicita secretos del negocio.

Al abrir:

```text
Splash breve
  -> verificar assets locales
  -> cargar instalación
  -> comprobar compatibilidad
  -> Login
  -> restaurar módulo autorizado
```

Es de instancia única. La segunda apertura lleva al frente la ventana existente.
Inicio automático con Windows es opcional.

---

## 5. Identidades separadas

```text
InstallationId     instalación de Auraly Desktop
DeviceId           dispositivo registrado
CashRegisterId     caja lógica
WarehouseId        bodega heredada por la caja
UserId             persona autenticada
CashSessionId      turno/arqueo abierto
```

Una persona puede usar varias cajas y una caja varias personas por turnos. El
dispositivo no es el usuario.

---

## 6. Modos

```text
Backoffice
PointOfSale
Hybrid
```

### Backoffice

- no exige `CashRegisterId`;
- muestra menú por permisos;
- normalmente opera online;
- no descarga catálogo POS ni rangos fiscales;
- no instala periféricos salvo necesidad.

### PointOfSale

- exige dispositivo enrolado y caja activa;
- hereda la bodega de la caja;
- descarga catálogo/precios;
- recibe numeración;
- habilita almacenamiento/outbox offline;
- valida sesión/arqueo;
- integra periféricos.

### Hybrid

Permite POS y Backoffice en el mismo equipo. Es una capacidad configurada, no otro
ejecutable.

---

## 7. Enrolamiento

Primera apertura:

1. genera `InstallationId` UUIDv7;
2. recibe URL/entorno;
3. administrador inicia sesión o usa código temporal;
4. servidor registra dispositivo;
5. asigna perfil;
6. si es POS, asigna una caja existente;
7. la caja determina la bodega;
8. descarga configuración, catálogo y numeración;
9. prueba periféricos.

La caja no se configura escribiendo libremente “Caja 1” en un archivo.

```text
DeviceCashRegisterAssignment
  DeviceCashRegisterAssignmentId
  DeviceId
  CashRegisterId
  ValidFromUtc
  ValidUntilUtc?
  Status
  AssignedByUserId
  RowVersion
```

Reasignar requiere permiso, conectividad y auditoría. Revocar invalida futuras
sincronizaciones y asignaciones de números.

---

## 8. Login

La app exige autenticación.

### Online

- OIDC/OAuth;
- access token corto;
- refresh token protegido;
- permisos/alcances del servidor;
- revocación remota.

### Offline

Solo ingresa un usuario que inició online previamente en ese equipo, está
autorizado para el negocio/caja y tiene credencial offline vigente.

No se guarda su contraseña. Se usa PIN o desbloqueo local contra:

- material cifrado con protección Windows;
- snapshot firmado de permisos mínimos;
- expiración configurable;
- revocación aplicada al reconectar.

Las capacidades que requieren servidor no se habilitan por tener sesión offline.

---

## 9. Apertura POS

```text
validar asignación y bodega
validar configuración
validar catálogo y precios
validar rango de factura
validar periféricos requeridos
abrir/seleccionar sesión de caja
abrir Facturación
```

Siempre muestra:

```text
Caja C03 · Bodega Norte · Usuario Ana · Arqueo #... · Online/Offline
```

Si falta algo crítico, presenta diagnóstico; no abre una venta aparentemente
funcional.

---

## 10. Assets y offline

La aplicación instala una versión firmada del frontend. No descarga
HTML/JavaScript en cada apertura.

```text
Desktop shell
  -> assets React locales
  -> local store
  -> API cuando hay red
```

Así abre aunque Internet, Azure o el servidor on-premise estén temporalmente
inaccesibles.

La versión de assets y contratos se valida. Una actualización incompatible no se
aplica en mitad de una venta.

---

## 11. POS Edge

```text
Auraly Desktop
      |
      | canal local autenticado
      v
Auraly POS Edge
      +-- balanza
      +-- impresora
      +-- cajón
      +-- lector no-keyboard-wedge
      +-- SQLite
```

Se usa named pipe o loopback restringido con token rotatorio, protocolo versionado
y sin puerto abierto a la red. El lector keyboard wedge escribe directamente en
la captura.

---

## 12. Actualizaciones

- firmadas;
- canales `Stable`, `Pilot`, `Internal`;
- descarga en segundo plano;
- instalación fuera de una factura activa;
- rollback si falla el arranque;
- compatibilidad con API/base local.

Cloud usa manifiesto administrado por Auraly. On-premise usa servidor local o
paquete firmado. La empresa puede controlar la ventana de despliegue.

---

## 13. Navegación

```text
auraly://pos
auraly://orders/{id}
auraly://products/{id}
auraly://sync/status
```

Todo deep link valida sesión, negocio y permiso, y nunca ejecuta una acción
destructiva al abrir.

---

## 14. Seguridad local

- secretos en almacén protegido;
- base local protegida/cifrada según amenaza;
- paquetes y numeración firmados;
- bloqueo por inactividad;
- cierre sin borrar outbox;
- logs sin tokens ni datos de tarjeta;
- CSP estricta;
- bridge con comandos permitidos;
- JavaScript remoto arbitrario bloqueado.

El usuario Windows no obtiene permisos Auraly automáticamente.

---

## 15. Cloud y on-premise

```text
EnvironmentProfile
  ProfileId
  DisplayName
  ApiBaseUrl
  IdentityAuthority
  UpdateChannelUrl
  DeploymentMode
  TrustPolicy
```

Cloud viene preconfigurado. On-premise usa paquete/código del servidor del
cliente. Cambiar entorno exige permiso y logout. No mezcla catálogo, tokens,
rangos ni outbox.

---

## 16. Experiencia sin caja

Un PC administrativo puede:

```text
abrir Auraly
iniciar sesión
ver su menú
usar Productos, Compras, Inventario, Reportes y Usuarios
```

No muestra caja, arqueo, rango ni periféricos. El escritorio es distribución, no
un permiso.

---

## 17. Nombre

Producto y ventana:

```text
Auraly
```

Un acceso puede llamarse `Auraly POS` si abre directamente la caja, pero sigue
siendo el mismo producto.

No aparecen Talkio, Auraly, Xion o Pedidos OK en solución, instalador,
ventana, carpetas nuevas, servicios, protocolo o certificados.

---

## 18. Diagnóstico

```text
versión Desktop/frontend/POS Edge
entorno
DeviceId
CashRegisterId
última sincronización
operaciones pendientes
periféricos
espacio local
actualización
```

Se exporta paquete sanitizado con permiso, sin secretos ni información completa de
clientes/facturas.

---

## 19. Pruebas

- instalación, actualización, rollback y desinstalación;
- enrolamiento, revocación y reasignación;
- Backoffice sin caja e Hybrid;
- login online/offline, expiración y revocación;
- abrir sin red, vender, cerrar/reabrir y sincronizar;
- lector, balanza, impresión y cajón;
- POS Edge detenido/reiniciado;
- deep links y bridge maliciosos;
- manipulación/copia de configuración local;
- clonación de caja;
- actualización o paquete sin firma.

---

## 20. Criterios de aceptación

- se instala y abre como aplicación Windows;
- no muestra barra ni controles del navegador;
- comparte React con Auraly Web;
- POS exige login, dispositivo y `CashRegisterId`;
- la caja hereda `WarehouseId`;
- Backoffice funciona instalado sin caja;
- abre sin Internet con assets locales;
- no mezcla entornos;
- POS Edge usa canal local seguro;
- actualización no interrumpe una venta;
- usuario ve caja, bodega, sesión y conectividad;
- no quedan nombres legados.

---

## 21. Conclusión

Auraly Desktop entrega una experiencia instalada, similar en comportamiento a una
app de mensajería, sin duplicar Auraly Web.

Para caja es la superficie operativa completa y offline. Para usuarios no POS es
una alternativa cómoda al navegador. La diferencia se controla por enrolamiento,
capacidades y permisos.
