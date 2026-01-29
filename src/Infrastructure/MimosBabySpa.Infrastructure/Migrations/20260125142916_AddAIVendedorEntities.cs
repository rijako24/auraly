using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAIVendedorEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentStage = table.Column<int>(type: "int", nullable: false),
                    PreviousStage = table.Column<int>(type: "int", nullable: true),
                    StageEnteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StageAttempts = table.Column<int>(type: "int", nullable: false),
                    CurrentIntent = table.Column<int>(type: "int", nullable: false),
                    LastIntent = table.Column<int>(type: "int", nullable: true),
                    LastUserMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastBotResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastInteractionAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DesiredService = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DesiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DesiredTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    AvailabilityConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    CurrentGoalName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppliedTactic = table.Column<int>(type: "int", nullable: true),
                    ClosingAttempts = table.Column<int>(type: "int", nullable: false),
                    LastClosingAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectionsRaisedJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ObjectionsHandledJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConversationSummary = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false)
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
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    LifetimeValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalPurchases = table.Column<int>(type: "int", nullable: false),
                    TotalConversations = table.Column<int>(type: "int", nullable: false),
                    BabyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BabyAgeMonths = table.Column<int>(type: "int", nullable: true),
                    BabyConditions = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    PreferredServices = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ServiceInterestScore = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    PreferredTimeOfDay = table.Column<TimeSpan>(type: "time", nullable: true),
                    PreferredDays = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AveragePurchaseValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AverageDecisionTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    ConversationsBeforeFirstPurchase = table.Column<int>(type: "int", nullable: false),
                    PreferredPaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommonObjections = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    SuccessfulResponses = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ConversionProbability = table.Column<double>(type: "float", nullable: false),
                    ChurnRisk = table.Column<double>(type: "float", nullable: false),
                    RecommendedPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSuccessfulStage = table.Column<int>(type: "int", nullable: true),
                    FirstContactAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastContactAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastPurchaseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CustomAttributes = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true)
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
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InteractionAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    TacticApplied = table.Column<int>(type: "int", nullable: true),
                    Tone = table.Column<int>(type: "int", nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BotResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DetectedIntent = table.Column<int>(type: "int", nullable: false),
                    WasSuccessful = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ObjectionDetected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MetadataJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationSessions");

            migrationBuilder.DropTable(
                name: "CustomerProfiles");

            migrationBuilder.DropTable(
                name: "SalesInteractions");
        }
    }
}
