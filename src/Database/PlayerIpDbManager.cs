using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class PlayerIpDbManager
{
    private readonly ISwiftlyCore _core;
    private const int MaxPlayerNameLength = 64;

    public PlayerIpDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            MigrationRunner.RunMigrations(connection);

            var cutoff = DateTime.UtcNow.AddHours(-12);
            await connection.ExecuteAsync(
                """
                UPDATE `admin_player_sessions`
                SET `disconnected_at` = @Now
                WHERE `disconnected_at` IS NULL
                  AND `connected_at` < @Cutoff
                """,
                new { Now = DateTime.UtcNow, Cutoff = cutoff });
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] Player IP database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Player IP database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task UpsertPlayerIpAsync(ulong steamId, string? playerName, string? ipAddress)
    {
        var normalizedIp = NormalizeIpAddress(ipAddress);
        if (steamId == 0 || string.IsNullOrWhiteSpace(normalizedIp))
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] UpsertPlayerIp skipped: steamId={SteamId} rawIp={RawIp} normalizedIp={NormalizedIp}", steamId, ipAddress ?? "null", normalizedIp ?? "null");
            return;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;
            var safeName = ClampPlayerName(string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName.Trim());

            var existing = connection.FirstOrDefault<PlayerIpRecord>(x => x.SteamId == steamId);
            if (existing == null)
            {
                connection.Insert(new PlayerIpRecord
                {
                    SteamId = steamId,
                    PlayerName = safeName,
                    IpAddress = normalizedIp,
                    LastSeenAt = now
                });
            }
            else
            {
                existing.PlayerName = safeName;
                existing.IpAddress = normalizedIp;
                existing.LastSeenAt = now;
                connection.Update(existing);
            }

            // Keep an IP history per SteamID for stronger alt-account and unban correlation.
            var existingHistory = connection.FirstOrDefault<PlayerIpHistoryRecord>(x => x.SteamId == steamId && x.IpAddress == normalizedIp);
            if (existingHistory == null)
            {
                connection.Insert(new PlayerIpHistoryRecord
                {
                    SteamId = steamId,
                    PlayerName = safeName,
                    IpAddress = normalizedIp,
                    FirstSeenAt = now,
                    LastSeenAt = now
                });
            }
            else
            {
                existingHistory.PlayerName = safeName;
                existingHistory.LastSeenAt = now;
                connection.Update(existingHistory);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error upserting player ip: {Message}", ex.Message);
        }
    }

    public async Task<long> InsertPlayerSessionAsync(ulong steamId, string? playerName, string? ipAddress, DateTime connectedAt)
    {
        if (steamId == 0)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] InsertPlayerSession skipped: steamId is 0");
            return 0;
        }

        try
        {
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] InsertPlayerSession attempting: steamId={SteamId} name={Name} ip={Ip}", steamId, playerName, ipAddress);
            using var connection = _core.Database.GetConnection("admins");
            var safeName = ClampPlayerName(string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName.Trim());
            var safeIp = NormalizeIpAddress(ipAddress) ?? string.Empty;
            var record = new PlayerSessionRecord
            {
                SteamId = steamId,
                PlayerName = safeName,
                IpAddress = safeIp,
                ConnectedAt = connectedAt,
                DisconnectedAt = null
            };
            var id = Convert.ToInt64(connection.Insert(record));
            _core.Logger.LogInformationIfEnabled("[CS2_Admin] InsertPlayerSession succeeded: steamId={SteamId} sessionId={SessionId}", steamId, id);
            return id;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error inserting player session: {Message} | StackTrace: {Stack}", ex.Message, ex.StackTrace);
            return 0;
        }
    }

    public async Task ClosePlayerSessionAsync(long sessionId, string? playerName, DateTime disconnectedAt, ulong steamId = 0)
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var safeName = !string.IsNullOrWhiteSpace(playerName) ? ClampPlayerName(playerName.Trim()) : null;

            int affected = 0;

            if (sessionId > 0)
            {
                affected = await connection.ExecuteAsync(
                    safeName != null
                        ? """
                          UPDATE `admin_player_sessions`
                          SET `disconnected_at` = @DisconnectedAt, `player_name` = @PlayerName
                          WHERE `id` = @Id AND `disconnected_at` IS NULL
                          """
                        : """
                          UPDATE `admin_player_sessions`
                          SET `disconnected_at` = @DisconnectedAt
                          WHERE `id` = @Id AND `disconnected_at` IS NULL
                          """,
                    new { Id = sessionId, DisconnectedAt = disconnectedAt, PlayerName = safeName });
            }

            if (affected == 0 && steamId != 0)
            {
                affected = await connection.ExecuteAsync(
                    safeName != null
                        ? """
                          UPDATE `admin_player_sessions`
                          SET `disconnected_at` = @DisconnectedAt, `player_name` = @PlayerName
                          WHERE `steamid` = @SteamId AND `disconnected_at` IS NULL
                          ORDER BY `connected_at` DESC
                          LIMIT 1
                          """
                        : """
                          UPDATE `admin_player_sessions`
                          SET `disconnected_at` = @DisconnectedAt
                          WHERE `steamid` = @SteamId AND `disconnected_at` IS NULL
                          ORDER BY `connected_at` DESC
                          LIMIT 1
                          """,
                    new { SteamId = (long)steamId, DisconnectedAt = disconnectedAt, PlayerName = safeName });
            }

            _core.Logger.LogInformationIfEnabled(
                "[CS2_Admin] ClosePlayerSession: sessionId={SessionId} steamId={SteamId} affected={Affected}",
                sessionId, steamId, affected);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error closing player session: {Message}", ex.Message);
        }
    }

    private static string ClampPlayerName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return name.Length <= MaxPlayerNameLength
            ? name
            : name[..MaxPlayerNameLength];
    }

    public async Task<IReadOnlyList<PlayerSessionRecord>> GetRecentDisconnectedAsync(int limit)
    {
        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var rows = await connection.QueryAsync<PlayerSessionRecord>(
                """
                SELECT id, steamid, player_name, ip_address, connected_at, disconnected_at
                FROM `admin_player_sessions`
                WHERE steamid != 0
                  AND disconnected_at IS NOT NULL
                ORDER BY disconnected_at DESC
                LIMIT @Limit
                """,
                new { Limit = limit * 3 });

            var seen = new HashSet<ulong>();
            var result = new List<PlayerSessionRecord>();
            foreach (var row in rows)
            {
                if (seen.Add(row.SteamId))
                {
                    result.Add(row);
                    if (result.Count >= limit)
                    {
                        break;
                    }
                }
            }

            _core.Logger.LogInformationIfEnabled("[CS2_Admin] GetRecentDisconnected: returning={Returning}", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error querying recent disconnected players: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<string?> GetLatestIpAsync(ulong steamId)
    {
        if (steamId == 0)
        {
            return null;
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var existing = connection.FirstOrDefault<PlayerIpRecord>(x => x.SteamId == steamId);
            return existing?.IpAddress;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error loading player ip: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetAllKnownIpsAsync(ulong steamId)
    {
        if (steamId == 0)
        {
            return [];
        }

        try
        {
            using var connection = _core.Database.GetConnection("admins");
            var rows = connection.Query<string>(
                """
                SELECT DISTINCT `ip_address`
                FROM `admin_player_ip_history`
                WHERE `steamid` = @SteamId
                  AND `ip_address` IS NOT NULL
                  AND `ip_address` <> ''
                ORDER BY `ip_address`
                """,
                new { SteamId = steamId })
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Select(ip => NormalizeIpAddress(ip))
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Select(ip => ip!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rows.Count > 0)
            {
                return rows;
            }

            var latest = await GetLatestIpAsync(steamId);
            if (!string.IsNullOrWhiteSpace(latest))
            {
                return [latest];
            }

            return [];
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error loading known ips: {Message}", ex.Message);
            return [];
        }
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

        return normalized;
    }
}
