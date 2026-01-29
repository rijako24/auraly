using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InsertAttributeConfigurationExample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationSessions");

            migrationBuilder.DropTable(
                name: "CustomerProfiles");

            migrationBuilder.DropTable(
                name: "SalesInteractions");

            // Insertar configuración de atributos de ejemplo para el BusinessId de ejemplo
            // Esta configuración permite al sistema extraer información específica como edad del bebé, nombre, etc.
            var configJson = @"{
  ""BabyAge"": {
    ""Name"": ""BabyAge"",
    ""DisplayName"": ""Edad del bebé"",
    ""Description"": ""Edad del bebé en meses"",
    ""Type"": ""Number"",
    ""IsRequired"": false,
    ""ValidationPattern"": ""^\\d{1,3}$"",
    ""Metadata"": {
      ""min"": ""0"",
      ""max"": ""120""
    }
  },
  ""BabyName"": {
    ""Name"": ""BabyName"",
    ""DisplayName"": ""Nombre del bebé"",
    ""Description"": ""Nombre del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""minLength"": ""2"",
      ""maxLength"": ""50""
    }
  },
  ""SpecialConditions"": {
    ""Name"": ""SpecialConditions"",
    ""DisplayName"": ""Condiciones especiales"",
    ""Description"": ""Condiciones médicas o especiales del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""maxLength"": ""500""
    }
  }
}";

            // Insertar o actualizar configuración para el BusinessId de ejemplo
            migrationBuilder.Sql($@"
                DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
                DECLARE @ConfigKey INT = 2; -- BusinessConfigurationKey.EntityExtractionConfig

                IF EXISTS (
                    SELECT 1 
                    FROM BusinessConfigurations 
                    WHERE BusinessId = @BusinessId 
                    AND [Key] = @ConfigKey
                )
                BEGIN
                    UPDATE BusinessConfigurations
                    SET Value = N'{configJson.Replace("'", "''")}',
                        UpdatedAt = GETUTCDATE()
                    WHERE BusinessId = @BusinessId 
                    AND [Key] = @ConfigKey;
                END
                ELSE
                BEGIN
                    INSERT INTO BusinessConfigurations (
                        BusinessConfigurationId,
                        BusinessId,
                        [Key],
                        Value,
                        Description,
                        IsActive,
                        CreatedAt,
                        UpdatedAt
                    )
                    VALUES (
                        NEWID(),
                        @BusinessId,
                        @ConfigKey,
                        N'{configJson.Replace("'", "''")}',
                        'Configuración de atributos para extracción de entidades (Baby Spa)',
                        1,
                        GETUTCDATE(),
                        NULL
                    );
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar configuración de atributos insertada
            migrationBuilder.Sql(@"
                DELETE FROM BusinessConfigurations
                WHERE BusinessId = '22222222-2222-2222-2222-222222222222'
                AND [Key] = 2; -- EntityExtractionConfig
            ");

            migrationBuilder.CreateTable(
                name: "ConversationSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppliedTactic = table.Column<int>(type: "int", nullable: true),
                    AvailabilityConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosingAttempts = table.Column<int>(type: "int", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationSummary = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentGoalName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentIntent = table.Column<int>(type: "int", nullable: false),
                    CurrentStage = table.Column<int>(type: "int", nullable: false),
                    CustomerPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DesiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DesiredService = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DesiredTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastBotResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastClosingAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastIntent = table.Column<int>(type: "int", nullable: true),
                    LastInteractionAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUserMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ObjectionsHandledJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ObjectionsRaisedJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    PreviousStage = table.Column<int>(type: "int", nullable: true),
                    StageAttempts = table.Column<int>(type: "int", nullable: false),
                    StageEnteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AverageDecisionTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    AveragePurchaseValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChurnRisk = table.Column<double>(type: "float", nullable: false),
                    CommonObjections = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ConversationsBeforeFirstPurchase = table.Column<int>(type: "int", nullable: false),
                    ConversionProbability = table.Column<double>(type: "float", nullable: false),
                    CustomAttributes = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstContactAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastPurchaseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSuccessfulStage = table.Column<int>(type: "int", nullable: true),
                    LifetimeValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PreferredDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreferredPaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreferredServices = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    PreferredTimeOfDay = table.Column<TimeSpan>(type: "time", nullable: true),
                    RecommendedPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    ServiceInterestScore = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    SuccessfulResponses = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    TotalConversations = table.Column<int>(type: "int", nullable: false),
                    TotalPurchases = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "SalesInteractions",
                columns: table => new
                {
                    InteractionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BotResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DetectedIntent = table.Column<int>(type: "int", nullable: false),
                    InteractionAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MetadataJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ObjectionDetected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    TacticApplied = table.Column<int>(type: "int", nullable: true),
                    Tone = table.Column<int>(type: "int", nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    WasSuccessful = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInteractions", x => x.InteractionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_BusinessId_CustomerPhoneNumber_IsActive",
                table: "ConversationSessions",
                columns: new[] { "BusinessId", "CustomerPhoneNumber", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_ConversationId",
                table: "ConversationSessions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_BusinessId_PhoneNumber",
                table: "CustomerProfiles",
                columns: new[] { "BusinessId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_Segment",
                table: "CustomerProfiles",
                column: "Segment");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInteractions_BusinessId_Stage_InteractionAt",
                table: "SalesInteractions",
                columns: new[] { "BusinessId", "Stage", "InteractionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInteractions_ProfileId",
                table: "SalesInteractions",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInteractions_SessionId",
                table: "SalesInteractions",
                column: "SessionId");
        }
    }
}
