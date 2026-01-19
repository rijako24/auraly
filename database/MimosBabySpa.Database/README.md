# Proyecto de Base de Datos - Mimos Baby Spa

Este proyecto contiene la definición de la base de datos SQL Server para la aplicación Mimos Baby Spa.

## Estructura del Proyecto

```
MimosBabySpa.Database/
├── Tables/              # Scripts de creación de tablas
│   ├── Conversations.sql
│   ├── Messages.sql
│   └── Leads.sql
├── Scripts/             # Scripts de despliegue
│   ├── PreDeployment.sql
│   ├── PostDeployment.sql
│   ├── CreateDatabase.ps1
│   └── Publish.ps1
└── MimosBabySpa.Database.sqlproj
```

## Requisitos

- SQL Server 2019 o superior
- SQL Server Data Tools (SSDT) o Visual Studio con carga de trabajo de base de datos
- PowerShell 5.1 o superior
- SqlPackage.exe (incluido con SQL Server)

## Tablas

### Conversations
Almacena las conversaciones de WhatsApp con los clientes.

- **ConversationId** (PK, UNIQUEIDENTIFIER): Identificador único de la conversación
- **UserNumber** (NVARCHAR(50), NOT NULL): Número de teléfono del usuario
- **LastMessage** (NVARCHAR(1000)): Último mensaje de la conversación
- **LastIntent** (NVARCHAR(50)): Última intención detectada
- **Timestamp** (DATETIME2): Fecha y hora de creación/actualización
- **CustomerName** (NVARCHAR(100)): Nombre del cliente
- **BabyAge** (INT): Edad del bebé en meses
- **RecommendedPlan** (NVARCHAR(100)): Plan recomendado

**Índices:**
- IX_Conversations_UserNumber: Índice en UserNumber para búsquedas rápidas

### Messages
Almacena todos los mensajes intercambiados en las conversaciones.

- **MessageId** (PK, UNIQUEIDENTIFIER): Identificador único del mensaje
- **ConversationId** (FK, UNIQUEIDENTIFIER, NOT NULL): Referencia a Conversations
- **Sender** (NVARCHAR(20), NOT NULL): "User" o "Bot"
- **MessageText** (NVARCHAR(2000), NOT NULL): Contenido del mensaje
- **Intent** (NVARCHAR(50), NOT NULL): Intención clasificada del mensaje
- **Timestamp** (DATETIME2): Fecha y hora del mensaje

**Relaciones:**
- FK_Messages_Conversations: Foreign key a Conversations con CASCADE DELETE

**Índices:**
- IX_Messages_ConversationId: Índice en ConversationId para búsquedas rápidas

### Leads
Almacena información de los leads/clientes potenciales.

- **LeadId** (PK, UNIQUEIDENTIFIER): Identificador único del lead
- **UserNumber** (NVARCHAR(50), NOT NULL): Número de teléfono del usuario
- **BabyAge** (INT): Edad del bebé en meses
- **RecommendedPlan** (NVARCHAR(100)): Plan recomendado
- **Status** (NVARCHAR(20), NOT NULL, DEFAULT 'New'): Estado del lead (New, Contacted, Closed)
- **Timestamp** (DATETIME2): Fecha y hora de creación
- **CustomerName** (NVARCHAR(100)): Nombre del cliente
- **Notes** (NVARCHAR(1000)): Notas adicionales

**Índices:**
- IX_Leads_UserNumber: Índice en UserNumber para búsquedas rápidas

## Despliegue

### Opción 1: Usando PowerShell (Recomendado)

1. **Crear la base de datos:**
```powershell
cd database\MimosBabySpa.Database\Scripts
.\CreateDatabase.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

2. **Publicar el esquema:**
```powershell
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

### Opción 2: Usando Visual Studio / SSDT

1. Abre el proyecto `MimosBabySpa.Database.sqlproj` en Visual Studio
2. Clic derecho en el proyecto → **Publicar**
3. Configura la conexión al servidor SQL Server
4. Haz clic en **Publicar**

### Opción 3: Usando SqlPackage.exe directamente

```powershell
# Compilar el proyecto primero
dotnet build MimosBabySpa.Database.sqlproj

# Publicar usando SqlPackage
SqlPackage.exe /Action:Publish `
    /SourceFile:"bin\Debug\MimosBabySpa.Database.dacpac" `
    /TargetServerName:"localhost" `
    /TargetDatabaseName:"MimosBabySpa" `
    /TargetTrustServerCertificate:True
```

## Configuración de Conexión

### Autenticación Integrada (Windows)
```powershell
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa" -UseIntegratedSecurity
```

### Autenticación SQL Server
```powershell
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa" `
    -Username "sa" -Password "TuPassword" -UseIntegratedSecurity:$false
```

## Scripts de Migración

Si necesitas hacer cambios en el esquema después del despliegue inicial:

1. Modifica los archivos `.sql` correspondientes en la carpeta `Tables/`
2. Compila el proyecto: `dotnet build`
3. Publica nuevamente usando cualquiera de los métodos anteriores

SqlPackage comparará el esquema actual con el nuevo y generará un script de migración automáticamente.

## Notas Importantes

- **Backup**: Siempre haz un backup de la base de datos antes de publicar cambios importantes
- **Desarrollo**: Usa una base de datos de desarrollo para probar cambios antes de publicar a producción
- **Migraciones**: Los cambios destructivos (DROP TABLE, etc.) requieren confirmación explícita en SqlPackage

## Troubleshooting

### Error: "SqlPackage.exe no encontrado"
Asegúrate de tener SQL Server Data Tools instalado o especifica la ruta completa a SqlPackage.exe en el script.

### Error: "No se puede conectar al servidor"
- Verifica que SQL Server esté ejecutándose
- Verifica que el firewall permita conexiones
- Verifica las credenciales de autenticación

### Error: "Base de datos ya existe"
Si la base de datos ya existe, SqlPackage actualizará el esquema automáticamente. Si quieres recrearla, elimínala primero.
