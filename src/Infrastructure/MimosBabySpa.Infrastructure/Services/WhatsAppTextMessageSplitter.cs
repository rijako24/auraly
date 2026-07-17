namespace MimosBabySpa.Infrastructure.Services;

public static class WhatsAppTextMessageSplitter
{
    public static IReadOnlyList<string> Split(string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];
        maxLength = Math.Clamp(maxLength, 500, 4096);
        var normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length <= maxLength)
            return [normalized];

        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(block => SplitBlock(block.Trim(), maxLength));
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var block in blocks)
        {
            var candidate = current.Length == 0 ? block : $"{current}\n\n{block}";
            if (candidate.Length <= maxLength)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
                chunks.Add(current);
            current = block;
        }
        if (current.Length > 0)
            chunks.Add(current);
        return chunks;
    }

    private static IEnumerable<string> SplitBlock(string block, int maxLength)
    {
        if (block.Length <= maxLength)
        {
            yield return block;
            yield break;
        }

        var current = string.Empty;
        foreach (var line in block.Split('\n'))
        {
            foreach (var part in SplitLongLine(line.TrimEnd(), maxLength))
            {
                var candidate = current.Length == 0 ? part : $"{current}\n{part}";
                if (candidate.Length <= maxLength)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                    yield return current;
                current = part;
            }
        }
        if (current.Length > 0)
            yield return current;
    }

    private static IEnumerable<string> SplitLongLine(string line, int maxLength)
    {
        while (line.Length > maxLength)
        {
            var splitAt = line.LastIndexOf(' ', maxLength - 1, maxLength);
            if (splitAt < maxLength / 2)
                splitAt = maxLength;
            yield return line[..splitAt].TrimEnd();
            line = line[splitAt..].TrimStart();
        }
        if (line.Length > 0)
            yield return line;
    }
}
