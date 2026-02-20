using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Services;
using System.Text.RegularExpressions;

namespace MimosBabySpa.Tests.TestScenarios;

/// <summary>
/// Pruebas de comportamiento conversacional del bot
/// Valida las mejoras implementadas para evitar antipatrones:
/// 1. Saludo contextual (no repetitivo)
/// 2. Verificación automática de disponibilidad
/// 3. No promesas falsas (no prometer acciones no ejecutadas)
/// 4. Horarios reales del backend (no inventados)
/// 5. Inferencia de referencias implícitas
/// </summary>
public class ConversationalBehaviorTests
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationService _conversationService;
    private readonly Guid _businessId;
    private readonly ILogger<ConversationalBehaviorTests> _logger;

    public ConversationalBehaviorTests(
        HybridTransactionalOrchestrator orchestrator,
        IConversationStateManager stateManager,
        IConversationService conversationService,
        ILogger<ConversationalBehaviorTests> logger)
    {
        _orchestrator = orchestrator;
        _stateManager = stateManager;
        _conversationService = conversationService;
        _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222"); // BusinessId por defecto
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta todas las pruebas de comportamiento conversacional
    /// </summary>
    public async Task RunAllTestsAsync()
    {
        Console.WriteLine("================================================");
        Console.WriteLine("  PRUEBAS DE COMPORTAMIENTO CONVERSACIONAL");
        Console.WriteLine("================================================");
        Console.WriteLine();

        var results = new List<(string TestName, bool Success, string Error)>();

        try
        {
            results.Add(await Test1_GreetingBehaviorAsync());
            results.Add(await Test2_AutomaticAvailabilityCheckAsync());
            results.Add(await Test3_NoFalsePromisesAsync());
            results.Add(await Test4_RealBackendTimeSlotsAsync());
            results.Add(await Test5_ImplicitReferenceInferenceAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando pruebas de comportamiento conversacional");
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
    /// Test 1: Validar que el bot saluda correctamente y NO repite saludos
    /// </summary>
    public async Task<(string TestName, bool Success, string Error)> Test1_GreetingBehaviorAsync()
    {
        var testName = "Test 1: Comportamiento de saludo contextual";
        // Usar un número único con timestamp para evitar colisiones con ejecuciones previas
        var phone = $"+5555000001-{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            // Crear conversación nueva
            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;
            
            // Verificar que es realmente el primer mensaje (estado vacío)
            var initialState = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);
            
            if (!string.IsNullOrEmpty(initialState.CustomerName) || 
                !string.IsNullOrEmpty(initialState.Service))
            {
                Console.WriteLine($"   ⚠️ Advertencia: El estado no está vacío al inicio. Esto podría afectar el test.");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 1: PRIMER MENSAJE - Debe saludar y presentarse
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: Hola tengo un bebé de 5 meses que planes me puedes ofrecer");
            var response1.Response = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "Hola tengo un bebé de 5 meses que planes me puedes ofrecer",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1.Response}");

            // ASSERTIONS
            if (!response1.Response.Contains("Hola", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Primera respuesta debe contener saludo 'Hola'");
            }

            if (!response1.Response.Contains("María", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Primera respuesta debe presentarse como 'María'");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 2: SEGUNDO MENSAJE - NO debe volver a saludar
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: me llamo richard, para mañana estaria bien");
            var response2 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "me llamo richard, para mañana estaria bien",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2.Response}");

            // ASSERTIONS: NO debe contener saludos repetitivos
            if (response2.Response.Contains("¡Hola!", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Segunda respuesta NO debe contener '¡Hola!'");
            }

            if (response2.Response.Contains("¡Hola Richard!", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Segunda respuesta NO debe contener '¡Hola Richard!'");
            }

            // Debe usar transiciones naturales
            var naturalTransitions = new[] { "Perfecto", "Genial", "Entendido", "Claro", "Excelente" };
            var hasNaturalTransition = naturalTransitions.Any(t => response2.Response.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!hasNaturalTransition)
            {
                Console.WriteLine($"   ⚠️ Advertencia: No se detectó transición natural en respuesta");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 3: TERCER MENSAJE - Sigue sin saludar
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: el que me recomendaste");
            var response3 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "el que me recomendaste",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3.Response}");

            // ASSERTIONS: NO debe saludar nuevamente
            if (response3.Response.Contains("¡Hola", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Tercera respuesta NO debe contener '¡Hola'");
            }

            // Verificar que el nombre se usa ocasionalmente (no en cada mensaje)
            var richardCount = CountOccurrences(response2.Response + response3.Response, "Richard");
            if (richardCount > 2)
            {
                Console.WriteLine($"   ⚠️ Advertencia: El nombre 'Richard' aparece {richardCount} veces (idealmente ≤ 2)");
            }

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
    /// Test 2: Validar que el bot verifica disponibilidad cuando debe
    /// </summary>
    public async Task<(string TestName, bool Success, string Error)> Test2_AutomaticAvailabilityCheckAsync()
    {
        var testName = "Test 2: Verificación automática de disponibilidad";
        var phone = "+5555000002";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // ═══════════════════════════════════════════════════════
            // PASO 1: Usuario proporciona TODOS los datos necesarios
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: Hola soy Juan, quiero el Plan Marineritos para mañana");
            var response1.Response = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "Hola soy Juan, quiero el Plan Marineritos para mañana",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1.Response}");
            
            // Verificar que se extrajo la información básica
            var stateAfterFirst = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);
            
            if (string.IsNullOrEmpty(stateAfterFirst.Service))
            {
                throw new Exception(
                    $"El servicio debe haberse extraído del primer mensaje. " +
                    $"Estado: Service={stateAfterFirst.Service}, Date={stateAfterFirst.DesiredDate}");
            }
            
            if (!stateAfterFirst.DesiredDate.HasValue)
            {
                throw new Exception(
                    $"La fecha debe haberse extraído del primer mensaje. " +
                    $"Estado: Service={stateAfterFirst.Service}, Date={stateAfterFirst.DesiredDate}");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 2: Bot debe MOSTRAR horarios disponibles
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: que horas tienes disponible");
            var response2 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "que horas tienes disponible",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2.Response}");

            // Verificar estado
            var state = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);

            // ASSERTIONS CRÍTICAS
            if (!state.AvailabilityConfirmed)
            {
                throw new Exception(
                    $"AvailabilityConfirmed debe ser true después de verificar disponibilidad. " +
                    $"Estado: Service={state.Service}, Date={state.DesiredDate}, Time={state.DesiredTime}");
            }

            if (string.IsNullOrEmpty(state.AvailableTimeSlots))
            {
                throw new Exception("AvailableTimeSlots debe contener horarios del backend");
            }

            // ASSERTIONS en la respuesta
            if (!ContainsTimeSlots(response2.Response))
            {
                throw new Exception(
                    "La respuesta debe MOSTRAR los horarios disponibles, no solo decir 'hay disponibilidad'. " +
                    $"Respuesta: {response2}");
            }

            // Ejemplo: debe contener formato de horarios como "9:00", "11:00", etc.
            var timePattern = @"\b\d{1,2}:\d{2}\b";
            if (!Regex.IsMatch(response2, timePattern))
            {
                throw new Exception($"La respuesta debe contener horarios en formato HH:MM. Respuesta: {response2}");
            }

            // NO debe decir solo "Sí hay disponibilidad" sin mostrar horarios
            if (response2.Response.Contains("disponibilidad", StringComparison.OrdinalIgnoreCase) &&
                !ContainsTimeSlots(response2.Response))
            {
                throw new Exception(
                    "Si menciona disponibilidad, debe MOSTRAR los horarios específicos. " +
                    $"Respuesta: {response2}");
            }

            Console.WriteLine($"   ✓ Disponibilidad confirmada: {state.AvailabilityConfirmed}");
            Console.WriteLine($"   ✓ Horarios disponibles: {state.AvailableTimeSlots}");

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
    /// Test 3: Validar que el bot NO promete acciones que no ejecuta
    /// </summary>
    public async Task<(string TestName, bool Success, string Error)> Test3_NoFalsePromisesAsync()
    {
        var testName = "Test 3: No promesas falsas";
        var phone = "+5555000003";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // ═══════════════════════════════════════════════════════
            // SETUP: Configurar datos completos
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: Hola soy Richard, quiero el Plan Marineritos para mañana");
            await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "Hola soy Richard, quiero el Plan Marineritos para mañana",
                CancellationToken.None);

            Console.WriteLine("\n📤 Usuario: mi bebe se llama thomas, que horas tienes disponible");
            var response2 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "mi bebe se llama thomas, que horas tienes disponible",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2.Response}");

            // ═══════════════════════════════════════════════════════
            // PASO CRÍTICO: Usuario selecciona horario
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: a las 9 esta bien");
            var response3 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "a las 9 esta bien",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3.Response}");

            // Verificar estado ANTES de confirmación
            var stateBeforeConfirm = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);

            // ASSERTIONS CRÍTICAS: NO debe haber creado reserva aún
            if (stateBeforeConfirm.ReservationCreated)
            {
                throw new Exception(
                    "ReservationCreated NO debe ser true si el usuario no confirmó explícitamente. " +
                    $"Estado: ReservationConfirmed={stateBeforeConfirm.ReservationConfirmed}");
            }

            // ASSERTIONS en la respuesta: NO debe prometer sin ejecutar
            var forbiddenPhrases = new[]
            {
                "Ahora procederé a confirmar",
                "voy a confirmar",
                "estoy confirmando",
                "procederé a crear",
                "procederé a reservar"
            };

            foreach (var phrase in forbiddenPhrases)
            {
                if (response3.Response.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"La respuesta NO debe contener '{phrase}' sin ejecutar la acción. " +
                        $"Respuesta: {response3}");
                }
            }

            // Debe PREGUNTAR si confirma
            var confirmationQuestions = new[]
            {
                "¿Confirmo tu reserva?",
                "¿Te gustaría que confirme?",
                "¿Deseas confirmar?",
                "¿Procedo con la reserva?",
                "¿Confirmo?"
            };

            var hasConfirmationQuestion = confirmationQuestions.Any(q =>
                response3.Response.Contains(q, StringComparison.OrdinalIgnoreCase));

            if (!hasConfirmationQuestion)
            {
                Console.WriteLine($"   ⚠️ Advertencia: No se detectó pregunta de confirmación explícita");
                Console.WriteLine($"   Respuesta: {response3}");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 4: Usuario confirma EXPLÍCITAMENTE
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: sí, confirma la reserva");
            var response4 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "sí, confirma la reserva",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response4}");

            // Verificar estado DESPUÉS de confirmación
            var stateAfterConfirm = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);

            // AHORA SÍ debe estar creada (si todos los datos están completos)
            if (!stateAfterConfirm.ReservationCreated)
            {
                Console.WriteLine($"   ⚠️ Advertencia: ReservationCreated es false después de confirmación");
                Console.WriteLine($"   Estado: ReservationConfirmed={stateAfterConfirm.ReservationConfirmed}, " +
                    $"Service={stateAfterConfirm.Service}, Date={stateAfterConfirm.DesiredDate}, " +
                    $"Time={stateAfterConfirm.DesiredTime}, CustomerName={stateAfterConfirm.CustomerName}");
            }

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
    /// Test 4: Validar que los horarios son del backend, no inventados
    /// </summary>
    public async Task<(string TestName, bool Success, string Error)> Test4_RealBackendTimeSlotsAsync()
    {
        var testName = "Test 4: Horarios del backend (no inventados)";
        var phone = "+5555000004";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // ═══════════════════════════════════════════════════════
            // PASO 1: Solicitar disponibilidad
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: Hola soy Carlos, quiero el Plan Marineritos para mañana");
            await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "Hola soy Carlos, quiero el Plan Marineritos para mañana",
                CancellationToken.None);

            Console.WriteLine("\n📤 Usuario: que horarios tienes libres");
            var response2 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "que horarios tienes libres",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response2.Response}");

            // Obtener estado
            var state = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);

            // ═══════════════════════════════════════════════════════
            // VALIDAR: Horarios del estado vs respuesta
            // ═══════════════════════════════════════════════════════
            if (string.IsNullOrEmpty(state.AvailableTimeSlots))
            {
                throw new Exception("AvailableTimeSlots debe tener horarios del backend");
            }

            // Parsear horarios del estado
            var backendSlots = state.AvailableTimeSlots
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            if (backendSlots.Count == 0)
            {
                throw new Exception("Debe haber al menos 1 horario del backend");
            }

            Console.WriteLine($"   ✓ Horarios del backend: {string.Join(", ", backendSlots)}");

            // VALIDAR que TODOS los horarios del estado están en la respuesta
            foreach (var slot in backendSlots)
            {
                if (!response2.Response.Contains(slot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"El horario '{slot}' del backend debe aparecer en la respuesta. " +
                        $"Respuesta: {response2}");
                }
            }

            // ═══════════════════════════════════════════════════════
            // VALIDAR: NO hay horarios inventados
            // ═══════════════════════════════════════════════════════
            var responseTimeSlots = ExtractTimeSlots(response2.Response);

            foreach (var responseSlot in responseTimeSlots)
            {
                var normalizedResponseSlot = NormalizeTimeSlot(responseSlot);
                var foundInBackend = backendSlots.Any(bs => NormalizeTimeSlot(bs) == normalizedResponseSlot);

                if (!foundInBackend)
                {
                    Console.WriteLine($"   ⚠️ Advertencia: El horario '{responseSlot}' en la respuesta no está exactamente en el backend");
                    Console.WriteLine($"   Backend tiene: [{string.Join(", ", backendSlots)}]");
                }
            }

            Console.WriteLine($"   ✓ Horarios en respuesta: {string.Join(", ", responseTimeSlots)}");

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
    /// Test 5: Validar inferencia de referencias implícitas
    /// </summary>
    public async Task<(string TestName, bool Success, string Error)> Test5_ImplicitReferenceInferenceAsync()
    {
        var testName = "Test 5: Inferencia de referencias implícitas";
        var phone = "+5555000005";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // ═══════════════════════════════════════════════════════
            // PASO 1: Bot recomienda un servicio específico
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: Hola tengo un bebé de 5 meses que planes me puedes ofrecer");
            var response1.Response = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "Hola tengo un bebé de 5 meses que planes me puedes ofrecer",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response1.Response}");

            // Verificar que recomendó Plan Marineritos
            if (!response1.Response.Contains("Plan Marineritos", StringComparison.OrdinalIgnoreCase) &&
                !response1.Response.Contains("Marineritos", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   ⚠️ Advertencia: No se detectó recomendación de 'Plan Marineritos' en primera respuesta");
            }

            // ═══════════════════════════════════════════════════════
            // PASO 2: Usuario dice "el que me recomendaste"
            // ═══════════════════════════════════════════════════════
            Console.WriteLine("\n📤 Usuario: me llamo richard, para mañana estaria bien");
            await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "me llamo richard, para mañana estaria bien",
                CancellationToken.None);

            Console.WriteLine("\n📤 Usuario: el que me recomendaste");
            var response3 = await _orchestrator.ProcessMessageAsync(
                conversationId, _businessId, phone,
                "el que me recomendaste",
                CancellationToken.None);
            Console.WriteLine($"📥 Bot: {response3.Response}");

            // Verificar estado
            var state = await _stateManager.GetOrCreateStateAsync(
                conversationId, _businessId, phone, CancellationToken.None);

            // ASSERTIONS CRÍTICAS
            if (string.IsNullOrEmpty(state.Service))
            {
                throw new Exception(
                    "Debe haber inferido el servicio de la referencia implícita 'el que me recomendaste'. " +
                    $"Estado actual: Service={state.Service}");
            }

            if (!state.Service.Contains("Marineritos", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   ⚠️ Advertencia: Service inferido es '{state.Service}', se esperaba 'Plan Marineritos'");
            }

            if (!response3.Response.Contains(state.Service, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   ⚠️ Advertencia: La respuesta debe mencionar el servicio inferido '{state.Service}'");
            }

            Console.WriteLine($"   ✓ Servicio inferido: {state.Service}");

            // ═══════════════════════════════════════════════════════
            // VARIANTE: Probar "ese está bien"
            // ═══════════════════════════════════════════════════════
            var phone2 = "+5555000006";
            var conversation2 = await _conversationService.GetOrCreateConversationAsync(_businessId, phone2);
            var conversationId2 = conversation2.ConversationId;

            Console.WriteLine("\n📤 Usuario (variante): Hola, tengo un bebé de 5 meses");
            await _orchestrator.ProcessMessageAsync(
                conversationId2, _businessId, phone2,
                "Hola, tengo un bebé de 5 meses",
                CancellationToken.None);

            Console.WriteLine("\n📤 Usuario (variante): ese esta bien");
            await _orchestrator.ProcessMessageAsync(
                conversationId2, _businessId, phone2,
                "ese esta bien",
                CancellationToken.None);

            var state2 = await _stateManager.GetOrCreateStateAsync(
                conversationId2, _businessId, phone2, CancellationToken.None);

            if (!string.IsNullOrEmpty(state2.Service))
            {
                Console.WriteLine($"   ✓ Servicio inferido de 'ese está bien': {state2.Service}");
            }
            else
            {
                Console.WriteLine($"   ⚠️ Advertencia: No se infirió servicio de 'ese está bien'");
            }

            Console.WriteLine("\n✅ Test 5 completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test 5 falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════

    private bool ContainsTimeSlots(string response)
    {
        // Verificar que contiene al menos 2 horarios en formato HH:MM
        var timePattern = @"\b\d{1,2}:\d{2}\b";
        var matches = Regex.Matches(response, timePattern);
        return matches.Count >= 2;
    }

    private List<string> ExtractTimeSlots(string response)
    {
        // Extraer todos los horarios en formato HH:MM o H:MM
        var pattern = @"\b(\d{1,2}:\d{2})\b";
        var matches = Regex.Matches(response, pattern);
        return matches.Select(m => m.Value).Distinct().ToList();
    }

    private string NormalizeTimeSlot(string slot)
    {
        // Normalizar formato de hora para comparación
        // "9:00" -> "09:00", "09:00" -> "09:00"
        if (TimeOnly.TryParse(slot, out var time))
        {
            return time.ToString("HH:mm");
        }
        return slot.Trim();
    }

    private int CountOccurrences(string text, string word)
    {
        return Regex.Matches(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase).Count;
    }
}
