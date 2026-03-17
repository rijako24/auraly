# Proyecto de Base de Datos - Mimos Baby Spa

Este proyecto contiene la definición completa de la base de datos SQL Server para la aplicación Mimos Baby Spa. **Es la única fuente de verdad del esquema**; ya no se usan migraciones de Entity Framework.

## 📁 Estructura

```
database/
├── MimosBabySpa.Database.sln          # Solución de Visual Studio
├── MimosBabySpa.Database/
│   ├── MimosBabySpa.Database.sqlproj  # Proyecto de base de datos
│   ├── Tables/                         # Scripts de tablas (28 tablas)
│   │   ├── Tenants.sql
│   │   ├── Businesses.sql
│   │   ├── AppUsers.sql
│   │   ├── AppRoles.sql
│   │   ├── Conversations.sql
│   │   ├── Messages.sql
│   │   ├── Leads.sql
│   ├── Scripts/                        # Scripts de despliegue
│   │   ├── CreateDatabase.ps1         # Crear base de datos
│   │   ├── Deploy.ps1                 # Despliegue completo
│   │   ├── Publish.ps1                 # Publicar esquema
│   │   ├── PreDeployment.sql           # Scripts pre-despliegue
│   │   └── PostDeployment.sql          # Scripts post-despliegue
│   └── README.md                       # Documentación del proyecto
└── DEPLOY.md                           # Guía completa de despliegue
```

## 🚀 Inicio Rápido

### Despliegue Automático (Recomendado)

```powershell
cd database\MimosBabySpa.Database\Scripts
.\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

### Despliegue Manual

1. **Crear base de datos:**
```powershell
.\CreateDatabase.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

2. **Compilar proyecto** (en Visual Studio o con MSBuild)

3. **Publicar esquema:**
```powershell
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"
```

## 📊 Esquema de Base de Datos

### Tablas (28 en total)

**Multitenancy y negocio:** Tenants, Businesses, BusinessWhatsAppNumbers, BusinessConfigurations, BusinessResources, SystemConfigurations

**Conversaciones:** Conversations, ConversationContexts, ConversationStates, Messages, Leads

**Servicios y reservas:** Services, ServiceAddOnRules, ServiceBundleItems, ServiceResourceUsages, Employees, EmployeeServices, Reservations, ReservationAddOns

**Identidad y auditoría:** AppUsers, AppRoles, Permissions, UserRoles, RolePermissions, UserExternalLogins, RefreshTokens, AuditLogs

**Pagos:** PaymentTransactions

## 📖 Documentación

- **[DEPLOY.md](DEPLOY.md)**: Guía completa de despliegue con todas las opciones
- **[MimosBabySpa.Database/README.md](MimosBabySpa.Database/README.md)**: Documentación técnica del proyecto

## 🔧 Requisitos

- SQL Server 2019 o superior
- SQL Server Data Tools (SSDT) o Visual Studio con carga de trabajo de base de datos
- PowerShell 5.1 o superior

## 📝 Notas

- **Ya no se usan migraciones de EF** – El esquema se gestiona exclusivamente desde este proyecto
- Este proyecto está en una solución separada para facilitar el despliegue independiente
- Los scripts de PowerShell incluyen manejo de errores y validaciones
- El proyecto usa DACPAC para despliegues incrementales y comparación de esquemas
- Los cambios de esquema se hacen editando los archivos en `Tables/` y desplegando con `Deploy.ps1`
