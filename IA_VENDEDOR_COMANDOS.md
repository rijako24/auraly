# ⚡ COMANDOS ÚTILES: IA VENDEDOR

## 🚀 COMANDOS DE INICIO RÁPIDO

### Aplicar Migración
```powershell
cd c:\Users\RichardJacome\MimosBabySpa

dotnet ef database update `
    --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj `
    --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj `
    --context ApplicationDbContext
```

### Compilar Solución
```powershell
dotnet build
```

### Ejecutar Pruebas
```powershell
dotnet test
```

### Ejecutar API Local
```powershell
cd src/API/MimosBabySpa.API
func start
```

---

## 🔧 COMANDOS DE DESARROLLO

### Crear Nueva Migración
```powershell
dotnet ef migrations add <NombreMigracion> `
    --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj `
    --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj
```

### Revertir Última Migración
```powershell
dotnet ef migrations remove `
    --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj `
    --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj
```

### Ver Estado de Migraciones
```powershell
dotnet ef migrations list `
    --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj `
    --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj
```

### Generar Script SQL de Migración
```powershell
dotnet ef migrations script `
    --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj `
    --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj `
    --output migration.sql
```

---

## 🧪 COMANDOS DE TESTING

### Ejecutar Todas las Pruebas
```powershell
dotnet test src/Tests/MimosBabySpa.Tests/MimosBabySpa.Tests.csproj
```

### Ejecutar con Cobertura
```powershell
dotnet test src/Tests/MimosBabySpa.Tests/MimosBabySpa.Tests.csproj `
    --collect:"XPlat Code Coverage"
```

### Ejecutar Pruebas Específicas
```powershell
dotnet test --filter "FullyQualifiedName~MessageServiceTests"
```

### Ver Resultados Detallados
```powershell
dotnet test --logger "console;verbosity=detailed"
```

---

## 📊 COMANDOS DE BASE DE DATOS

### Conectar a BD y Ver Tablas
```sql
-- En SQL Server Management Studio o Azure Data Studio

SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME IN (
    'ConversationSessions',
    'CustomerProfiles',
    'SalesInteractions'
);
```

### Ver Sesiones Activas
```sql
SELECT 
    SessionId,
    CustomerPhoneNumber,
    CurrentStage,
    StageAttempts,
    IsActive,
    ExpiresAt
FROM ConversationSessions
WHERE IsActive = 1
ORDER BY CreatedAt DESC;
```

### Ver Perfiles de Alto Valor
```sql
SELECT 
    CustomerName,
    PhoneNumber,
    Segment,
    TotalPurchases,
    LifetimeValue,
    ConversionProbability
FROM CustomerProfiles
WHERE LifetimeValue > 500
ORDER BY LifetimeValue DESC;
```

### Ver Interacciones Recientes
```sql
SELECT TOP 100
    InteractionAt,
    Stage,
    TacticApplied,
    UserMessage,
    BotResponse,
    WasSuccessful
