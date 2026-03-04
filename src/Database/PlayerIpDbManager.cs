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
            using var connection = _core.Database.GetConnection("admins");
            var now = DateTime.UtcNow;
            var safeName = string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName.Trim();

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
