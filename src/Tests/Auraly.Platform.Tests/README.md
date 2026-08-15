# Pruebas Unitarias - Auraly

Este proyecto contiene las pruebas unitarias para validar el flujo mínimo del sistema de chatbot de WhatsApp.

## Estructura de Pruebas

### Functions
- `WhatsAppWebhookFunctionTests.cs`: Pruebas del webhook principal que validan:
  - Verificación del webhook (GET)
  - Procesamiento de mensajes entrantes (POST)
  - Flujo completo de procesamiento de mensajes
  - Manejo de transferencia a humano
  - Manejo de errores

### Services
- `MessageServiceTests.cs`: Pruebas del servicio de mensajes
- `LeadServiceTests.cs`: Pruebas del servicio de leads
- `ConversationServiceTests.cs`: Pruebas del servicio de conversaciones

## Ejecutar Pruebas

```powershell
cd src/Tests/Auraly.Platform.Tests
dotnet test
```

## Ejecutar Pruebas con Cobertura

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

## Flujo Mínimo Validado

Las pruebas validan el siguiente flujo mínimo:

1. **Recepción de mensaje**: El webhook recibe un mensaje de WhatsApp
2. **Creación/Obtención de conversación**: Se obtiene o crea una conversación para el usuario
3. **Creación/Obtención de lead**: Se obtiene o crea un lead para el usuario
4. **Clasificación de intención**: Se clasifica la intención del mensaje usando IA
5. **Guardado de mensaje del usuario**: Se guarda el mensaje recibido
6. **Generación de respuesta**: Se genera una respuesta usando IA
7. **Guardado de respuesta del bot**: Se guarda la respuesta generada
8. **Actualización de contexto**: Se actualiza el contexto de la conversación
9. **Envío de respuesta**: Se envía la respuesta al usuario
10. **Actualización de estado del lead**: Se actualiza el estado del lead según la intención

## Tecnologías Utilizadas

- **xUnit**: Framework de pruebas
- **Moq**: Framework de mocking
- **FluentAssertions**: Librería de aserciones más legibles
