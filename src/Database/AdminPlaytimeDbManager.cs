using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public readonly record struct AdminPlaytimeSnapshot(ulong SteamId, string PlayerName);

public class AdminPlaytimeDbManager
{
    private readonly ISwiftlyCore _core;
    private readonly AdminDbManager _adminDbManager;

    public AdminPlaytimeDbManager(ISwiftlyCore core, AdminDbManager adminDbManager)
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
            _core.Logger.LogInformationIfEnabled("Admin playtime database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Admin playtime database initialization warning: {Message}", ex.Message);
        }
    }

    public async Task TrackOnlineAdminsAsync(IReadOnlyList<AdminPlaytimeSnapshot> onlinePlayers, int minutesToAdd = 1)
    {
        if (onlinePlayers.Count == 0 || minutesToAdd <= 0)
        {
            return;
        }

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var allEntries = connection.GetAll<AdminPlaytime>().ToDictionary(x => x.SteamId);
            var now = DateTime.UtcNow;

            foreach (var player in onlinePlayers)
            {
                var admin = await _adminDbManager.GetAdminAsync(player.SteamId);
                if (admin == null || !admin.IsActive)
                {
                    continue;
                }

                if (allEntries.TryGetValue(player.SteamId, out var existing))
                {
                    existing.PlayerName = NormalizePlayerName(player.PlayerName, player.SteamId);
                    existing.PlaytimeMinutes += minutesToAdd;
                    existing.UpdatedAt = now;
                    connection.Update(existing);
                    continue;
                }

                var row = new AdminPlaytime
                {
                    SteamId = player.SteamId,
                    PlayerName = NormalizePlayerName(player.PlayerName, player.SteamId),
                    PlaytimeMinutes = minutesToAdd,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                var id = connection.Insert(row);
                row.Id = Convert.ToInt64(id);
                allEntries[player.SteamId] = row;
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error tracking admin playtime: {Message}", ex.Message);
        }
    }

    public async Task<List<AdminPlaytime>> GetTopAdminsAsync(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);

        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            return connection
                .GetAll<AdminPlaytime>()
                .OrderByDescending(x => x.PlaytimeMinutes)
                .ThenBy(x => x.PlayerName)
                .Take(safeLimit)
                .ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error getting admin playtime top list: {Message}", ex.Message);
            return [];
        }
    }

    public async Task ResetAllAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "TRUNCATE TABLE admin_playtime";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error resetting admin playtime: {Message}", ex.Message);
        }
    }

    private static string NormalizePlayerName(string? playerName, ulong steamId)
    {
        return SafeName.ForPlayer(playerName, steamId);
    }
}


