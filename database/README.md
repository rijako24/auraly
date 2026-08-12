# Proyecto de Base de Datos - Auraly

Este proyecto contiene la definiciÃ³n completa de la base de datos SQL Server para la aplicaciÃ³n Auraly. **Es la Ãºnica fuente de verdad del esquema**; ya no se usan migraciones de Entity Framework.

## ðŸ“ Estructura

```
database/
â”œâ”€â”€ Auraly.Database.sln          # SoluciÃ³n de Visual Studio
â”œâ”€â”€ Auraly.Database/
â”‚   â”œâ”€â”€ Auraly.Database.sqlproj  # Proyecto de base de datos
â”‚   â”œâ”€â”€ Tables/                         # Scripts de tablas (28 tablas)
â”‚   â”‚   â”œâ”€â”€ Tenants.sql
â”‚   â”‚   â”œâ”€â”€ Businesses.sql
â”‚   â”‚   â”œâ”€â”€ AppUsers.sql
â”‚   â”‚   â”œâ”€â”€ AppRoles.sql
â”‚   â”‚   â”œâ”€â”€ Conversations.sql
â”‚   â”‚   â”œâ”€â”€ Messages.sql
â”‚   â”‚   â”œâ”€â”€ Leads.sql
â”‚   â”œâ”€â”€ Scripts/                        # Scripts de despliegue
â”‚   â”‚   â”œâ”€â”€ CreateDatabase.ps1         # Crear base de datos
â”‚   â”‚   â”œâ”€â”€ Deploy.ps1                 # Despliegue completo
â”‚   â”‚   â”œâ”€â”€ Publish.ps1                 # Publicar esquema
â”‚   â”‚   â”œâ”€â”€ PreDeployment.sql           # Scripts pre-despliegue
â”‚   â”‚   â””â”€â”€ PostDeployment.sql          # Scripts post-despliegue
â”‚   â””â”€â”€ README.md                       # DocumentaciÃ³n del proyecto
â””â”€â”€ DEPLOY.md                           # GuÃ­a completa de despliegue
```

## ðŸš€ Inicio RÃ¡pido

### Despliegue AutomÃ¡tico (Recomendado)

```powershell
cd database\Auraly.Database\Scripts
.\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "Auraly"
```

### Despliegue Manual

1. **Crear base de datos:**
```powershell
.\CreateDatabase.ps1 -ServerInstance "localhost" -DatabaseName "Auraly"
```

2. **Compilar proyecto** (en Visual Studio o con MSBuild)

3. **Publicar esquema:**
```powershell
.\Publish.ps1 -ServerInstance "localhost" -DatabaseName "Auraly"
```

## ðŸ“Š Esquema de Base de Datos

### Tablas (28 en total)

**Multitenancy y negocio:** Tenants, Businesses, BusinessWhatsAppNumbers, BusinessResources, SystemConfigurations

**Conversaciones:** Conversations, ConversationContexts, ConversationStates, Messages, Leads

**Servicios y reservas:** Services, ServiceAddOnRules, ServiceBundleItems, ServiceResourceUsages, Employees, EmployeeServices, Reservations, ReservationAddOns

**Identidad y auditorÃ­a:** AppUsers, AppRoles, Permissions, UserRoles, RolePermissions, UserExternalLogins, RefreshTokens, AuditLogs

**Pagos:** PaymentTransactions

## ðŸ“– DocumentaciÃ³n

- **[DEPLOY.md](DEPLOY.md)**: GuÃ­a completa de despliegue con todas las opciones
- **[Auraly.Database/README.md](Auraly.Database/README.md)**: DocumentaciÃ³n tÃ©cnica del proyecto

## ðŸ”§ Requisitos

- SQL Server 2019 o superior
- SQL Server Data Tools (SSDT) o Visual Studio con carga de trabajo de base de datos
- PowerShell 5.1 o superior

## ðŸ“ Notas

- **Ya no se usan migraciones de EF** â€“ El esquema se gestiona exclusivamente desde este proyecto
- Este proyecto estÃ¡ en una soluciÃ³n separada para facilitar el despliegue independiente
- Los scripts de PowerShell incluyen manejo de errores y validaciones
- El proyecto usa DACPAC para despliegues incrementales y comparaciÃ³n de esquemas
- Los cambios de esquema se hacen editando los archivos en `Tables/` y desplegando con `Deploy.ps1`

