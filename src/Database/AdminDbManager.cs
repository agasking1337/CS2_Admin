using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Data;

namespace CS2_Admin.Database;

public class AdminDbManager
{
    private readonly ISwiftlyCore _core;
    private readonly GroupDbManager _groupManager;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, Admin> _adminCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime> _adminCacheTimestamps = new();
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public AdminDbManager(ISwiftlyCore core, GroupDbManager groupManager)
    {
        _core = core;
        _groupManager = groupManager;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Admin database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Admin database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task<bool> AddAdminAsync(
        ulong steamId,
        string name,
        string flags,
        int immunity,
        string groups,
        string? addedBy,
        ulong? addedBySteamId,
        int? durationDays = null)
    {
        try
        {
            var groupsValidation = await ValidateGroupsAsync(groups);
            if (!groupsValidation.IsValid)
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] AddAdmin rejected for {SteamId}: invalid groups '{Groups}'", steamId, groups);
                return false;
            }

            var normalizedName = NormalizeDbString(name, 64);
            var normalizedAddedBy = NormalizeDbString(addedBy, 64);

            DateTime? expiresAt = durationDays.HasValue && durationDays.Value > 0
                ? DateTime.UtcNow.AddDays(durationDays.Value)
                : null;

            var normalizedGroups = groupsValidation.NormalizedGroups;
            var resolvedImmunity = immunity > 0 ? immunity : groupsValidation.MaxGroupImmunity;

            using var connection = _core.Database.GetConnection("admins");
            var existingAdmin = FindAdminRecordBySteamId(connection, steamId);

            if (existingAdmin != null)
            {
                existingAdmin.Name = normalizedName;
                existingAdmin.Flags = string.Empty;
                existingAdmin.Groups = normalizedGroups;
                existingAdmin.Immunity = resolvedImmunity;
                existingAdmin.ExpiresAt = expiresAt;
                existingAdmin.AddedBy = normalizedAddedBy;
                existingAdmin.AddedBySteamId = addedBySteamId;
                connection.Update(existingAdmin);
                _adminCache[steamId] = existingAdmin;
            }
            else
            {
                var admin = new Admin
                {
                    SteamId = steamId,
                    Name = normalizedName,
                    Flags = string.Empty,
                    Groups = normalizedGroups,
                    Immunity = resolvedImmunity,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    AddedBy = normalizedAddedBy,
                    AddedBySteamId = addedBySteamId
                };
                var id = connection.Insert(admin);
                admin.Id = Convert.ToInt32(id);
                _adminCache[steamId] = admin;
            }

            _adminCacheTimestamps[steamId] = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error adding admin: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> EditAdminAsync(ulong steamId, string field, string value)
    {
        var existingAdmin = await GetAdminAsync(steamId);
        if (existingAdmin == null)
        {
            return false;
        }

        switch (field.ToLowerInvariant())
        {
            case "name":
                existingAdmin.Name = value;
                break;
            case "flags":
                return false;
            case "groups":
            {
                var groupsValidation = await ValidateGroupsAsync(value);
                if (!groupsValidation.IsValid)
                {
                    return false;
                }

                existingAdmin.Groups = groupsValidation.NormalizedGroups;

                if (existingAdmin.Immunity <= 0)
                {
                    existingAdmin.Immunity = groupsValidation.MaxGroupImmunity;
                }
                break;
            }
            case "immunity":
                if (!int.TryParse(value, out var immunity))
                {
                    return false;
                }
                existingAdmin.Immunity = immunity;
                break;
            case "duration":
                if (!int.TryParse(value, out var days))
                {
                    return false;
                }
                existingAdmin.ExpiresAt = days > 0 ? DateTime.UtcNow.AddDays(days) : null;
                break;
            default:
                return false;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            connection.Update(existingAdmin);
            _adminCache[steamId] = existingAdmin;
            _adminCacheTimestamps[steamId] = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error editing admin: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> RemoveAdminAsync(ulong steamId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var admin = FindAdminRecordBySteamId(connection, steamId);
            if (admin == null)
            {
                return false;
            }

            connection.Delete(admin);
            _adminCache.TryRemove(steamId, out _);
            _adminCacheTimestamps.TryRemove(steamId, out _);
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error removing admin: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<Admin?> GetAdminAsync(ulong steamId)
    {
        try
        {
            if (_adminCache.TryGetValue(steamId, out var cachedAdmin) &&
                _adminCacheTimestamps.TryGetValue(steamId, out var cachedAt) &&
                DateTime.UtcNow - cachedAt < _cacheLifetime)
            {
                if (cachedAdmin.IsExpired)
                {
                    _adminCache.TryRemove(steamId, out _);
                    _adminCacheTimestamps.TryRemove(steamId, out _);
                    return null;
                }
                return cachedAdmin;
            }

            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;

            // Use GetAll + in-memory filter to avoid broken Dommel expression translation
            // for ulong SteamId comparisons and nullable ExpiresAt IS NULL checks.
            var admin = connection
                .GetAll<Admin>()
                .FirstOrDefault(a => a.SteamId == steamId && (a.ExpiresAt == null || a.ExpiresAt > now));

            if (admin != null)
            {
                _adminCache[steamId] = admin;
                _adminCacheTimestamps[steamId] = DateTime.UtcNow;
            }
            else
            {
                _adminCache.TryRemove(steamId, out _);
                _adminCacheTimestamps.TryRemove(steamId, out _);
            }

            return admin;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting admin: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<List<Admin>> GetAllAdminsAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;

            // Use GetAll + in-memory filter: Dommel's Select<T>(predicate) generates
            // broken SQL for nullable columns (ExpiresAt IS NULL) on MySQL/SQLite.
            var admins = connection
                .GetAll<Admin>()
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .OrderByDescending(a => a.Immunity)
                .ThenBy(a => a.Name)
                .ToList();

            foreach (var admin in admins)
            {
                _adminCache[admin.SteamId] = admin;
                _adminCacheTimestamps[admin.SteamId] = now;
            }
            return admins;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error getting all admins: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<int> GetEffectiveImmunityAsync(ulong steamId)
    {
        var admin = await GetAdminAsync(steamId);
        if (admin == null || !admin.IsActive)
        {
            return 0;
        }

        var groups = await ResolveGroupsAsync(admin.GroupList);
        var groupImmunity = groups.Count == 0 ? 0 : groups.Max(g => g.Immunity);
        return Math.Max(admin.Immunity, groupImmunity);
    }

    public async Task<string[]> GetEffectiveFlagsAsync(ulong steamId)
    {
        var admin = await GetAdminAsync(steamId);
        if (admin == null || !admin.IsActive)
        {
            return [];
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in admin.Flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(flag);
        }

        var groups = await ResolveGroupsAsync(admin.GroupList);
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Flags))
            {
                continue;
            }

            foreach (var groupFlag in group.Flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                result.Add(groupFlag);
            }
        }

        return [.. result];
    }

    public async Task<string?> GetPrimaryGroupNameAsync(ulong steamId)
    {
        var admin = await GetAdminAsync(steamId);
        if (admin == null || !admin.IsActive || admin.GroupList.Count == 0)
        {
            return null;
        }

        var groups = await ResolveGroupsAsync(admin.GroupList);
        if (groups.Count == 0)
        {
            // Fallback: keep the first configured group name if DB group lookup temporarily misses.
            return admin.GroupList.FirstOrDefault();
        }

        var best = groups
            .OrderByDescending(g => g.Immunity)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return best?.Name;
    }

    public async Task CleanupExpiredAdminsAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;

            // Use GetAll + in-memory filter to avoid broken Dommel nullable predicate translation.
            var expiredAdmins = connection
                .GetAll<Admin>()
                .Where(a => a.ExpiresAt != null && a.ExpiresAt <= now)
                .ToList();

            var cleaned = 0;
            foreach (var admin in expiredAdmins)
            {
                connection.Delete(admin);
                _adminCache.TryRemove(admin.SteamId, out _);
                _adminCacheTimestamps.TryRemove(admin.SteamId, out _);
                cleaned++;
            }

            if (cleaned > 0)
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Removed {Count} expired admins", cleaned);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error cleaning expired admins: {Message}", ex.Message);
        }
    }

