using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega soporte para agrupación de servicios por variantes y estrategia de ventas por tenant.
    ///
    /// Cambios en esquema:
    ///   - Services.GroupKey  (nvarchar(100), nullable): clave de agrupación de variantes
    ///   - Services.Tier      (int, default 0):          nivel de recomendación (0=Base, 1=Premium, 2=Deluxe)
    ///   - Índice en (BusinessId, GroupKey) para consultas de variantes por grupo
    ///
    /// Cambios en datos (Mimos Baby Spa):
    ///   - Asigna GroupKey y Tier a los 7 servicios existentes
    ///   - Actualiza descripciones de planes premium para que el LLM tenga argumentos de venta concretos
    ///   - Inserta la configuración SalesStrategy (key=2) del negocio
    /// </summary>
    public partial class AddServiceTierAndSalesStrategy : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        private const string SvcMarineritos        = "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA";
        private const string SvcAventurasMarinas   = "AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA";
        private const string SvcSuavesMimos        = "AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA";
        private const string SvcCumpleMes1         = "AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA";
        private const string SvcCumpleMes2         = "AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA";
        private const string SvcTallerGrupal       = "AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA";
        private const string SvcClasePersonalizada = "AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnsToServices(migrationBuilder);
            AssignGroupKeysAndTiers(migrationBuilder);
            UpdatePremiumDescriptions(migrationBuilder);
            InsertSalesStrategy(migrationBuilder);
        }

        // ── Columnas nuevas ───────────────────────────────────────────────────────

        private static void AddColumnsToServices(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupKey",
                table: "Services",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "Services",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Services_BusinessId_GroupKey",
                table: "Services",
                columns: new[] { "BusinessId", "GroupKey" });
        }

        // ── Asignación de grupos y tiers ──────────────────────────────────────────
        //
        // Grupos:
        //   "marineritos"     → Plan Marineritos (Base=0) + Cumplemes Marineritos (Premium=1)
        //   "aventuras"       → Plan Aventuras Marinas (Base=0) + Cumplemes Aventuras (Premium=1)
        //   "estimulacion"    → Taller Grupal (Base=0) + Clase Personalizada (Premium=1)
        //
        // Sin grupo (null, independiente):
        //   Plan Suaves Mimos – Post Vacunas  (no tiene variante premium)

        private static void AssignGroupKeysAndTiers(MigrationBuilder migrationBuilder)
        {
            var assignments = new[]
            {
                // GroupKey                 Tier  ServiceId
                ("marineritos",               0,  SvcMarineritos),
                ("marineritos",               1,  SvcCumpleMes1),
                ("aventuras",                 0,  SvcAventurasMarinas),
                ("aventuras",                 1,  SvcCumpleMes2),
                ("estimulacion",              0,  SvcTallerGrupal),
                ("estimulacion",              1,  SvcClasePersonalizada),
            };

            foreach (var (groupKey, tier, serviceId) in assignments)
            {
                migrationBuilder.Sql($@"
                    UPDATE [dbo].[Services]
                    SET [GroupKey] = N'{groupKey}', [Tier] = {tier}, [UpdatedAt] = GETUTCDATE()
                    WHERE [ServiceId] = '{serviceId}';
                ");
            }

            // Plan Suaves Mimos — sin grupo, Tier=0 (default ya aplicado)
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcSuavesMimos}';
            ");
        }

        // ── Descripciones optimizadas para venta ──────────────────────────────────
        //
        // Los planes premium explican qué incluyen DEL PLAN BASE + qué agregan,
        // para que el LLM tenga argumentos concretos de valor.

        private static void UpdatePremiumDescriptions(MigrationBuilder migrationBuilder)
        {
            // ── Cumplemes – Plan Marineritos + Decoración (Premium) ─────────────
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'La experiencia más completa para celebrar el primer añito. Incluye TODO el Plan Marineritos (Estimulación en Baby Gym + Hidroterapia en tinas especiales + Masaje infantil relajante) MÁS una decoración temática que convierte la sesión en una fiesta inolvidable. Opciones de decoración: Bouquet personalizado con número de la edad ($155.000) o Decoración sencilla con globos y número de la edad ($135.000). Por solo $35.000 más que el plan base, el bebé y los papás se llevan un recuerdo único e irrepetible.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes1}';
            ");

            // ── Cumplemes – Plan Aventuras Marinas + Decoración (Premium) ───────
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'La celebración de cumplemes con hidroterapia y masaje más decoración especial. Incluye TODO el Plan Aventuras Marinas (Hidroterapia relajante + Masaje suave) MÁS decoración festiva para hacer el momento único. Opciones: Bouquet personalizado con número de la edad ($135.000) o Decoración con globos y número de la edad ($115.000). Por solo $35.000 más que el plan base, conviertes una sesión de relajación en una celebración completa con fotos dignas de guardar.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes2}';
            ");

            // ── Clase Personalizada de Estimulación Temprana (Premium) ───────────
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'La opción más completa para el desarrollo del bebé: sesión 100% personalizada según sus necesidades específicas. Incluye TODO lo del Taller Grupal PLUS atención exclusiva uno a uno con la terapeuta, ritmo adaptado al bebé y participación activa de los papás como co-terapeutas. Además incorpora estimulación acuática que el taller grupal no incluye. Precios: 1 clase $80.000 | Plan mensual 1 día/sem $270.000 | 2 días/sem $370.000 | 3 días/sem $450.000. Ideal para bebés que necesitan atención diferenciada o papás que quieren máximo impacto en cada sesión.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcClasePersonalizada}';
            ");
        }

        // ── SalesStrategy ─────────────────────────────────────────────────────────

        private static void InsertSalesStrategy(MigrationBuilder migrationBuilder)
        {
            var strategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. Menciona qué INCLUYE DE MÁS la opción premium respecto a la base — usa la descripción para construir argumentos concretos.
3. Enmarca la diferencia de precio como inversión: 'Por solo $35.000 más obtienes decoración + fotos del recuerdo' (no digas 'cuesta más').
4. Si el cliente pregunta por el plan base, informa su precio Y luego di: '¿Sabías que por X pesos más puedes agregarle decoración y hacer del cumplemes algo aún más especial?'
5. Para estimulación: la Clase Personalizada es el diferencial clave — enfatiza la atención individual y la estimulación acuática exclusiva.
6. Si el cliente menciona la edad del bebé, recomienda el plan adecuado para esa etapa Y su versión premium si existe.
7. Nunca presiones: termina con una pregunta abierta ('¿Te gustaría saber más sobre ese plan?' o '¿Reservamos una sesión?').
8. Prioridad de recomendación: Cumplemes Plan Marineritos > Cumplemes Plan Aventuras Marinas > Clase Personalizada > Plan Marineritos > Plan Aventuras > Plan Suaves Mimos > Taller Grupal."
                .Replace("'", "''");

            migrationBuilder.Sql($@"
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[BusinessConfigurations]
                    WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2
                )
                BEGIN
                    INSERT INTO [dbo].[BusinessConfigurations]
                        ([BusinessConfigurationId], [BusinessId], [Key], [Value], [Description], [IsActive], [CreatedAt])
                    VALUES
                        (NEWID(), '{BusinessId}', 2, N'{strategy}',
                         N'Estrategia de recomendación y venta para el asistente virtual', 1, GETUTCDATE());
                END
            ");
        }

        // ── Down ──────────────────────────────────────────────────────────────────

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar SalesStrategy
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[BusinessConfigurations]
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;
            ");

            // Restaurar descripciones originales de los planes premium
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'Celebración de cumplemes con Plan Marineritos completo (Estimulación + Hidroterapia + Masaje) más decoración. Opciones: Bouquet personalizado + número de la edad ($155.000) o Decoración sencilla con globos + número de la edad ($135.000).',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes1}';

                UPDATE [dbo].[Services]
                SET [Description] = N'Celebración de cumplemes con Plan Aventuras Marinas (Hidroterapia + Masaje) más decoración. Opciones: Bouquet personalizado + número de la edad ($135.000) o Decoración sencilla con globos + número de la edad ($115.000).',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes2}';

                UPDATE [dbo].[Services]
                SET [Description] = N'Sesión individual adaptada a las necesidades del bebé. Desarrollo cognitivo, motor, emocional y social con participación activa de los padres. Incluye estimulación acuática. Precios: 1 clase $80.000 | Plan mensual 1 día/sem $270.000 | 2 días/sem $370.000 | 3 días/sem $450.000.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcClasePersonalizada}';
            ");

            // Limpiar GroupKey y Tier
            migrationBuilder.Sql(@"
                UPDATE [dbo].[Services] SET [GroupKey] = NULL, [Tier] = 0;
            ");

            migrationBuilder.DropIndex(
                name: "IX_Services_BusinessId_GroupKey",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "GroupKey",
                table: "Services");
        }
    }
}
