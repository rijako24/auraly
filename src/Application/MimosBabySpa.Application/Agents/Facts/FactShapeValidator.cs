using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Facts;

public sealed record FactShapeResult(bool Ok, string? ErrorCode, string? Remediation)
{
    public static FactShapeResult Valid { get; } = new(true, null, null);
}

/// <summary>
/// Valida solo la forma del valor según el tipo declarado en factSchema (sin razonamiento semántico).
/// </summary>
public static class FactShapeValidator
{
    private static readonly Regex E164Phone = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    public static FactShapeResult Validate(FactSchemaEntry entry, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return Fail("empty_value", "Provide a non-empty structured value.");

        return entry.Type.ToLowerInvariant() switch
        {
            "number" => ValidateNumber(entry, trimmed),
            "date" => ValidateDate(trimmed),
            "time" => ValidateTime(trimmed),
            "phone" => ValidatePhone(trimmed),
            "email" => ValidateEmail(trimmed),
            _ => FactShapeResult.Valid,
        };
    }

    private static FactShapeResult ValidateNumber(FactSchemaEntry entry, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return Fail("not_a_number", "Use an integer number for this fact.");
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            && entry.Range is not null)
        {
            if (entry.Range.Min.HasValue && i < entry.Range.Min.Value)
                return Fail("out_of_range", $"Minimum value is {entry.Range.Min.Value}.");
            if (entry.Range.Max.HasValue && i > entry.Range.Max.Value)
                return Fail("out_of_range", $"Maximum value is {entry.Range.Max.Value}.");
        }

        return FactShapeResult.Valid;
    }

    private static FactShapeResult ValidateDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? FactShapeResult.Valid
            : Fail("not_a_date", "Use date format YYYY-MM-DD.");

    private static FactShapeResult ValidateTime(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? FactShapeResult.Valid
            : Fail("not_a_time", "Use time format HH:mm (24h).");

    private static FactShapeResult ValidatePhone(string value) =>
        E164Phone.IsMatch(value)
            ? FactShapeResult.Valid
            : Fail("not_a_phone", "Use E.164 format, e.g. +573001234567.");

    private static FactShapeResult ValidateEmail(string value) =>
        MailAddress.TryCreate(value, out _)
            ? FactShapeResult.Valid
            : Fail("not_an_email", "Use a valid email address.");

    private static FactShapeResult Fail(string code, string remediation) =>
        new(false, code, remediation);
}
