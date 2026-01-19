using System;
using System.IO;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;

// Leer configuración desde local.settings.json
var currentDir = Directory.GetCurrentDirectory();
var apiPath = Path.Combine(currentDir, "..", "src", "API", "MimosBabySpa.API", "local.settings.json");

if (!File.Exists(apiPath))
{
    Console.WriteLine($"❌ Error: No se encontró el archivo local.settings.json en: {apiPath}");
    Environment.Exit(1);
}

var jsonContent = File.ReadAllText(apiPath);
using var doc = JsonDocument.Parse(jsonContent);

var values = doc.RootElement.GetProperty("Values");
var endpoint = values.GetProperty("OpenAI:Endpoint").GetString();
var apiKey = values.GetProperty("OpenAI:ApiKey").GetString();
var deploymentName = values.GetProperty("OpenAI:TextDeploymentName").GetString();

if (string.IsNullOrEmpty(endpoint) || endpoint.Contains("<"))
{
    Console.WriteLine("❌ Error: OpenAI:Endpoint no está configurado correctamente en local.settings.json");
    Environment.Exit(1);
}

if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("<"))
{
    Console.WriteLine("❌ Error: OpenAI:ApiKey no está configurado correctamente en local.settings.json");
    Environment.Exit(1);
}

Console.WriteLine("🔌 Conectando a Azure OpenAI...");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine($"   Deployment: {deploymentName}");
Console.WriteLine();

try
{
    var client = new OpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
    
    Console.WriteLine("✅ Conexión establecida correctamente");
    Console.WriteLine();
    Console.WriteLine("💬 Enviando pregunta de prueba...");
    Console.WriteLine("   Pregunta: 'Hola, ¿qué servicios ofrecen?'");
    Console.WriteLine();
    
    var chatMessages = new List<ChatRequestMessage>
    {
        new ChatRequestSystemMessage("Eres un asistente amable y profesional."),
        new ChatRequestUserMessage("Hola, ¿qué servicios ofrecen?")
    };
    
    var options = new ChatCompletionsOptions(deploymentName!, chatMessages)
    {
        Temperature = 0.7f,
        MaxTokens = 200
    };
    
    var response = await client.GetChatCompletionsAsync(options);
    var answer = response.Value.Choices[0].Message.Content;
    
    Console.WriteLine("✅ Respuesta recibida:");
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.WriteLine(answer);
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.WriteLine();
    Console.WriteLine("🎉 ¡Prueba exitosa! Azure OpenAI está funcionando correctamente.");
}
catch (Exception ex)
{
    Console.WriteLine("❌ Error al conectar con Azure OpenAI:");
    Console.WriteLine($"   {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Detalle: {ex.InnerException.Message}");
    }
    Console.WriteLine();
    Console.WriteLine("Verifica:");
    Console.WriteLine("   1. Que el Endpoint sea correcto");
    Console.WriteLine("   2. Que la ApiKey sea válida");
    Console.WriteLine("   3. Que el TextDeploymentName exista en Azure");
    Console.WriteLine("   4. Que tengas acceso a los modelos (gpt-4)");
    Environment.Exit(1);
}
