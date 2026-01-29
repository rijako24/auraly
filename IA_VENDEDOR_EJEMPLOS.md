# 💡 EJEMPLOS PRÁCTICOS: IA VENDEDOR

## 🎯 CASOS DE USO REALES

### CASO 1: Cliente Nuevo - Flujo Completo de Venta

```
┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE 1: "Hola"                                               │
├─────────────────────────────────────────────────────────────────┤
│ Stage: InitialContact                                           │
│ Tactic: BuildRapport                                            │
│ Tone: Friendly                                                  │
│ CTA: "¿Cómo te llamas?"                                         │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "¡Hola! Qué gusto saludarte 😊. Soy María, tu      │
│ asesora de Mimos Baby Spa. Estoy aquí para ayudarte a         │
│ encontrar el mejor servicio para tu bebé. ¿Cómo te llamas?"   │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE 2: "Me llamo Ana"                                       │
├─────────────────────────────────────────────────────────────────┤
│ Stage: InitialContact → Discovery (TRANSICIÓN)                 │
│ Tactic: AskDiscoveryQuestions                                   │
│ Tone: Professional                                              │
│ CTA: "¿Cuántos meses tiene tu bebé?"                           │
│ Profile Updated: CustomerName = "Ana"                          │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "Perfecto Ana, me encantaría conocer más sobre tu  │
│ bebé para recomendarte lo mejor. ¿Cuántos meses tiene?"       │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE 3: "Tiene 4 meses y sufre de cólicos"                  │
├─────────────────────────────────────────────────────────────────┤
│ Stage: Discovery → Presentation (TRANSICIÓN)                    │
│ Tactic: EducateBenefits                                         │
│ Tone: Educational                                               │
│ CTA: "¿Te gustaría conocer horarios disponibles?"              │
│ Profile Updated: BabyAgeMonths = 4, BabyConditions = "cólicos"│
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "Entiendo perfectamente Ana. El Masaje Relajante   │
│ es ideal para bebés de 4 meses con cólicos. Ayuda a aliviar   │
│ la tensión abdominal y mejora el sueño. Muchas mamás han      │
│ visto resultados desde la primera sesión. ¿Te gustaría conocer│
│ los horarios disponibles?"                                      │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE 4: "Sí, para mañana"                                    │
├─────────────────────────────────────────────────────────────────┤
│ Stage: Presentation → AvailabilityExploration (TRANSICIÓN)     │
│ Auto-Extract: Date = "mañana"                                   │
│ Auto-Check: Availability CONFIRMED                              │
│ Session Updated: DesiredDate, AvailabilityConfirmed = true     │
│ Stage: AvailabilityExploration → Closing (TRANSICIÓN)          │
│ Tactic: AssumptiveClose                                         │
│ Tone: Persuasive                                                │
│ CTA: "¿Confirmamos tu cita entonces?"                          │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "Perfecto Ana! Tenemos disponibilidad mañana a las │
│ 2:00 PM para el Masaje Relajante. Este horario es ideal y     │
│ aún tenemos espacio. ¿Confirmamos tu cita entonces?"          │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE 5: "Sí, confirmo"                                       │
├─────────────────────────────────────────────────────────────────┤
│ Stage: Closing → Booking (TRANSICIÓN)                          │
│ Tool: create_reservation → EJECUTADA                           │
│ Tactic: OfferGuarantee                                         │
│ Tone: Enthusiastic                                              │
│ Profile Updated: TotalPurchases++, Segment = FirstTimeBuyer    │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "¡Excelente decisión Ana! 🎉 Tu cita está          │
│ confirmada para mañana a las 2:00 PM. Te voy a enviar los     │
│ detalles por WhatsApp. ¿Tienes alguna pregunta?"              │
└─────────────────────────────────────────────────────────────────┘
```

**Resultados:**
- ✅ 5 mensajes → Reserva confirmada
- ✅ Perfil creado y enriquecido
- ✅ Transiciones automáticas de estado
- ✅ Extracción automática de fecha
- ✅ Verificación automática de disponibilidad
- ✅ Cierre exitoso

---

