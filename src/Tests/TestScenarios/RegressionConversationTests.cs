using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Services;
using System.Text.RegularExpressions;

namespace MimosBabySpa.Tests.TestScenarios;

/// <summary>
/// Pruebas de regresión a partir de conversaciones reales que expusieron bugs.
/// Documentan los flujos exactos y validan invariantes críticas que las pruebas
/// anteriores no cubrían.
/// </summary>
public class RegressionConversationTests
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationService _conversationService;
    private readonly Guid _businessId;
    private readonly ILogger<RegressionConversationTests> _logger;

    public RegressionConversationTests(
        HybridTransactionalOrchestrator orchestrator,
        IConversationStateManager stateManager,
        IConversationService conversationService,
        ILogger<RegressionConversationTests> logger)
    {
        _orchestrator = orchestrator;
        _stateManager = stateManager;
        _conversationService = conversationService;
        _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _logger = logger;
    }

    public async Task RunAllTestsAsync()
    {
        Console.WriteLine("================================================");
        Console.WriteLine("  PRUEBAS DE REGRESIÓN (Conversaciones Reales)");
        Console.WriteLine("================================================");
        Console.WriteLine();

        var results = new List<(string TestName, bool Success, string Error)>();

        try
        {
            results.Add(await RunTest_ThomasConversation_SlotUnavailableAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando pruebas de regresión");
            Console.WriteLine($"❌ Error crítico: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("================================================");
        Console.WriteLine("  RESUMEN");
        Console.WriteLine("================================================");
        var passed = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        Console.WriteLine($"Total: {results.Count} | ✅ {passed} | ❌ {failed}");
        Console.WriteLine();
        foreach (var (testName, success, error) in results)
        {
            var icon = success ? "✅" : "❌";
            Console.WriteLine($"{icon} {testName}");
            if (!success && !string.IsNullOrEmpty(error))
                Console.WriteLine($"   Error: {error}");
        }
    }

    /// <summary>
    /// Conversación Thomas — Bug: slot 09:00 ya estaba reservado pero el bot afirmaba disponibilidad,
    /// nunca llamó a CreateReservation y decía "confirmo" sin haber creado la reserva.
    /// Invariantes: cuando AvailabilityConfirmed=false con alternativas, el bot NO debe afirmar que el slot está disponible.
    /// Cuando el bot dice "confirmo/agendado", ReservationCreated DEBE ser true.
    /// </summary>
    private async Task<(string TestName, bool Success, string Error)> RunTest_ThomasConversation_SlotUnavailableAsync()
    {
        var testName = "Regresión: Thomas — slot ocupado, add-ons, confirmación falsa";
        var phone = $"+5555999{DateTime.UtcNow:HHmmss}";

        try
        {
            Console.WriteLine($"\n🧪 {testName}");
            Console.WriteLine("────────────────────────────────────────");

            var conversation = await _conversationService.GetOrCreateConversationAsync(_businessId, phone);
            var conversationId = conversation.ConversationId;

            // ── Paso 1 ──
            Console.WriteLine("\n📤 Usuario: hola tengo un bebe de 5 meses");
            var r1 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone,
                "hola tengo un bebe de 5 meses", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r1.Response}");

            // ── Paso 2 ──
            Console.WriteLine("\n📤 Usuario: si");
            var r2 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "si", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r2.Response}");

            // ── Paso 3 ──
            Console.WriteLine("\n📤 Usuario: explicame mas sobre el plan y esas decoraciones");
            var r3 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone,
                "explicame mas sobre el plan y esas decoraciones", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r3.Response}");

            // ── Paso 4 ──
            Console.WriteLine("\n📤 Usuario: si con la sencilla");
            var r4 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone,
                "si con la sencilla", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r4.Response}");

            // ── Paso 5: Usuario pide disponibilidad para mañana 09:00 ──
            Console.WriteLine("\n📤 Usuario: para mañana a las 9 tienes disponibilidad");
            var r5 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone,
                "para mañana a las 9 tienes disponibilidad", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r5.Response}");

            var stateAfter5 = await _stateManager.GetOrCreateStateAsync(conversationId, _businessId, phone, CancellationToken.None);

            // INVARIANTE: Si 09:00 NO está disponible, el bot NO debe afirmar que SÍ lo está
            if (!stateAfter5.AvailabilityConfirmed && !string.IsNullOrEmpty(stateAfter5.AvailableTimeSlots))
            {
                if (ImpliesSlotAvailable(r5.Response, "09:00"))
                {
                    throw new Exception(
                        "INVARIANTE: El slot 09:00 NO está disponible pero el bot afirmó que sí. " +
                        "Debe indicar que no está disponible y mostrar alternativas. " +
                        $"Alternativas del sistema: {stateAfter5.AvailableTimeSlots}");
                }
                Console.WriteLine($"   ✓ Slot 09:00 rechazado correctamente. Alternativas: {stateAfter5.AvailableTimeSlots}");
            }

            // ── Paso 6 ──
            Console.WriteLine("\n📤 Usuario: a las 9");
            var r6 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "a las 9", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r6.Response}");

            // Si seguimos con slot rechazado, el bot debe presentar alternativas, no "agendar" a las 9
            var stateAfter6 = await _stateManager.GetOrCreateStateAsync(conversationId, _businessId, phone, CancellationToken.None);
            if (!stateAfter6.AvailabilityConfirmed && ImpliesReservationConfirmed(r6.Response))
            {
                throw new Exception(
                    "INVARIANTE: El bot NO debe decir que agendó/confirmó cuando AvailabilityConfirmed=false. " +
                    $"Respuesta: {r6.Response}");
            }

            // ── Paso 7 ──
            Console.WriteLine("\n📤 Usuario: thomas");
            var r7 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "thomas", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r7.Response}");

            // ── Paso 8: Usuario confirma ──
            Console.WriteLine("\n📤 Usuario: si");
            var r8 = await _orchestrator.ProcessMessageAsync(conversationId, _businessId, phone, "si", CancellationToken.None);
            Console.WriteLine($"📥 Bot: {r8.Response}");

            var stateFinal = await _stateManager.GetOrCreateStateAsync(conversationId, _businessId, phone, CancellationToken.None);

            // INVARIANTE CRÍTICA: Si el bot dice "confirmo/agendado/te esperamos", ReservationCreated DEBE ser true
            if (ImpliesReservationConfirmed(r8.Response) && !stateFinal.ReservationCreated)
            {
                throw new Exception(
                    "INVARIANTE CRÍTICA: El bot afirmó que confirmó/agendó pero ReservationCreated=false. " +
                    "Nunca se llamó a CreateReservation. " +
                    $"Estado: AvailabilityConfirmed={stateFinal.AvailabilityConfirmed}, " +
                    $"ReservationConfirmed={stateFinal.ReservationConfirmed}");
            }

            if (stateFinal.ReservationCreated)
                Console.WriteLine($"   ✓ Reserva creada correctamente: {stateFinal.ReservationId}");
            else
                Console.WriteLine($"   ✓ Sin reserva (slot no disponible) — bot no hizo promesas falsas");

            Console.WriteLine("\n✅ Test Thomas completado exitosamente");
            return (testName, true, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test Thomas falló: {ex.Message}");
            return (testName, false, ex.Message);
        }
    }

    /// <summary>Detecta si la respuesta implica que un slot específico está disponible (falsa promesa cuando el slot fue rechazado).</summary>
    private static bool ImpliesSlotAvailable(string response, string timeSlot)
    {
        var normalized = response.ToLowerInvariant();
        var slot = timeSlot.ToLowerInvariant();
        // Patrones que indican "este slot está disponible" (bug cuando en realidad fue rechazado)
        return normalized.Contains($"{slot} está disponible")
            || normalized.Contains($"disponibilidad para mañana a las {slot}")
            || normalized.Contains($"tengo disponibilidad para mañana a las {slot}");
    }

    /// <summary>Detecta si la respuesta implica que la reserva fue confirmada/creada.</summary>
    private static bool ImpliesReservationConfirmed(string response)
    {
        var normalized = response.ToLowerInvariant();
        var phrases = new[]
        {
            "confirmo tu reserva",
            "queda confirmada",
            "te agendo",
            "te esperamos",
            "reserva confirmada",
            "agendado para",
            "listo, te esperamos"
        };
        return phrases.Any(p => normalized.Contains(p));
    }
}
