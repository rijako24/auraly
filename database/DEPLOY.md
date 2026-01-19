# Guía de Despliegue - Base de Datos Mimos Baby Spa

Esta guía te ayudará a desplegar la base de datos en diferentes entornos.

## Requisitos Previos

- SQL Server 2019 o superior
- SQL Server Data Tools (SSDT) o Visual Studio con carga de trabajo de base de datos
- PowerShell 5.1 o superior
- Permisos para crear bases de datos y publicar esquemas

## Estructura del Proyecto

```
database/
├── MimosBabySpa.Database/
│   ├── Tables/              # Scripts de creación de tablas
│   │   ├── Conversations.sql
│   │   ├── Messages.sql
│   │   └── Leads.sql
│   ├── Scripts/            # Scripts de despliegue
│   │   ├── CreateDatabase.ps1
│   │   ├── Deploy.ps1      # Script completo de despliegue
│   │   └── Publish.ps1
│   └── MimosBabySpa.Database.sqlproj
└── MimosBabySpa.Database.sln
```

## Opciones de Despliegue

### Opción 1: Despliegue Completo Automático (Recomendado)

El script `Deploy.ps1` ejecuta todos los pasos necesarios:

```powershell
cd database\MimosBabySpa.Database\Scripts
.\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

**Con autenticación SQL Server:**
```powershell
.\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa" `
    -Username "sa" -Password "TuPassword" -UseIntegratedSecurity:$false
```

### Opción 2: Pasos Manuales

#### Paso 1: Crear la Base de Datos

```powershell
cd database\MimosBabySpa.Database\Scripts
.\CreateDatabase.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

#### Paso 2: Compilar el Proyecto

**Opción A: Usando Visual Studio**
1. Abre `MimosBabySpa.Database.sln` en Visual Studio
2. Clic derecho en el proyecto → **Compilar**

**Opción B: Usando MSBuild**
```powershell
cd database\MimosBabySpa.Database
msbuild MimosBabySpa.Database.sqlproj /t:Build /p:Configuration=Debug
```

#### Paso 3: Publicar el Esquema

```powershell
cd database\MimosBabySpa.Database\Scripts
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

### Opción 3: Usando Visual Studio / SSDT

1. Abre `database\MimosBabySpa.Database.sln` en Visual Studio
2. Clic derecho en el proyecto `MimosBabySpa.Database` → **Publicar**
3. Configura la conexión:
   - **Servidor:** localhost (o tu servidor)
   - **Base de datos:** MimosBabySpa
   - **Autenticación:** Windows o SQL Server
4. Haz clic en **Publicar**

### Opción 4: Usando SqlPackage.exe Directamente

```powershell
# Primero compila el proyecto (ver Opción 2)

# Luego publica
SqlPackage.exe /Action:Publish `
    /SourceFile:"database\MimosBabySpa.Database\bin\Debug\MimosBabySpa.Database.dacpac" `
    /TargetServerName:"localhost" `
    /TargetDatabaseName:"MimosBabySpa" `
    /TargetTrustServerCertificate:True
```

## Entornos

### Desarrollo Local

```powershell
.\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa_Dev"
```

### Staging

```powershell
.\Deploy.ps1 -ServerInstance "staging-sql-server" -DatabaseName "MimosBabySpa_Staging" `
    -Username "deploy_user" -Password "SecurePassword123" -UseIntegratedSecurity:$false
```

### Producción

⚠️ **IMPORTANTE:** Siempre haz un backup antes de desplegar a producción.

```powershell
# 1. Backup primero
Backup-SqlDatabase -ServerInstance "prod-sql-server" -Database "MimosBabySpa" `
    -BackupFile "C:\Backups\MimosBabySpa_$(Get-Date -Format 'yyyyMMdd_HHmmss').bak"

# 2. Desplegar
.\Deploy.ps1 -ServerInstance "prod-sql-server" -DatabaseName "MimosBabySpa" `
    -Username "deploy_user" -Password "SecurePassword123" -UseIntegratedSecurity:$false
```

## Verificación Post-Despliegue

Después del despliegue, verifica que las tablas se crearon correctamente:

```sql
USE MimosBabySpa;
GO

-- Verificar tablas
SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;

-- Verificar índices
SELECT 
    t.name AS TableName,
    i.name AS IndexName
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.is_ms_shipped = 0
ORDER BY t.name, i.name;

-- Verificar foreign keys
SELECT 
    fk.name AS ForeignKeyName,
    OBJECT_NAME(fk.parent_object_id) AS TableName,
    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTableName,
    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumnName
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
ORDER BY TableName, ForeignKeyName;
```

## Troubleshooting

### Error: "SqlPackage.exe no encontrado"

**Solución:** Instala SQL Server Data Tools (SSDT) desde:
- Visual Studio Installer → Modificar → Carga de trabajo "Desarrollo de almacenamiento y procesamiento de datos"
- O descarga SSDT desde: https://docs.microsoft.com/sql/ssdt/download-sql-server-data-tools-ssdt

### Error: "No se puede conectar al servidor"

**Verificaciones:**
1. SQL Server está ejecutándose: `Get-Service MSSQLSERVER`
2. SQL Server Browser está ejecutándose: `Get-Service SQLBROWSER`
3. Firewall permite conexiones en el puerto 1433
4. Las credenciales son correctas

### Error: "Base de datos ya existe"

Si la base de datos ya existe, SqlPackage actualizará el esquema automáticamente. Si quieres recrearla:

```sql
USE master;
GO
DROP DATABASE IF EXISTS MimosBabySpa;
GO
```

Luego ejecuta el script de despliegue nuevamente.

### Error: "No se puede compilar el proyecto"

**Solución:** Asegúrate de tener Visual Studio con SSDT instalado, o usa MSBuild directamente:

```powershell
# Encuentra MSBuild
$msbuild = & "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe

# Compila
& $msbuild database\MimosBabySpa.Database\MimosBabySpa.Database.sqlproj `
    /t:Build /p:Configuration=Debug
```

## Actualizaciones Futuras

Cuando necesites actualizar el esquema:

1. Modifica los archivos `.sql` correspondientes en `Tables/`
2. Compila el proyecto
3. Publica nuevamente - SqlPackage comparará y aplicará solo los cambios necesarios

## Scripts de Mantenimiento

### Backup de Base de Datos

```powershell
Backup-SqlDatabase -ServerInstance "localhost" -Database "MimosBabySpa" `
    -BackupFile "C:\Backups\MimosBabySpa_$(Get-Date -Format 'yyyyMMdd_HHmmss').bak"
```

### Restaurar Base de Datos

```powershell
Restore-SqlDatabase -ServerInstance "localhost" -Database "MimosBabySpa" `
    -BackupFile "C:\Backups\MimosBabySpa_20240101_120000.bak" -ReplaceDatabase
```

## Contacto y Soporte

Para problemas o preguntas sobre el despliegue, consulta la documentación del proyecto o contacta al equipo de desarrollo.