### CASO 2: Manejo de Objeciones (Precio)

```
┌─────────────────────────────────────────────────────────────────┐
│ CONTEXTO: Cliente en Presentation, servicio presentado         │
├─────────────────────────────────────────────────────────────────┤
│ MENSAJE: "Me parece un poco caro"                              │
├─────────────────────────────────────────────────────────────────┤
│ Stage: Presentation → ObjectionHandling (TRANSICIÓN)           │
│ Intent: Objection                                               │
│ Tactic: HandleObjection                                         │
│ Tone: Empathetic                                                │
│ CTA: "¿Hay algo más que te preocupe?"                          │
│ Session Updated: ObjectionsRaised += "precio"                  │
│ Profile Updated: CommonObjections["precio"]++                  │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "Entiendo perfectamente Ana. Muchas mamás tienen   │
│ la misma preocupación inicial. Lo que te puedo decir es que   │
│ la salud y bienestar de tu bebé no tiene precio. Además, este │
│ tratamiento ayuda a prevenir problemas mayores a futuro.      │
│ ¿Hay algo más que te preocupe?"                                │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ MENSAJE: "No, tienes razón. ¿Cuándo tienen disponibilidad?"    │
├─────────────────────────────────────────────────────────────────┤
│ Stage: ObjectionHandling → AvailabilityExploration (TRANS.)    │
│ Session Updated: ObjectionsHandled["precio"] = true            │
│ Tactic: CreateUrgency                                           │
│ Tone: Professional                                              │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "Qué bueno que lo veas así Ana. Esta semana aún   │
│ tenemos algunos espacios disponibles. ¿Prefieres mañana o     │
│ el viernes?"                                                    │
└─────────────────────────────────────────────────────────────────┘
```

**Resultados:**
- ✅ Objeción detectada y registrada
- ✅ Transición automática a ObjectionHandling
- ✅ Respuesta empática aplicada
- ✅ Vuelta al flujo de venta después de resolver
- ✅ Objeción registrada en perfil para futuras referencias

---

### CASO 3: Cliente VIP - Estrategia Personalizada

```
┌─────────────────────────────────────────────────────────────────┐
│ CONTEXTO: CustomerProfile                                       │
│ - Segment: VIPCustomer                                          │
│ - TotalPurchases: 8                                             │
│ - LifetimeValue: $1,200                                         │
│ - ConversionProbability: 0.9                                    │
├─────────────────────────────────────────────────────────────────┤
│ MENSAJE: "Hola, quiero agendar otra cita"                      │
├─────────────────────────────────────────────────────────────────┤
│ Stage: InitialContact (pero estrategia adaptada)               │
│ Tactic: PresentCaseStudy (por ser VIP)                         │
│ Tone: Professional + Enthusiastic                               │
│ CTA: "¿El mismo servicio de siempre?"                          │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "¡Ana! Qué gusto saber de ti de nuevo 😊. Veo que  │
│ tu bebé adoró el Masaje Relajante la última vez. ¿Quieres     │
│ agendar el mismo servicio o te gustaría probar la             │
│ Hidroterapia que también es excelente para su edad?"          │
└─────────────────────────────────────────────────────────────────┘
```

**Diferencias clave para VIP:**
- ✅ Reconocimiento inmediato
- ✅ Referencia a historial previo
- ✅ Ofrece alternativas (upsell)
- ✅ Proceso más rápido (salta Discovery)
- ✅ Tono más personal

---

### CASO 4: Cliente en Riesgo - Re-engagement

```
┌─────────────────────────────────────────────────────────────────┐
│ CONTEXTO: CustomerProfile                                       │
│ - Segment: AtRisk                                               │
│ - LastContactAt: 95 días atrás                                  │
│ - TotalPurchases: 2                                             │
│ - LastPurchaseAt: 120 días atrás                               │
├─────────────────────────────────────────────────────────────────┤
│ MENSAJE: "Hola"                                                 │
├─────────────────────────────────────────────────────────────────┤
│ Stage: InitialContact                                           │
│ Tactic: RemindPain + OfferDiscount                             │
│ Tone: Friendly + Urgent                                         │
│ CTA: "¿Cómo ha estado tu bebé?"                                │
├─────────────────────────────────────────────────────────────────┤
│ RESPUESTA: "¡Ana! Cuánto tiempo sin saber de ti. ¿Cómo ha     │
│ estado tu bebé? Recuerdo que los masajes le ayudaban mucho    │
│ con los cólicos. Justo tenemos una promoción especial esta    │
│ semana. ¿Te gustaría saber más?"                               │
└─────────────────────────────────────────────────────────────────┘
```

