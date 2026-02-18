using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace MimosBabySpa.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = GetConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static string GetConnectionString()
    {
        // 1. Intentar appSettings.json del proyecto Console (formato estándar)
        var consolePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Console", "MimosBabySpa.Console");
        var appSettingsPaths = new[] { "appSettings.json", "appsettings.json" };
        foreach (var fileName in appSettingsPaths)
        {
            var path = Path.Combine(consolePath, fileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connSection) &&
                    connSection.TryGetProperty("DefaultConnection", out var connProp))
                {
                    var cs = connProp.GetString();
                    if (!string.IsNullOrEmpty(cs)) return cs;
                }
            }
        }

        // 2. Fallback: local.settings.json del proyecto API (Azure Functions)
        var apiPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "API", "MimosBabySpa.API");
        var localSettingsPath = Path.Combine(apiPath, "local.settings.json");
        if (File.Exists(localSettingsPath))
        {
            var json = File.ReadAllText(localSettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Values", out var values) &&
                values.TryGetProperty("ConnectionStrings:DefaultConnection", out var connProp))
            {
                var cs = connProp.GetString();
                if (!string.IsNullOrEmpty(cs)) return cs;
            }
        }

        throw new InvalidOperationException(
            "No se encontró cadena de conexión. Colócala en Console/appSettings.json (ConnectionStrings:DefaultConnection) o API/local.settings.json (Values.ConnectionStrings:DefaultConnection)");
    }
}
