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

        await _tagDbManager.SyncWithGroupsAsync(groupManager);
    }

    public ChatTagGroupStyle GetStyleForGroup(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return CreateDefaultStyle();

        var key = groupName.Trim();

        if (_tagDbManager != null)
        {
            var tag = _tagDbManager.GetTagFromCache(key);
            if (tag != null)
            {
                return new ChatTagGroupStyle
                {
                    ChatColor = NormalizeColor(tag.ChatColor, "[white]"),
                    TagColor = NormalizeColor(tag.TagColor, "[default]"),
                    NameColor = NormalizeColor(tag.NameColor, "[default]"),
                    TagText = tag.TagText
                };
            }
        }

        return GetPresetStyle(key);
    }

    private static ChatTagGroupStyle GetPresetStyle(string groupName)
    {
        return groupName.ToLowerInvariant() switch
        {
            "owner" or "headadmin" or "founder"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[red]", NameColor = "[gold]", TagText = groupName },
            "admin" or "administrator"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[darkred]", NameColor = "[red]", TagText = groupName },
            "senioradmin" or "sradmin" or "srmod"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[purple]", NameColor = "[lightpurple]", TagText = groupName },
            "moderator" or "mod"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[blue]", NameColor = "[lightblue]", TagText = groupName },
            "helper" or "support"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[green]", NameColor = "[lime]", TagText = groupName },
            "vip" or "vip+"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[gold]", NameColor = "[yellow]", TagText = groupName },
            "tester"
                => new ChatTagGroupStyle { ChatColor = "[white]", TagColor = "[olive]", NameColor = "[lightyellow]", TagText = groupName },
            _ => CreateDefaultStyle()
        };
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
