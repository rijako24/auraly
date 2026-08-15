# Despliegue de Auraly

La única guía vigente de aprovisionamiento, release, publicación y reversión es [infrastructure/azure/README.md](infrastructure/azure/README.md).

El flujo obligatorio es:

1. Compilar y probar `Auraly.Commerce.sln` y `admin`.
2. Crear un release reproducible con `infrastructure/azure/New-AuralyRelease.ps1`.
3. Publicar y validar primero en DEV.
4. Promover exactamente el mismo artefacto a PROD solo con autorización explícita.

No uses perfiles locales de Visual Studio ni comandos históricos de publicación directa.