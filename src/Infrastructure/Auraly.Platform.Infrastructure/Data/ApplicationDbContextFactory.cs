using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace Auraly.Platform.Infrastructure.Data;

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
        // 1. appsettings.json del proyecto Console (prioridad - se usa para migraciones)
        var baseDir = Directory.GetCurrentDirectory();
        var assemblyDir = AppContext.BaseDirectory;
        var candidateDirs = new[]
        {
            Path.Combine(baseDir, "src", "Console", "Auraly.Platform.Console"),
            Path.Combine(baseDir, "Console", "Auraly.Platform.Console"),
            Path.Combine(assemblyDir, "..", "..", "..", "..", "src", "Console", "Auraly.Platform.Console"),
            Path.Combine(baseDir, "..", "..", "Console", "Auraly.Platform.Console")
        };
        var appSettingsNames = new[] { "appsettings.json", "appSettings.json" };
        foreach (var dir in candidateDirs)
        {
            var resolved = Path.GetFullPath(dir);
            foreach (var fileName in appSettingsNames)
            {
                var path = Path.Combine(resolved, fileName);
                if (File.Exists(path))
                {
                    var cs = ReadConnectionStringFrom(path);
                    if (!string.IsNullOrEmpty(cs)) return cs;
                }
            }
        }

        // 2. Fallback: local.settings.json del proyecto API (Azure Functions)
        var apiDirs = new[]
        {
            Path.Combine(baseDir, "src", "API", "Auraly.Platform.Worker"),
            Path.Combine(baseDir, "API", "Auraly.Platform.Worker"),
            Path.Combine(assemblyDir, "..", "..", "..", "..", "src", "API", "Auraly.Platform.Worker")
        };
        foreach (var apiDir in apiDirs)
        {
            var localSettingsPath = Path.Combine(Path.GetFullPath(apiDir), "local.settings.json");
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
        }

        throw new InvalidOperationException(
            "No se encontró cadena de conexión. Colócala en Console/appsettings.json (ConnectionStrings:DefaultConnection) o API/local.settings.json (Values.ConnectionStrings:DefaultConnection)");
    }

    private static string? ReadConnectionStringFrom(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connSection) &&
            connSection.TryGetProperty("DefaultConnection", out var connProp))
        {
            return connProp.GetString();
        }
        return null;
    }
}
