using FluentAssertions;

using MimosBabySpa.Application.Agents.Configuration;

using MimosBabySpa.Application.Agents.Facts;

using Xunit;



namespace MimosBabySpa.Tests.Agents;



public sealed class FactSchemaPromptTests

{

    private static readonly IReadOnlyList<FactSchemaEntry> Schema =

    [

        new()

        {

            Key = "baby_name",

            Label = "nombre del bebé",

            Source = "user"

        },

        new()

        {

            Key = "baby_age_months",

            Label = "edad del bebé (meses)",

            Source = "user",

            Type = "number"

        }

    ];



    [Fact]

    public void ResolveCollectKeys_ignores_result_markers()

    {

        var keys = FactSchemaPrompt.ResolveCollectKeys(

            Schema,

            ["baby_name", "result:slot_confirmed=true"]);



        keys.Should().Equal("baby_name");

    }



    [Fact]

    public void MissingUserFactKeys_returns_only_absent_collect_keys()

    {

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        {

            ["baby_name"] = "Thomas"

        };



        var missing = FactSchemaPrompt.MissingUserFactKeys(

            Schema, ["baby_name", "baby_age_months"], facts);



        missing.Should().Equal("baby_age_months");

    }

}