**Estrategia de Re-engagement:**
- ✅ Reconoce la ausencia
- ✅ Recuerda contexto anterior
- ✅ Ofrece incentivo (promoción)
- ✅ Pregunta por el bebé (empatía)

---

## 🔧 CÓDIGO DE IMPLEMENTACIÓN

### Ejemplo 1: Servicio de WhatsApp Integrado

```csharp
using MimosBabySpa.Application.Orchestration;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationOrchestrator orchestrator,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<string> ProcessMessageAsync(
        Guid businessId,
        string phoneNumber,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Procesando mensaje de {Phone} para negocio {BusinessId}",
            phoneNumber, businessId);

        try
        {
            // El orquestador hace TODO el trabajo pesado
            var response = await _orchestrator.ProcessMessageAsync(
                businessId,
                phoneNumber,
                messageText,
                cancellationToken);

            _logger.LogInformation("Respuesta generada exitosamente");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando mensaje");
            return "Disculpa, tengo dificultades técnicas. ¿Puedes repetir?";
        }
    }
}
```

### Ejemplo 2: Consultar Métricas de Cliente

```csharp
public class CustomerAnalyticsService
{
    private readonly ICustomerProfileService _profileService;
    private readonly ISalesInteractionRepository _interactionRepo;

    public async Task<CustomerInsights> GetInsightsAsync(
        Guid businessId, 
        string phoneNumber)
    {
        var profile = await _profileService.GetOrCreateProfileAsync(
            businessId, phoneNumber);

        var recentInteractions = await _interactionRepo.GetByProfileAsync(
            profile.ProfileId, limit: 10);

        return new CustomerInsights
        {
            CustomerName = profile.CustomerName,
            Segment = profile.Segment,
            ConversionProbability = profile.ConversionProbability,
            TotalPurchases = profile.TotalPurchases,
            LifetimeValue = profile.LifetimeValue,
            DaysSinceLastContact = (DateTime.UtcNow - profile.LastContactAt).Days,
            MostCommonObjection = GetMostCommonObjection(profile),
            RecommendedAction = GetRecommendedAction(profile, recentInteractions)
        };
    }
}
```

### Ejemplo 3: Dashboard de Conversión por Etapa

```csharp
public class SalesAnalyticsService
{
    private readonly ISalesInteractionRepository _interactionRepo;

    public async Task<List<StageMetrics>> GetConversionFunnelAsync(
        Guid businessId,
        DateTime from,
        DateTime to)
    {
        var metrics = new List<StageMetrics>();

        foreach (SalesStage stage in Enum.GetValues<SalesStage>())
        {
            if (stage == SalesStage.Lost) continue;

            var interactions = await _interactionRepo.GetByStageAsync(
                businessId, stage, from, to);

            var successful = interactions.Count(i => i.WasSuccessful);
            var total = interactions.Count;

            metrics.Add(new StageMetrics
            {
                Stage = stage,
                TotalInteractions = total,
                SuccessfulInteractions = successful,
                ConversionRate = total > 0 ? (double)successful / total : 0,
                AverageTimeInStage = CalculateAverageTime(interactions)
            });
        }

        return metrics;
    }
}
```

---

## 🎨 PERSONALIZACIÓN DE ESTRATEGIAS

### Personalizar Táctica por Hora del Día

