namespace Auraly.Pos.Edge.Host;

public static class PosStartupModes
{
    public const string Online = "online";
    public const string Enrolled = "enrolled";

    public static bool IsValid(string? value) =>
        string.Equals(value, Online, StringComparison.Ordinal) ||
        string.Equals(value, Enrolled, StringComparison.Ordinal);
}

public sealed class PosStartupModeStore(string path)
{
    public string Load(bool hasEnrollment)
    {
        if (!File.Exists(path))
            return hasEnrollment ? PosStartupModes.Enrolled : PosStartupModes.Online;

        var value = File.ReadAllText(path).Trim().ToLowerInvariant();
        return PosStartupModes.IsValid(value)
            ? value
            : hasEnrollment ? PosStartupModes.Enrolled : PosStartupModes.Online;
    }

    public void Save(string mode)
    {
        var normalized = mode.Trim().ToLowerInvariant();
        if (!PosStartupModes.IsValid(normalized))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported POS startup mode.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".new";
        File.WriteAllText(temporaryPath, normalized);
        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed record PosStartupModeRequest(string Mode);
