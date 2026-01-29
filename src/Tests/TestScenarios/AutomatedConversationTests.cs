using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Tests.TestScenarios;

/// <summary>
/// Pruebas automatizadas de conversaciones completas
/// Simula 5 conversaciones donde se envían datos, se guardan, se revisa disponibilidad y se crean reservas
/// </summary>
public class AutomatedConversationTests
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IConversationStateManager _stateManager;
    private readonly Application.Services.IConversationService _conversationService;
    private readonly Guid _businessId;
    private readonly ILogger<AutomatedConversationTests> _logger;

    public AutomatedConversationTests(
        HybridTransactionalOrchestrator orchestrator,
        IConversationStateManager stateManager,
        Application.Services.IConversationService conversationService,
        ILogger<AutomatedConversationTests> logger)
    {
        _orchestrator = orchestrator;
        _stateManager = stateManager;
        _conversationService = conversationService;
        _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222"); // BusinessId por defecto
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta todas las pruebas automatizadas
    /// </summary>
    public async Task RunAllTestsAsync()
    {
        Console.WriteLine("================================================");
        Console.WriteLine("  PRUEBAS AUTOMATIZADAS DE CONVERSACIONES");
        Console.WriteLine("================================================");
        Console.WriteLine();

        var results = new List<(string TestName, bool Success, string Error)>();

        try
        {
            results.Add(await RunTest1_CompleteReservationFlowAsync());
            results.Add(await RunTest2_MultipleAttributesAsync());
            results.Add(await RunTest3_AvailabilityCheckAsync());
            results.Add(await RunTest4_ReservationWithCorrectionsAsync());
            results.Add(await RunTest5_QuickReservationAsync());
            results.Add(await RunTest6_InformationQueryBeforeBookingAsync());
            results.Add(await RunTest7_ServiceChangeAsync());
            results.Add(await RunTest8_MultipleServicesQueryAsync());
            results.Add(await RunTest9_ReservationWithCustomerNameFirstAsync());
            results.Add(await RunTest10_SpecialConditionsFlowAsync());
            results.Add(await RunTest11_ReservationWithEmailAsync());
            results.Add(await RunTest12_NextWeekReservationAsync());
            results.Add(await RunTest13_CancellationAndRestartAsync());
            results.Add(await RunTest14_SpecificTimeSlotAsync());
            results.Add(await RunTest15_CompleteFlowWithAllValidationsAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando pruebas automatizadas");
            Console.WriteLine($"❌ Error crítico: {ex.Message}");
        }

        // Resumen
        Console.WriteLine();
        Console.WriteLine("================================================");
        Console.WriteLine("  RESUMEN DE PRUEBAS");
        Console.WriteLine("================================================");
        
        var passed = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        
        Console.WriteLine($"Total: {results.Count}");
        Console.WriteLine($"✅ Pasadas: {passed}");
        Console.WriteLine($"❌ Fallidas: {failed}");
        Console.WriteLine();

        foreach (var (testName, success, error) in results)
        {
            var icon = success ? "✅" : "❌";
            Console.WriteLine($"{icon} {testName}");
            if (!success && !string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"   Error: {error}");
            }
        }
    }

    /// <summary>
    /// Test 1: Flujo completo de reserva con todos los datos
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest1_CompleteReservationFlowAsync()
    {
        var testName = "Test 1: Flujo completo de reserva";
        var phone = "+12345678901";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Obtener conversación ÚNICA para todo el flujo
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Saludo inicial
            Console.WriteLine("\n📤 Usuario: Hola");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");

            // Paso 2: Información del bebé
            Console.WriteLine("\n📤 Usuario: Tengo un bebé de 5 meses");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Tengo un bebé de 5 meses", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "BabyAge", "5");

            // Paso 3: Nombre del bebé
            Console.WriteLine("\n📤 Usuario: Se llama Mateo");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Se llama Mateo", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "BabyName", "Mateo");

            // Paso 4: Selección de servicio
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Marineritos");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Marineritos", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");

            // Paso 5: Fecha deseada
            Console.WriteLine("\n📤 Usuario: Para mañana a las 3pm");
            var response5 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Para mañana a las 3pm", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response5}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "15:00");

            // Paso 6: Verificar disponibilidad
            Console.WriteLine("\n📤 Usuario: ¿Hay disponibilidad?");
            var response6 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Hay disponibilidad?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response6}");
            await VerifyStateContains(conversationId, "AvailabilityConfirmed", "true");

            // Paso 7: Proporcionar nombre del cliente
            Console.WriteLine("\n📤 Usuario: Mi nombre es María González");
            var response7 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi nombre es María González", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response7}");
            await VerifyStateContains(conversationId, "CustomerName", "María González");

            // Paso 8: Confirmar reserva
            Console.WriteLine("\n📤 Usuario: Sí, confirma la reserva");
            var response8 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Sí, confirma la reserva", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response8}");
            await VerifyStateContains(conversationId, "ReservationCreated");

            Console.WriteLine("\n✅ Test 1 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 1 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 2: Múltiples atributos en un solo mensaje
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest2_MultipleAttributesAsync()
    {
        var testName = "Test 2: Múltiples atributos";
        var phone = "+12345678902";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Obtener conversación ÚNICA para todo el flujo
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Información completa del bebé
            Console.WriteLine("\n📤 Usuario: Hola, tengo un bebé de 8 meses llamado Sofía");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, tengo un bebé de 8 meses llamado Sofía", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "BabyAge", "8");
            await VerifyStateContains(conversationId, "BabyName", "Sofía");

            // Paso 2: Servicio y fecha
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Suaves Mimos para el viernes a las 10am");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Suaves Mimos para el viernes a las 10am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "10:00");

            Console.WriteLine("\n✅ Test 2 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 2 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 3: Verificación de disponibilidad
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest3_AvailabilityCheckAsync()
    {
        var testName = "Test 3: Verificación de disponibilidad";
        var phone = "+12345678903";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Obtener conversación ÚNICA para todo el flujo
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Configurar datos básicos
            Console.WriteLine("\n📤 Usuario: Hola, quiero reservar el Plan Aventuras Marinas");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, quiero reservar el Plan Aventuras Marinas", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Aventuras Marinas");

            // Paso 2: Fecha y hora
            Console.WriteLine("\n📤 Usuario: Para mañana a las 2pm");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Para mañana a las 2pm", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "14:00");

            // Paso 3: Solicitar disponibilidad
            Console.WriteLine("\n📤 Usuario: Verifica si hay disponibilidad");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Verifica si hay disponibilidad", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "AvailabilityConfirmed");

            Console.WriteLine("\n✅ Test 3 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 3 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 4: Reserva con correcciones
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest4_ReservationWithCorrectionsAsync()
    {
        var testName = "Test 4: Reserva con correcciones";
        var phone = "+12345678904";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Obtener conversación ÚNICA para todo el flujo
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Información inicial incorrecta
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Marineritos para hoy");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Marineritos para hoy", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");

            // Paso 2: Corrección de fecha
            Console.WriteLine("\n📤 Usuario: Mejor para mañana a las 11am");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mejor para mañana a las 11am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "11:00");

            // Paso 3: Agregar información del bebé
            Console.WriteLine("\n📤 Usuario: Mi bebé tiene 6 meses");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi bebé tiene 6 meses", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "BabyAge", "6");

            // Paso 4: Confirmar
            Console.WriteLine("\n📤 Usuario: Sí, confirma");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Sí, confirma", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");

            Console.WriteLine("\n✅ Test 4 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 4 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 5: Reserva rápida con información mínima
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest5_QuickReservationAsync()
    {
        var testName = "Test 5: Reserva rápida";
        var phone = "+12345678905";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Obtener conversación ÚNICA para todo el flujo
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Mensaje con toda la información
            Console.WriteLine("\n📤 Usuario: Hola, quiero reservar el Plan Suaves Mimos para mañana a las 4pm, mi bebé tiene 3 meses");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, quiero reservar el Plan Suaves Mimos para mañana a las 4pm, mi bebé tiene 3 meses", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "16:00");
            await VerifyStateContains(conversationId, "BabyAge", "3");

            // Paso 2: Confirmar
            Console.WriteLine("\n📤 Usuario: Perfecto, confirma");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Perfecto, confirma", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");

            Console.WriteLine("\n✅ Test 5 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 5 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 6: Cliente pregunta información antes de reservar
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest6_InformationQueryBeforeBookingAsync()
    {
        var testName = "Test 6: Consulta de información antes de reservar";
        var phone = "+12345678906";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Consulta general
            Console.WriteLine("\n📤 Usuario: Hola, ¿qué servicios ofrecen para bebés?");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, ¿qué servicios ofrecen para bebés?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");

            // Paso 2: Más información
            Console.WriteLine("\n📤 Usuario: Mi bebé tiene 7 meses, ¿cuál me recomiendas?");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi bebé tiene 7 meses, ¿cuál me recomiendas?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "BabyAge", "7");

            // Paso 3: Decidir reservar
            Console.WriteLine("\n📤 Usuario: Me gusta el Plan Marineritos, quiero reservar para mañana");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Me gusta el Plan Marineritos, quiero reservar para mañana", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");

            Console.WriteLine("\n✅ Test 6 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 6 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 7: Cambio de servicio durante la conversación
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest7_ServiceChangeAsync()
    {
        var testName = "Test 7: Cambio de servicio";
        var phone = "+12345678907";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Selección inicial
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Aventuras Marinas");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Aventuras Marinas", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Aventuras Marinas");

            // Paso 2: Cambio de opinión
            Console.WriteLine("\n📤 Usuario: Mejor quiero el Plan Suaves Mimos");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mejor quiero el Plan Suaves Mimos", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");

            // Paso 3: Continuar con la reserva
            Console.WriteLine("\n📤 Usuario: Para mañana a las 9am");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Para mañana a las 9am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "DesiredTime", "09:00");

            Console.WriteLine("\n✅ Test 7 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 7 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 8: Consulta sobre múltiples servicios
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest8_MultipleServicesQueryAsync()
    {
        var testName = "Test 8: Consulta de múltiples servicios";
        var phone = "+12345678908";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Pregunta sobre diferencias
            Console.WriteLine("\n📤 Usuario: ¿Cuál es la diferencia entre Plan Marineritos y Plan Suaves Mimos?");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Cuál es la diferencia entre Plan Marineritos y Plan Suaves Mimos?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");

            // Paso 2: Proporcionar información del bebé
            Console.WriteLine("\n📤 Usuario: Mi bebé tiene 4 meses, se llama Lucas");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi bebé tiene 4 meses, se llama Lucas", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "BabyAge", "4");
            await VerifyStateContains(conversationId, "BabyName", "Lucas");

            // Paso 3: Seleccionar servicio
            Console.WriteLine("\n📤 Usuario: Entonces voy con el Plan Suaves Mimos");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Entonces voy con el Plan Suaves Mimos", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");

            Console.WriteLine("\n✅ Test 8 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 8 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 9: Reserva con nombre del cliente desde el inicio
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest9_ReservationWithCustomerNameFirstAsync()
    {
        var testName = "Test 9: Nombre del cliente al inicio";
        var phone = "+12345678909";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Presentación con nombre
            Console.WriteLine("\n📤 Usuario: Hola, soy Patricia Rodríguez");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, soy Patricia Rodríguez", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "CustomerName", "Patricia Rodríguez");

            // Paso 2: Información completa
            Console.WriteLine("\n📤 Usuario: Quiero reservar el Plan Marineritos para mi bebé de 6 meses llamado Diego, para mañana a las 3pm");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero reservar el Plan Marineritos para mi bebé de 6 meses llamado Diego, para mañana a las 3pm", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");
            await VerifyStateContains(conversationId, "BabyAge", "6");
            await VerifyStateContains(conversationId, "BabyName", "Diego");
            await VerifyStateContains(conversationId, "DesiredTime", "15:00");

            // Paso 3: Verificar disponibilidad
            Console.WriteLine("\n📤 Usuario: ¿Hay disponibilidad?");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Hay disponibilidad?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");

            // Paso 4: Confirmar
            Console.WriteLine("\n📤 Usuario: Perfecto, confirmo la reserva");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Perfecto, confirmo la reserva", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");

            Console.WriteLine("\n✅ Test 9 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 9 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 10: Flujo con condiciones especiales
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest10_SpecialConditionsFlowAsync()
    {
        var testName = "Test 10: Flujo con condiciones especiales";
        var phone = "+12345678910";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Información básica
            Console.WriteLine("\n📤 Usuario: Hola, quiero el Plan Aventuras Marinas para mañana a las 11am");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, quiero el Plan Aventuras Marinas para mañana a las 11am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Aventuras Marinas");
            await VerifyStateContains(conversationId, "DesiredTime", "11:00");

            // Paso 2: Información del bebé
            Console.WriteLine("\n📤 Usuario: Mi bebé tiene 9 meses y se llama Isabella");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi bebé tiene 9 meses y se llama Isabella", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "BabyAge", "9");
            await VerifyStateContains(conversationId, "BabyName", "Isabella");

            // Paso 3: Condiciones especiales
            Console.WriteLine("\n📤 Usuario: Tiene alergias leves");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Tiene alergias leves", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");

            // Paso 4: Verificar y confirmar
            Console.WriteLine("\n📤 Usuario: Verifica disponibilidad y confirma");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Verifica disponibilidad y confirma", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");

            Console.WriteLine("\n✅ Test 10 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 10 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 11: Reserva con email
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest11_ReservationWithEmailAsync()
    {
        var testName = "Test 11: Reserva con email";
        var phone = "+12345678911";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Presentación completa
            Console.WriteLine("\n📤 Usuario: Hola, soy Ana López, mi email es ana.lopez@email.com");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, soy Ana López, mi email es ana.lopez@email.com", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "CustomerName", "Ana López");

            // Paso 2: Información de reserva
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Suaves Mimos para mi bebé de 5 meses, mañana a las 10am");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Suaves Mimos para mi bebé de 5 meses, mañana a las 10am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");
            await VerifyStateContains(conversationId, "BabyAge", "5");
            await VerifyStateContains(conversationId, "DesiredTime", "10:00");

            // Paso 3: Confirmar
            Console.WriteLine("\n📤 Usuario: Confirma la reserva");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Confirma la reserva", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");

            Console.WriteLine("\n✅ Test 11 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 11 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 12: Reserva para la próxima semana
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest12_NextWeekReservationAsync()
    {
        var testName = "Test 12: Reserva para próxima semana";
        var phone = "+12345678912";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Solicitud con fecha específica
            Console.WriteLine("\n📤 Usuario: Hola, quiero reservar para el próximo lunes a las 2pm");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, quiero reservar para el próximo lunes a las 2pm", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "14:00");

            // Paso 2: Seleccionar servicio
            Console.WriteLine("\n📤 Usuario: El Plan Marineritos por favor");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "El Plan Marineritos por favor", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");

            // Paso 3: Información del bebé
            Console.WriteLine("\n📤 Usuario: Para mi bebé de 8 meses llamado Miguel");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Para mi bebé de 8 meses llamado Miguel", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "BabyAge", "8");
            await VerifyStateContains(conversationId, "BabyName", "Miguel");

            Console.WriteLine("\n✅ Test 12 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 12 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 13: Cliente cancela y vuelve a iniciar
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest13_CancellationAndRestartAsync()
    {
        var testName = "Test 13: Cancelación y reinicio";
        var phone = "+12345678913";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Inicio
            Console.WriteLine("\n📤 Usuario: Quiero el Plan Aventuras Marinas para mañana");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero el Plan Aventuras Marinas para mañana", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "Service", "Plan Aventuras Marinas");

            // Paso 2: Cambio de opinión total
            Console.WriteLine("\n📤 Usuario: Mejor déjalo, no estoy segura");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mejor déjalo, no estoy segura", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");

            // Paso 3: Reinicio (el estado debería mantener Service pero permitir cambios)
            Console.WriteLine("\n📤 Usuario: Ahora sí, quiero el Plan Suaves Mimos para el miércoles a las 4pm");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Ahora sí, quiero el Plan Suaves Mimos para el miércoles a las 4pm", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "Service", "Plan Suaves Mimos");
            await VerifyStateContains(conversationId, "DesiredTime", "16:00");

            Console.WriteLine("\n✅ Test 13 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 13 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 14: Horario específico y verificación
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest14_SpecificTimeSlotAsync()
    {
        var testName = "Test 14: Horario específico";
        var phone = "+12345678914";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Pregunta por horarios
            Console.WriteLine("\n📤 Usuario: ¿Tienen disponibilidad mañana por la tarde?");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Tienen disponibilidad mañana por la tarde?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");

            // Paso 2: Especificar hora exacta
            Console.WriteLine("\n📤 Usuario: A las 3:30pm específicamente");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "A las 3:30pm específicamente", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "15:30");

            // Paso 3: Seleccionar servicio
            Console.WriteLine("\n📤 Usuario: Para el Plan Marineritos");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Para el Plan Marineritos", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");
            await VerifyStateContains(conversationId, "Service", "Plan Marineritos");

            // Paso 4: Información del bebé
            Console.WriteLine("\n📤 Usuario: Mi bebé tiene 7 meses");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Mi bebé tiene 7 meses", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");
            await VerifyStateContains(conversationId, "BabyAge", "7");

            Console.WriteLine("\n✅ Test 14 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 14 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Test 15: Flujo completo con todas las validaciones
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest15_CompleteFlowWithAllValidationsAsync()
    {
        var testName = "Test 15: Flujo completo con todas las validaciones";
        var phone = "+12345678915";
        
        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // Paso 1: Presentación completa
            Console.WriteLine("\n📤 Usuario: Hola, me llamo Roberto Sánchez");
            var response1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Hola, me llamo Roberto Sánchez", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1}");
            await VerifyStateContains(conversationId, "CustomerName", "Roberto Sánchez");

            // Paso 2: Información del bebé
            Console.WriteLine("\n📤 Usuario: Tengo un bebé de 10 meses llamado Alejandro");
            var response2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Tengo un bebé de 10 meses llamado Alejandro", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2}");
            await VerifyStateContains(conversationId, "BabyAge", "10");
            await VerifyStateContains(conversationId, "BabyName", "Alejandro");

            // Paso 3: Consulta de servicios
            Console.WriteLine("\n📤 Usuario: ¿Qué planes tienen para su edad?");
            var response3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Qué planes tienen para su edad?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3}");

            // Paso 4: Selección de servicio
            Console.WriteLine("\n📤 Usuario: Me interesa el Plan Aventuras Marinas");
            var response4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Me interesa el Plan Aventuras Marinas", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");
            await VerifyStateContains(conversationId, "Service", "Plan Aventuras Marinas");

            // Paso 5: Fecha y hora
            Console.WriteLine("\n📤 Usuario: Quiero reservar para mañana a las 11:30am");
            var response5 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Quiero reservar para mañana a las 11:30am", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response5}");
            await VerifyStateContains(conversationId, "DesiredDate");
            await VerifyStateContains(conversationId, "DesiredTime", "11:30");

            // Paso 6: Verificar disponibilidad
            Console.WriteLine("\n📤 Usuario: ¿Está disponible ese horario?");
            var response6 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "¿Está disponible ese horario?", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response6}");
            await VerifyStateContains(conversationId, "AvailabilityConfirmed");

            // Paso 7: Condiciones especiales
            Console.WriteLine("\n📤 Usuario: No tiene condiciones especiales");
            var response7 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "No tiene condiciones especiales", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response7}");

            // Paso 8: Confirmar reserva
            Console.WriteLine("\n📤 Usuario: Perfecto, confirma la reserva por favor");
            var response8 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "Perfecto, confirma la reserva por favor", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response8}");
            await VerifyStateContains(conversationId, "ReservationCreated");

            Console.WriteLine("\n✅ Test 15 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 15 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>
    /// Verifica que el estado contiene un campo específico
    /// </summary>
    private async Task VerifyStateContains(Guid conversationId, string fieldName, string? expectedValue = null)
    {
        // Obtener phone desde conversationId (necesitamos cargar la conversación)
        var conversation = await _conversationService.GetConversationByIdAsync(conversationId);
        if (conversation == null)
        {
            throw new Exception($"Conversación {conversationId} no encontrada");
        }
        
        var state = await _stateManager.GetOrCreateStateAsync(conversationId, _businessId, conversation.UserNumber, CancellationToken.None);
        
        bool found = false;
        string? actualValue = null;

        switch (fieldName.ToLowerInvariant())
        {
            case "babyage":
                found = state.Attributes.ContainsKey("BabyAge");
                if (found) actualValue = state.Attributes["BabyAge"];
                break;
            case "babyname":
                found = state.Attributes.ContainsKey("BabyName");
                if (found) actualValue = state.Attributes["BabyName"];
                break;
            case "service":
                found = !string.IsNullOrEmpty(state.Service);
                if (found) actualValue = state.Service;
                break;
            case "desireddate":
                found = state.DesiredDate.HasValue;
                if (found) actualValue = state.DesiredDate!.Value.ToString("yyyy-MM-dd");
                break;
            case "desiredtime":
                found = state.DesiredTime.HasValue;
                if (found) actualValue = state.DesiredTime!.Value.ToString("HH:mm");
                break;
            case "availabilityconfirmed":
                // Para bool, consideramos que existe si es true
                // (si es false, significa que no se ha verificado disponibilidad)
                found = state.AvailabilityConfirmed;
                actualValue = found.ToString();
                
                // Si esperamos que sea true, validar
                if (expectedValue != null && expectedValue.ToLowerInvariant() == "true" && !found)
                {
                    throw new Exception($"Campo 'AvailabilityConfirmed' es false, se esperaba true. " +
                        $"El estado actual es: Service={state.Service ?? "null"}, Date={state.DesiredDate}, Time={state.DesiredTime}");
                }
                break;
            case "reservationcreated":
                found = state.ReservationCreated;
                actualValue = found.ToString();
                break;
            default:
                // Buscar en atributos
                found = state.Attributes.ContainsKey(fieldName);
                if (found) actualValue = state.Attributes[fieldName];
                break;
        }

        if (!found)
        {
            throw new Exception($"Campo '{fieldName}' no encontrado en el estado");
        }

        if (expectedValue != null && !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Campo '{fieldName}' tiene valor '{actualValue}' pero se esperaba '{expectedValue}'");
        }

        Console.WriteLine($"   ✓ Verificado: {fieldName} = {actualValue}");
    }
}
