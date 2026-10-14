using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public sealed record PlayerSessionSnapshot(
    int PlayerId,
    ulong SteamId,
    string PlayerName,
    string? IpAddress);

public sealed class PlayerPlaytimeEntry
{
    public ulong SteamId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public long TotalSeconds { get; set; }
}

public class PlayerSessionManager
{
    private readonly ISwiftlyCore _core;
    private readonly AdminDbManager _adminDbManager;
    private readonly ConcurrentDictionary<ulong, long> _activeSessionIds = new();

    public PlayerSessionManager(ISwiftlyCore core, AdminDbManager adminDbManager)
    {
        _core = core;
        _adminDbManager = adminDbManager;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Player session database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Player session database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task ReconcileOpenSessionsAsync(IReadOnlyList<PlayerSessionSnapshot> onlinePlayers)
    {
        var serverId = GetServerId();
        var now = DateTime.UtcNow;
        var onlineBySteamId = onlinePlayers
            .Where(x => x.SteamId != 0)
            .GroupBy(x => x.SteamId)
            .ToDictionary(g => g.Key, g => g.First());

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var openSessions = (await connection.QueryAsync<PlayerSessionRecord>(
                """
                SELECT *
                FROM `admin_player_sessions`
                WHERE `server_id` = @ServerId
                  AND `disconnected_at` IS NULL
                ORDER BY `connected_at` ASC
                """,
                new { ServerId = serverId }))
                .ToList();

            foreach (var session in openSessions)
            {
                if (onlineBySteamId.TryGetValue(session.SteamId, out var online))
                {
                    session.PlayerName = SafeName.ForPlayer(online.PlayerName, session.SteamId);
                    session.LastUserId = online.PlayerId;
                    session.LastIp = NormalizeIpAddress(online.IpAddress);
                    session.UpdatedAt = now;
                    connection.Update(session);
                    _activeSessionIds[session.SteamId] = session.Id;
                    onlineBySteamId.Remove(session.SteamId);
                    continue;
                }

                CloseSessionRecord(connection, session, now, session.LastUserId, session.LastIp);
            }

            foreach (var online in onlineBySteamId.Values)
            {
                await OpenSessionAsync(online.SteamId, online.PlayerName, online.PlayerId, online.IpAddress, now);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error reconciling player sessions: {Message}", ex.Message);
        }
    }

    public async Task OpenSessionAsync(ulong steamId, string? playerName, int? playerId, string? ipAddress, DateTime? connectedAtUtc = null)
    {
        if (steamId == 0)
        {
            return;
        }

        var serverId = GetServerId();
        var now = connectedAtUtc ?? DateTime.UtcNow;
            var safeName = SafeName.ForPlayer(playerName, steamId);
        var normalizedIp = NormalizeIpAddress(ipAddress);

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = await GetOpenSessionAsync(connection, steamId, serverId);
            if (existing != null)
            {
                existing.PlayerName = safeName;
                existing.LastUserId = playerId;
                existing.LastIp = normalizedIp;
                existing.UpdatedAt = now;
                connection.Update(existing);
                _activeSessionIds[steamId] = existing.Id;
                return;
            }

            var record = new PlayerSessionRecord
            {
                SteamId = steamId,
                PlayerName = safeName,
                ServerId = serverId,
                ConnectedAt = now,
                DisconnectedAt = null,
                DurationSeconds = 0,
                LastUserId = playerId,
                LastIp = normalizedIp,
                CreatedAt = now,
                UpdatedAt = now
            };

            var sessionId = connection.Insert(record);
            _activeSessionIds[steamId] = Convert.ToInt64(sessionId);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error opening player session: {Message}", ex.Message);
        }
    }

