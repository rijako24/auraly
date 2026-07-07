using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Scenarios;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// -----------------------------------------------------------------------------
// Escenarios de reserva completa: check_availability -> create_reservation.
// Las fechas usan 2026-08 para garantizar que esten en el futuro.
// -----------------------------------------------------------------------------

public class FullReservationStyle1FormalScenario : TestScenario
{
    public override string Id => "full_1_formal";
    public override string Description => "Estilo formal: datos completos, lenguaje cortes.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Buenos dias, me gustaria reservar un Plan Marineritos para el 2026-08-15 a las 10:00. Mi nombre es Maria Gonzalez.",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00"}""",
                "Hay disponibilidad para el 15 de agosto a las 10:00. Confirmas la reserva a nombre de Maria Gonzalez?"),
            "disponib"),
        new("Perfecto, confirmo la reserva con esos datos.",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00","customer_name":"Maria Gonzalez","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Tu reserva ha sido creada exitosamente! Te esperamos el 15 de agosto a las 10:00."),
            "reserva")
    ];
}

public class FullReservationStyle2ColloquialScenario : TestScenario
{
    public override string Id => "full_2_colloquial";
    public override string Description => "Estilo coloquial: mensajes breves, lenguaje casual.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Oye quiero el Plan Post Vacunas 2026-08-16 a las 3pm, soy Ana",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:00"}""",
                "Hay disponibilidad el 16 de agosto a las 15:00. Confirmamos la reserva?"),
            "disponib"),
        new("Dale confirma",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:00","customer_name":"Ana","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva confirmada, Ana! Te esperamos el 16 de agosto."),
            "reserva")
    ];
}

public class FullReservationStyle3AllInOneScenario : TestScenario
{
    public override string Id => "full_3_all_in_one";
    public override string Description => "Usuario proporciona todo en un solo mensaje.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola quiero Plan Aventuras Marinas 2026-08-20 14:00 soy Carlos Ruiz",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Aventuras Marinas","date":"2026-08-20","time":"14:00"}""",
                "Tenemos disponibilidad el 20 de agosto a las 14:00. Confirmamos la reserva, Carlos Ruiz?"),
            "disponib"),
        new("Confirma",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Aventuras Marinas","date":"2026-08-20","time":"14:00","customer_name":"Carlos Ruiz","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Perfecto! Reserva creada para Carlos Ruiz el 20 de agosto."),
            "reserva")
    ];
}

public class FullReservationStyle4ConversationalScenario : TestScenario
{
    public override string Id => "full_4_conversational";
    public override string Description => "Flujo conversacional con varios intercambios.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola que servicios tienen?",
            FakeLlmScript.TextOnly("Hola! Tenemos Plan Marineritos, Plan Post Vacunas, Plan Aventuras Marinas y Plan Deluxe. Cual te interesa?"),
            ""),
        new("Quiero Plan Marineritos para el 2026-08-18 a las 11am, soy Patricia",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-18","time":"11:00"}""",
                "Hay disponibilidad para el 18 de agosto a las 11:00. Confirmamos, Patricia?"),
            "disponib"),
        new("Si confirmo la reserva",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-18","time":"11:00","customer_name":"Patricia","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada! Te esperamos el 18 de agosto a las 11:00, Patricia."),
            "reserva")
    ];
}

public class FullReservationStyle5DateCorrectionsScenario : TestScenario
{
    public override string Id => "full_5_date_corrections";
    public override string Description => "Usuario corrige fecha durante la conversacion.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Marineritos para 2026-08-14 a las 9am",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-14","time":"09:00"}""",
                "Hay disponibilidad el 14 de agosto a las 9:00. Confirmas o prefieres otra fecha?"),
            ""),
        new("Mejor 2026-08-15 a las 11, soy Diego",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"11:00"}""",
                "Perfecto, tambien hay disponibilidad el 15 de agosto a las 11:00. Confirmamos, Diego?"),
            "disponib"),
        new("Confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"11:00","customer_name":"Diego","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva confirmada para Diego el 15 de agosto!"),
            "reserva")
    ];
}

public class FullReservationStyle6WithAddOnScenario : TestScenario
{
    public override string Id => "full_6_with_addon";
    public override string Description => "Plan Deluxe con add-on: valida escalamiento y seleccion.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation", "ReservationMustIncludeAddOns"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Quiero Plan Deluxe 2026-08-15 a las 10am, soy Laura",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-08-15","time":"10:00"}""",
                "Hay disponibilidad. Te gustaria agregar algun add-on como Masaje Extra 15m?"),
            "disponib"),
        new("Si agrega el Masaje Extra 15m y confirma",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Deluxe","date":"2026-08-15","time":"10:00","customer_name":"Laura","customer_phone":"+5491100000000","add_ons":"Masaje Extra 15m","customer_confirmed":true}""",
                "Reserva creada con add-on Masaje Extra 15m! Te esperamos, Laura."),
            "reserva")
    ];
}

