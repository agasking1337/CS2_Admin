using System.Collections.Concurrent;
using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class TagDbManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<string, AdminTag> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public TagDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _core.Logger.LogInformationIfEnabled("Admin tags database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Admin tags database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<List<AdminTag>> GetAllTagsAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var tags = connection.GetAll<AdminTag>().ToList();
            foreach (var tag in tags)
                _cache[NormalizeGroupName(tag.GroupName)] = tag;
            _lastCacheUpdate = DateTime.UtcNow;
            return tags;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting tags: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<AdminTag?> GetTagForGroupAsync(string groupName)
    {
        var normalized = NormalizeGroupName(groupName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (_cache.TryGetValue(normalized, out var cached)
            && DateTime.UtcNow - _lastCacheUpdate < _cacheLifetime)
        {
            return cached;
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var tag = connection.GetAll<AdminTag>()
                .FirstOrDefault(t => t.GroupName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (tag != null)
            {
                _cache[normalized] = tag;
                _lastCacheUpdate = DateTime.UtcNow;
            }
            return tag;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting tag for group {Group}: {Message}", groupName, ex.Message);
            return null;
        }
    }

    public AdminTag? GetTagForGroupSync(string groupName)
    {
        var normalized = NormalizeGroupName(groupName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (_cache.TryGetValue(normalized, out var cached))
            return cached;

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var tag = connection.GetAll<AdminTag>()
                .FirstOrDefault(t => t.GroupName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (tag != null)
            {
                _cache[normalized] = tag;
                _lastCacheUpdate = DateTime.UtcNow;
            }
            return tag;
        }
        catch
        {
            return null;
        }
    }

    public AdminTag? GetTagFromCache(string groupName)
    {
        var normalized = NormalizeGroupName(groupName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        return _cache.TryGetValue(normalized, out var cached) ? cached : null;
    }

    public async Task<bool> AddOrUpdateTagAsync(string groupName, string tagText, string tagColor, string chatColor, string nameColor)
    {
        var normalized = NormalizeGroupName(groupName);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.GetAll<AdminTag>()
                .FirstOrDefault(t => t.GroupName.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.TagText = tagText;
                existing.TagColor = tagColor;
                existing.ChatColor = chatColor;
                existing.NameColor = nameColor;
                existing.UpdatedAt = DateTime.UtcNow;
                connection.Update(existing);
                _cache[normalized] = existing;
            }
            else
            {
                var tag = new AdminTag
                {
                    GroupName = normalized,
                    TagText = tagText,
                    TagColor = tagColor,
                    ChatColor = chatColor,
                    NameColor = nameColor,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                connection.Insert(tag);
                _cache[normalized] = tag;
            }

            _lastCacheUpdate = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error adding/updating tag for group {Group}: {Message}", groupName, ex.Message);
            return false;
        }
    }

    public async Task<bool> RemoveTagAsync(string groupName)
    {
        var normalized = NormalizeGroupName(groupName);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.GetAll<AdminTag>()
                .FirstOrDefault(t => t.GroupName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                return false;

            connection.Delete(existing);
            _cache.TryRemove(normalized, out _);
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error removing tag for group {Group}: {Message}", groupName, ex.Message);
            return false;
        }
    }

    public async Task SyncWithGroupsAsync(GroupDbManager groupManager)
    {
        var groups = await groupManager.GetAllGroupsAsync();
        var groupNames = new HashSet<string>(groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

        var allTags = await GetAllTagsAsync();
        var existingTagGroups = new HashSet<string>(allTags.Select(t => t.GroupName), StringComparer.OrdinalIgnoreCase);

        bool changed = false;

        foreach (var name in groupNames)
        {
            if (existingTagGroups.Contains(name))
                continue;

            var preset = GetPresetStyle(name);
            await AddOrUpdateTagAsync(name, name, preset.TagColor, preset.ChatColor, preset.NameColor);
            changed = true;
        }

        foreach (var tag in allTags)
        {
            if (!groupNames.Contains(tag.GroupName))
            {
                await RemoveTagAsync(tag.GroupName);
                changed = true;
            }
        }

        if (changed)
        {
            _core.Logger.LogInformationIfEnabled("Admin tags synchronized with groups");
        }
    }

    public void ClearCache()
    {
        _cache.Clear();
        _lastCacheUpdate = DateTime.MinValue;
    }

    private static (string TagColor, string ChatColor, string NameColor) GetPresetStyle(string groupName)
    {
        return groupName.ToLowerInvariant() switch
        {
            "owner" or "headadmin" or "founder"
                => ("[red]", "[white]", "[gold]"),
            "admin" or "administrator"
                => ("[darkred]", "[white]", "[red]"),
            "senioradmin" or "sradmin" or "srmod"
                => ("[purple]", "[white]", "[lightpurple]"),
            "moderator" or "mod"
                => ("[blue]", "[white]", "[lightblue]"),
            "helper" or "support"
                => ("[green]", "[white]", "[lime]"),
            "vip" or "vip+"
                => ("[gold]", "[white]", "[yellow]"),
            "tester"
                => ("[olive]", "[white]", "[lightyellow]"),
            _ => ("[green]", "[white]", "[default]")
        };
    }

    private static string NormalizeGroupName(string rawName)
    {
        return string.IsNullOrWhiteSpace(rawName)
            ? string.Empty
            : rawName.Trim().TrimStart('#', '@');
    }
}
