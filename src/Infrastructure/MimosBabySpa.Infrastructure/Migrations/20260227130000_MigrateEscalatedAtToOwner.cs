using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Migra EscalatedAt → Owner + LastEscalatedAt en StateJson.
    /// - Si escalatedAt tenía valor: owner=Human, lastEscalatedAt=ese valor.
    /// - Si no: owner=Bot.
    /// - Elimina escalatedAt.
    /// </summary>
    public partial class MigrateEscalatedAtToOwner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server 2016+: JSON_MODIFY. ConversationStates tiene StateJson (nvarchar).
            // Caso 1: escalatedAt presente y no null → owner=Human, lastEscalatedAt=valor, quitar escalatedAt
            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY(
                    JSON_MODIFY(
                        JSON_MODIFY([StateJson], '$.owner', 'Human'),
                        '$.lastEscalatedAt',
                        JSON_VALUE([StateJson], '$.escalatedAt')
                    ),
                    '$.escalatedAt',
                    NULL
                )
                WHERE JSON_VALUE([StateJson], '$.escalatedAt') IS NOT NULL;
            ");

            // Caso 2: owner ausente → owner=Bot (para todos los que no lo tienen)
            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.owner', 'Bot')
                WHERE JSON_VALUE([StateJson], '$.owner') IS NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reversible: descartar owner/lastEscalatedAt y restaurar escalatedAt donde lastEscalatedAt exista
            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY(
                    JSON_MODIFY([StateJson], '$.escalatedAt', JSON_VALUE([StateJson], '$.lastEscalatedAt')),
                    '$.owner',
                    NULL
                )
                WHERE JSON_VALUE([StateJson], '$.lastEscalatedAt') IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE [dbo].[ConversationStates]
                SET [StateJson] = JSON_MODIFY([StateJson], '$.owner', NULL)
                WHERE JSON_VALUE([StateJson], '$.owner') IS NOT NULL;
            ");
        }
    }
}