    public void ClearCache()
    {
        _adminCache.Clear();
        _adminCacheTimestamps.Clear();
    }


    private static string NormalizeDbString(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }


    private async Task<(bool IsValid, string NormalizedGroups, int MaxGroupImmunity)> ValidateGroupsAsync(string groups)
    {
        var normalizedNames = groups
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => NormalizeGroupName(g))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedNames.Count == 0)
        {
            return (false, string.Empty, 0);
        }

        var maxImmunity = 0;
        foreach (var groupName in normalizedNames)
        {
            var group = await _groupManager.GetGroupAsync(groupName);
            if (group == null)
            {
                return (false, string.Empty, 0);
            }

            maxImmunity = Math.Max(maxImmunity, group.Immunity);
        }

        return (true, string.Join(",", normalizedNames), maxImmunity);
    }

    private static string NormalizeGroupName(string rawGroupName)
    {
        return string.IsNullOrWhiteSpace(rawGroupName)
            ? string.Empty
            : rawGroupName.Trim().TrimStart('#', '@');
    }

    private async Task<List<AdminGroup>> ResolveGroupsAsync(IEnumerable<string> rawGroupNames)
    {
        var groups = new List<AdminGroup>();

        foreach (var rawName in rawGroupNames)
        {
            var normalizedName = NormalizeGroupName(rawName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var group = await _groupManager.GetGroupAsync(normalizedName);
            if (group != null)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private static Admin? FindAdminRecordBySteamId(IDbConnection connection, ulong steamId)
    {
        return connection.GetAll<Admin>().FirstOrDefault(a => a.SteamId == steamId);
    }
}