public class FullReservationStyle7NoAddOnScenario : TestScenario
{
    public override string Id => "full_7_no_addon";
    public override string Description => "Plan Deluxe, usuario rechaza add-ons explicitamente.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Deluxe 2026-08-17 14:00, no quiero add-ons, soy Ricardo",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-08-17","time":"14:00"}""",
                "Disponibilidad confirmada para el 17 de agosto a las 14:00, sin add-ons. Confirmamos, Ricardo?"),
            "disponib"),
        new("Confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Deluxe","date":"2026-08-17","time":"14:00","customer_name":"Ricardo","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada sin add-ons! Te esperamos el 17 de agosto, Ricardo."),
            "reserva")
    ];
}

public class FullReservationStyle8OneWordScenario : TestScenario
{
    public override string Id => "full_8_one_word";
    public override string Description => "Usuario responde con una palabra a la vez.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Post Vacunas 2026-08-19 10am Maria Lopez",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-19","time":"10:00"}""",
                "Disponibilidad para el 19 de agosto a las 10:00. Confirmamos, Maria Lopez?"),
            "disponib"),
        new("Si",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Post Vacunas","date":"2026-08-19","time":"10:00","customer_name":"Maria Lopez","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva confirmada, Maria Lopez!"),
            "reserva")
    ];
}

public class FullReservationStyle9WithEmailScenario : TestScenario
{
    public override string Id => "full_9_with_email";
    public override string Description => "Usuario incluye email en los datos.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola soy Carmen, carmen@email.com. Quiero Plan Marineritos 2026-08-21 11am",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-21","time":"11:00"}""",
                "Disponibilidad para el 21 de agosto a las 11:00, Carmen. Confirmamos?"),
            "disponib"),
        new("Confirmo la reserva",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-21","time":"11:00","customer_name":"Carmen","customer_phone":"+5491100000000","customer_email":"carmen@email.com","customer_confirmed":true}""",
                "Reserva creada! Te enviamos detalles a carmen@email.com."),
            "reserva")
    ];
}

public class FullReservationStyle10FutureDateScenario : TestScenario
{
    public override string Id => "full_10_future_date";
    public override string Description => "Reserva para fecha en el futuro lejano.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Quiero reservar Plan Aventuras Marinas para el 2026-09-10 a las 2pm, soy Pablo Martin",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Aventuras Marinas","date":"2026-09-10","time":"14:00"}""",
                "Disponibilidad para el 10 de septiembre a las 14:00. Confirmamos, Pablo Martin?"),
            "disponib"),
        new("Perfecto confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Aventuras Marinas","date":"2026-09-10","time":"14:00","customer_name":"Pablo Martin","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva confirmada para Pablo Martin el 10 de septiembre!"),
            "reserva")
    ];
}

public class FullReservationStyle11LongMessageScenario : TestScenario
{
    public override string Id => "full_11_long_message";
    public override string Description => "Usuario envia mensaje largo con todos los detalles.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Buenos dias, les escribo porque he visto su pagina. Tengo una bebe de 7 meses y quiero reservar el Plan Marineritos para el proximo viernes 2026-08-22 a las 10 de la manana. Me llamo Claudia Vega.",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-22","time":"10:00"}""",
                "Hola Claudia! Hay disponibilidad el 22 de agosto a las 10:00. Confirmamos la reserva?"),
            "disponib"),
        new("Confirmo la reserva con esos datos por favor",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-22","time":"10:00","customer_name":"Claudia Vega","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada, Claudia Vega! Te esperamos el 22 de agosto a las 10:00."),
            "reserva")
    ];
}

public class FullReservationStyle12ImpatientScenario : TestScenario
{
    public override string Id => "full_12_impatient";
    public override string Description => "Usuario impaciente, mensajes urgentes.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Reserva ya Plan Post Vacunas 2026-08-16 4pm Pedro Diaz",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"16:00"}""",
                "Disponible el 16 de agosto a las 16:00. Confirmamos, Pedro Diaz?"),
            "disponib"),
        new("Confirma ya",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"16:00","customer_name":"Pedro Diaz","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada de inmediato! Te esperamos el 16, Pedro."),
            "reserva")
    ];
}

public class FullReservationStyle13ServiceChangeScenario : TestScenario
{
    public override string Id => "full_13_service_change";
    public override string Description => "Usuario cambia de servicio durante la conversacion.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Quiero Plan Aventuras Marinas 2026-08-18 9am",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Aventuras Marinas","date":"2026-08-18","time":"09:00"}""",
                "Hay disponibilidad para Plan Aventuras Marinas el 18 de agosto a las 9:00. Confirmamos?"),
            "disponib"),
        new("Mejor Plan Marineritos, soy Fernando Castro",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-18","time":"09:00"}""",
                "Claro, Plan Marineritos tambien disponible el 18 de agosto a las 9:00. Confirmamos, Fernando Castro?"),
            ""),
        new("Si confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-18","time":"09:00","customer_name":"Fernando Castro","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada para Fernando Castro el 18 de agosto!"),
            "reserva")
    ];
}

