using CS2_Admin.Config;
using CS2_Admin.Database;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class ChatTagConfigManager
{
    private static readonly string[] SwiftlyColorNames =
    [
        "default", "white", "silver", "gray", "grey", "lightyellow", "yellow",
        "gold", "lightred", "red", "darkred", "olive", "lime", "green",
        "lightblue", "blue", "darkblue", "bluegrey", "lightpurple", "purple", "magenta"
    ];
    private static readonly HashSet<string> SwiftlyColorSet = new(SwiftlyColorNames, StringComparer.OrdinalIgnoreCase);

    private readonly ISwiftlyCore _core;
    private TagDbManager? _tagDbManager;

    public ChatTagsFileConfig Config { get; private set; } = new();

    public ChatTagConfigManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public void SetTagDbManager(TagDbManager tagDbManager)
    {
        _tagDbManager = tagDbManager;
    }

    public void Load()
    {
        Config = new ChatTagsFileConfig();
    }

    public void Load(ChatTagsFileConfig config)
    {
        Config = config ?? new ChatTagsFileConfig();
    }

    public async Task SyncWithGroupsAsync(GroupDbManager groupManager)
    {
        if (_tagDbManager == null)
            return;

        // Tags are explicitly configured in tags.json; do not generate tags from admin groups.
    }

    public ChatTagGroupStyle GetStyleForGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return CreateDefaultStyle();

        var key = groupName.Trim();
        var configuredStyle = Config.Groups.FirstOrDefault(pair =>
            string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        if (configuredStyle == null)
            return CreateDefaultStyle();
        return new ChatTagGroupStyle { ChatColor = NormalizeColor(configuredStyle.ChatColor, "[white]"), TagColor = NormalizeColor(configuredStyle.TagColor, "[default]"), NameColor = NormalizeColor(configuredStyle.NameColor, "[default]"), TagText = string.IsNullOrWhiteSpace(configuredStyle.TagText) ? key : configuredStyle.TagText };

    }

    private static ChatTagGroupStyle CreateDefaultStyle()
    {
        return new ChatTagGroupStyle
        {
            ChatColor = "[white]",
            TagColor = "[green]",
            NameColor = "[default]",
            TagText = ""
        };
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            var alias = trimmed[1..].Trim();
            if (SwiftlyColorSet.Contains(alias))
                return $"[{alias.ToLowerInvariant()}]";
            return fallback;
        }

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
        {
            var name = trimmed[1..^1].Trim();
            if (SwiftlyColorSet.Contains(name))
                return $"[{name.ToLowerInvariant()}]";
            return fallback;
        }

        if (SwiftlyColorSet.Contains(trimmed))
            return $"[{trimmed.ToLowerInvariant()}]";

        return fallback;
    }

    public void Save() { }
}
