using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace Auraly.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Folds optional-filter presence and Boolean modes in trusted SQL templates.</summary>
public static class SqlQueryShape
{
    // Quoted text, identifiers and comments are opaque. Only actual parameter
    // tokens may select a shape; no non-null data value becomes SQL text.
    private static readonly Regex Tokens = new(
        @"'(?:''|[^'])*'|""(?:""""|[^""])*""|\[(?:\]\]|[^\]])*\]|--[^\r\n]*|/\*.*?\*/|(?<presence>(?<parameter>@\w+)\s+IS\s+(?<not>NOT\s+)?NULL\b)|(?<reference>@\w+\b)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string FoldOptionalPredicates(DbCommand command) => Tokens.Replace(command.CommandText, match =>
    {
        var presence = match.Groups["presence"].Success;
        var name = match.Groups[presence ? "parameter" : "reference"].Value;
        if (name.Length == 0 || !command.Parameters.Contains(name)) return match.Value;
        var parameter = command.Parameters[name];
        if (presence)
            return (parameter.Value is DBNull) != match.Groups["not"].Success ? "(1=1)" : "(1=0)";
        if (parameter.Value is DBNull) return "NULL";
        return parameter.DbType == DbType.Boolean && parameter.Value is bool enabled
            ? enabled ? "CONVERT(bit,1)" : "CONVERT(bit,0)" : match.Value;
    });
}
