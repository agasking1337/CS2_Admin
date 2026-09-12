using CS2_Admin.Utils;
using Dapper;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public class PlayerIpDbManager
{
    private readonly ISwiftlyCore _core;

    public PlayerIpDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
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
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var safeName = string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName.Trim();
            using var connection = _core.Database.GetConnection("mysql_detailed");

            await connection.ExecuteAsync(
                """
                INSERT INTO `admin_player_ips` (`steamid`, `player_name`, `ip_address`, `last_seen_at`)
                VALUES (@SteamId, @PlayerName, @IpAddress, @LastSeenAt)
                ON DUPLICATE KEY UPDATE
                    `player_name` = VALUES(`player_name`),
                    `ip_address` = VALUES(`ip_address`),
                    `last_seen_at` = VALUES(`last_seen_at`)
                """,
                new
                {
                    SteamId = Convert.ToInt64(steamId),
                    PlayerName = safeName,
                    IpAddress = normalizedIp,
                    LastSeenAt = now
                });

            // Keep an IP history per SteamID for stronger alt-account and unban correlation.
            var updatedHistory = await connection.ExecuteAsync(
                """
                UPDATE `admin_player_ip_history`
                SET `player_name` = @PlayerName,
                    `last_seen_at` = @LastSeenAt
                WHERE `steamid` = @SteamId
                  AND `ip_address` = @IpAddress
                """,
                new
                {
                    SteamId = Convert.ToInt64(steamId),
                    PlayerName = safeName,
                    IpAddress = normalizedIp,
                    LastSeenAt = now
                });

            if (updatedHistory == 0)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO `admin_player_ip_history` (`steamid`, `player_name`, `ip_address`, `first_seen_at`, `last_seen_at`)
                    VALUES (@SteamId, @PlayerName, @IpAddress, @FirstSeenAt, @LastSeenAt)
                    """,
                    new
                    {
                        SteamId = Convert.ToInt64(steamId),
                        PlayerName = safeName,
                        IpAddress = normalizedIp,
                        FirstSeenAt = now,
                        LastSeenAt = now
                    });
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error upserting player ip: {Message}", ex.Message);
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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            return await connection.QueryFirstOrDefaultAsync<string?>(
                """
                SELECT `ip_address`
                FROM `admin_player_ips`
                WHERE `steamid` = @SteamId
                LIMIT 1
                """,
                new { SteamId = Convert.ToInt64(steamId) });
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
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var rows = connection.Query<string>(
                """
                SELECT DISTINCT `ip_address`
                FROM `admin_player_ip_history`
                WHERE `steamid` = @SteamId
                  AND `ip_address` IS NOT NULL
                  AND `ip_address` <> ''
                ORDER BY `ip_address`
                """,
                new { SteamId = Convert.ToInt64(steamId) })
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

    public async Task<IReadOnlyList<PlayerIpAccountLink>> FindAccountsByIpAsync(string? ipAddress, ulong excludeSteamId, int maxResults = 8)
    {
        var normalizedIp = NormalizeIpAddress(ipAddress);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return [];
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var rows = await connection.QueryAsync<PlayerIpAccountLink>(
                """
                SELECT `steamid` AS `SteamId`, MAX(`player_name`) AS `PlayerName`, MAX(`seen_at`) AS `LastSeenAt`
                FROM (
                    SELECT `steamid`, `player_name`, `last_seen_at` AS `seen_at`
                    FROM `admin_player_ip_history`
                    WHERE `ip_address` = @IpAddress
                    UNION ALL
                    SELECT `steamid`, `player_name`, `updated_at` AS `seen_at`
                    FROM `admin_player_sessions`
                    WHERE `last_ip` = @IpAddress
                ) AS `seen`
                WHERE `steamid` <> @ExcludeSteamId
                GROUP BY `steamid`
                ORDER BY `LastSeenAt` DESC
                LIMIT @MaxResults
                """,
                new
                {
                    IpAddress = normalizedIp,
                    ExcludeSteamId = Convert.ToInt64(excludeSteamId),
                    MaxResults = Math.Max(1, maxResults)
                });

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("[CS2_Admin] Error finding accounts by ip: {Message}", ex.Message);
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

public sealed class PlayerIpAccountLink
{
    public ulong SteamId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
}


