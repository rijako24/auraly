# Auraly POS desktop installer evidence

Date: 2026-07-30

## Artifact

- File: `artifacts/auraly-pos/Auraly POS Setup.exe`
- Size: 112,447,488 bytes
- SHA-256: `BC51E711A9F23E2666BFD922D851DA79B7D37846C47B7D4F369713E0F95F5016`
- Reproducible build: `scripts/Build-AuralyPosInstaller.ps1`

The installer bundles the Auraly desktop launcher, the production Next.js
standalone build, Node.js runtime and the self-contained POS Edge host. It
preserves `%LOCALAPPDATA%\Auraly\PosEdge` during an update.

## Prueba local aislada (2026-09-22)

`Build-AuralyPosInstaller.ps1 -PayloadOnly -ApiUrl http://127.0.0.1:5097
-WebPort 57830 -EdgePort 57831` construye el mismo payload de Desktop sin
publicarlo ni instalarlo sobre otra versión. Los puertos predeterminados del
instalador siguen siendo 47830/47831. Antes de ejecutar el Desktop de prueba,
`AURALY_POS_DATA_DIRECTORY` permite elegir una carpeta absoluta independiente
para SQLite, claves, enrolamiento, WebView2, impresión y logs. Desktop conserva
su propietario de procesos y reinicios; el mutex distingue esa carpeta del
perfil instalado. Sin la variable, se conserva el comportamiento instalado.

La API y su base de prueba deben configurarse por separado. Un payload que use
una API local no publica cambios en Azure. Esta modalidad permite probar desde
el login y la preparación con una carpeta vacía, sin reutilizar los datos del
perfil instalado. Cerrar esa ventana termina solamente sus propios procesos.

## Binary versions during upgrades (2026-09-21)

Windows Installer compares numeric `FileVersion`, not the commit in
`ProductVersion` or the version in `desktopsettings.json`. The DEV rc190
installation log showed `Existing file is of an equal version` for Desktop
and Edge; the UI had updated while both binaries still came from September 11.
Those legacy binaries use `FileVersion=1.0.0.0`.

The existing packaging script now publishes both .NET graphs with
`FileVersion=1.<MSI product version>`; for example rc191 becomes `1.0.1.791`.
The leading `1` preserves compatibility with the legacy file version, and the
remaining components reuse the existing release ordering, including alpha,
beta, rc and stable releases. `AssemblyVersion` remains unchanged. Packaging
checks every Auraly EXE/DLL before signing and rejects a mixed payload.

The transactional `afterInstallExecute` upgrade remains in place, as does the
existing ownership of process shutdown and the SQLite data directory. No
history or returns endpoint is duplicated to accommodate obsolete binaries.
Restoring an older release requires its installer repair or reinstall; publishing
a new release must always use a greater release version.

Reference: [Windows Installer file versioning rules](https://learn.microsoft.com/en-us/windows/win32/msi/file-versioning-rules).

## Process ownership during updates

The desktop launcher assigns its Node.js and POS Edge children to one Windows
Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. If Windows
Installer must terminate the launcher during an update, Windows also terminates
both local children and releases ports 47830 and 47831 before the new version is
launched. This prevents an older Node.js process from serving stale Next.js
chunks after the package files have been replaced.

At startup, after obtaining the single-instance mutex and before opening either
local port, Desktop also removes a stale process only when its executable path
matches the installed Auraly Node.js or POS Edge binary exactly. This recovers
machines affected before Job Object ownership existed without terminating an
unrelated Node.js or web process.

## Automated verification

- `dotnet build Auraly.Commerce.sln --configuration Release --no-restore`:
  0 errors, 0 warnings.
- Foundation tests: 110 passed.
- POS Edge host tests: 9 passed.
- SQL Server integration tests: 47 passed.
- `npx tsc --noEmit`: passed.
- SQL Database project and DACPAC: built as part of the solution.

## Installed vertical-flow verification

The generated installer was executed successfully with exit code 0. The
installed application opened in Edge application mode and retained its
existing physical SQLite database after the update.

The test performed:

1. Online login and register enrollment for company AURALY, branch AURALY,
   register 01.
2. Automatic local identity and catalog synchronization.
3. Offline catalog lookup with barcode `7700000000001`.
4. Sale confirmation for `VTA01-00000001` / fiscal number `FE1`.
5. Receipt generation.
6. Durable upload to Auraly Server.
7. Fiscal snapshot and CUFE verification.
8. Exactly-once commercial processing.
9. Installer update and process restart.

Post-update local evidence:

- one issued sale;
- one uploaded outbox message;
- one catalog product;
- original fiscal number and CUFE retained.

Post-update SQL Server evidence:

- one sales document;
- one inventory movement;
- one payment;
- one processing receipt;
- one server outbox event;
- `FiscalVerified`;
- `Completed`;
- received CUFE equals calculated CUFE.

The central APIs used for this local demonstration are test services and are
not embedded into each register installer. The installer contains the local
POS runtime and POS Edge, while business processing remains in Auraly Server.
