using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Collections.Concurrent;

namespace CS2_Admin.Database;

public class PlayerNameHistoryManager
{
    private readonly ISwiftlyCore _core;
    private readonly ConcurrentDictionary<ulong, string> _lastObservedNames = new();
    private readonly ConcurrentDictionary<ulong, string> _customNames = new();

    public PlayerNameHistoryManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Player name history database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Player name history database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task ObserveNameAsync(ulong steamId, string? playerName, DateTime? observedAtUtc = null, bool forceWrite = false)
    {
        if (steamId == 0)
        {
            return;
        }

            var safeName = SafeName.ForPlayer(playerName, steamId);
        var normalized = NormalizeName(safeName);
        if (!forceWrite && _lastObservedNames.TryGetValue(steamId, out var cached) && string.Equals(cached, normalized, StringComparison.Ordinal))
        {
            return;
        }

        var now = observedAtUtc ?? DateTime.UtcNow;
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = await connection.QueryFirstOrDefaultAsync<PlayerNameHistoryRecord>(
                """
                SELECT *
                FROM `admin_player_names_history`
                WHERE `steamid` = @SteamId
                  AND LOWER(`player_name`) = @NormalizedName
                ORDER BY `id` ASC
                LIMIT 1
                """,
                new
                {
                    SteamId = steamId,
                    NormalizedName = normalized
                });

            if (existing == null)
            {
                connection.Insert(new PlayerNameHistoryRecord
                {
                    SteamId = steamId,
                    PlayerName = safeName,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.LastSeenAt = now;
                existing.UpdatedAt = now;
                connection.Update(existing);
            }

            _lastObservedNames[steamId] = normalized;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error observing player name history: {Message}", ex.Message);
        }
    }

    public void PrimeObservedName(ulong steamId, string? playerName)
    {
        if (steamId == 0)
        {
            return;
        }

            _lastObservedNames[steamId] = NormalizeName(SafeName.ForPlayer(playerName, steamId));
    }

    public async Task SetCustomNameAsync(ulong steamId, string? playerName)
    {
        if (steamId == 0)
        {
            return;
        }

            var safeName = SafeName.ForPlayer(playerName, steamId);
        var now = DateTime.UtcNow;
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = await connection.QueryFirstOrDefaultAsync<PlayerCustomNameRecord>(
                """
                SELECT *
                FROM `admin_player_custom_names`
                WHERE `steamid` = @SteamId
                LIMIT 1
                """,
                new { SteamId = steamId });

            if (existing == null)
            {
                connection.Insert(new PlayerCustomNameRecord
                {
                    SteamId = steamId,
                    PlayerName = safeName,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.PlayerName = safeName;
                existing.UpdatedAt = now;
                connection.Update(existing);
            }

            _customNames[steamId] = safeName;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error saving custom player name: {Message}", ex.Message);
        }
    }

    public async Task<string?> GetCustomNameAsync(ulong steamId)
    {
        if (steamId == 0)
        {
            return null;
        }

        if (_customNames.TryGetValue(steamId, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var value = await connection.QueryFirstOrDefaultAsync<string?>(
                "SELECT `player_name` FROM `admin_player_custom_names` WHERE `steamid` = @SteamId LIMIT 1",
                new { SteamId = steamId });

            if (!string.IsNullOrWhiteSpace(value))
            {
                _customNames[steamId] = value;
            }

            return value;
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error loading custom player name: {Message}", ex.Message);
            return null;
        }
    }

    public async Task DeleteCustomNameAsync(ulong steamId)
    {
        if (steamId == 0)
        {
            return;
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            await connection.ExecuteAsync("DELETE FROM `admin_player_custom_names` WHERE `steamid` = @SteamId", new { SteamId = steamId });
            _customNames.TryRemove(steamId, out _);
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error deleting custom player name: {Message}", ex.Message);
        }
    }

    public async Task<string?> GetOriginalNameAsync(ulong steamId)
    {
        if (steamId == 0)
        {
            return null;
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            return await connection.QueryFirstOrDefaultAsync<string?>(
                "SELECT `player_name` FROM `admin_player_names_history` WHERE `steamid` = @SteamId ORDER BY `id` ASC LIMIT 1",
                new { SteamId = steamId });
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting original name: {Message}", ex.Message);
            return null;
        }
    }

    private static string NormalizeName(string playerName)
    {
        return playerName.Trim().ToLowerInvariant();
    }
}
