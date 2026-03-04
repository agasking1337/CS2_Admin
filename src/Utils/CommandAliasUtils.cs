namespace CS2_Admin.Utils;

public static class CommandAliasUtils
{
    public static string[] NormalizeCommandArgs(string[] args, IReadOnlyList<string> aliases)
    {
        var normalized = args
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToList();

        if (normalized.Count == 0)
        {
            return [];
        }

        var first = normalized[0].TrimStart('!', '/');
        if (IsAlias(first, aliases))
        {
            normalized.RemoveAt(0);
        }

        return [.. normalized];
    }

    public static string GetPreferredExecutionAlias(IReadOnlyList<string> aliases, string fallback)
    {
        var alias = aliases.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = fallback;
        }

        return ToSwAlias(alias);
    }

    public static string ToSwAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        var trimmed = alias.Trim();
        return trimmed.StartsWith("sw_", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"sw_{trimmed}";
    }

    private static bool IsAlias(string value, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()))
        {
            if (alias.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var swAlias = ToSwAlias(alias);
            if (swAlias.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
