using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Migración de datos: TransactionStage enum renumerado.
    /// - 4 (ConfirmingBooking) → 5
    /// - 5 (BookingCompleted) → 6
    /// - Nuevo 4 = CompletingProfile (disponibilidad confirmada, faltan campos)
    /// El estado se almacena como JSON en StateJson (camelCase: currentStage).
    /// </summary>
    [Migration("20260218100003_RenumberTransactionStageEnum")]
    public class RenumberTransactionStageEnum : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orden crítico: primero 5→6, luego 4→5 (evitar colisión)
            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.currentStage', 6)
                WHERE JSON_VALUE([StateJson], '$.currentStage') = '5';

                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.currentStage', 5)
                WHERE JSON_VALUE([StateJson], '$.currentStage') = '4';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir: 6→5, 5→4
            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.currentStage', 5)
                WHERE JSON_VALUE([StateJson], '$.currentStage') = '6';

                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.currentStage', 4)
                WHERE JSON_VALUE([StateJson], '$.currentStage') = '5';
            ");
        }
    }
}
