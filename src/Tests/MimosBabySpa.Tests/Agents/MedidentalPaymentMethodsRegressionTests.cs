using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Checkout;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class MedidentalPaymentMethodsRegressionTests
{
    [Fact]
    public async Task Seed_ListsItsThreeConfiguredPaymentMethods()
    {
        var seedPath = Path.Combine(
            FindSolutionRoot(),
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedMedidental.sql");
        var sql = File.ReadAllText(seedPath);
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        var config = JsonSerializer.Deserialize<AgentConfig>(
            match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            });
        config.Should().NotBeNull();

        var operation = new ListPaymentMethodsOperation();
        using var input = JsonDocument.Parse("{}");
        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext
            {
                Config = config!,
                ConversationState = new ConversationState()
            });

        var presentation = outcome.Presentations.Should().ContainSingle().Subject;
        var labels = ((IEnumerable<Dictionary<string, object?>>)presentation.Data["payment_methods"]!)
            .Select(item => item["label"]?.ToString())
            .ToArray();
        labels.Should().BeEquivalentTo(
            "efectivo al recibir",
            "datafono al recibir",
            "transferencia manual");
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }
}
