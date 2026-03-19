using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class GroupDbManager
{
    private readonly ISwiftlyCore _core;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AdminGroup> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _cacheTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public GroupDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Group database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Group database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<AdminGroup?> GetGroupAsync(string name)
    {
        var normalizedName = NormalizeGroupName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        if (_cache.TryGetValue(normalizedName, out var cached) &&
            _cacheTimestamps.TryGetValue(normalizedName, out var cachedAt) &&
            DateTime.UtcNow - cachedAt < _cacheLifetime)
        {
            return cached;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var group = connection.FirstOrDefault<AdminGroup>(g => g.Name == normalizedName)
                        ?? connection.GetAll<AdminGroup>()
                            .FirstOrDefault(g => g.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (group != null)
            {
                var key = NormalizeGroupName(group.Name);
                _cache[key] = group;
                _cacheTimestamps[key] = DateTime.UtcNow;
            }
            return group;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting group: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<List<AdminGroup>> GetAllGroupsAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var groups = connection.GetAll<AdminGroup>().OrderByDescending(x => x.Immunity).ThenBy(x => x.Name).ToList();
            var now = DateTime.UtcNow;
            foreach (var group in groups)
            {
                var key = NormalizeGroupName(group.Name);
                _cache[key] = group;
                _cacheTimestamps[key] = now;
            }
            return groups;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting groups: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<bool> AddOrUpdateGroupAsync(string name, string flags, int immunity)
    {
        var normalizedName = NormalizeGroupName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var existing = connection.FirstOrDefault<AdminGroup>(g => g.Name == normalizedName)
                           ?? connection.GetAll<AdminGroup>()
                               .FirstOrDefault(g => g.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = normalizedName;
                existing.Flags = flags;
                existing.Immunity = immunity;
                existing.UpdatedAt = DateTime.UtcNow;
                connection.Update(existing);
                _cache[normalizedName] = existing;
                _cacheTimestamps[normalizedName] = DateTime.UtcNow;
            }
            else
            {
                var group = new AdminGroup
                {
                    Name = normalizedName,
                    Flags = flags,
                    Immunity = immunity,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                connection.Insert(group);
                _cache[normalizedName] = group;
                _cacheTimestamps[normalizedName] = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding/updating group: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> RemoveGroupAsync(string name)
    {
        var normalizedName = NormalizeGroupName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var existing = connection.FirstOrDefault<AdminGroup>(g => g.Name == normalizedName)
                           ?? connection.GetAll<AdminGroup>()
                               .FirstOrDefault(g => g.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return false;
            }

            connection.Delete(existing);
            _cache.TryRemove(normalizedName, out _);
            _cacheTimestamps.TryRemove(normalizedName, out _);
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error removing group: {Message}", ex.Message);
            return false;
        }
    }

    public int GetGroupImmunitySync(IEnumerable<string> groupNames)
    {
        var max = 0;
        foreach (var groupName in groupNames)
        {
            var normalizedName = NormalizeGroupName(groupName);
            if (_cache.TryGetValue(normalizedName, out var group))
            {
                max = Math.Max(max, group.Immunity);
            }
        }
        return max;
    }

    public IEnumerable<string> ExpandFlags(IEnumerable<string> groupNames)
    {
        foreach (var groupName in groupNames)
        {
            var normalizedName = NormalizeGroupName(groupName);
            if (_cache.TryGetValue(normalizedName, out var group) && !string.IsNullOrWhiteSpace(group.Flags))
            {
                foreach (var flag in group.Flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    yield return flag;
                }
            }
        }
    }

    public string? GetPrimaryGroupNameSync(IEnumerable<string> groupNames)
    {
        AdminGroup? best = null;
        string? fallback = null;

        foreach (var groupName in groupNames)
        {
            var normalizedName = NormalizeGroupName(groupName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            fallback ??= normalizedName;

            if (!_cache.TryGetValue(normalizedName, out var group))
            {
                continue;
            }

            if (best == null || group.Immunity > best.Immunity ||
                (group.Immunity == best.Immunity && string.Compare(group.Name, best.Name, StringComparison.OrdinalIgnoreCase) < 0))
            {
                best = group;
            }
        }

        return best?.Name ?? fallback;
    }

    public void ClearCache()
    {
        _cache.Clear();
        _cacheTimestamps.Clear();
    }

    private static string NormalizeGroupName(string rawName)
    {
        return string.IsNullOrWhiteSpace(rawName)
            ? string.Empty
            : rawName.Trim().TrimStart('#', '@');
    }
}
