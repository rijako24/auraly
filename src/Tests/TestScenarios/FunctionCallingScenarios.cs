using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MimosBabySpa.Tests.TestScenarios;

/// <summary>
/// Escenarios de prueba para validar Function Calling y extracción de información del cliente.
/// Prueba casos reales de conversación para asegurar que el sistema funciona correctamente.
/// </summary>
public class FunctionCallingScenarios
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IConversationService _conversationService;
    private readonly Guid _testBusinessId;
    private readonly string _testPhoneNumber;

    public FunctionCallingScenarios(
        HybridTransactionalOrchestrator orchestrator,
        IConversationService conversationService)
    {
        _orchestrator = orchestrator;
        _conversationService = conversationService;
        _testBusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _testPhoneNumber = "+1234567890";
    }

    /// <summary>
    /// Ejecuta todos los escenarios de prueba.
    /// </summary>
    public async Task RunAllScenariosAsync()
    {
        Console.WriteLine("================================================");
        Console.WriteLine("  ESCENARIOS DE PRUEBA - FUNCTION CALLING");
        Console.WriteLine("================================================");
        Console.WriteLine();

        var scenarios = new List<(string Name, Func<Task<string>> Test)>
        {
            ("Escenario 1: Usuario proporciona edad del bebé", Scenario1_BabyAge),
            ("Escenario 2: Usuario proporciona nombre y edad", Scenario2_NameAndAge),
            ("Escenario 3: Usuario menciona preocupaciones", Scenario3_Concerns),
            ("Escenario 4: Usuario menciona permiso de redes sociales", Scenario4_SocialMedia),
            ("Escenario 5: Información completa en un mensaje", Scenario5_CompleteInfo),
            ("Escenario 6: Información en múltiples mensajes", Scenario6_MultipleMessages),
            ("Escenario 7: Usuario corrige información", Scenario7_CorrectInfo),
            ("Escenario 8: Usuario proporciona edad en diferentes formatos", Scenario8_DifferentAgeFormats),
            ("Escenario 9: Conversación natural con información dispersa", Scenario9_NaturalConversation),
            ("Escenario 10: Usuario menciona edad después de saludo", Scenario10_AgeAfterGreeting),
            ("Escenario 11: Usuario menciona múltiples preocupaciones", Scenario11_MultipleConcerns),
            ("Escenario 12: Usuario proporciona información parcial", Scenario12_PartialInfo),
            ("Escenario 13: Usuario menciona edad y preocupación juntos", Scenario13_AgeAndConcern),
            ("Escenario 14: Usuario menciona nombre del padre", Scenario14_ParentName),
            ("Escenario 15: Conversación con información implícita", Scenario15_ImplicitInfo),
            ("Escenario 16: Usuario menciona edad en contexto de pregunta", Scenario16_AgeInQuestion),
            ("Escenario 17: Usuario proporciona información en orden inverso", Scenario17_ReverseOrder),
            ("Escenario 18: Usuario menciona información con negación", Scenario18_WithNegation),
            ("Escenario 19: Conversación larga con múltiples extracciones", Scenario19_LongConversation),
            ("Escenario 20: Usuario menciona información con variaciones lingüísticas", Scenario20_LinguisticVariations)
        };

        var passed = 0;
        var failed = 0;

        for (int i = 0; i < scenarios.Count; i++)
        {
            var (name, test) = scenarios[i];
            Console.WriteLine($"[{i + 1}/20] {name}");
            Console.WriteLine("─".PadRight(80, '─'));

            try
            {
                var response = await test();
                Console.WriteLine($"✅ PASÓ");
                Console.WriteLine($"Respuesta: {response.Substring(0, Math.Min(100, response.Length))}...");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FALLÓ: {ex.Message}");
                failed++;
            }

            Console.WriteLine();
            await Task.Delay(500); // Pequeña pausa entre escenarios
        }

        Console.WriteLine("================================================");
        Console.WriteLine($"RESUMEN: {passed} pasaron, {failed} fallaron de {scenarios.Count} escenarios");
        Console.WriteLine("================================================");
    }

    private async Task<string> Scenario1_BabyAge()
    {
        var phone = _testPhoneNumber;
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "hola tengo un bebe de 4 meses");
    }

    private async Task<string> Scenario2_NameAndAge()
    {
        var phone = _testPhoneNumber + "1";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Hola, soy María y mi bebé tiene 6 meses");
    }

    private async Task<string> Scenario3_Concerns()
    {
        var phone = _testPhoneNumber + "2";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi bebé tiene cólicos y problemas para dormir");
    }

    private async Task<string> Scenario4_SocialMedia()
    {
        var phone = _testPhoneNumber + "3";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Hola, mi bebé tiene 3 meses y sí permito que publiquen fotos en redes sociales");
    }

    private async Task<string> Scenario5_CompleteInfo()
    {
        var phone = _testPhoneNumber + "4";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Soy Juan, mi bebé tiene 5 meses, tiene cólicos y sí permito redes sociales");
    }

    private async Task<string> Scenario6_MultipleMessages()
    {
        var phone = _testPhoneNumber + "5";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        var conversationId = conv.ConversationId;
        
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Hola");
        return await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Mi bebé tiene 7 meses");
    }

    private async Task<string> Scenario7_CorrectInfo()
    {
        var phone = _testPhoneNumber + "6";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        var conversationId = conv.ConversationId;
        
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Mi bebé tiene 4 meses");
        return await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Perdón, tiene 5 meses");
    }

    private async Task<string> Scenario8_DifferentAgeFormats()
    {
        var phone = _testPhoneNumber + "7";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi pequeñín acaba de cumplir 4 mesecitos");
    }

    private async Task<string> Scenario9_NaturalConversation()
    {
        var phone = _testPhoneNumber + "8";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Hola, buenos días. Tengo un bebé de 8 meses y está muy inquieto últimamente");
    }

    private async Task<string> Scenario10_AgeAfterGreeting()
    {
        var phone = _testPhoneNumber + "9";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        var conversationId = conv.ConversationId;
        
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Hola");
        return await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Mi bebé tiene 9 meses");
    }

    private async Task<string> Scenario11_MultipleConcerns()
    {
        var phone = _testPhoneNumber + "10";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi bebé tiene reflujo, cólicos y problemas para dormir");
    }

    private async Task<string> Scenario12_PartialInfo()
    {
        var phone = _testPhoneNumber + "11";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Hola, mi nombre es Carlos");
    }

    private async Task<string> Scenario13_AgeAndConcern()
    {
        var phone = _testPhoneNumber + "12";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi bebé de 10 meses tiene mucho estrés");
    }

    private async Task<string> Scenario14_ParentName()
    {
        var phone = _testPhoneNumber + "13";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Hola, soy Laura, la mamá de un bebé de 11 meses");
    }

    private async Task<string> Scenario15_ImplicitInfo()
    {
        var phone = _testPhoneNumber + "14";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi pequeñín tiene 12 meses y le encanta el agua");
    }

    private async Task<string> Scenario16_AgeInQuestion()
    {
        var phone = _testPhoneNumber + "15";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "¿Tienen servicios para bebés de 2 meses?");
    }

    private async Task<string> Scenario17_ReverseOrder()
    {
        var phone = _testPhoneNumber + "16";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Tiene problemas de sueño, mi bebé de 13 meses");
    }

    private async Task<string> Scenario18_WithNegation()
    {
        var phone = _testPhoneNumber + "17";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Mi bebé tiene 14 meses y no tiene cólicos pero sí tiene problemas para dormir");
    }

    private async Task<string> Scenario19_LongConversation()
    {
        var phone = _testPhoneNumber + "18";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        var conversationId = conv.ConversationId;
        
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Hola");
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Soy Pedro");
        await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Mi bebé tiene 15 meses");
        return await _orchestrator.ProcessMessageAsync(conversationId, _testBusinessId, phone, "Tiene cólicos");
    }

    private async Task<string> Scenario20_LinguisticVariations()
    {
        var phone = _testPhoneNumber + "19";
        var conv = await _conversationService.GetOrCreateConversationAsync(_testBusinessId, phone);
        return await _orchestrator.ProcessMessageAsync(conv.ConversationId, _testBusinessId, phone, "Buenas, el peque tiene 16 mesecitos y está muy tenso últimamente");
    }
}