public class FullReservationStyle14TimeWithMinutesScenario : TestScenario
{
    public override string Id => "full_14_time_minutes";
    public override string Description => "Hora con minutos (11:30).";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Marineritos 2026-08-20 11:30 soy Roberto Sanchez",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-20","time":"11:30"}""",
                "Disponibilidad para el 20 de agosto a las 11:30. Confirmamos, Roberto Sanchez?"),
            "disponib"),
        new("Adelante confirma",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-20","time":"11:30","customer_name":"Roberto Sanchez","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada para Roberto Sanchez el 20 de agosto a las 11:30!"),
            "reserva")
    ];
}

public class FullReservationStyle15AskFirstScenario : TestScenario
{
    public override string Id => "full_15_ask_first";
    public override string Description => "Usuario pregunta por horarios antes de especificar.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Que horarios tienen para el 2026-08-16?",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-16"}""",
                "Para el 16 de agosto tenemos disponibilidad a las 9:00, 11:00 y 15:00. Cual prefieres?"),
            ""),
        new("A las 3pm para Plan Post Vacunas, soy Juan Perez",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:00"}""",
                "Plan Post Vacunas disponible el 16 a las 15:00. Confirmamos, Juan Perez?"),
            "disponib"),
        new("Confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:00","customer_name":"Juan Perez","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada, Juan Perez! Te esperamos el 16 de agosto a las 15:00."),
            "reserva")
    ];
}

public class FullReservationStyle16DeluxeAddOnTwoStepsScenario : TestScenario
{
    public override string Id => "full_16_deluxe_addon_2steps";
    public override string Description => "Plan Deluxe: add-on en segundo paso.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation", "ReservationMustIncludeAddOns"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Deluxe 2026-08-25 10am Sandra Torres",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-08-25","time":"10:00"}""",
                "Disponible el 25 de agosto a las 10:00, Sandra. Anadimos algun add-on?"),
            "disponib"),
        new("Si quiero Masaje Extra 15m. Confirmo.",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Deluxe","date":"2026-08-25","time":"10:00","customer_name":"Sandra Torres","customer_phone":"+5491100000000","add_ons":"Masaje Extra 15m","customer_confirmed":true}""",
                "Reserva creada con Masaje Extra 15m! Te esperamos, Sandra."),
            "reserva")
    ];
}

public class FullReservationStyle17ConfirmationSynonymsScenario : TestScenario
{
    public override string Id => "full_17_confirmation_synonyms";
    public override string Description => "Usuario confirma con varias formas (adelante, procede).";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Aventuras Marinas 2026-08-22 12pm soy Lucia Mendoza",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Aventuras Marinas","date":"2026-08-22","time":"12:00"}""",
                "Disponible el 22 de agosto a las 12:00. Confirmamos, Lucia Mendoza?"),
            "disponib"),
        new("Adelante hazlo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Aventuras Marinas","date":"2026-08-22","time":"12:00","customer_name":"Lucia Mendoza","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada, Lucia! Te esperamos el 22 de agosto a las 12:00."),
            "reserva")
    ];
}

public class FullReservationStyle18CompoundNameScenario : TestScenario
{
    public override string Id => "full_18_compound_name";
    public override string Description => "Nombre con apellido compuesto.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Post Vacunas 2026-08-23 9am, Maria Jose Garcia Lopez",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-23","time":"09:00"}""",
                "Disponible el 23 de agosto a las 9:00. Confirmamos, Maria Jose Garcia Lopez?"),
            "disponib"),
        new("Procede con la reserva",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Post Vacunas","date":"2026-08-23","time":"09:00","customer_name":"Maria Jose Garcia Lopez","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva confirmada para Maria Jose Garcia Lopez el 23 de agosto!"),
            "reserva")
    ];
}

public class FullReservationStyle19MinimalScenario : TestScenario
{
    public override string Id => "full_19_minimal";
    public override string Description => "Flujo minimo: datos esenciales, 2 mensajes.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Plan Marineritos 2026-08-24 10:00 Ana",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-24","time":"10:00"}""",
                "Disponible el 24 de agosto a las 10:00. Confirmamos, Ana?"),
            "disponib"),
        new("Ok",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-24","time":"10:00","customer_name":"Ana","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada, Ana! Te esperamos el 24 de agosto."),
            "reserva")
    ];
}

public class FullReservationStyle20FourStepsScenario : TestScenario
{
    public override string Id => "full_20_four_steps";
    public override string Description => "Flujo en 4 pasos: info, reserva, disponibilidad, confirmar.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;
    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola",
            FakeLlmScript.TextOnly("Hola! Bienvenido a MimosBabySpa. En que te puedo ayudar?"),
            ""),
        new("Quiero Plan Marineritos",
            FakeLlmScript.TextOnly("Excelente eleccion! Para que fecha y hora quieres reservar?"),
            ""),
        new("Para 2026-08-26 a las 11am, soy Elena Ruiz",
            FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-26","time":"11:00"}""",
                "Disponible el 26 de agosto a las 11:00. Confirmamos, Elena Ruiz?"),
            "disponib"),
        new("Confirmo",
            FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-26","time":"11:00","customer_name":"Elena Ruiz","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada para Elena Ruiz el 26 de agosto a las 11:00!"),
            "reserva")
    ];
}