FROM SalesInteractions
ORDER BY InteractionAt DESC;
```

### Métricas de Conversión por Etapa
```sql
SELECT 
    Stage,
    COUNT(*) as TotalInteracciones,
    SUM(CASE WHEN WasSuccessful = 1 THEN 1 ELSE 0 END) as Exitosas,
    CAST(SUM(CASE WHEN WasSuccessful = 1 THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) as TasaExito
FROM SalesInteractions
WHERE BusinessId = @BusinessId
GROUP BY Stage
ORDER BY Stage;
```

---

## 🐛 COMANDOS DE DEBUGGING

### Ver Logs de Orquestador
```powershell
# En Azure Function App
func azure functionapp logstream <nombre-app>
```

### Ejecutar en Modo Debug
```powershell
cd src/API/MimosBabySpa.API
$env:FUNCTIONS_WORKER_RUNTIME="dotnet-isolated"
func start --verbose
```

### Limpiar y Recompilar
```powershell
dotnet clean
dotnet build
```

---

## 🚢 COMANDOS DE DESPLIEGUE

### Desplegar a Azure
```powershell
cd src/API/MimosBabySpa.API
func azure functionapp publish <nombre-function-app>
```

### Desplegar con Configuración
```powershell
func azure functionapp publish <nombre-function-app> `
    --publish-settings-only
```

### Ver Configuración de Azure
```powershell
func azure functionapp list-functions <nombre-function-app>
```

---

## 🔍 COMANDOS DE INSPECCIÓN

### Ver Archivos Nuevos Creados
```powershell
git status --untracked-files=all | Select-String "??"
```

### Contar Líneas de Código
```powershell
# Domain
(Get-ChildItem -Path src/Domain/MimosBabySpa.Domain -Include *.cs -Recurse | 
    Get-Content | Measure-Object -Line).Lines

# Application
(Get-ChildItem -Path src/Application/MimosBabySpa.Application -Include *.cs -Recurse | 
    Get-Content | Measure-Object -Line).Lines
```

### Ver Estructura de Carpetas
```powershell
tree src/Application/MimosBabySpa.Application /F
```

---

## 📦 COMANDOS DE BACKUP

### Crear Backup de BD Antes de Migrar
```sql
BACKUP DATABASE [MimosBabySpa] 
TO DISK = 'C:\Backups\MimosBabySpa_PreAIVendedor.bak'
WITH FORMAT, INIT, COMPRESSION;
```

### Exportar Datos de Prueba
```sql
-- Exportar datos antes de migrar
SELECT * INTO ConversationsBackup FROM Conversations;
SELECT * INTO LeadsBackup FROM Leads;
```

---

## 🧹 COMANDOS DE LIMPIEZA

### Limpiar Sesiones Expiradas (Manual)
```sql
UPDATE ConversationSessions
SET IsActive = 0
WHERE IsActive = 1 
    AND ExpiresAt < GETUTCDATE();
```

### Limpiar Archivos de Compilación
```powershell
Get-ChildItem -Include bin,obj -Recurse | Remove-Item -Recurse -Force
```

---

## 📊 COMANDOS DE MONITOREO

### Ver Sesiones Activas en Tiempo Real
```sql
-- Ejecutar cada 30 segundos
SELECT 
    COUNT(*) as SesionesActivas,
    AVG(StageAttempts) as IntentoPromedio,
    MAX(ClosingAttempts) as MaxIntentoCierre
FROM ConversationSessions
WHERE IsActive = 1;
```

### Dashboard de Conversión
```sql
SELECT 
    CAST(CurrentStage AS VARCHAR) as Etapa,
    COUNT(*) as CantidadSesiones,
    AVG(CAST(StageAttempts AS FLOAT)) as IntentosPromedio
FROM ConversationSessions
WHERE IsActive = 1
GROUP BY CurrentStage
ORDER BY CurrentStage;
```

### Top Clientes por Probabilidad
```sql
SELECT TOP 10
    CustomerName,
    PhoneNumber,
    ConversionProbability,
    TotalConversations,
    TotalPurchases
FROM CustomerProfiles
WHERE ConversionProbability > 0.7
    AND TotalPurchases = 0
ORDER BY ConversionProbability DESC;
```

---

## 🎯 COMANDOS DE OPTIMIZACIÓN

### Rebuild de Índices
```sql
-- Optimizar rendimiento de consultas
ALTER INDEX ALL ON ConversationSessions REBUILD;
ALTER INDEX ALL ON CustomerProfiles REBUILD;
ALTER INDEX ALL ON SalesInteractions REBUILD;
```

### Actualizar Estadísticas
```sql
UPDATE STATISTICS ConversationSessions;
UPDATE STATISTICS CustomerProfiles;
UPDATE STATISTICS SalesInteractions;
```

---

## 🔐 COMANDOS DE CONFIGURACIÓN

### Configurar Feature Flag
```powershell
# En local.settings.json (local)
{
  "Values": {
    "Features__UseAIVendedor": "true"
  }
}

# En Azure (producción)
az functionapp config appsettings set `
    --name <nombre-function-app> `
    --resource-group <resource-group> `
    --settings "Features__UseAIVendedor=true"
```

### Ver Variables de Entorno
```powershell
# Local
Get-Content local.settings.json

# Azure
az functionapp config appsettings list `
    --name <nombre-function-app> `
    --resource-group <resource-group>
```

---

## 🎓 COMANDOS DE APRENDIZAJE

### Explorar el Código
```powershell
# Ver orquestador
code src/Application/MimosBabySpa.Application/Orchestration/ConversationOrchestrator.cs

# Ver máquina de estados
code src/Application/MimosBabySpa.Application/Sales/SalesStateMachine.cs

# Ver motor de estrategia
code src/Application/MimosBabySpa.Application/Sales/SalesStrategyEngine.cs
```

### Buscar Ejemplos
```powershell
# Buscar uso de SalesStage
Select-String -Path src/**/*.cs -Pattern "SalesStage"

# Buscar uso de CustomerProfile
Select-String -Path src/**/*.cs -Pattern "CustomerProfile"
```

---

## 📝 NOTAS IMPORTANTES

### Antes de Desplegar
- [ ] Aplicar migración de BD
- [ ] Ejecutar todas las pruebas
- [ ] Verificar configuración de Azure
- [ ] Hacer backup de BD
- [ ] Revisar logs en staging

### Después de Desplegar
- [ ] Monitorear sesiones activas
- [ ] Revisar métricas de conversión
- [ ] Ajustar umbrales si es necesario
- [ ] Validar comportamiento con usuarios reales

---

**Comandos listos para usar. ¡Éxito con el despliegue!** 🚀
