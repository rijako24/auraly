# Implementación Escalable de Recursos y Servicios

## Resumen

Se ha refactorizado completamente el sistema de recursos y disponibilidad para que sea **100% escalable y genérico**, permitiendo que cualquier tipo de negocio configure sus propios recursos, servicios y reglas de coexistencia sin necesidad de modificar código.

## Arquitectura Escalable

### Modelo de Datos

El sistema ahora usa **4 tablas principales** en lugar de valores hardcodeados:

1. **BusinessResources** - Recursos disponibles por negocio
   - `ResourceName` (ej: "Baby Gym", "Hidroterapia", "Masaje")
   - `Quantity` (cantidad disponible del recurso)

2. **Services** - Servicios ofrecidos por negocio
   - `ServiceName` (ej: "Marineritos", "Aventuras Marinas")
   - `DurationMinutes` (duración del servicio)
   - `IsActive` (si el servicio está activo)

3. **ServiceResourceUsages** - Uso de recursos por servicio
   - Relación muchos-a-muchos entre Services y BusinessResources
   - `Quantity` (cantidad del recurso que usa el servicio)

4. **ServiceCoexistenceRules** - Reglas explícitas de coexistencia
   - Define qué servicios pueden coexistir en el mismo horario
   - `CanCoexist` (true/false para permitir o prohibir explícitamente)
   - **Permite reglas donde ServiceId1 = ServiceId2** para múltiples reservas del mismo servicio

## Ventajas de la Nueva Arquitectura

### ✅ Escalabilidad Total
- Cada negocio define sus propios recursos
- Cada negocio define sus propios servicios
- No hay límites en la cantidad de recursos o servicios
- Fácil agregar nuevos negocios sin modificar código

### ✅ Flexibilidad
- Reglas de coexistencia configurables por negocio (incluyendo mismo servicio consigo mismo)
- Recursos con cantidades variables
- Permite múltiples reservas del mismo servicio mediante reglas explícitas

### ✅ Mantenibilidad
- Toda la configuración está en base de datos
- Cambios de reglas sin deploy de código
- Fácil auditar y modificar reglas de negocio

## Archivos Creados

### Entidades de Dominio
1. `BusinessResource.cs` - Recurso del negocio
2. `Service.cs` - Servicio del negocio
3. `ServiceResourceUsage.cs` - Uso de recursos por servicio
4. `ServiceCoexistenceRule.cs` - Regla de coexistencia

### Repositorios
1. `IServiceRepository.cs` / `ServiceRepository.cs`
2. `IBusinessResourceRepository.cs` / `BusinessResourceRepository.cs`
3. `IServiceCoexistenceRuleRepository.cs` / `ServiceCoexistenceRuleRepository.cs`

### Scripts SQL
1. `BusinessResources.sql` - Tabla de recursos
2. `Services.sql` - Tabla de servicios
3. `ServiceResourceUsages.sql` - Tabla de uso de recursos
4. `ServiceCoexistenceRules.sql` - Tabla de reglas de coexistencia
5. `SeedDefaultResources.sql` - Script para poblar datos por defecto

### Servicios Refactorizados
1. `ResourceConfigurationService.cs` - Ahora lee de BD en lugar de valores hardcodeados
2. `AvailabilityService.cs` - Usa modelo de recursos desde BD y verifica reglas de coexistencia (incluyendo mismo servicio)

## Flujo de Disponibilidad Actualizado

```
Usuario menciona fecha/hora
    ↓
Backend detecta fecha/hora
    ↓
Backend obtiene modelo de recursos desde BD:
  - Recursos disponibles del negocio
  - Servicios activos del negocio
  - Uso de recursos por servicio
  - Reglas de coexistencia
    ↓
Backend verifica solapamiento temporal
    ↓
Si hay solapamiento → Backend verifica:
  1. ¿Es el mismo servicio?
     - Buscar regla en ServiceCoexistenceRules donde ServiceId1 = ServiceId2
     - Si no hay regla → NO DISPONIBLE (por defecto)
     - Si hay regla y CanCoexist = true → Continuar verificación
  2. ¿Pueden coexistir según reglas? (servicios diferentes)
     - Buscar en ServiceCoexistenceRules
     - Si no hay regla → NO DISPONIBLE (por defecto)
  3. ¿Hay recursos suficientes?
     - Sumar uso de recursos de ambos servicios
     - Verificar contra recursos disponibles
     - Si no hay suficientes → NO DISPONIBLE
    ↓
Backend retorna is_available = true/false
    ↓
Modelo solo presenta el resultado
```

## Múltiples Reservas del Mismo Servicio

Para permitir múltiples reservas del mismo servicio en el mismo horario, crear una regla de coexistencia donde `ServiceId1 = ServiceId2`:

```sql
-- Ejemplo: Permitir múltiples reservas de "Clase Grupal" simultáneamente
INSERT INTO ServiceCoexistenceRules (BusinessId, ServiceId1, ServiceId2, CanCoexist)
VALUES (@BusinessId, @ClaseGrupalId, @ClaseGrupalId, 1);
```

