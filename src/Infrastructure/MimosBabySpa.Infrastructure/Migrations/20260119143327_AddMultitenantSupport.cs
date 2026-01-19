using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultitenantSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_UserNumber",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserNumber",
                table: "Conversations");

            // Definir el BusinessId que se usará para datos existentes
            var defaultBusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: defaultBusinessId);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: defaultBusinessId);

            migrationBuilder.CreateTable(
                name: "SystemConfigurations",
                columns: table => new
                {
                    SystemConfigurationId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigurations", x => x.SystemConfigurationId);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "Businesses",
                columns: table => new
                {
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.BusinessId);
                    table.ForeignKey(
                        name: "FK_Businesses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessConfigurations",
                columns: table => new
                {
                    BusinessConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessConfigurations", x => x.BusinessConfigurationId);
                    table.ForeignKey(
                        name: "FK_BusinessConfigurations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessWhatsAppNumbers",
                columns: table => new
                {
                    BusinessWhatsAppNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WhatsAppPhoneNumberId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WhatsAppAccessToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessWhatsAppNumbers", x => x.BusinessWhatsAppNumberId);
                    table.ForeignKey(
                        name: "FK_BusinessWhatsAppNumbers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_BusinessId_UserNumber",
                table: "Leads",
                columns: new[] { "BusinessId", "UserNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BusinessId_UserNumber",
                table: "Conversations",
                columns: new[] { "BusinessId", "UserNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessConfigurations_BusinessId",
                table: "BusinessConfigurations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessConfigurations_BusinessId_Key",
                table: "BusinessConfigurations",
                columns: new[] { "BusinessId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_TenantId",
                table: "Businesses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessWhatsAppNumbers_BusinessId",
                table: "BusinessWhatsAppNumbers",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessWhatsAppNumbers_WhatsAppPhoneNumberId",
                table: "BusinessWhatsAppNumbers",
                column: "WhatsAppPhoneNumberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Email",
                table: "Tenants",
                column: "Email",
                unique: true);

            // Insertar datos iniciales ANTES de agregar las foreign keys
            var now = DateTime.UtcNow;
            
            migrationBuilder.InsertData(
                table: "SystemConfigurations",
                columns: new[] { "SystemConfigurationId", "Value", "Description", "CreatedAt", "IsActive" },
                values: new object[,]
                {
                    {
                        1, // ToneAndStyle
                        @"TONO Y ESTILO:
- Habla de forma NATURAL y CONVERSACIONAL, como una persona real, no como un robot
- Sé EMPÁTICA y CÁLIDA, muestra interés genuino por el bebé
- Usa lenguaje COTIDIANO y AMIGABLE, evita sonar formal o corporativo
- Explica las cosas de forma CONVERSACIONAL, NO uses listas numeradas o viñetas estructuradas
- Da la información de forma FLUIDA y NATURAL, como si estuvieras platicando con una amiga
- Usa emojis de forma NATURAL (👶🛁💆✨) pero sin exagerar
- Varía tus respuestas, no uses siempre las mismas frases
- NO siempre cierres con una pregunta - a veces solo responde, otras veces pregunta, varía
- Cuando expliques planes, hazlo de forma narrativa y conversacional, NO como lista de características
- Evita frases como ""Incluye:"", ""Características:"", ""Beneficios:"" - mejor integra la información en el texto de forma natural",
                        "Configuración de tono y estilo para todos los asistentes del sistema",
                        now,
                        true
                    },
                    {
                        2, // DefaultGreeting
                        "Hola, ¿en qué puedo ayudarte hoy?",
                        "Saludo por defecto",
                        now,
                        true
                    },
                    {
                        3, // DefaultFarewell
                        "¡Gracias por contactarnos! Estamos aquí cuando nos necesites.",
                        "Despedida por defecto",
                        now,
                        true
                    },
                    {
                        4, // MaxConversationHistory
                        "5",
                        "Número máximo de mensajes del historial a incluir en el contexto",
                        now,
                        true
                    },
                    {
                        5, // DefaultTemperature
                        "0.7",
                        "Temperatura por defecto para las respuestas de IA",
                        now,
                        true
                    },
                    {
                        6, // DefaultMaxTokens
                        "500",
                        "Número máximo de tokens por defecto para las respuestas de IA",
                        now,
                        true
                    },
                    {
                        7, // IntentClassificationPrompt
                        @"Clasifica el siguiente mensaje en UNA de estas intenciones:
- Greeting: Saludo inicial
- AskAge: Pregunta por edad del bebé
- AskInfo: Pregunta sobre el spa (horarios, ubicación, seguridad)
- AskPrice: Pregunta sobre planes o precios
- Objecion: Dudas o miedos
- ReservationRequest: Quiere reservar
- TalkToHuman: Pide hablar con humano
- FollowUp: Continuación de conversación

Mensaje: ""{messageText}""

Responde SOLO con el nombre de la intención, sin explicaciones.",
                        "Prompt para clasificación de intenciones de mensajes",
                        now,
                        true
                    },
                    {
                        7, // ContextExtractionPrompt
                        @"Analiza el siguiente mensaje del cliente y realiza DOS tareas:

TAREA 1 - CLASIFICAR INTENCIÓN:
Clasifica el siguiente mensaje en UNA de estas intenciones:

{intentDefinitions}

Mensaje: ""{messageText}""

Responde SOLO con el nombre de la intención, sin explicaciones.

TAREA 2 - EXTRAER CONTEXTO:
Del mismo mensaje, extrae SOLO la información IMPORTANTE que debe guardarse para futuras conversaciones según las siguientes instrucciones:

{contextData}

Información del negocio (para contexto):
{generalInfo}

Reglas de planes (si aplica):
{planRules}

INSTRUCCIONES PARA EXTRAER CONTEXTO:
1. Analiza el mensaje cuidadosamente y extrae únicamente la información relevante según las instrucciones proporcionadas
2. Si el mensaje menciona edad (meses o años), extrae la edad y conviértela a meses si es necesario para mantener consistencia
3. Si existen reglas de planes y se menciona edad, determina el plan recomendado aplicando las reglas correspondientes
4. Extrae SOLO información que sea RELEVANTE y ÚTIL para futuras conversaciones, evitando datos temporales o irrelevantes
5. NO extraigas información temporal, superficial o que no aporte valor al contexto de la conversación
6. Formatea cada dato extraído como una oración natural y completa, fácil de entender
7. El contexto debe ser SOLO una lista de strings, donde cada string es una oración completa con información relevante extraída del mensaje

Responde SOLO en formato JSON con esta estructura:
{{
  ""intent"": ""NombreDeLaIntencion"",
  ""context"": [
    ""El bebé tiene 24 meses"",
    ""El plan recomendado es Plan Marineritos"",
    ""Quiere venir el sábado""
  ]
}}

IMPORTANTE: El campo ""context"" es SOLO una lista de strings (array de strings). Cada elemento del array es una oración completa con información relevante extraída del mensaje.

Si no hay información relevante para el contexto, el array ""context"" debe estar vacío: []

Ejemplo si el mensaje dice ""mi bebé tiene 2 años"":
{{
  ""intent"": ""AskAge"",
  ""context"": [
    ""El bebé tiene 24 meses"",
    ""El plan recomendado es Plan Marineritos""
  ]
}}",
                        "Prompt unificado para clasificación de intención y extracción de contexto",
                        now,
                        true
                    }
                });

            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "TenantId", "Name", "Email", "IsActive", "CreatedAt" },
                values: new object[] { tenantId, "Mimos Baby Spa", "contacto@mimosbabyspa.com", true, now });

            // Insertar Business de ejemplo
            var businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            migrationBuilder.InsertData(
                table: "Businesses",
                columns: new[] { "BusinessId", "TenantId", "Name", "IsActive", "CreatedAt" },
                values: new object[] { businessId, tenantId, "Mimos Baby Spa - Valledupar", true, now });

            // Insertar BusinessWhatsAppNumber (valores placeholder - deben actualizarse con valores reales)
            migrationBuilder.InsertData(
                table: "BusinessWhatsAppNumbers",
                columns: new[] { "BusinessWhatsAppNumberId", "BusinessId", "PhoneNumber", "WhatsAppPhoneNumberId", "WhatsAppAccessToken", "IsActive", "CreatedAt" },
                values: new object[] 
                { 
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    businessId,
                    "+573194823017",
                    "<WHATSAPP_PHONE_NUMBER_ID>", // Debe actualizarse con el valor real
                    "<WHATSAPP_ACCESS_TOKEN>", // Debe actualizarse con el valor real
                    true,
                    now
                });

            // Insertar BusinessConfiguration con datos del GetSystemPrompt viejo usando SQL directo
            var configId0 = Guid.Parse("44444444-4444-4444-4444-444444444440"); // Persona (primera)
            var configId1 = Guid.Parse("44444444-4444-4444-4444-444444444444"); // Objective
            var configId2 = Guid.Parse("55555555-5555-5555-5555-555555555555"); // GeneralInformation
            var configId3 = Guid.Parse("66666666-6666-6666-6666-666666666666"); // BusinessRules
            var configId9 = Guid.Parse("99999999-9999-9999-9999-999999999999"); // ContextData
            var configId10 = Guid.Parse("10101010-1010-1010-1010-101010101010"); // PlanRules
            
            var personaValue = @"Eres María, una asesora comercial experta y muy humana de Mimos Baby Spa, especializada en servicios de spa para bebés. Hablas de forma natural, cálida y conversacional, como si fueras una amiga que conoce mucho sobre el cuidado de bebés.";
            
            var objectiveValue = @"Crear una conexión genuina con los padres y ayudarlos a encontrar el plan perfecto para su bebé, convirtiendo la conversación en una reserva real.";
            var generalInfoValue = @"Mimos Baby Spa es un centro especializado en el bienestar y desarrollo integral de bebés, enfocado en:
- Hidroterapia en tinas especiales para bebés
- Masaje infantil
- Estimulación temprana
- Cumplemes y celebraciones
- Talleres grupales de estimulación temprana
- Clases personalizadas de estimulación temprana

📍 Ubicación: Cra 13 #9C-19, Barrio San Joaquín, Valledupar – Cesar, Colombia
📞 WhatsApp: 319-482-3017

🌊 PLANES DE SPA PARA BEBÉS:

🟦 PLAN MARINERITOS
- Duración: 60 minutos
- Incluye 3 estaciones:
  1. Estimulación temprana en Baby Gym: Actividades diseñadas para fomentar el desarrollo motor, cognitivo y social del bebé
  2. Hidroterapia en tinas especiales: Uso de tinas adaptadas para bebés y niños, en un entorno seguro y controlado
  3. Masaje infantil: Masaje relajante que ayuda a mejorar la circulación, aliviar tensiones y fortalecer el vínculo entre padres y bebé

🟦 PLAN AVENTURAS MARINAS
- Duración: 45 minutos
- Incluye 2 estaciones:
  1. Hidroterapia en tinas especiales: Sesión relajante que aprovecha la flotación y el movimiento en el agua
  2. Masaje infantil: Masaje suave para relajar y consentir al bebé

🟦 PLAN SUAVES MIMOS – POST VACUNAS
- Duración: 45 minutos
- Incluye:
  1. Hidroterapia en tinas especiales: El agua tibia ayuda a relajar los músculos y calmar molestias posteriores a la vacunación
  2. Masaje infantil suave: Ayuda a calmar al bebé y promover un sueño reparador (⚠️ No se toca la zona de punción)
- Beneficios: Reducción de molestias e inflamación, mejora del estado de ánimo, relajación y reducción del estrés

🎉 PLANES CUMPLEMES:

Opción 1 – Plan Marineritos + Decoración
- Incluye: Estimulación temprana en Baby Gym + Hidroterapia + Masaje infantil + Decoración
- Precios:
  • Decoración con bouquet personalizado + número de la edad: $155.000
  • Decoración sencilla con globos + número de la edad: $135.000

Opción 2 – Plan Aventuras Marinas + Decoración
- Incluye: Hidroterapia + Masaje infantil + Decoración
- Precios:
  • Decoración con bouquet personalizado + número de la edad: $135.000
  • Decoración sencilla con globos + número de la edad: $115.000

👶 TALLERES GRUPALES DE ESTIMULACIÓN TEMPRANA:

Diseñados para apoyar el desarrollo integral de bebés y niños mediante actividades lúdicas, sensoriales y físicas.

Objetivos: Desarrollo motor, cognitivo y social; fortalecer el vínculo afectivo padres-hijos; socialización en entorno grupal; aprendizaje a través del juego

Estructura:
- Duración: 45 a 60 minutos
- Frecuencia: semanal, jornada de la tarde
- Opciones: 1, 2 o 3 veces por semana
- Grupos organizados por edad y etapa de desarrollo

Organización por edades:
- Estrellitas de Mar: 2 a 4 meses
- Pulpitos: 4 a 7 meses
- Cangrejitos: 7 a 10 meses
- Tiburoncitos 1: 10 a 13 meses
- Tiburoncitos 2: 13 meses en adelante

Metodología: Baby Gym, Música y movimiento, Juegos sensoriales, Narración de cuentos

Beneficios: Mejora coordinación, fuerza y equilibrio; estimula curiosidad y aprendizaje; desarrollo social; fortalecimiento del vínculo familiar

Precios:
- Clase individual: $70.000
- Plan mensual 1 día/semana: $230.000
- Plan mensual 2 días/semana: $280.000
- Plan mensual 3 días/semana: $330.000

🌟 CLASES PERSONALIZADAS DE ESTIMULACIÓN TEMPRANA:

Sesiones individuales adaptadas a las necesidades específicas de cada bebé.

Características: Enfoque individualizado; desarrollo cognitivo, motor, emocional y social; participación activa de los padres; incluye estimulación acuática

Beneficios: Atención personalizada; estimulación precisa según etapa de desarrollo; fortalecimiento del vínculo familiar; mayor confianza y seguridad en el agua

Precios:
- 1 clase personalizada: $80.000
- Plan mensual 1 día/semana (4 clases): $270.000
- Plan mensual 2 días/semana (8 clases): $370.000
- Plan mensual 3 días/semana (12 clases): $450.000";
            var businessRulesValue = @"⚠️ REGLA CRÍTICA: NUNCA inventes servicios, precios o información fuera del contexto proporcionado. Solo usa la información oficial de Mimos Baby Spa.

REGLAS DE NEGOCIO:
1. SIEMPRE pregunta la edad del bebé si no se conoce (acepta meses o años, convierte años a meses internamente)
2. Cuando conozcas la edad del bebé:
   - Pregunta si ya conoce nuestros planes (pero de forma natural, no siempre)
   - Si no los conoce, EXPLICA los planes de forma CONVERSACIONAL y NARRATIVA, integrando la información de forma natural
   - Si ya los conoce, pregunta si está interesado en alguno específico o simplemente ofrece ayuda
   - Explica los BENEFICIOS de forma natural, como si estuvieras contándole a una amiga
3. Explica beneficios ANTES de mencionar precio
4. Valida emocionalmente dudas o miedos del cliente con EMPATÍA
5. Recomienda plan según edad del bebé (en meses):
   - Bebés menores de 3 meses: Plan Aventuras Marinas o Plan Suaves Mimos (si es post vacunas)
   - Bebés 3-6 meses: Plan Marineritos o Plan Aventuras Marinas
   - Bebés mayores de 6 meses: Plan Marineritos (más completo)
6. Si preguntan por precio: explica los planes de forma conversacional, menciona los precios de forma natural
7. Si piden hablar con humano: transfiere sin fricción, agradece por contactar
8. NUNCA inventes información fuera del contexto proporcionado
9. Mantén la conversación FLUIDA y NATURAL, como si estuvieras chateando con una amiga
10. Si preguntan por talleres o clases personalizadas, explica las opciones disponibles según la edad del bebé de forma conversacional
11. IMPORTANTE: NO siempre cierres con pregunta. Varía: a veces pregunta, a veces solo responde, a veces hace un comentario amable
12. Cuando expliques planes, NO uses formato de lista. Integra la información en párrafos naturales y conversacionales
13. Evita frases robóticas como ""Te explico:"", ""A continuación:"", ""Características:"". Mejor integra todo en el flujo natural de la conversación";
            
            var contextDataValue = @"Extraer edad del niño (en meses o años, convertir años a meses si es necesario)
Extraer qué plan se le puede recomendar según la edad del bebé
Extraer cuándo es la fecha que le gustaría venir
Extraer nombre del cliente si se menciona";
            
            var planRulesValue = @"Reglas para determinar planes según la edad del bebé (en meses):
- Bebés menores de 3 meses: Plan Aventuras Marinas o Plan Suaves Mimos (si es post vacunas)
- Bebés 3-6 meses: Plan Marineritos o Plan Aventuras Marinas
- Bebés mayores de 6 meses: Plan Marineritos (más completo)

Formato JSON alternativo:
{
  ""rules"": [
    { ""minAge"": 0, ""maxAge"": 2, ""plan"": ""Plan Aventuras Marinas"" },
    { ""minAge"": 3, ""maxAge"": 6, ""plan"": ""Plan Marineritos"" },
    { ""minAge"": 6, ""maxAge"": null, ""plan"": ""Plan Marineritos"" }
  ]
}";
            
            migrationBuilder.Sql($@"
                INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt)
                VALUES 
                    ('{configId0}', '{businessId}', 0, N'{personaValue.Replace("'", "''")}', N'Persona e identidad del asistente: quién es y cómo debe comunicarse', 1, '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('{configId1}', '{businessId}', 1, N'{objectiveValue.Replace("'", "''")}', N'Objetivo principal del negocio', 1, '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('{configId2}', '{businessId}', 2, N'{generalInfoValue.Replace("'", "''")}', N'Información general del negocio: servicios, planes, precios, ubicación', 1, '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('{configId3}', '{businessId}', 3, N'{businessRulesValue.Replace("'", "''")}', N'Reglas de negocio y comportamiento del asistente', 1, '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('{configId9}', '{businessId}', 9, N'{contextDataValue.Replace("'", "''")}', N'Instrucciones de qué datos extraer del contexto de conversación', 1, '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('{configId10}', '{businessId}', 10, N'{planRulesValue.Replace("'", "''")}', N'Reglas para determinar planes recomendados según la edad del bebé', 1, '{now:yyyy-MM-dd HH:mm:ss}');
            ");

            // Actualizar conversaciones existentes para asignarles el BusinessId por defecto
            migrationBuilder.Sql($@"
                UPDATE Conversations
                SET BusinessId = '{businessId}'
                WHERE BusinessId = '00000000-0000-0000-0000-000000000000';
            ");

            // Actualizar leads existentes para asignarles el BusinessId por defecto
            migrationBuilder.Sql($@"
                UPDATE Leads
                SET BusinessId = '{businessId}'
                WHERE BusinessId = '00000000-0000-0000-0000-000000000000';
            ");

            // Agregar foreign keys DESPUÉS de insertar los datos
            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Businesses_BusinessId",
                table: "Conversations",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Businesses_BusinessId",
                table: "Leads",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Businesses_BusinessId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Businesses_BusinessId",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "BusinessConfigurations");

            migrationBuilder.DropTable(
                name: "BusinessWhatsAppNumbers");

            migrationBuilder.DropTable(
                name: "SystemConfigurations");

            migrationBuilder.DropTable(
                name: "Businesses");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Leads_BusinessId_UserNumber",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_BusinessId_UserNumber",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_UserNumber",
                table: "Leads",
                column: "UserNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserNumber",
                table: "Conversations",
                column: "UserNumber");
        }
    }
}
