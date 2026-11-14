using System.Data;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Foundation.Tests;

public sealed class SqlQueryShapeTests
{
    [Theory]
    [InlineData(true, "CONVERT(bit,1)")]
    [InlineData(false, "CONVERT(bit,0)")]
    public void Shape_folds_only_presence_and_modes_and_keeps_data_parameterized(bool enabled, string literal)
    {
        using var command = new SqliteCommand("SELECT @Absent, @Name, @Amount, @Date, @Unknown WHERE @Absent IS NULL AND @Name IS NOT NULL AND @Enabled=1;");
        command.Parameters.AddWithValue("@Absent", DBNull.Value);
        command.Parameters.AddWithValue("@Name", "x'; DROP TABLE accounts; --");
        command.Parameters.AddWithValue("@Amount", 13.5m);
        command.Parameters.AddWithValue("@Date", new DateTime(2026, 11, 14));
        var mode = command.CreateParameter();
        mode.ParameterName = "@Enabled"; mode.DbType = DbType.Boolean; mode.Value = enabled;
        command.Parameters.Add(mode);

        Assert.Equal($"SELECT NULL, @Name, @Amount, @Date, @Unknown WHERE (1=1) AND (1=1) AND {literal}=1;",
            SqlQueryShape.FoldOptionalPredicates(command));
        Assert.Equal(5, command.Parameters.Count);
        Assert.Contains("@Absent IS NULL", command.CommandText);
    }

    [Fact]
    public void Shape_preserves_quoted_text_identifiers_comments_and_unbound_local_variables()
    {
        using var command = new SqliteCommand("""
            SELECT N'@Absent IS NULL; ''@Enabled''', [@Absent IS NULL], "@Enabled", @Local;
            -- @Absent IS NOT NULL AND @Enabled
            /* @Enabled=1 AND @Absent IS NULL */
            WHERE @Absent IS NOT NULL;
            """);
        command.Parameters.AddWithValue("@Absent", DBNull.Value);
        command.Parameters.AddWithValue("@Enabled", true);
        Assert.Equal(command.CommandText.Replace("WHERE @Absent IS NOT NULL;", "WHERE (1=0);", StringComparison.Ordinal),
            SqlQueryShape.FoldOptionalPredicates(command));
    }
}