    public async Task TouchSessionAsync(ulong steamId, string? playerName, int? playerId, string? ipAddress)
    {
        if (steamId == 0)
        {
            return;
        }

        var serverId = GetServerId();
        var now = DateTime.UtcNow;
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var session = await GetOpenSessionAsync(connection, steamId, serverId);
            if (session == null)
            {
                await OpenSessionAsync(steamId, playerName, playerId, ipAddress, now);
                return;
            }

            session.PlayerName = SafeName.ForPlayer(playerName, steamId);
            session.LastUserId = playerId;
            session.LastIp = NormalizeIpAddress(ipAddress);
            session.UpdatedAt = now;
            connection.Update(session);
            _activeSessionIds[steamId] = session.Id;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error touching player session: {Message}", ex.Message);
        }
    }

    public async Task CloseSessionAsync(ulong steamId, string? playerName, int? playerId, string? ipAddress, DateTime? disconnectedAtUtc = null)
    {
        if (steamId == 0)
        {
            return;
        }

        var serverId = GetServerId();
        var now = disconnectedAtUtc ?? DateTime.UtcNow;

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var session = await GetOpenSessionAsync(connection, steamId, serverId);
            if (session == null)
            {
                _activeSessionIds.TryRemove(steamId, out _);
                return;
            }

            session.PlayerName = SafeName.ForPlayer(playerName, steamId);
            session.LastUserId = playerId;
            session.LastIp = NormalizeIpAddress(ipAddress);
            CloseSessionRecord(connection, session, now, playerId, session.LastIp);
            _activeSessionIds.TryRemove(steamId, out _);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error closing player session: {Message}", ex.Message);
        }
    }

    public async Task<List<PlayerPlaytimeEntry>> GetTopPlaytimeAsync(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        var serverId = GetServerId();
        var adminSteamIds = (await _adminDbManager.GetAllAdminsAsync())
            .Where(x => x.IsActive)
            .Select(x => x.SteamId)
            .ToHashSet();

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var rows = await connection.QueryAsync<PlayerPlaytimeEntry>(
                """
                SELECT
                    `steamid` AS `SteamId`,
                    MAX(`player_name`) AS `PlayerName`,
                    SUM(
                        CASE
                            WHEN `disconnected_at` IS NULL THEN TIMESTAMPDIFF(SECOND, `connected_at`, UTC_TIMESTAMP())
                            ELSE COALESCE(`duration_seconds`, 0)
                        END
                    ) AS `TotalSeconds`
                FROM `admin_player_sessions`
                WHERE `server_id` = @ServerId
                GROUP BY `steamid`
                ORDER BY `TotalSeconds` DESC, `PlayerName` ASC
                LIMIT @ExpandedLimit
                """,
                new { ServerId = serverId, ExpandedLimit = Math.Max(safeLimit * 5, 100) });

            return rows
                .Where(x => x.SteamId != 0 && !adminSteamIds.Contains(x.SteamId))
                .Take(safeLimit)
                .ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting top player playtime list: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<List<PlayerPlaytimeEntry>> GetGlobalTopPlaytimeAsync(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        var adminSteamIds = (await _adminDbManager.GetAllAdminsAsync())
            .Where(x => x.IsActive)
            .Select(x => x.SteamId)
            .ToHashSet();

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var rows = await connection.QueryAsync<PlayerPlaytimeEntry>(
                """
                SELECT
                    `steamid` AS `SteamId`,
                    MAX(`player_name`) AS `PlayerName`,
                    SUM(
                        CASE
                            WHEN `disconnected_at` IS NULL THEN TIMESTAMPDIFF(SECOND, `connected_at`, UTC_TIMESTAMP())
                            ELSE COALESCE(`duration_seconds`, 0)
                        END
                    ) AS `TotalSeconds`
                FROM `admin_player_sessions`
                GROUP BY `steamid`
                ORDER BY `TotalSeconds` DESC, `PlayerName` ASC
                LIMIT @ExpandedLimit
                """,
                new { ExpandedLimit = Math.Max(safeLimit * 5, 100) });

            return rows
                .Where(x => x.SteamId != 0 && !adminSteamIds.Contains(x.SteamId) && !_core.Permission.PlayerHasPermission(x.SteamId, "@css/generic") && !_core.Permission.PlayerHasPermission(x.SteamId, "@css/root"))
                .Take(safeLimit)
                .ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting global top player playtime list: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<List<RecentPlayerInfo>> GetRecentDisconnectedPlayersAsync(int limit = 30)
    {
        var safeLimit = Math.Clamp(limit, 5, 100);
        var serverId = GetServerId();

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var rows = await connection.QueryAsync<PlayerSessionRecord>(
                """
                SELECT *
                FROM `admin_player_sessions`
                WHERE `server_id` = @ServerId
                  AND `disconnected_at` IS NOT NULL
                ORDER BY `disconnected_at` DESC, `id` DESC
                LIMIT @Limit
                """,
                new { ServerId = serverId, Limit = safeLimit });

            return rows
                .Where(row => row.SteamId != 0 && row.DisconnectedAt.HasValue)
                .Select(row => new RecentPlayerInfo(
                    row.SteamId,
                    SafeName.ForPlayer(row.PlayerName, row.SteamId),
                    row.LastIp ?? string.Empty,
                    row.DisconnectedAt!.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting recent disconnected players: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<PlayerSessionRecord?> GetOpenSessionAsync(System.Data.IDbConnection connection, ulong steamId, string serverId)
    {
        if (_activeSessionIds.TryGetValue(steamId, out var cachedSessionId))
        {
            var cached = await connection.GetAsync<PlayerSessionRecord>(cachedSessionId);
            if (cached != null && cached.DisconnectedAt == null && string.Equals(cached.ServerId, serverId, StringComparison.Ordinal))
            {
                return cached;
            }

            _activeSessionIds.TryRemove(steamId, out _);
        }

        var session = await connection.QueryFirstOrDefaultAsync<PlayerSessionRecord>(
            """
            SELECT *
            FROM `admin_player_sessions`
            WHERE `steamid` = @SteamId
              AND `server_id` = @ServerId
              AND `disconnected_at` IS NULL
            ORDER BY `connected_at` DESC
            LIMIT 1
            """,
            new { SteamId = steamId, ServerId = serverId });

        if (session != null)
        {
            _activeSessionIds[steamId] = session.Id;
        }

        return session;
    }

    private static void CloseSessionRecord(System.Data.IDbConnection connection, PlayerSessionRecord session, DateTime disconnectedAtUtc, int? lastUserId, string? lastIp)
    {
        session.DisconnectedAt = disconnectedAtUtc;
        session.DurationSeconds = (int)Math.Max(0, (disconnectedAtUtc - session.ConnectedAt).TotalSeconds);
        session.LastUserId = lastUserId;
        session.LastIp = lastIp;
        session.UpdatedAt = disconnectedAtUtc;
        connection.Update(session);
    }

    private string GetServerId()
    {
        var serverId = ServerIdentity.GetServerId(_core);
        return string.IsNullOrWhiteSpace(serverId) ? string.Empty : serverId.Trim();
    }

    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var normalized = ipAddress.Trim();
        var colonIndex = normalized.IndexOf(':');
        if (colonIndex > 0)
        {
            normalized = normalized[..colonIndex];
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
