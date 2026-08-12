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