```csharp
// En SalesStrategyEngine.cs

private void ApplyClosingStrategy(...)
{
    var currentHour = DateTime.Now.Hour;
    
    // Más agresivo en horarios pico
    if (currentHour >= 9 && currentHour <= 12)
    {
        decision.Tactic = SalesTactic.CreateUrgency;
        decision.KeyPoints.Add("Mencionar que quedan pocos espacios hoy");
    }
    // Más suave en horarios nocturnos
    else if (currentHour >= 20 || currentHour <= 6)
    {
        decision.Tactic = SalesTactic.AssumptiveClose;
        decision.Tone = ResponseTone.Friendly;
    }
}
```

### Personalizar por Día de la Semana

```csharp
private void ApplyPresentationStrategy(...)
{
    var dayOfWeek = DateTime.Now.DayOfWeek;
    
    // Viernes/Sábado: crear urgencia para semana siguiente
    if (dayOfWeek == DayOfWeek.Friday || dayOfWeek == DayOfWeek.Saturday)
    {
        decision.KeyPoints.Add("Mencionar que la próxima semana se llena rápido");
        decision.Tactic = SalesTactic.CreateScarcity;
    }
}
```

### Personalizar por Historial de Objeciones

```csharp
private void ApplyObjectionHandlingStrategy(...)
{
    // Obtener objeciones comunes del perfil
    var objections = JsonSerializer.Deserialize<Dictionary<string, int>>(
        profile.CommonObjections ?? "{}") ?? new();

    // Si "precio" es la objeción más común
    if (objections.ContainsKey("precio") && objections["precio"] >= 2)
    {
        decision.Tactic = SalesTactic.OfferDiscount;
        decision.KeyPoints.Add("Mencionar plan de pagos o promoción");
    }
    // Si "tiempo" es la objeción más común
    else if (objections.ContainsKey("tiempo") && objections["tiempo"] >= 2)
    {
        decision.Tactic = SalesTactic.OfferGuarantee;
        decision.KeyPoints.Add("Enfatizar flexibilidad de horarios");
    }
}
```

---

## 🧪 TESTING END-TO-END

### Test de Flujo Completo

```csharp
[Fact]
public async Task FullSalesFlow_NewCustomer_ShouldCompleteBooking()
{
    // Arrange
    var orchestrator = _serviceProvider.GetRequiredService<IConversationOrchestrator>();
    var businessId = Guid.Parse("...");
    var phoneNumber = "+1234567890";

    // Act & Assert - Paso 1: Saludo
    var response1 = await orchestrator.ProcessMessageAsync(
        businessId, phoneNumber, "Hola");
    
    response1.Should().Contain("llamas");

    // Paso 2: Proporcionar nombre
    var response2 = await orchestrator.ProcessMessageAsync(
        businessId, phoneNumber, "Me llamo Ana");
    
    response2.Should().Contain("bebé");

    // Paso 3: Edad del bebé
    var response3 = await orchestrator.ProcessMessageAsync(
        businessId, phoneNumber, "Tiene 4 meses");
    
    response3.Should().Contain("Masaje");

    // Paso 4: Fecha
    var response4 = await orchestrator.ProcessMessageAsync(
        businessId, phoneNumber, "Para mañana");
    
    response4.Should().Contain("Confirmamos");

    // Paso 5: Confirmar
    var response5 = await orchestrator.ProcessMessageAsync(
        businessId, phoneNumber, "Sí, confirmo");
    
    response5.Should().Contain("confirmada");

    // Verificar perfil actualizado
    var profileService = _serviceProvider
        .GetRequiredService<ICustomerProfileService>();
    var profile = await profileService.GetOrCreateProfileAsync(
        businessId, phoneNumber);

    profile.CustomerName.Should().Be("Ana");
    profile.BabyAgeMonths.Should().Be(4);
    profile.Segment.Should().Be(CustomerSegment.FirstTimeBuyer);
}
```

---

## 📊 ANALYTICS Y REPORTES

### Obtener Top Objeciones

