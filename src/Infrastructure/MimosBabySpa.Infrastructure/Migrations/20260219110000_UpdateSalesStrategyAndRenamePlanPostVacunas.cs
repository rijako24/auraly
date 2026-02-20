using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Actualiza la SalesStrategy (key=2) con reglas de edad explícitas y nuevo nombre del plan.
    /// Renombra "Plan Suaves Mimos – Post Vacunas" a "Plan Post Vacunas" para evitar content filter de Azure.
    /// Solo datos; sin cambios de esquema.
    /// </summary>
    [Migration("20260219110000_UpdateSalesStrategyAndRenamePlanPostVacunas")]
    public partial class UpdateSalesStrategyAndRenamePlanPostVacunas : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";
        private const string SvcPostVacunas = "AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var strategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Sin excepciones.
2. Menciona qué INCLUYE DE MÁS respecto a opciones base — usa la descripción para argumentos concretos.
3. Enmarca la diferencia de precio como inversión, nunca como gasto.
4. Reglas por edad del bebé:
   - 0-2 meses: Plan Aventuras Marinas. Ofrecer Plan Post Vacunas solo si mencionan vacunación.
   - 3-6 meses: Plan Marineritos (incluye estimulación + hidro + masaje). Alternativa: Aventuras Marinas.
   - 6+ meses: Plan Marineritos (la experiencia más completa para esta etapa).
   Incluso con estas reglas, siempre presenta primero la opción de mayor tier disponible para la edad.
5. Para estimulación: Clase Personalizada es el diferencial — enfatiza atención individual y estimulación acuática.
6. Prioridad de recomendación: Cumplemes Marineritos > Cumplemes Aventuras > Clase Personalizada > Plan Marineritos > Plan Aventuras > Plan Post Vacunas > Taller Grupal."
                .Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{strategy}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;

                UPDATE [dbo].[Services]
                SET [ServiceName] = N'Plan Post Vacunas',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcPostVacunas}';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var originalStrategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

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
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{originalStrategy}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;

                UPDATE [dbo].[Services]
                SET [ServiceName] = N'Plan Suaves Mimos – Post Vacunas',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcPostVacunas}';
            ");
        }
    }
}