**Comportamiento por defecto:**
- Sin regla explícita: NO se permiten múltiples reservas del mismo servicio
- Con regla `ServiceId1 = ServiceId2` y `CanCoexist = true`: Se permiten múltiples reservas (si hay recursos suficientes)

## Configuración de un Nuevo Negocio

### Paso 1: Crear Recursos
```sql
INSERT INTO BusinessResources (BusinessId, ResourceName, Quantity)
VALUES 
  (@BusinessId, 'Recurso 1', 2),
  (@BusinessId, 'Recurso 2', 3);
```

### Paso 2: Crear Servicios
```sql
INSERT INTO Services (BusinessId, ServiceName, DurationMinutes)
VALUES 
  (@BusinessId, 'Servicio A', 60),
  (@BusinessId, 'Servicio B', 90);
```

### Paso 3: Asignar Recursos a Servicios
```sql
INSERT INTO ServiceResourceUsages (ServiceId, BusinessResourceId, Quantity)
VALUES 
  (@ServicioAId, @Recurso1Id, 1),
  (@ServicioAId, @Recurso2Id, 2);
```

### Paso 4: Definir Reglas de Coexistencia
```sql
-- Entre servicios diferentes
INSERT INTO ServiceCoexistenceRules (BusinessId, ServiceId1, ServiceId2, CanCoexist)
VALUES 
  (@BusinessId, @ServicioAId, @ServicioBId, 1); -- Pueden coexistir

-- Para permitir múltiples reservas del mismo servicio (ej: clases grupales)
INSERT INTO ServiceCoexistenceRules (BusinessId, ServiceId1, ServiceId2, CanCoexist)
VALUES 
  (@BusinessId, @ServicioBId, @ServicioBId, 1); -- Servicio B puede tener múltiples reservas simultáneas
```

## Migración

### 1. Ejecutar Scripts SQL
```sql
-- En orden:
1. BusinessResources.sql
2. Services.sql
3. ServiceResourceUsages.sql
4. ServiceCoexistenceRules.sql
5. SeedDefaultResources.sql (opcional, para datos de ejemplo)
```

### 2. Crear Migración de Entity Framework
```bash
dotnet ef migrations add AddResourceManagementTables --project src/Infrastructure/MimosBabySpa.Infrastructure
dotnet ef database update --project src/Infrastructure/MimosBabySpa.Infrastructure
```

### 3. Registrar Repositorios en DI
Los repositorios ya están registrados automáticamente a través de `UnitOfWork`.

## Ejemplos de Uso

### Ejemplo 1: Negocio de Spa (Baby Spa)
- Recursos: Baby Gym (1), Hidroterapia (2), Masaje (2)
- Servicios: Marineritos, Aventuras Marinas, Suaves Mimos, Clase Grupal
- Reglas: Ver `SeedDefaultResources.sql`

### Ejemplo 2: Negocio de Peluquería
- Recursos: Silla 1 (1), Silla 2 (1), Estación de Lavado (2)
- Servicios: Corte, Peinado, Tinte, Tratamiento
- Reglas: Corte + Peinado pueden coexistir, pero no dos Cortes simultáneos (sin regla ServiceId1 = ServiceId2)

### Ejemplo 3: Negocio de Gimnasio
- Recursos: Cancha (1), Máquinas (10), Salón de Clases (1)
- Servicios: Entrenamiento Personal, Clase Grupal, Uso Libre
- Reglas: 
  - Clase Grupal puede tener múltiples reservas (regla: ServiceId1 = ServiceId2 = ClaseGrupalId, CanCoexist = true)
  - Entrenamiento Personal + Uso Libre pueden coexistir

## Beneficios para SaaS

✅ **Multi-tenant**: Cada negocio tiene su propia configuración
✅ **Sin código hardcodeado**: Todo configurable desde BD
✅ **Escalable**: Agregar nuevos negocios sin límites
✅ **Flexible**: Diferentes tipos de negocios con diferentes reglas
✅ **Mantenible**: Cambios de reglas sin deploy

## Próximos Pasos

1. ✅ Crear migración de Entity Framework
2. ✅ Ejecutar scripts SQL
3. ✅ Poblar datos iniciales con `SeedDefaultResources.sql`
4. ⏳ Crear API/admin para gestionar recursos y servicios
5. ⏳ Agregar validaciones en creación de reglas
6. ⏳ Implementar caché para modelo de recursos (performance)

## Notas Técnicas

- Los repositorios incluyen `Include()` para eager loading de relaciones
- Los índices únicos previenen duplicados
- Las foreign keys con `ON DELETE RESTRICT` previenen eliminaciones accidentales
- El campo `IsActive` permite desactivar servicios sin eliminarlos
- **Eliminado campo `AllowSameHourReservation`**: Ahora se usa `ServiceCoexistenceRules` con `ServiceId1 = ServiceId2` para el mismo propósito