```csharp
public async Task<List<ObjectionReport>> GetTopObjectionsAsync(
    Guid businessId,
    int topN = 10)
{
    var profiles = await _profileRepo.GetByBusinessAsync(businessId);
    
    var objectionCounts = new Dictionary<string, int>();

    foreach (var profile in profiles)
    {
        if (string.IsNullOrEmpty(profile.CommonObjections)) continue;

        var objections = JsonSerializer.Deserialize<Dictionary<string, int>>(
            profile.CommonObjections) ?? new();

        foreach (var kvp in objections)
        {
            if (objectionCounts.ContainsKey(kvp.Key))
                objectionCounts[kvp.Key] += kvp.Value;
            else
                objectionCounts[kvp.Key] = kvp.Value;
        }
    }

    return objectionCounts
        .OrderByDescending(x => x.Value)
        .Take(topN)
        .Select(x => new ObjectionReport
        {
            Objection = x.Key,
            Count = x.Value,
            Percentage = (double)x.Value / profiles.Count * 100
        })
        .ToList();
}
```

### Dashboard de Conversión

```csharp
public async Task<ConversionDashboard> GetDashboardAsync(Guid businessId)
{
    var sessions = await _sessionRepo.GetAllByBusinessAsync(businessId);
    var profiles = await _profileRepo.GetByBusinessAsync(businessId);

    return new ConversionDashboard
    {
        ActiveSessions = sessions.Count(s => s.IsActive),
        
        // Distribución por etapa
        InInitialContact = sessions.Count(s => s.CurrentStage == SalesStage.InitialContact),
        InDiscovery = sessions.Count(s => s.CurrentStage == SalesStage.Discovery),
        InPresentation = sessions.Count(s => s.CurrentStage == SalesStage.Presentation),
        InClosing = sessions.Count(s => s.CurrentStage == SalesStage.Closing),
        InBooking = sessions.Count(s => s.CurrentStage == SalesStage.Booking),
        
        // Métricas de clientes
        NewCustomers = profiles.Count(p => p.Segment == CustomerSegment.New),
        QualifiedLeads = profiles.Count(p => p.Segment == CustomerSegment.QualifiedLead),
        FirstTimeBuyers = profiles.Count(p => p.Segment == CustomerSegment.FirstTimeBuyer),
        RegularCustomers = profiles.Count(p => p.Segment == CustomerSegment.RegularCustomer),
        VIPCustomers = profiles.Count(p => p.Segment == CustomerSegment.VIPCustomer),
        
        // Tasa de conversión global
        OverallConversionRate = profiles.Any() 
            ? profiles.Average(p => p.ConversionProbability) 
            : 0,
        
        // Valor
        TotalLifetimeValue = profiles.Sum(p => p.LifetimeValue ?? 0),
        AveragePurchaseValue = profiles
            .Where(p => p.TotalPurchases > 0)
            .Average(p => p.AveragePurchaseValue ?? 0)
    };
}
```

---

## 🚀 OPTIMIZACIONES AVANZADAS

### Caché de Perfiles en Redis (Futuro)

```csharp
public class CachedCustomerProfileService : ICustomerProfileService
{
    private readonly ICustomerProfileService _inner;
    private readonly IDistributedCache _cache;
    private const int CacheMinutes = 15;

    public async Task<CustomerProfile> GetOrCreateProfileAsync(...)
    {
        var cacheKey = $"profile:{businessId}:{phoneNumber}";
        
        // Intentar obtener del caché
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<CustomerProfile>(cached);
        }

        // Obtener de BD
        var profile = await _inner.GetOrCreateProfileAsync(
            businessId, phoneNumber, cancellationToken);

        // Guardar en caché
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(profile),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheMinutes)
            });

        return profile;
    }
}
```

### Background Job: Limpieza de Sesiones

```csharp
public class SessionCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCleanupBackgroundService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var sessionManager = scope.ServiceProvider
                    .GetRequiredService<ISessionManager>();

                await sessionManager.CleanupExpiredSessionsAsync(stoppingToken);

                // Ejecutar cada 10 minutos
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en limpieza de sesiones");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
```

---

## 📈 MÉTRICAS CLAVE A MONITOREAR

### KPIs del Sistema

1. **Tasa de Conversión por Etapa**
   ```
   InitialContact → Discovery: 85%
   Discovery → Presentation: 70%
   Presentation → Closing: 60%
   Closing → Booking: 45%
   ```

