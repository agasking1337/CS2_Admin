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
    private readonly Dictionary<ulong, Admin> _adminCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
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

            DateTime? expiresAt = durationDays.HasValue && durationDays.Value > 0
                ? DateTime.UtcNow.AddDays(durationDays.Value)
                : null;

            var normalizedGroups = groupsValidation.NormalizedGroups;
            var resolvedImmunity = immunity > 0 ? immunity : groupsValidation.MaxGroupImmunity;

            using var connection = _core.Database.GetConnection("admins");
            var existingAdmin = FindAdminRecordBySteamId(connection, steamId);

            if (existingAdmin != null)
            {
                existingAdmin.Name = name;
                existingAdmin.Flags = string.Empty;
                existingAdmin.Groups = normalizedGroups;
                existingAdmin.Immunity = resolvedImmunity;
                existingAdmin.ExpiresAt = expiresAt;
                existingAdmin.AddedBy = addedBy;
                existingAdmin.AddedBySteamId = addedBySteamId;
                connection.Update(existingAdmin);
                _adminCache[steamId] = existingAdmin;
            }
            else
            {
                var admin = new Admin
                {
                    SteamId = steamId,
                    Name = name,
                    Flags = string.Empty,
                    Groups = normalizedGroups,
                    Immunity = resolvedImmunity,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    AddedBy = addedBy,
                    AddedBySteamId = addedBySteamId
                };
                var id = connection.Insert(admin);
                admin.Id = Convert.ToInt32(id);
                _adminCache[steamId] = admin;
            }

            _lastCacheUpdate = DateTime.UtcNow;
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
            _adminCache.Remove(steamId);
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
                DateTime.UtcNow - _lastCacheUpdate < _cacheLifetime)
            {
                if (cachedAdmin.IsExpired)
                {
                    _adminCache.Remove(steamId);
                    return null;
                }
                return cachedAdmin;
            }

            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;
            var admin = connection.FirstOrDefault<Admin>(a =>
                a.SteamId == steamId &&
                (a.ExpiresAt == null || a.ExpiresAt > now));

            // Some providers/runtime mappings are inconsistent with ulong predicates.
            // Fallback to in-memory filtering over all records to avoid false negatives.
            if (admin == null)
            {
                admin = connection
                    .GetAll<Admin>()
                    .FirstOrDefault(a => a.SteamId == steamId && (a.ExpiresAt == null || a.ExpiresAt > now));
            }

            if (admin != null)
            {
                _adminCache[steamId] = admin;
                _lastCacheUpdate = DateTime.UtcNow;
            }
            else
            {
                _adminCache.Remove(steamId);
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
            var admins = connection.Select<Admin>(a =>
                a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(a => a.Immunity)
                .ThenBy(a => a.Name)
                .ToList();

            foreach (var admin in admins)
            {
                _adminCache[admin.SteamId] = admin;
            }
            _lastCacheUpdate = DateTime.UtcNow;
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
            var expiredAdmins = connection.Select<Admin>(a =>
                a.ExpiresAt != null &&
                a.ExpiresAt <= DateTime.UtcNow);

            var cleaned = 0;
            foreach (var admin in expiredAdmins)
            {
                connection.Delete(admin);
                cleaned++;
            }

            if (cleaned > 0)
            {
                _core.Logger.LogInformationIfEnabled("[CS2_Admin] Removed {Count} expired admins", cleaned);
                _adminCache.Clear();
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
        _lastCacheUpdate = DateTime.MinValue;
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
        var admin = connection.FirstOrDefault<Admin>(a => a.SteamId == steamId);
        if (admin != null)
        {
            return admin;
        }

        return connection.GetAll<Admin>().FirstOrDefault(a => a.SteamId == steamId);
    }
}
