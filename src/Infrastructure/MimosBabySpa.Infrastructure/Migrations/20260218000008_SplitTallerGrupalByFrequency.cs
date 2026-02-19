using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    [Migration("20260218000008_SplitTallerGrupalByFrequency")]
    /// <summary>
    /// Divide el Taller Grupal de Estimulación en servicios por frecuencia de asistencia.
    ///
    /// Cambios en datos (Mimos Baby Spa):
    ///   - 0006: Renombrado a "Taller Grupal - 1 día/semana", GroupKey=taller_grupal, Tier=0, $230.000
    ///   - 0007: Clase Personalizada → standalone (GroupKey=NULL)
    ///   - 0012: Nuevo "Taller Grupal - 2 días/semana", $280.000, Tier=Premium
    ///   - 0013: Nuevo "Taller Grupal - 3 días/semana", $330.000, Tier=Deluxe
    ///   - 0014: Nuevo "Taller Grupal - Clase Individual", $70.000, standalone
    ///   - Descripciones con grupos por edad del bebé (meses) — Opción A
    ///   - ServiceResourceUsages y EmployeeServices para los nuevos servicios
    ///   - SalesStrategy actualizada
    /// </summary>
    public class SplitTallerGrupalByFrequency : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";
        private const string SvcTaller1Dia = "AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA";
        private const string SvcClasePersonalizada = "AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA";
        private const string SvcTaller2Dias = "AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA";
        private const string SvcTaller3Dias = "AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA";
        private const string SvcTallerIndividual = "AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA";
        private const string BabyGymId = "BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB";
        private const string EmpIntegralId = "CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC";

        private const string DescTallerGrupal = "Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad del bebé: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13+ meses). Al reservar, te asignaremos al grupo correcto según la edad del bebé.";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpdateTaller1Dia(migrationBuilder);
            UpdateClasePersonalizada(migrationBuilder);
            InsertNewServices(migrationBuilder);
            InsertServiceResourceUsages(migrationBuilder);
            InsertEmployeeServices(migrationBuilder);
            UpdateSalesStrategy(migrationBuilder);
        }

        private static void UpdateTaller1Dia(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET
                    [ServiceName]     = N'Taller Grupal - 1 día/semana',
                    [Description]     = N'{DescTallerGrupal.Replace("'", "''")}',
                    [DurationMinutes] = 60,
                    [Price]           = 230000,
                    [GroupKey]        = N'taller_grupal',
                    [Tier]            = 0,
                    [UpdatedAt]       = GETUTCDATE()
                WHERE [ServiceId] = '{SvcTaller1Dia}';
            ");
        }

        private static void UpdateClasePersonalizada(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [GroupKey] = NULL, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcClasePersonalizada}';
            ");
        }

        private static void InsertNewServices(MigrationBuilder migrationBuilder)
        {
            var desc = DescTallerGrupal.Replace("'", "''");
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = '{SvcTaller2Dias}')
                INSERT INTO [dbo].[Services]
                    ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
                     [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
                VALUES
                    ('{SvcTaller2Dias}', '{BusinessId}', N'Taller Grupal - 2 días/semana', N'{desc}',
                     60, 280000, 1, N'taller_grupal', 1, GETUTCDATE());

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = '{SvcTaller3Dias}')
                INSERT INTO [dbo].[Services]
                    ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
                     [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
                VALUES
                    ('{SvcTaller3Dias}', '{BusinessId}', N'Taller Grupal - 3 días/semana', N'{desc}',
                     60, 330000, 1, N'taller_grupal', 2, GETUTCDATE());

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = '{SvcTallerIndividual}')
                INSERT INTO [dbo].[Services]
                    ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
                     [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
                VALUES
                    ('{SvcTallerIndividual}', '{BusinessId}', N'Taller Grupal - Clase Individual', N'{desc}',
                     60, 70000, 1, NULL, 0, GETUTCDATE());
            ");
        }

        private static void InsertServiceResourceUsages(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = '{SvcTaller2Dias}' AND [BusinessResourceId] = '{BabyGymId}')
                    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
                    VALUES (NEWID(), '{SvcTaller2Dias}', '{BabyGymId}', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = '{SvcTaller3Dias}' AND [BusinessResourceId] = '{BabyGymId}')
                    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
                    VALUES (NEWID(), '{SvcTaller3Dias}', '{BabyGymId}', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = '{SvcTallerIndividual}' AND [BusinessResourceId] = '{BabyGymId}')
                    INSERT INTO [dbo].[ServiceResourceUsages] ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
                    VALUES (NEWID(), '{SvcTallerIndividual}', '{BabyGymId}', 1);
            ");
        }

        private static void InsertEmployeeServices(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = '{EmpIntegralId}' AND [ServiceId] = '{SvcTaller2Dias}')
                    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
                    VALUES (NEWID(), '{EmpIntegralId}', '{SvcTaller2Dias}', GETUTCDATE());

                IF NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = '{EmpIntegralId}' AND [ServiceId] = '{SvcTaller3Dias}')
                    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
                    VALUES (NEWID(), '{EmpIntegralId}', '{SvcTaller3Dias}', GETUTCDATE());

                IF NOT EXISTS (SELECT 1 FROM [dbo].[EmployeeServices] WHERE [EmployeeId] = '{EmpIntegralId}' AND [ServiceId] = '{SvcTallerIndividual}')
                    INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
                    VALUES (NEWID(), '{EmpIntegralId}', '{SvcTallerIndividual}', GETUTCDATE());
            ");
        }

        private static void UpdateSalesStrategy(MigrationBuilder migrationBuilder)
        {
            var strategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. El catálogo muestra la composición exacta de cada bundle (qué incluye). Úsala para argumentar. NUNCA inventes diferencias que no estén en la composición listada.
3. La diferencia entre variantes del mismo grupo es SOLO el componente extra (decoración, personalización, o frecuencia), NO las actividades base — esas son siempre iguales.
4. Enmarca la diferencia de precio como inversión: ''Por solo $X más obtienes [componente extra]''.
5. Para Taller Grupal: recomienda primero 3 días/semana (Deluxe), luego 2 días (Premium) o 1 día (Base). Más frecuencia = más impacto en el desarrollo del bebé.
6. Para estimulación: la Clase Personalizada agrega atención 1 a 1 y estimulación acuática — son diferencias reales, úsalas. Si preguntan por Taller Grupal, menciona también la Clase Individual ($70.000) como opción para probar una clase.
7. Si el cliente menciona la edad del bebé, indica el grupo correspondiente (Estrellitas de Mar, Pulpitos, Cangrejitos, Tiburoncitos 1 o 2) y recomienda el plan adecuado.
8. Nunca presiones: termina con una pregunta abierta.
9. Prioridad de recomendación por grupo: Cumplemes+Bouquet > Cumplemes+Sencilla > Plan base. Taller Grupal 3 días > 2 días > 1 día. Clase Personalizada y Taller Individual según necesidad."
                .Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{strategy}', [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar EmployeeServices de los nuevos servicios
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[EmployeeServices]
                WHERE [ServiceId] IN ('{SvcTaller2Dias}', '{SvcTaller3Dias}', '{SvcTallerIndividual}')
                  AND [EmployeeId] = '{EmpIntegralId}';
            ");

            // Eliminar ServiceResourceUsages de los nuevos servicios
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[ServiceResourceUsages]
                WHERE [ServiceId] IN ('{SvcTaller2Dias}', '{SvcTaller3Dias}', '{SvcTallerIndividual}');
            ");

            // Eliminar los 3 servicios nuevos
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[Services]
                WHERE [ServiceId] IN ('{SvcTaller2Dias}', '{SvcTaller3Dias}', '{SvcTallerIndividual}');
            ");

            // Restaurar Taller Grupal original (0006)
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET
                    [ServiceName]     = N'Taller Grupal de Estimulación Temprana',
                    [Description]     = N'Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad: Estrellitas de Mar (2-4m), Pulpitos (4-7m), Cangrejitos (7-10m), Tiburoncitos 1 (10-13m), Tiburoncitos 2 (13m+). Precios: Clase individual $70.000 | Plan mensual 1 día/sem $230.000 | 2 días/sem $280.000 | 3 días/sem $330.000.',
                    [DurationMinutes] = 60,
                    [Price]           = 70000,
                    [GroupKey]        = N'estimulacion',
                    [Tier]            = 0,
                    [UpdatedAt]       = GETUTCDATE()
                WHERE [ServiceId] = '{SvcTaller1Dia}';
            ");

            // Restaurar Clase Personalizada al grupo estimulacion
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [GroupKey] = N'estimulacion', [Tier] = 1, [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcClasePersonalizada}';
            ");

            // Restaurar SalesStrategy anterior (versión de 007)
            var strategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. El catálogo muestra la composición exacta de cada bundle (qué incluye). Úsala para argumentar. NUNCA inventes diferencias que no estén en la composición listada.
3. La diferencia entre variantes del mismo grupo es SOLO el componente extra (decoración, personalización), NO las actividades base — esas son siempre iguales.
4. Enmarca la diferencia de precio como inversión: ''Por solo $X más obtienes [componente extra]''.
5. Para estimulación: la Clase Personalizada agrega atención 1 a 1 y estimulación acuática — son diferencias reales, úsalas.
6. Si el cliente menciona la edad del bebé, recomienda el plan adecuado y su versión de mayor tier.
7. Nunca presiones: termina con una pregunta abierta.
8. Prioridad de recomendación por grupo: Cumplemes+Bouquet > Cumplemes+Sencilla > Plan base."
                .Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{strategy}', [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;
            ");
        }
    }
}
