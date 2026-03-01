using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Infrastructure.Configuration;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Console;

/// <summary>
/// Migración única: copia Calendar y Wompi desde appsettings a BusinessConfiguration.Integrations
/// para cada negocio existente. Ejecutar con: dotnet run -- migrate-integrations
/// </summary>
public static class IntegrationsDataMigration
{
    public static async Task RunAsync(IConfiguration configuration, string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var context = new ApplicationDbContext(options);

        var calendarSection = configuration.GetSection(CalendarSettings.SectionName);
        var wompiSection = configuration.GetSection(WompiSettings.SectionName);

        // Si no hay Calendar ni Wompi en appsettings, no sobrescribir (config ya está en BusinessConfiguration)
        if (!calendarSection.Exists() && !wompiSection.Exists())
        {
            System.Console.WriteLine("Calendar y Wompi no encontrados en appsettings. Omitiendo (la config está en BusinessConfiguration).");
            return;
        }

        var integrations = new IntegrationsConfiguration
        {
            GoogleCalendar = new GoogleCalendarIntegration
            {
                Enabled = calendarSection.GetValue<bool>("Enabled", !string.IsNullOrWhiteSpace(calendarSection["ClientId"])),
                Provider = calendarSection["Provider"] ?? "Google",
                ClientId = calendarSection["ClientId"] ?? "",
                ClientSecret = calendarSection["ClientSecret"] ?? "",
                RefreshToken = calendarSection["RefreshToken"] ?? "",
                CalendarId = calendarSection["CalendarId"] ?? "primary",
                TimeZone = calendarSection["TimeZone"] ?? "America/Bogota",
                Scopes = calendarSection["Scopes"]
            },
            Wompi = new WompiIntegration
            {
                PrivateKey = wompiSection["PrivateKey"] ?? "",
                PublicKey = wompiSection["PublicKey"] ?? "",
                EventsSecret = wompiSection["EventsSecret"] ?? "",
                IntegritySecret = wompiSection["IntegritySecret"] ?? "",
                UseSandbox = wompiSection.GetValue<bool>("UseSandbox", true),
                SandboxBaseUrl = wompiSection["SandboxBaseUrl"] ?? "https://sandbox.wompi.co/v1",
                ProductionBaseUrl = wompiSection["ProductionBaseUrl"] ?? "https://production.wompi.co/v1",
                RequestTimeoutSeconds = wompiSection.GetValue<int>("RequestTimeoutSeconds", 30),
                CheckoutBaseUrl = wompiSection["CheckoutBaseUrl"] ?? "https://checkout.wompi.co/l/"
            }
        };

        var json = JsonSerializer.Serialize(integrations, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var businessIds = await context.Businesses.Select(b => b.BusinessId).ToListAsync();

        foreach (var businessId in businessIds)
        {
            var existing = await context.BusinessConfigurations
                .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Key == BusinessConfigurationKey.Integrations);

            if (existing != null)
            {
                existing.Value = json;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                context.BusinessConfigurations.Add(new BusinessConfiguration
                {
                    BusinessConfigurationId = Guid.NewGuid(),
                    BusinessId = businessId,
                    Key = BusinessConfigurationKey.Integrations,
                    Value = json,
                    Description = "Integraciones: Google Calendar, Wompi",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();
        System.Console.WriteLine($"Integraciones migradas para {businessIds.Count} negocios.");
    }
}
