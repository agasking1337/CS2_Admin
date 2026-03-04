using System.Globalization;
using System.Text.Json;
using SwiftlyS2.Shared.Translation;

namespace CS2_Admin.Utils;

public sealed class JsonFileLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _primary;
    private readonly Dictionary<string, string> _fallback;

    private JsonFileLocalizer(Dictionary<string, string> primary, Dictionary<string, string> fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public string this[string key] => Resolve(key);

    public string this[string key, params object[] args]
    {
        get
        {
            var text = Resolve(key);
            if (args.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, text, args);
            }
            catch
            {
                return text;
            }
        }
    }

    public static JsonFileLocalizer? TryCreate(string directoryPath, string language)
    {
        try
        {
            var normalized = (language ?? "en").Trim().ToLowerInvariant();
            var primaryFile = Path.Combine(directoryPath, $"{normalized}.jsonc");
            var fallbackFile = Path.Combine(directoryPath, "en.jsonc");

            var primary = File.Exists(primaryFile) ? LoadMap(primaryFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fallback = File.Exists(fallbackFile) ? LoadMap(fallbackFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (primary.Count == 0 && fallback.Count == 0)
            {
                return null;
            }

            return new JsonFileLocalizer(primary, fallback);
        }
        catch
        {
            return null;
        }
    }

    private string Resolve(string key)
    {
        if (_primary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return key;
    }

    private static Dictionary<string, string> LoadMap(string path)
    {
        var raw = File.ReadAllText(path);
        var cleaned = RemoveSingleLineComments(raw);
        var document = JsonDocument.Parse(cleaned);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                map[item.Name] = item.Value.GetString() ?? string.Empty;
            }
        }

        return map;
    }

    private static string RemoveSingleLineComments(string input)
    {
        var lines = input.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                lines[i] = line[..commentIndex];
            }
        }

        return string.Join('\n', lines);
    }
}
