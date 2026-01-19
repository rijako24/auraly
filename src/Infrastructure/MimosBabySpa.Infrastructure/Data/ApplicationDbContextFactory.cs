using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace MimosBabySpa.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var apiPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "API", "MimosBabySpa.API");
        var localSettingsPath = Path.Combine(apiPath, "local.settings.json");

        if (!File.Exists(localSettingsPath))
        {
            throw new FileNotFoundException($"No se encontró el archivo local.settings.json en {localSettingsPath}");
        }

        // Leer directamente la cadena de conexión del JSON
        var jsonContent = File.ReadAllText(localSettingsPath);
        using var doc = JsonDocument.Parse(jsonContent);
        
        var connectionString = doc.RootElement
            .GetProperty("Values")
            .GetProperty("ConnectionStrings:DefaultConnection")
            .GetString();

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection en local.settings.json");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