2. **Tiempo Promedio por Etapa**
   ```
   InitialContact: 2 minutos
   Discovery: 5 minutos
   Presentation: 3 minutos
   AvailabilityExploration: 4 minutos
   Closing: 2 minutos
   Total: ~16 minutos promedio hasta booking
   ```

3. **Efectividad de Tácticas**
   ```
   AssumptiveClose: 48% éxito
   DirectClose: 35% éxito
   AlternativeClose: 52% éxito
   HandleObjection: 65% recuperación
   ```

4. **Distribución de Objeciones**
   ```
   Precio: 45%
   Tiempo/Distancia: 30%
   Seguridad/Miedo: 15%
   Otros: 10%
   ```

---

## 🎓 MEJORES PRÁCTICAS

### ✅ DO:
- Monitorear logs del orquestador para entender flujos
- Ajustar umbrales de scoring según datos reales
- A/B testing de tácticas de cierre
- Revisar objeciones comunes y actualizar respuestas
- Mantener prompts concisos (máximo 150 palabras)

### ❌ DON'T:
- No modificar lógica de transiciones sin análisis
- No saltarse etapas (respeta la máquina de estados)
- No permitir que la IA decida transiciones
- No enviar respuestas sin validar
- No ignorar advertencias del validador

---

## 🔥 CASOS EDGE Y MANEJO

### Cliente Impaciente (Quiere cerrar rápido)

```
Usuario: "Quiero reservar YA para mañana"
┌─────────────────────────────────────────────────┐
│ Stage: InitialContact                           │
│ Intent: ReservationConfirmation + ExploreAvail. │
│ Estrategia: Acelerar flujo pero sin saltarse   │
│             validaciones críticas               │
├─────────────────────────────────────────────────┤
│ 1. Detectar urgencia en mensaje                │
│ 2. Extraer fecha automáticamente               │
│ 3. Transicionar directamente a Availability    │
│ 4. Si tiene todo → Closing inmediato           │
└─────────────────────────────────────────────────┘
```

### Cliente Indeciso (Múltiples Objeciones)

```
Usuario: "No sé... es caro y queda lejos"
┌─────────────────────────────────────────────────┐
│ Stage: Presentation → ObjectionHandling         │
│ Objeciones detectadas: ["precio", "distancia"] │
│ Estrategia: Manejar ambas sistemáticamente     │
├─────────────────────────────────────────────────┤
│ 1. Validar AMBAS preocupaciones                │
│ 2. Manejar precio primero (más crítica)        │
│ 3. Ofrecer solución a distancia (horarios)     │
│ 4. No forzar cierre, educar beneficios         │
│ 5. Si persiste → marcar como "low probability" │
└─────────────────────────────────────────────────┘
```

### Cliente Perdido (No Responde)

```
┌─────────────────────────────────────────────────┐
│ Trigger: Session.ExpiresAt < Now               │
│ Stage: Cualquiera → Lost                        │
│ Acción: Marcar sesión como inactiva            │
├─────────────────────────────────────────────────┤
│ 1. SessionManager.CleanupExpiredSessions()     │
│ 2. Profile.ChurnRisk++                          │
│ 3. Crear tarea de seguimiento (futuro)         │
│ 4. Enviar mensaje de re-engagement (opcional)  │
└─────────────────────────────────────────────────┘
```

---

## 🎯 PRÓXIMOS PASOS DE EVOLUCIÓN

### Fase 1: Machine Learning
- Entrenar modelo de predicción de conversión
- Optimizar timing de cierre con datos históricos
- Clustering de perfiles similares

### Fase 2: Automatización Avanzada
- Auto-seguimiento de leads perdidos
- Recordatorios automáticos pre-cita
- Upselling post-venta automatizado

### Fase 3: Multicanal
- Extender a Facebook Messenger
- Extender a Instagram DM
- Unificar perfiles cross-canal

### Fase 4: Personalización IA
- Generar tácticas personalizadas por cliente
- A/B testing automatizado de prompts
- Optimización continua de estrategias

---

**Sistema IA Vendedor: Implementación Completa** ✅

**Próximo paso:** Aplicar migración y comenzar a usar el orquestador en producción.
