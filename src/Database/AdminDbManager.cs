using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Data;

namespace CS2_Admin.Database;

public class AdminDbManager
{
    private const string AdminSelectColumns = @"
        `id` AS `Id`,
        `steamid` AS `SteamId`,
        `name` AS `Name`,
        `flags` AS `Flags`,
        `groups` AS `Groups`,
        `immunity` AS `Immunity`,
        `created_at` AS `CreatedAt`,
        `expires_at` AS `ExpiresAt`,
        `added_by` AS `AddedBy`,
        `added_by_steamid` AS `AddedBySteamId`";

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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Admin database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Admin database initialization warning: {Message}", ex.Message);
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
                _core.Logger.LogWarningIfEnabled("AddAdmin rejected for {SteamId}: invalid groups '{Groups}'", steamId, groups);
                return false;
            }

            DateTime? expiresAt = durationDays.HasValue && durationDays.Value > 0
                ? DateTime.UtcNow.AddDays(durationDays.Value)
                : null;

            var normalizedGroups = groupsValidation.NormalizedGroups;
            var resolvedImmunity = immunity > 0 ? immunity : groupsValidation.MaxGroupImmunity;

            using var connection = _core.Database.GetConnection("mysql_detailed");
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
            _core.Logger.LogErrorIfEnabled("Error adding admin: {Message}", ex.Message);
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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            connection.Update(existingAdmin);
            _adminCache[steamId] = existingAdmin;
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error editing admin: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> RemoveAdminAsync(ulong steamId)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
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
            _core.Logger.LogErrorIfEnabled("Error removing admin: {Message}", ex.Message);
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

            using var connection = _core.Database.GetConnection("mysql_detailed");
            var now = DateTime.UtcNow;

            var admin = FindActiveAdminRecordBySteamId(connection, steamId, now);

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
            _core.Logger.LogErrorIfEnabled("Error getting admin: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<List<Admin>> GetAllAdminsAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var now = DateTime.UtcNow;

            var admins = QueryActiveAdminRecords(connection, now);

            foreach (var admin in admins)
            {
                _adminCache[admin.SteamId] = admin;
            }
            _lastCacheUpdate = DateTime.UtcNow;
            return admins;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting all admins: {Message}", ex.Message);
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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            EnsureOpen(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM `admin_admins` WHERE `expires_at` IS NOT NULL AND `expires_at` <= @Now";
            AddParameter(command, "@Now", DateTime.UtcNow);
            var cleaned = command.ExecuteNonQuery();

            if (cleaned > 0)
            {
                _core.Logger.LogInformationIfEnabled("Removed {Count} expired admins", cleaned);
                _adminCache.Clear();
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error cleaning expired admins: {Message}", ex.Message);
        }
    }

        public Admin? GetAdminFromCache(ulong steamId)
    {
        lock (_adminCache)
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
        }
        return null;
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
        EnsureOpen(connection);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {AdminSelectColumns} FROM `admin_admins` WHERE `steamid` = @SteamId LIMIT 1";
        AddParameter(command, "@SteamId", Convert.ToInt64(steamId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAdmin(reader) : null;
    }

    private static Admin? FindActiveAdminRecordBySteamId(IDbConnection connection, ulong steamId, DateTime now)
    {
        EnsureOpen(connection);
        using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT {AdminSelectColumns}
            FROM `admin_admins`
            WHERE `steamid` = @SteamId
              AND (`expires_at` IS NULL OR `expires_at` > @Now)
            LIMIT 1";
        AddParameter(command, "@SteamId", Convert.ToInt64(steamId));
        AddParameter(command, "@Now", now);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAdmin(reader) : null;
    }

    private static List<Admin> QueryActiveAdminRecords(IDbConnection connection, DateTime now)
    {
        EnsureOpen(connection);
        using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT {AdminSelectColumns}
            FROM `admin_admins`
            WHERE `expires_at` IS NULL OR `expires_at` > @Now
            ORDER BY `immunity` DESC, `name` ASC";
        AddParameter(command, "@Now", now);
        using var reader = command.ExecuteReader();

        var admins = new List<Admin>();
        while (reader.Read())
        {
            admins.Add(MapAdmin(reader));
        }

        return admins;
    }

    private static Admin MapAdmin(IDataRecord record)
    {
        return new Admin
        {
            Id = Convert.ToInt32(record["Id"]),
            SteamId = Convert.ToUInt64(record["SteamId"]),
            Name = ReadString(record, "Name"),
            Flags = ReadString(record, "Flags"),
            Groups = ReadString(record, "Groups"),
            Immunity = Convert.ToInt32(record["Immunity"]),
            CreatedAt = ReadDateTime(record, "CreatedAt") ?? DateTime.UtcNow,
            ExpiresAt = ReadDateTime(record, "ExpiresAt"),
            AddedBy = ReadNullableString(record, "AddedBy"),
            AddedBySteamId = ReadNullableUInt64(record, "AddedBySteamId")
        };
    }

    private static string ReadString(IDataRecord record, string name)
    {
        var value = record[name];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    private static string? ReadNullableString(IDataRecord record, string name)
    {
        var value = record[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static DateTime? ReadDateTime(IDataRecord record, string name)
    {
        var value = record[name];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    private static ulong? ReadNullableUInt64(IDataRecord record, string name)
    {
        var value = record[name];
        return value == DBNull.Value ? null : Convert.ToUInt64(value);
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void EnsureOpen(IDbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
    }
}


