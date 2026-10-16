using CS2_Admin.Models;
using CS2_Admin.Utils;
using Dommel;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Database;

public sealed record DiscordServerStatusSnapshot(
    string ServerId,
    string HubKey,
    string ServerName,
    string ButtonLabel,
    string ServerIp,
    int ServerPort,
    string MapName,
    int PlayerCount,
    int MaxPlayers,
    string JoinUrl);

public class DiscordServerStatusDbManager
{
    private readonly ISwiftlyCore _core;

    public DiscordServerStatusDbManager(ISwiftlyCore core)
    {
        _core = core;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            MigrationRunner.RunMigrations(connection);
            _core.Logger.LogInformationIfEnabled("Discord server status database initialized successfully");
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("Discord server status initialization warning: {Message}", ex.Message);
        }
    }

    public async Task UpsertStatusAsync(DiscordServerStatusSnapshot snapshot)
    {
        try
        {
            using var connection = _core.Database.GetConnection("mysql_detailed");
            var existing = connection.FirstOrDefault<DiscordServerStatus>(x => x.ServerId == snapshot.ServerId);
            var now = DateTime.UtcNow;

            if (existing != null)
            {
                existing.HubKey = snapshot.HubKey;
                existing.ServerName = snapshot.ServerName;
                existing.ButtonLabel = snapshot.ButtonLabel;
                existing.ServerIp = snapshot.ServerIp;
                existing.ServerPort = snapshot.ServerPort;
                existing.MapName = snapshot.MapName;
                existing.PlayerCount = snapshot.PlayerCount;
                existing.MaxPlayers = snapshot.MaxPlayers;
                existing.JoinUrl = snapshot.JoinUrl;
                existing.LastHeartbeatAt = now;
                existing.UpdatedAt = now;
                connection.Update(existing);
                return;
            }

            connection.Insert(new DiscordServerStatus
            {
                ServerId = snapshot.ServerId,
                HubKey = snapshot.HubKey,
                ServerName = snapshot.ServerName,
                ButtonLabel = snapshot.ButtonLabel,
                ServerIp = snapshot.ServerIp,
                ServerPort = snapshot.ServerPort,
                MapName = snapshot.MapName,
                PlayerCount = snapshot.PlayerCount,
                MaxPlayers = snapshot.MaxPlayers,
                JoinUrl = snapshot.JoinUrl,
                LastHeartbeatAt = now,
                UpdatedAt = now
            });
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error upserting discord server status: {Message}", ex.Message);
        }
    }

    public async Task<List<DiscordServerStatus>> GetActiveStatusesAsync(string hubKey, TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            using var connection = _core.Database.GetConnection("mysql_detailed");
            return connection
                .Select<DiscordServerStatus>(x => x.HubKey == hubKey && x.LastHeartbeatAt >= cutoff)
                .OrderBy(x => x.ButtonLabel)
                .ThenBy(x => x.ServerName)
                .ToList();
        }
        catch (Exception ex)
        {
            _core.Logger.LogErrorIfEnabled("Error reading discord server statuses: {Message}", ex.Message);
            return [];
        }
    }
}
